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

        public BookingService(IUnitOfWork unitOfWork, ILogger<BookingService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
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

            // Kiểm tra khung giờ nằm trong giờ mở/đóng cửa của cụm sân
            if (court.Facility != null
                && (dto.StartTime < court.Facility.OpeningTime || dto.EndTime > court.Facility.ClosingTime))
            {
                return BookingResult.Failure($"Khung giờ đặt phải nằm trong giờ hoạt động của sân ({court.Facility.OpeningTime:hh\\:mm} - {court.Facility.ClosingTime:hh\\:mm}).");
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
                    CustomerId = dto.CustomerId ?? "demo-customer-id",
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
            var openTime = facility?.OpeningTime ?? new TimeSpan(6, 0, 0);
            var closeTime = facility?.ClosingTime ?? new TimeSpan(22, 0, 0);

            // Chỉ lấy các đơn còn hiệu lực (đã loại đơn hủy và đơn giữ chỗ quá hạn)
            var bookings = await _unitOfWork.Bookings.GetBookingsByCourtAndDateAsync(courtId, date);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentTime = DateTime.Now.TimeOfDay;

            var slots = new List<TimeSlotMatrixDto>();
            int slotIndex = 1;

            // Duyệt từng block 1 tiếng từ giờ mở cửa đến giờ đóng cửa
            for (var time = openTime; time < closeTime; time = time.Add(TimeSpan.FromHours(1)))
            {
                var slotEnd = time.Add(TimeSpan.FromHours(1));
                if (slotEnd > closeTime) slotEnd = closeTime;

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

            var timelineHours = new List<TimeSpan>();
            for (var time = facility.OpeningTime; time < facility.ClosingTime; time = time.Add(TimeSpan.FromHours(1)))
            {
                timelineHours.Add(time);
            }

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
        /// Ghi nhận thanh toán cho đơn sau khi đã kiểm tra hợp lệ. RowVersion đảm bảo không ghi đè
        /// nếu đơn vừa bị hủy (do hết hạn) ở một request khác.
        /// </summary>
        private async Task<BookingResult> MarkAsPaidAsync(Booking booking, Action<Booking> applyPayment, string successMessage)
        {
            booking.PaymentStatus = PaymentStatus.Paid;
            booking.Status = BookingStatus.Confirmed;
            booking.PaidAt = DateTime.UtcNow;
            applyPayment(booking);

            try
            {
                await _unitOfWork.CompleteAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                var entry = _unitOfWork.Context.Entry(booking);
                await entry.ReloadAsync();
                _logger.LogWarning("Thanh toán đơn {BookingId} thất bại do đơn vừa thay đổi trạng thái ({Status})", booking.Id, booking.Status);
                return BookingResult.Failure("Đơn đặt sân vừa thay đổi trạng thái (có thể đã hết hạn giữ chỗ). Vui lòng kiểm tra lại.", booking);
            }

            return BookingResult.Success(booking, successMessage);
        }

        /// <summary>
        /// Xử lý thanh toán giả lập đơn đặt sân
        /// </summary>
        public async Task<BookingResult> ProcessPaymentAsync(int bookingId, string paymentMethod = "DemoGateway")
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            var check = await EvaluatePayableAsync(booking);
            if (!check.IsSuccess) return check;

            var result = await MarkAsPaidAsync(booking!, b =>
            {
                b.PaymentMethod = paymentMethod;
                if (string.IsNullOrEmpty(b.PaymentTransactionRef))
                {
                    b.PaymentTransactionRef = $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}";
                }
            }, "Thanh toán thành công! Sân đã được xác nhận giữ chỗ cho bạn.");

            if (result.IsSuccess)
            {
                _logger.LogInformation("Xử lý thanh toán thành công cho đơn {BookingId} bằng phương thức {Method}", bookingId, paymentMethod);
            }
            return result;
        }

        /// <summary>
        /// Xác nhận chuyển khoản ngân hàng ảo kèm ảnh minh chứng giao dịch
        /// </summary>
        public async Task<BookingResult> SubmitBankTransferProofAsync(int bookingId, string? proofImageUrl, string? transactionRef, string? paymentMethod = "VirtualBank", string? note = null)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            var check = await EvaluatePayableAsync(booking);
            if (!check.IsSuccess) return check;

            var result = await MarkAsPaidAsync(booking!, b =>
            {
                b.PaymentMethod = string.IsNullOrEmpty(paymentMethod) ? "VirtualBank" : paymentMethod;
                b.PaymentProofImage = proofImageUrl;
                b.PaymentTransactionRef = string.IsNullOrEmpty(transactionRef) ? $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}" : transactionRef;
                if (!string.IsNullOrEmpty(note))
                {
                    b.PaymentNote = note;
                }
            }, "Thanh toán chuyển khoản thành công! Sân đã được xác nhận giữ chỗ cho bạn.");

            if (result.IsSuccess)
            {
                _logger.LogInformation("Xác nhận chuyển khoản ngân hàng ảo cho đơn {BookingId}, mã GD: {Ref}, ảnh: {Proof}",
                    bookingId, booking!.PaymentTransactionRef, proofImageUrl);
            }
            return result;
        }

        /// <summary>
        /// Hủy đơn đặt sân. Khách chỉ tự hủy được trước giờ thi đấu; chủ cụm sân hủy được mọi đơn chưa hoàn thành.
        /// </summary>
        public async Task<BookingResult> CancelBookingAsync(int bookingId, string userId)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null)
            {
                return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            }

            var facilityOwnerId = await _unitOfWork.Courts.Query()
                .Where(c => c.Id == booking.CourtId)
                .Select(c => c.Facility!.OwnerId)
                .FirstOrDefaultAsync();

            bool isOwner = facilityOwnerId != null && facilityOwnerId == userId;
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

            // Khách tự hủy: không hủy khi đã đến giờ đá; đơn đã xác nhận / đã thanh toán phải hủy trước
            // ít nhất BookingRules.FreeCancelHours tiếng. Chủ cụm sân không bị giới hạn.
            if (!isOwner)
            {
                var blockReason = BookingRules.GetCustomerCancelBlockReason(booking, DateTime.Now);
                if (blockReason != null)
                {
                    return BookingResult.Failure(blockReason);
                }
            }

            booking.Status = BookingStatus.Cancelled;
            try
            {
                await _unitOfWork.CompleteAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _unitOfWork.Context.ChangeTracker.Clear();
                return BookingResult.Failure("Đơn đặt sân vừa được cập nhật ở nơi khác. Vui lòng tải lại trang và thử lại.");
            }

            var message = booking.PaymentStatus == PaymentStatus.Paid
                ? $"Đã hủy đơn đặt sân #BK-{bookingId}. Đơn đã thanh toán — vui lòng liên hệ chủ sân để được hỗ trợ hoàn tiền."
                : $"Đã hủy đơn đặt sân #BK-{bookingId} thành công!";
            return BookingResult.Success(booking, message);
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
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null)
            {
                return BookingResult.Failure("Đơn đặt sân không tồn tại.");
            }

            var now = DateTime.UtcNow;
            bool wasActive = BookingRules.IsActiveNow(booking, now);

            booking.Status = status;
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
            return BookingResult.Success(booking, $"Cập nhật đơn đặt sân #BK-{bookingId} thành công!");
        }

        /// <summary>
        /// Hủy toàn bộ đơn giữ chỗ quá hạn chưa thanh toán (chạy định kỳ bởi <see cref="ExpiredBookingCleanupService"/>).
        /// </summary>
        public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default)
        {
            var threshold = BookingRules.HoldThreshold(DateTime.UtcNow);
            var note = ExpiredHoldNote;

            return await _unitOfWork.Context.Bookings
                .Where(b => b.Status == BookingStatus.Pending
                         && b.PaymentStatus == PaymentStatus.Unpaid
                         && b.CreatedAt < threshold)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.PaymentNote, b => b.PaymentNote ?? note),
                    cancellationToken);
        }
    }
}
