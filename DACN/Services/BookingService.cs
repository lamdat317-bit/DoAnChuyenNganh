using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.DTOs;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DACN.Services
{
    public class BookingService : IBookingService
    {
        private static readonly string ExpiredHoldNote =
            $"[Tự động hủy: quá {BookingRules.HoldMinutes} phút giữ chỗ chưa thanh toán]";

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<BookingService> _logger;
        private readonly INotificationService _notifications;

        /// <summary>Khách chỉ được đặt trước tối đa ngần này ngày.</summary>
        public const int MaxAdvanceDays = 60;

        /// <summary>Thời lượng tối đa của 1 đơn khách đặt online (giờ).</summary>
        public const int MaxHoursPerBooking = 6;

        public BookingService(IUnitOfWork unitOfWork, ILogger<BookingService> logger, INotificationService notifications)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notifications = notifications;
        }

        /// <summary>
        /// Khách đặt sân online: đơn ở trạng thái Pending, giữ chỗ trong <see cref="BookingRules.HoldMinutes"/> phút chờ thanh toán.
        /// </summary>
        public Task<BookingResult> CreateBookingAsync(BookingDto dto)
            => CreateBookingCoreAsync(dto, isCounterBooking: false, totalAmountOverride: null, isPaid: false);

        /// <summary>
        /// Chủ sân đặt nhanh tại quầy: đơn được xác nhận ngay (Confirmed), có thể đã hoặc chưa thu tiền.
        /// </summary>
        public Task<BookingResult> CreateCounterBookingAsync(BookingDto dto, decimal? totalAmountOverride, bool isPaid)
            => CreateBookingCoreAsync(dto, isCounterBooking: true, totalAmountOverride, isPaid);

        /// <summary>
        /// Tạo đơn đặt sân với cơ chế bảo vệ Race Condition & Double Booking bằng Serializable Transaction.
        /// </summary>
        private async Task<BookingResult> CreateBookingCoreAsync(BookingDto dto, bool isCounterBooking, decimal? totalAmountOverride, bool isPaid)
        {
            // 1. Validation thời gian cơ bản
            var today = DateOnly.FromDateTime(DateTime.Now);
            var nowTime = DateTime.Now.TimeOfDay;

            if (dto.StartTime >= dto.EndTime)
            {
                return BookingResult.Failure("Giờ bắt đầu phải nhỏ hơn giờ kết thúc.");
            }

            if (dto.BookingDate < today)
            {
                return BookingResult.Failure("Không thể đặt sân cho ngày trong quá khứ.");
            }

            // Chỉ từ chối nếu toàn bộ khung giờ đã kết thúc trong ngày hôm nay
            if (dto.BookingDate == today && dto.EndTime <= nowTime)
            {
                return BookingResult.Failure("Khung giờ này đã kết thúc trong ngày hôm nay. Vui lòng chọn khung giờ tiếp theo.");
            }

            // 2. Kiểm tra sân có tồn tại và đang hoạt động hay không
            var court = await _unitOfWork.Courts.Query()
                .Include(c => c.Facility)
                .Include(c => c.TimeSlots)
                .FirstOrDefaultAsync(c => c.Id == dto.CourtId);

            if (court == null || !court.IsActive)
            {
                return BookingResult.Failure("Sân không tồn tại hoặc hiện đang tạm ngưng hoạt động.");
            }

            // Khách chỉ đặt được cụm sân đã được Admin duyệt
            if (!isCounterBooking && court.Facility?.ApprovalStatus != FacilityApprovalStatus.Approved)
            {
                return BookingResult.Failure("Cụm sân này chưa được duyệt nên chưa nhận đặt sân online.");
            }

            if (!isCounterBooking && (dto.EndTime - dto.StartTime).TotalHours > MaxHoursPerBooking)
            {
                return BookingResult.Failure($"Mỗi đơn đặt tối đa {MaxHoursPerBooking} tiếng liên tiếp.");
            }

            if (!isCounterBooking && dto.BookingDate > today.AddDays(MaxAdvanceDays))
            {
                return BookingResult.Failure($"Chỉ nhận đặt trước tối đa {MaxAdvanceDays} ngày.");
            }

            // Kiểm tra khung giờ nằm trong giờ hoạt động (hỗ trợ sân mở qua nửa đêm; một đơn không vắt qua 24:00)
            if (court.Facility != null && !BookingRules.IsWithinOpenHours(court.Facility, dto.StartTime, dto.EndTime))
            {
                return BookingResult.Failure($"Khung giờ đặt phải nằm trong giờ hoạt động của sân ({BookingRules.OpenHoursText(court.Facility)}). Khung giờ qua nửa đêm vui lòng đặt thành 2 đơn.");
            }

            // 3. Tính tiền sân theo bảng giá khung giờ (chủ sân đặt tại quầy được phép nhập giá riêng)
            decimal courtAmount = totalAmountOverride.HasValue && totalAmountOverride.Value > 0
                ? totalAmountOverride.Value
                : BookingRules.CalculatePrice(court.TimeSlots, dto.StartTime, dto.EndTime);

            // Tính tiền dịch vụ tiện ích đính kèm (nếu khách chọn) — chỉ nhận dịch vụ đang áp dụng của đúng cụm sân
            decimal servicesTotal = 0;
            var serviceDetails = new List<BookingServiceDetail>();
            if (dto.SelectedServiceIds != null && dto.SelectedServiceIds.Any())
            {
                var selectedIds = dto.SelectedServiceIds.Distinct().ToList();
                var services = await _unitOfWork.FacilityServices.Query()
                    .Where(s => selectedIds.Contains(s.Id) && s.FacilityId == court.FacilityId && s.IsActive)
                    .ToListAsync();

                foreach (var s in services)
                {
                    servicesTotal += s.Price;
                    serviceDetails.Add(new BookingServiceDetail
                    {
                        FacilityServiceId = s.Id,
                        Quantity = 1,
                        SubTotal = s.Price
                    });
                }
            }

            // 4. BẮT ĐẦU TRANSACTION VỚI ISOLATION LEVEL SERIALIZABLE (CHỐNG TRÙNG LỊCH)
            // Transaction chưa Commit sẽ tự Rollback khi bị Dispose (kể cả khi SQL Server đã hủy giao dịch do deadlock).
            await using var transaction = await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                // Kiểm tra xung đột thời gian với các đơn CÒN HIỆU LỰC:
                // đã xác nhận / đã thanh toán / đang trong thời hạn giữ chỗ. Đơn hủy và đơn giữ chỗ quá hạn không chặn.
                bool hasOverlap = await _unitOfWork.Bookings.HasOverlappingBookingAsync(
                    dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);

                if (hasOverlap)
                {
                    _logger.LogWarning("Phát hiện xung đột đặt sân: Court {CourtId}, Date {Date}, Time {Start}-{End}",
                        dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
                    return BookingResult.Failure(isCounterBooking
                        ? "Khung giờ này đã có người đặt trước hoặc đang được giữ chỗ."
                        : $"Rất tiếc! Khung giờ này vừa có người đặt hoặc đang trong {BookingRules.HoldMinutes} phút giữ chỗ thanh toán. Vui lòng chọn khung giờ khác.");
                }

                var booking = new Booking
                {
                    CustomerId = dto.CustomerId ?? throw new InvalidOperationException("Thiếu mã khách hàng cho đơn đặt sân."),
                    CustomerName = dto.CustomerName,
                    CustomerPhone = dto.CustomerPhone,
                    Note = dto.Note,
                    CourtId = dto.CourtId,
                    BookingDate = dto.BookingDate,
                    StartTime = dto.StartTime,
                    EndTime = dto.EndTime,
                    TotalAmount = courtAmount + servicesTotal,
                    Status = isCounterBooking ? BookingStatus.Confirmed : BookingStatus.Pending,
                    PaymentStatus = isPaid ? PaymentStatus.Paid : PaymentStatus.Unpaid,
                    PaymentMethod = isPaid ? "Tiền mặt tại quầy" : null,
                    PaidAt = isPaid ? DateTime.UtcNow : null,
                    CreatedAt = DateTime.UtcNow,
                    ServiceDetails = serviceDetails
                };

                await _unitOfWork.Bookings.AddAsync(booking);
                await _unitOfWork.CompleteAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Đặt sân thành công! BookingId: {BookingId}, Court: {CourtId}, Total: {Total}",
                    booking.Id, booking.CourtId, booking.TotalAmount);

                if (!isCounterBooking)
                {
                    await _notifications.NotifyFacilityOwnerAsync(court.FacilityId,
                        $"Đơn mới #BK-{booking.Id} · {court.Name}",
                        $"{booking.CustomerName} ({booking.CustomerPhone}) đặt {booking.BookingDate:dd/MM} {BookingRules.Hm(booking.StartTime)}–{BookingRules.Hm(booking.EndTime)}, {booking.TotalAmount:N0}đ. Đơn đang chờ khách thanh toán.",
                        $"/Owner/Facility/Bookings?facilityId={court.FacilityId}&q={booking.Id}",
                        "bi-calendar-plus", "green", isNewBooking: true);
                }

                return BookingResult.Success(booking, isCounterBooking
                    ? "Đặt sân tại quầy thành công!"
                    : "Đặt sân thành công! Vui lòng tiến hành thanh toán để giữ chỗ.");
            }
            catch (Exception ex) when (IsConcurrencyConflict(ex))
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Xung đột đồng thời khi đặt sân {CourtId}", dto.CourtId);
                return BookingResult.Failure("Khung giờ này vừa có người khác đặt cùng lúc. Vui lòng chọn lại khung giờ.");
            }
            catch (Exception ex)
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình đặt sân {CourtId}", dto.CourtId);
                return BookingResult.Failure("Đã có lỗi xảy ra trong quá trình đặt sân. Vui lòng thử lại.");
            }
        }

        /// <summary>
        /// Xung đột đồng thời: RowVersion không khớp, hoặc SQL Server chọn giao dịch này làm nạn nhân deadlock (1205)
        /// khi 2 giao dịch Serializable cùng tranh 1 khung giờ.
        /// </summary>
        private static bool IsConcurrencyConflict(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is DbUpdateConcurrencyException) return true;
                if (e is SqlException sql && sql.Number == 1205) return true;
            }
            return false;
        }

        /// <summary>
        /// Lấy ma trận các slot thời gian (1 tiếng / slot) của 1 sân con trong ngày
        /// </summary>
        public async Task<CourtScheduleMatrixViewModel> GetCourtScheduleMatrixAsync(int courtId, DateOnly date)
        {
            var court = await _unitOfWork.Courts.Query()
                .Include(c => c.Facility)
                    .ThenInclude(f => f!.SportType)
                .Include(c => c.TimeSlots)
                .FirstOrDefaultAsync(c => c.Id == courtId);

            if (court == null)
            {
                throw new ArgumentException("Sân không tồn tại.");
            }

            var facility = court.Facility;
            var blocks = facility != null
                ? BookingRules.HourBlocks(facility)
                : BookingRules.HourBlocks(new Facility { OpeningTime = new TimeSpan(6, 0, 0), ClosingTime = new TimeSpan(22, 0, 0) });

            // Chỉ lấy các đơn còn hiệu lực (đã loại đơn hủy và đơn giữ chỗ quá hạn)
            var bookings = await _unitOfWork.Bookings.GetBookingsByCourtAndDateAsync(courtId, date);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentTime = DateTime.Now.TimeOfDay;

            var slots = new List<TimeSlotMatrixDto>();
            int slotIndex = 1;

            // Duyệt từng block 1 tiếng trong các đoạn giờ hoạt động (sân mở qua đêm có 2 đoạn: 00:00→giờ đóng, giờ mở→24:00)
            foreach (var (time, slotEnd) in blocks)
            {

                // Giá hiển thị = đúng số tiền sẽ bị tính khi đặt block này (dùng chung hàm tính giá với lúc tạo đơn)
                decimal slotPrice = BookingRules.CalculatePrice(court.TimeSlots, time, slotEnd);
                bool isPeak = court.TimeSlots.Any()
                    ? court.TimeSlots.Any(t => t.IsPeakHour && t.StartTime < slotEnd && t.EndTime > time)
                    : time.Hours >= 17 && time.Hours <= 20;

                var matrixSlot = new TimeSlotMatrixDto
                {
                    SlotIndex = slotIndex++,
                    StartTime = time,
                    EndTime = slotEnd,
                    Price = slotPrice,
                    IsPeakHour = isPeak,
                    Availability = SlotAvailability.Available
                };

                if (date < today || (date == today && slotEnd <= currentTime))
                {
                    matrixSlot.Availability = SlotAvailability.PastTime;
                }
                else
                {
                    var matchedBooking = bookings.FirstOrDefault(b => time < b.EndTime && slotEnd > b.StartTime);

                    if (matchedBooking != null)
                    {
                        matrixSlot.BookingId = matchedBooking.Id;
                        matrixSlot.BookedByCustomerName = matchedBooking.Customer?.FullName ?? "Đã đặt";
                        matrixSlot.BookingStatus = matchedBooking.Status;

                        // Còn đang giữ chỗ chờ thanh toán => PendingPayment; còn lại (đã xác nhận/đã trả tiền) => Booked
                        bool isHolding = matchedBooking.Status == BookingStatus.Pending
                                         && matchedBooking.PaymentStatus != PaymentStatus.Paid;
                        matrixSlot.Availability = isHolding ? SlotAvailability.PendingPayment : SlotAvailability.Booked;
                    }
                }

                slots.Add(matrixSlot);
            }

            // Lấy các sân khác trong cùng cụm để khách tiện chuyển đổi sân
            var otherCourts = await _unitOfWork.Courts.Query()
                .Where(c => c.FacilityId == court.FacilityId && c.IsActive)
                .ToListAsync();

            // Lấy danh sách dịch vụ tiện ích có thể đặt kèm tại cụm sân
            var availableServices = await _unitOfWork.FacilityServices.Query()
                .Where(s => s.FacilityId == court.FacilityId && s.IsActive)
                .ToListAsync();

            return new CourtScheduleMatrixViewModel
            {
                FacilityId = court.FacilityId,
                FacilityName = court.Facility?.Name ?? string.Empty,
                FacilityAddress = court.Facility?.Address ?? string.Empty,
                CourtId = court.Id,
                CourtName = court.Name,
                SportTypeName = court.Facility?.SportType?.Name ?? "Thể thao",
                SelectedDate = date,
                Slots = slots,
                OtherCourtsInFacility = otherCourts,
                AvailableServices = availableServices
            };
        }

        /// <summary>
        /// Lấy bảng biểu thời gian (Timeline Schedule Board) của toàn bộ các sân trong cụm
        /// </summary>
        public async Task<FacilityTimelineScheduleViewModel> GetFacilityTimelineScheduleAsync(int facilityId, DateOnly date)
        {
            var facility = await _unitOfWork.Facilities.Query()
                .Include(f => f.Courts)
                .Include(f => f.SportType)
                .FirstOrDefaultAsync(f => f.Id == facilityId);

            if (facility == null)
            {
                throw new ArgumentException("Cụm sân không tồn tại.");
            }

            // Chỉ lấy các đơn còn hiệu lực (đã loại đơn hủy và đơn giữ chỗ quá hạn)
            var bookings = await _unitOfWork.Bookings.GetBookingsByFacilityAndDateAsync(facilityId, date);

            var courtBookings = new Dictionary<int, List<Booking>>();
            foreach (var court in facility.Courts)
            {
                courtBookings[court.Id] = bookings.Where(b => b.CourtId == court.Id).ToList();
            }

            var timelineHours = BookingRules.HourBlocks(facility).Select(b => b.Start).ToList();

            return new FacilityTimelineScheduleViewModel
            {
                Facility = facility,
                SelectedDate = date,
                TimelineHours = timelineHours,
                CourtBookings = courtBookings
            };
        }

        /// <summary>
        /// Kiểm tra đơn còn được phép thanh toán hay không.
        /// </summary>
        public async Task<BookingResult> CheckPayableAsync(int bookingId)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            return await EvaluatePayableAsync(booking);
        }

        /// <summary>
        /// Đơn chỉ được thanh toán khi: chưa thanh toán, chưa bị hủy / hoàn thành,
        /// và (đã được chủ sân xác nhận, hoặc vẫn còn trong thời hạn giữ chỗ).
        /// Đơn Pending quá hạn sẽ bị hủy ngay tại đây.
        /// </summary>
        private async Task<BookingResult> EvaluatePayableAsync(Booking? booking)
        {
            if (booking == null)
            {
                return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            }

            if (booking.PaymentStatus == PaymentStatus.Paid)
            {
                return BookingResult.Failure($"Đơn #BK-{booking.Id} đã được thanh toán trước đó.", booking);
            }

            if (booking.PaymentStatus == PaymentStatus.AwaitingVerification)
            {
                return BookingResult.Failure($"Đơn #BK-{booking.Id} đã gửi biên lai và đang chờ chủ sân xác nhận.", booking);
            }

            if (booking.Status == BookingStatus.Cancelled)
            {
                return BookingResult.Failure($"Đơn #BK-{booking.Id} đã bị hủy nên không thể thanh toán. Vui lòng đặt lại khung giờ khác.", booking);
            }

            if (booking.Status == BookingStatus.Completed)
            {
                return BookingResult.Failure($"Đơn #BK-{booking.Id} đã hoàn thành.", booking);
            }

            if (BookingRules.IsHoldExpired(booking, DateTime.UtcNow))
            {
                booking.Status = BookingStatus.Cancelled;
                booking.PaymentNote ??= ExpiredHoldNote;
                try
                {
                    await _unitOfWork.CompleteAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Job dọn đơn quá hạn vừa hủy đơn này trước — kết quả như nhau, chỉ cần nạp lại trạng thái.
                    await _unitOfWork.Context.Entry(booking).ReloadAsync();
                }

                return BookingResult.Failure(
                    $"Đơn giữ chỗ #BK-{booking.Id} đã quá thời hạn {BookingRules.HoldMinutes} phút và đã bị hủy. Khung giờ này đã được mở lại cho người khác.",
                    booking);
            }

            return BookingResult.Success(booking, string.Empty);
        }

        /// <summary>
        /// Khách gửi biên lai chuyển khoản: đơn chuyển sang "Chờ xác nhận chuyển khoản" (vẫn giữ khung giờ),
        /// chủ sân kiểm tra tài khoản rồi bấm Xác nhận (→ Đã thanh toán) hoặc Từ chối (→ hủy đơn).
        /// </summary>
        public async Task<BookingResult> SubmitBankTransferProofAsync(int bookingId, string? proofImageUrl, string? transactionRef, string? paymentMethod = "VirtualBank", string? note = null)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            var check = await EvaluatePayableAsync(booking);
            if (!check.IsSuccess) return check;

            booking!.PaymentStatus = PaymentStatus.AwaitingVerification;
            booking.PaymentMethod = string.IsNullOrEmpty(paymentMethod) ? "Chuyển khoản" : paymentMethod;
            booking.PaymentProofImage = proofImageUrl;
            booking.PaymentTransactionRef = string.IsNullOrEmpty(transactionRef) ? $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}" : transactionRef;
            if (!string.IsNullOrEmpty(note)) booking.PaymentNote = note;

            try
            {
                await _unitOfWork.CompleteAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await _unitOfWork.Context.Entry(booking).ReloadAsync();
                return BookingResult.Failure("Đơn đặt sân vừa thay đổi trạng thái (có thể đã hết hạn giữ chỗ). Vui lòng kiểm tra lại.", booking);
            }

            await _notifications.NotifyFacilityOwnerAsync(booking.Court!.FacilityId,
                $"Cần xác nhận chuyển khoản #BK-{booking.Id}",
                $"{booking.CustomerName} báo đã chuyển {booking.TotalAmount:N0}đ (mã GD {booking.PaymentTransactionRef}). Vui lòng kiểm tra tài khoản và xác nhận.",
                $"/Owner/Facility/Bookings?facilityId={booking.Court.FacilityId}&status=verify",
                "bi-shield-exclamation", "violet");

            _logger.LogInformation("Khách gửi biên lai cho đơn {BookingId}, mã GD {Ref}", bookingId, booking.PaymentTransactionRef);
            return BookingResult.Success(booking,
                "Đã gửi biên lai chuyển khoản! Khung giờ được giữ cho bạn trong lúc chủ sân kiểm tra và xác nhận thanh toán.");
        }

        /// <summary>Chủ sân xác nhận (approve = true) hoặc từ chối biên lai chuyển khoản của khách.</summary>
        public async Task<BookingResult> VerifyPaymentAsync(int bookingId, bool approve, string? reason)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null) return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            if (booking.PaymentStatus != PaymentStatus.AwaitingVerification || booking.Status == BookingStatus.Cancelled)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} không ở trạng thái chờ xác nhận chuyển khoản.");
            }

            if (approve)
            {
                booking.PaymentStatus = PaymentStatus.Paid;
                booking.Status = BookingStatus.Confirmed;
                booking.PaidAt = DateTime.UtcNow;
            }
            else
            {
                booking.PaymentStatus = PaymentStatus.Unpaid;
                booking.Status = BookingStatus.Cancelled;
                booking.CancelledAt = DateTime.UtcNow;
                booking.PaymentNote = "Chủ sân từ chối biên lai: " + (string.IsNullOrWhiteSpace(reason) ? "không nhận được tiền chuyển khoản" : reason.Trim());
            }

            try
            {
                await _unitOfWork.CompleteAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                return BookingResult.Failure("Đơn đặt sân vừa được cập nhật ở nơi khác. Vui lòng tải lại trang.");
            }

            var where = $"{booking.Court?.Facility?.Name} · {booking.Court?.Name} · {booking.BookingDate:dd/MM} {BookingRules.Hm(booking.StartTime)}–{BookingRules.Hm(booking.EndTime)}";
            if (approve)
            {
                await _notifications.NotifyUserAsync(booking.CustomerId, $"Thanh toán đơn #BK-{booking.Id} đã được xác nhận",
                    $"Chủ sân đã nhận {booking.TotalAmount:N0}đ. Hẹn gặp bạn tại {where}.",
                    $"/Booking/Confirmation?bookingId={booking.Id}", "bi-patch-check", "green", sendEmail: true);
                return BookingResult.Success(booking, $"Đã xác nhận thanh toán đơn #BK-{bookingId}.");
            }

<<<<<<< HEAD
            await CloseLinkedMatchPostsAsync(booking);
=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            await _notifications.NotifyUserAsync(booking.CustomerId, $"Biên lai đơn #BK-{booking.Id} bị từ chối",
                $"{booking.PaymentNote}. Đơn đã bị hủy ({where}). Nếu bạn đã chuyển tiền, vui lòng liên hệ chủ sân.",
                $"/Booking/Confirmation?bookingId={booking.Id}", "bi-x-octagon", "red", sendEmail: true);
            return BookingResult.Success(booking, $"Đã từ chối biên lai và hủy đơn #BK-{bookingId}.");
        }

        /// <summary>
        /// Hủy đơn đặt sân. Khách chỉ tự hủy được trước giờ thi đấu; chủ cụm sân / Admin hủy được mọi đơn chưa hoàn thành.
        /// Đơn đã trả tiền (hoặc đã gửi biên lai) khi hủy sẽ chuyển sang "Chờ hoàn tiền" để chủ sân hoàn lại.
        /// </summary>
        public async Task<BookingResult> CancelBookingAsync(int bookingId, string userId, string? reason = null)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null)
            {
                return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            }

            bool isAdmin = await _unitOfWork.Context.Users.AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin);
            bool isOwner = isAdmin || (booking.Court?.Facility?.OwnerId != null && booking.Court.Facility.OwnerId == userId);
            bool isCustomer = booking.CustomerId == userId;

            if (!isOwner && !isCustomer)
            {
                return BookingResult.Failure("Bạn không có quyền hủy đơn đặt sân này.");
            }

            if (booking.Status == BookingStatus.Cancelled)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} đã được hủy trước đó.");
            }

            if (booking.Status == BookingStatus.Completed)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} đã hoàn thành, không thể hủy.");
            }

<<<<<<< HEAD
            // Khách tự hủy: được hủy bất cứ lúc nào trước giờ đá; hủy sát giờ (dưới
            // BookingRules.FreeCancelHours tiếng) chỉ được hoàn một phần. Chủ cụm sân không bị giới hạn.
=======
            // Khách tự hủy: không hủy khi đã đến giờ đá; đơn đã xác nhận / đã thanh toán phải hủy trước
            // ít nhất BookingRules.FreeCancelHours tiếng. Chủ cụm sân không bị giới hạn.
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            if (!isOwner)
            {
                var blockReason = BookingRules.GetCustomerCancelBlockReason(booking, DateTime.Now);
                if (blockReason != null)
                {
                    return BookingResult.Failure(blockReason);
                }
            }

            bool byCustomer = isCustomer && !isOwner;
<<<<<<< HEAD
            int refundPercent = byCustomer ? BookingRules.CustomerRefundPercent(booking, DateTime.Now) : 100;
            bool needsRefund = ApplyCancellation(booking, reason, cancelledByOwner: !byCustomer, refundPercent);
=======
            bool needsRefund = ApplyCancellation(booking, reason, cancelledByOwner: !byCustomer);
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            try
            {
                await _unitOfWork.CompleteAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                return BookingResult.Failure("Đơn đặt sân vừa được cập nhật ở nơi khác. Vui lòng tải lại trang và thử lại.");
            }

<<<<<<< HEAD
            await CloseLinkedMatchPostsAsync(booking);
=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            await NotifyCancellationAsync(booking, byCustomer, needsRefund);

            var message = needsRefund
                ? $"Đã hủy đơn #BK-{bookingId}. Khoản {booking.RefundAmount:N0}đ đang chờ chủ sân hoàn lại — theo dõi trạng thái hoàn tiền trong Lịch đặt của tôi."
                : $"Đã hủy đơn đặt sân #BK-{bookingId} thành công!";
            return BookingResult.Success(booking, message);
        }

<<<<<<< HEAD
        /// <summary>
        /// Đơn đặt sân bị hủy → đóng các kèo ghép đội tạo từ đơn đó (không còn sân để chơi)
        /// và báo cho những người đã xin / đã được nhận vào kèo.
        /// </summary>
        private async Task CloseLinkedMatchPostsAsync(Booking booking)
        {
            var posts = await _unitOfWork.Context.MatchPosts
                .Include(p => p.JoinRequests)
                .Where(p => p.BookingId == booking.Id
                            && (p.Status == MatchPostStatus.Open || p.Status == MatchPostStatus.Full))
                .ToListAsync();
            if (posts.Count == 0) return;

            foreach (var p in posts) p.Status = MatchPostStatus.Cancelled;
            await _unitOfWork.CompleteAsync();

            foreach (var p in posts)
            {
                foreach (var r in p.JoinRequests.Where(r => r.Status != JoinRequestStatus.Rejected))
                {
                    await _notifications.NotifyUserAsync(r.UserId, $"Kèo \"{p.Title}\" đã bị hủy",
                        $"Đơn đặt sân của kèo ngày {p.MatchDate:dd/MM} {BookingRules.Hm(p.StartTime)} đã bị hủy nên kèo không còn diễn ra.",
                        $"/Teamwork/Detail/{p.Id}", "bi-calendar-x", "red");
                }
            }
        }

        /// <summary>Đánh dấu hủy; nếu khách đã trả tiền thì tạo yêu cầu hoàn tiền. Trả về true nếu cần hoàn tiền.</summary>
        private static bool ApplyCancellation(Booking booking, string? reason, bool cancelledByOwner, int refundPercent = 100)
=======
        /// <summary>Đánh dấu hủy; nếu khách đã trả tiền thì tạo yêu cầu hoàn tiền. Trả về true nếu cần hoàn tiền.</summary>
        private static bool ApplyCancellation(Booking booking, string? reason, bool cancelledByOwner)
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                booking.PaymentNote = (cancelledByOwner ? "Chủ sân hủy: " : "Khách hủy: ") + reason.Trim();
            }

            if (booking.PaymentStatus == PaymentStatus.Paid || booking.PaymentStatus == PaymentStatus.AwaitingVerification)
            {
                booking.PaymentStatus = PaymentStatus.RefundPending;
<<<<<<< HEAD
                // Hủy đúng hạn / do chủ sân hủy → hoàn 100%; khách hủy sát giờ → hoàn theo LateCancelRefundPercent
                booking.RefundAmount = BookingRules.RefundAmountFor(booking.TotalAmount, refundPercent);
                if (refundPercent < 100)
                {
                    var tag = $"[Hủy trong vòng {BookingRules.FreeCancelHours} tiếng trước giờ chơi: hoàn {refundPercent}%]";
                    var note = (booking.PaymentNote ?? "").Trim();
                    if (note.Length + tag.Length + 1 > 500) note = note[..Math.Max(0, 500 - tag.Length - 2)] + "…";
                    booking.PaymentNote = (note + " " + tag).Trim();
                }
=======
                booking.RefundAmount = booking.TotalAmount; // Hủy đúng hạn / do chủ sân hủy → hoàn 100%
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                return true;
            }
            return false;
        }

        private async Task NotifyCancellationAsync(Booking booking, bool cancelledByCustomer, bool needsRefund)
        {
            var where = $"{booking.Court?.Name} · {booking.BookingDate:dd/MM} {BookingRules.Hm(booking.StartTime)}–{BookingRules.Hm(booking.EndTime)}";
            if (cancelledByCustomer)
            {
                if (booking.Court != null)
                {
                    await _notifications.NotifyFacilityOwnerAsync(booking.Court.FacilityId,
                        $"Khách hủy đơn #BK-{booking.Id}",
                        $"{booking.CustomerName} đã hủy {where}." + (needsRefund ? $" Cần hoàn {booking.RefundAmount:N0}đ cho khách." : ""),
                        needsRefund ? $"/Owner/Facility/Refunds?facilityId={booking.Court.FacilityId}" : $"/Owner/Facility/Bookings?facilityId={booking.Court.FacilityId}&q={booking.Id}",
                        "bi-calendar-x", needsRefund ? "amber" : "red");
                }
            }
            else
            {
                await _notifications.NotifyUserAsync(booking.CustomerId, $"Đơn #BK-{booking.Id} đã bị chủ sân hủy",
                    $"{booking.Court?.Facility?.Name} · {where}." + (needsRefund ? $" Bạn sẽ được hoàn {booking.RefundAmount:N0}đ." : "") +
                    (string.IsNullOrEmpty(booking.PaymentNote) ? "" : $" {booking.PaymentNote}"),
                    $"/Booking/Confirmation?bookingId={booking.Id}", "bi-calendar-x", "red", sendEmail: true);
            }
        }

        /// <summary>Chủ sân xác nhận đã hoàn tiền cho khách.</summary>
        public async Task<BookingResult> MarkRefundedAsync(int bookingId, string? note)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null) return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            if (booking.PaymentStatus != PaymentStatus.RefundPending)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} không có yêu cầu hoàn tiền đang chờ.");
            }

            booking.PaymentStatus = PaymentStatus.Refunded;
            booking.RefundedAt = DateTime.UtcNow;
            booking.RefundNote = string.IsNullOrWhiteSpace(note) ? "Đã hoàn tiền qua chuyển khoản" : note.Trim();
            await _unitOfWork.CompleteAsync();

            await _notifications.NotifyUserAsync(booking.CustomerId, $"Đã hoàn {booking.RefundAmount:N0}đ cho đơn #BK-{booking.Id}",
                $"{booking.Court?.Facility?.Name}: {booking.RefundNote}.",
                $"/Booking/Confirmation?bookingId={booking.Id}", "bi-cash-stack", "green", sendEmail: true);
            return BookingResult.Success(booking, $"Đã ghi nhận hoàn tiền cho đơn #BK-{bookingId}.");
        }

        /// <summary>
        /// Chi tiết đơn đặt sân
        /// </summary>
        public async Task<Booking?> GetBookingByIdAsync(int bookingId)
        {
            return await _unitOfWork.Bookings.GetBookingDetailAsync(bookingId);
        }

        /// <summary>
        /// Lấy danh sách đơn đặt sân của khách hàng
        /// </summary>
        public async Task<List<Booking>> GetCustomerBookingsAsync(string customerId)
        {
            return await _unitOfWork.Bookings.GetCustomerBookingsAsync(customerId);
        }

        /// <summary>
        /// Cập nhật trạng thái đơn đặt sân (dành cho Chủ sân duyệt / xác nhận thu tiền mặt).
        /// Nếu đơn đang KHÔNG còn hiệu lực (đã hủy / giữ chỗ quá hạn) mà được kích hoạt lại,
        /// phải kiểm tra trùng lịch trước để không đè lên đơn của người khác.
        /// </summary>
        public async Task<BookingResult> UpdateBookingStatusAsync(int bookingId, BookingStatus status, PaymentStatus? paymentStatus = null)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null)
            {
                return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            }

<<<<<<< HEAD
            // Màn hình chủ sân chỉ được xác nhận "đã thu tiền"; các trạng thái hoàn tiền đi qua luồng Hủy / Hoàn tiền riêng
            if (paymentStatus.HasValue && paymentStatus.Value != PaymentStatus.Paid)
            {
                return BookingResult.Failure("Trạng thái thanh toán không hợp lệ.");
            }

            if (booking.Status == BookingStatus.Cancelled && status != BookingStatus.Cancelled
                && booking.PaymentStatus == PaymentStatus.Refunded)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} đã hoàn tiền cho khách nên không thể mở lại. Vui lòng tạo đơn mới bằng Đặt tại quầy.");
            }

            if (status == BookingStatus.Completed && booking.Status != BookingStatus.Completed
                && BookingRules.StartsAt(booking) > DateTime.Now)
            {
                return BookingResult.Failure($"Đơn #BK-{bookingId} chưa tới giờ chơi nên chưa thể đánh dấu hoàn thành.");
            }

=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            var now = DateTime.UtcNow;
            bool wasActive = BookingRules.IsActiveNow(booking, now);
            bool wasCancelled = booking.Status == BookingStatus.Cancelled;
            bool needsRefund = false;

            if (status == BookingStatus.Cancelled && !wasCancelled)
            {
                // Chủ sân hủy: dùng chung quy tắc hoàn tiền với luồng hủy thông thường
                needsRefund = ApplyCancellation(booking, null, cancelledByOwner: true);
            }
            else
            {
                booking.Status = status;
                // Kích hoạt lại đơn đã hủy đang chờ hoàn tiền -> trở lại trạng thái đã thanh toán
                if (wasCancelled && status != BookingStatus.Cancelled && booking.PaymentStatus == PaymentStatus.RefundPending)
                {
                    booking.PaymentStatus = PaymentStatus.Paid;
                    booking.RefundAmount = null;
                    booking.CancelledAt = null;
                }
            }
            if (paymentStatus.HasValue)
            {
                booking.PaymentStatus = paymentStatus.Value;
                if (paymentStatus.Value == PaymentStatus.Paid && booking.PaidAt == null)
                {
                    booking.PaidAt = now;
                    booking.PaymentMethod ??= "Tiền mặt tại quầy";
                }
            }

            bool isActiveAfter = BookingRules.IsActiveNow(booking, now);

            IDbContextTransaction? transaction = null;
            try
            {
                if (!wasActive && isActiveAfter)
                {
                    transaction = await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

                    bool hasOverlap = await _unitOfWork.Bookings.HasOverlappingBookingAsync(
                        booking.CourtId, booking.BookingDate, booking.StartTime, booking.EndTime, excludeBookingId: booking.Id);

                    if (hasOverlap)
                    {
                        _unitOfWork.Context.ChangeTracker.Clear();
                        return BookingResult.Failure($"Không thể kích hoạt lại đơn #BK-{bookingId} vì khung giờ này đã có đơn khác.");
                    }
                }

                await _unitOfWork.CompleteAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch (Exception ex) when (IsConcurrencyConflict(ex))
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                return BookingResult.Failure("Đơn đặt sân vừa được cập nhật ở nơi khác. Vui lòng tải lại trang và thử lại.");
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }

            _logger.LogInformation("Cập nhật đơn đặt sân {BookingId}: Status={Status}, Payment={Payment}",
                bookingId, status, paymentStatus);

            if (status == BookingStatus.Cancelled && !wasCancelled)
            {
<<<<<<< HEAD
                await CloseLinkedMatchPostsAsync(booking);
=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                await NotifyCancellationAsync(booking, cancelledByCustomer: false, needsRefund);
                return BookingResult.Success(booking, needsRefund
                    ? $"Đã hủy đơn #BK-{bookingId}. Đơn đã thanh toán nên được chuyển sang mục Hoàn tiền ({booking.RefundAmount:N0}đ)."
                    : $"Đã hủy đơn #BK-{bookingId}.");
            }
            return BookingResult.Success(booking, $"Cập nhật đơn đặt sân #BK-{bookingId} thành công!");
        }

        /// <summary>
        /// Hủy toàn bộ đơn giữ chỗ quá hạn chưa thanh toán (chạy định kỳ bởi <see cref="ExpiredBookingCleanupService"/>).
        /// </summary>
        public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default)
        {
            var threshold = BookingRules.HoldThreshold(DateTime.UtcNow);
            var note = ExpiredHoldNote;

<<<<<<< HEAD
            int released = await _unitOfWork.Context.Bookings
=======
            return await _unitOfWork.Context.Bookings
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                .Where(b => b.Status == BookingStatus.Pending
                         && b.PaymentStatus == PaymentStatus.Unpaid
                         && b.CreatedAt < threshold)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.PaymentNote, b => b.PaymentNote ?? note),
                    cancellationToken);
<<<<<<< HEAD

            // Kèo ghép đội gắn với đơn đã bị hủy (kể cả đơn giữ chỗ vừa hết hạn) → đóng kèo
            await _unitOfWork.Context.MatchPosts
                .Where(p => p.Booking != null && p.Booking.Status == BookingStatus.Cancelled
                            && (p.Status == MatchPostStatus.Open || p.Status == MatchPostStatus.Full))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, MatchPostStatus.Cancelled), cancellationToken);

            return released;
=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
        }
    }
}
