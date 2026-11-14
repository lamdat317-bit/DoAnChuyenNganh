using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.DTOs;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DACN.Services
{
    public class BookingService : IBookingService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<BookingService> _logger;

        public BookingService(IUnitOfWork unitOfWork, ILogger<BookingService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// Tạo đơn đặt sân với cơ chế bảo vệ Race Condition & Double Booking bằng Serializable Transaction.
        /// </summary>
        public async Task<BookingResult> CreateBookingAsync(BookingDto dto)
        {
            // 1. Validation thời gian cơ bản
            var today = DateOnly.FromDateTime(DateTime.Now);
            var nowTime = DateTime.Now.TimeOfDay;

            if (dto.BookingDate < today)
            {
                return BookingResult.Failure("Không thể đặt sân cho ngày trong quá khứ.");
            }

            // Kiểm tra khung giờ: Chỉ từ chối nếu toàn bộ khung giờ đã kết thúc trong ngày hôm nay
            if (dto.BookingDate == today && dto.EndTime <= nowTime)
            {
                return BookingResult.Failure("Khung giờ này đã kết thúc trong ngày hôm nay. Vui lòng chọn khung giờ tiếp theo.");
            }

            if (dto.StartTime >= dto.EndTime)
            {
                return BookingResult.Failure("Giờ bắt đầu phải nhỏ hơn giờ kết thúc.");
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
            if (court.Facility != null)
            {
                if (dto.StartTime < court.Facility.OpeningTime || dto.EndTime > court.Facility.ClosingTime)
                {
                    return BookingResult.Failure($"Khung giờ đặt phải nằm trong giờ hoạt động của sân ({court.Facility.OpeningTime:hh\\:mm} - {court.Facility.ClosingTime:hh\\:mm}).");
                }
            }

            // 3. Tính toán tổng tiền dựa trên cấu hình TimeSlots của sân
            decimal totalAmount = CalculatePrice(court, dto.StartTime, dto.EndTime);

            // 4. BẮT ĐẦU TRANSACTION VỚI ISOLATION LEVEL SERIALIZABLE (CHỐNG TRÙNG LỊCH TUYỆT ĐỐI)
            await using var transaction = await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                // Quy tắc giữ chỗ: Tối đa 5 phút kể từ thời điểm tạo đơn.
                var holdThreshold = DateTime.UtcNow.AddMinutes(-5);

                // Tự động giải phóng các đơn giữ chỗ Pending đã quá 5 phút chưa thanh toán
                var expiredBookings = await _unitOfWork.Context.Bookings
                    .Where(b => b.CourtId == dto.CourtId
                             && b.BookingDate == dto.BookingDate
                             && b.Status == BookingStatus.Pending
                             && b.PaymentStatus == PaymentStatus.Unpaid
                             && b.CreatedAt < holdThreshold)
                    .ToListAsync();

                if (expiredBookings.Any())
                {
                    foreach (var exp in expiredBookings)
                    {
                        exp.Status = BookingStatus.Cancelled;
                        exp.PaymentNote = (exp.PaymentNote ?? "") + " [Tự động hủy: Quá 5 phút giữ chỗ]";
                    }
                    await _unitOfWork.CompleteAsync();
                }

                // LINQ Query: Kiểm tra xung đột thời gian (Interval Overlap check)
                // Chỉ chặn nếu:
                // 1) Đã thanh toán (PaymentStatus == Paid hoặc Confirmed)
                // 2) Đang giữ chỗ Pending trong vòng 5 phút (CreatedAt >= holdThreshold)
                bool hasOverlap = await _unitOfWork.Context.Bookings
                    .AnyAsync(b => b.CourtId == dto.CourtId
                                && b.BookingDate == dto.BookingDate
                                && b.Status != BookingStatus.Cancelled
                                && (b.PaymentStatus == PaymentStatus.Paid || b.CreatedAt >= holdThreshold)
                                && dto.StartTime < b.EndTime
                                && dto.EndTime > b.StartTime);

                if (hasOverlap)
                {
                    await transaction.RollbackAsync();
                    _logger.LogWarning("Phát hiện xung đột đặt sân: Court {CourtId}, Date {Date}, Time {Start}-{End}",
                        dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
                    return BookingResult.Failure("Rất tiếc! Khung giờ này vừa có người đặt hoặc đang trong 5 phút giữ chỗ thanh toán. Vui lòng chọn khung giờ khác.");
                }

                // Tính tiền dịch vụ tiện ích đính kèm (nếu khách chọn)
                decimal servicesTotal = 0;
                var serviceDetails = new List<BookingServiceDetail>();
                if (dto.SelectedServiceIds != null && dto.SelectedServiceIds.Any())
                {
                    var services = await _unitOfWork.FacilityServices.Query()
                        .Where(s => dto.SelectedServiceIds.Contains(s.Id) && s.FacilityId == court.FacilityId && s.IsActive)
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

                // Khởi tạo Entity Booking mới
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
                    TotalAmount = totalAmount + servicesTotal,
                    Status = BookingStatus.Pending,
                    PaymentStatus = PaymentStatus.Unpaid,
                    CreatedAt = DateTime.UtcNow,
                    ServiceDetails = serviceDetails
                };

                await _unitOfWork.Bookings.AddAsync(booking);
                await _unitOfWork.CompleteAsync();

                // Xác nhận giao dịch hoàn tất thành công
                await transaction.CommitAsync();

                _logger.LogInformation("Đặt sân thành công! BookingId: {BookingId}, Court: {CourtId}, Total: {Total}",
                    booking.Id, booking.CourtId, booking.TotalAmount);

                return BookingResult.Success(booking, "Đặt sân thành công! Vui lòng tiến hành thanh toán để giữ chỗ.");
            }
            catch (DbUpdateConcurrencyException concEx)
            {
                await transaction.RollbackAsync();
                _logger.LogError(concEx, "Xung đột đồng thời (Optimistic Concurrency) khi đặt sân {CourtId}", dto.CourtId);
                return BookingResult.Failure("Xung đột phiên đồng thời: Lịch đặt sân đã thay đổi trong tích tắc. Vui lòng thử lại.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình đặt sân {CourtId}", dto.CourtId);
                return BookingResult.Failure($"Đã có lỗi xảy ra trong quá trình đặt sân: {ex.Message}");
            }
        }

        /// <summary>
        /// Tính toán giá tiền theo khung giờ và giờ cao điểm
        /// </summary>
        private decimal CalculatePrice(Court court, TimeSpan start, TimeSpan end)
        {
            var matchingSlots = court.TimeSlots
                .Where(ts => start < ts.EndTime && end > ts.StartTime)
                .ToList();

            if (matchingSlots.Any())
            {
                decimal total = 0;
                foreach (var slot in matchingSlots)
                {
                    // Tính thời gian giao thoa
                    var overlapStart = start > slot.StartTime ? start : slot.StartTime;
                    var overlapEnd = end < slot.EndTime ? end : slot.EndTime;
                    var hours = (decimal)(overlapEnd - overlapStart).TotalHours;
                    total += hours * slot.Price;
                }
                if (total > 0) return total;
            }

            // Giá mặc định nếu chưa cấu hình chi tiết (VD: 150.000 VNĐ / giờ)
            var durationHours = (decimal)(end - start).TotalHours;
            return durationHours * 150000m;
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

            // Lấy danh sách booking đã có trong ngày đó của sân này
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

                // Xác định giá slot
                var tsConfig = court.TimeSlots.FirstOrDefault(t => t.StartTime <= time && t.EndTime >= slotEnd);
                decimal slotPrice = tsConfig?.Price ?? 150000m;
                bool isPeak = tsConfig?.IsPeakHour ?? (time.Hours >= 17 && time.Hours <= 20);

                var matrixSlot = new TimeSlotMatrixDto
                {
                    SlotIndex = slotIndex++,
                    StartTime = time,
                    EndTime = slotEnd,
                    Price = slotPrice,
                    IsPeakHour = isPeak,
                    Availability = SlotAvailability.Available
                };

                // Kiểm tra slot đã qua trong ngày hay chưa
                if (date < today || (date == today && slotEnd <= currentTime))
                {
                    matrixSlot.Availability = SlotAvailability.PastTime;
                }
                else
                {
                    // Thời hạn giữ chỗ: 5 phút. Quá 5 phút chưa thanh toán thì tự động nhả slot cho người khác đặt
                    var holdThreshold = DateTime.UtcNow.AddMinutes(-5);

                    // Kiểm tra xem có booking nào trùng slot này còn hiệu lực không
                    var matchedBooking = bookings.FirstOrDefault(b => 
                        b.Status != BookingStatus.Cancelled &&
                        time < b.EndTime && slotEnd > b.StartTime &&
                        (b.PaymentStatus == PaymentStatus.Paid || b.Status == BookingStatus.Confirmed || b.CreatedAt >= holdThreshold));

                    if (matchedBooking != null)
                    {
                        matrixSlot.BookingId = matchedBooking.Id;
                        matrixSlot.BookedByCustomerName = matchedBooking.Customer?.FullName ?? "Đã đặt";
                        matrixSlot.BookingStatus = matchedBooking.Status;

                        if (matchedBooking.PaymentStatus == PaymentStatus.Paid || matchedBooking.Status == BookingStatus.Confirmed)
                        {
                            matrixSlot.Availability = SlotAvailability.Booked;
                        }
                        else
                        {
                            matrixSlot.Availability = SlotAvailability.PendingPayment;
                        }
                    }
                    else
                    {
                        matrixSlot.Availability = SlotAvailability.Available;
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
        /// Xử lý thanh toán giả lập đơn đặt sân
        /// </summary>
        public async Task<bool> ProcessPaymentAsync(int bookingId, string paymentMethod = "DemoGateway")
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null) return false;

            booking.PaymentStatus = PaymentStatus.Paid;
            booking.Status = BookingStatus.Confirmed;
            booking.PaymentMethod = paymentMethod;
            booking.PaidAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(booking.PaymentTransactionRef))
            {
                booking.PaymentTransactionRef = $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}";
            }

            _unitOfWork.Bookings.Update(booking);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Xử lý thanh toán thành công cho đơn {BookingId} bằng phương thức {Method}", bookingId, paymentMethod);
            return true;
        }

        /// <summary>
        /// Xác nhận chuyển khoản ngân hàng ảo kèm ảnh minh chứng giao dịch
        /// </summary>
        public async Task<bool> SubmitBankTransferProofAsync(int bookingId, string? proofImageUrl, string? transactionRef, string? paymentMethod = "VirtualBank", string? note = null)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null) return false;

            booking.PaymentStatus = PaymentStatus.Paid;
            booking.Status = BookingStatus.Confirmed;
            booking.PaymentMethod = string.IsNullOrEmpty(paymentMethod) ? "VirtualBank" : paymentMethod;
            booking.PaymentProofImage = proofImageUrl;
            booking.PaymentTransactionRef = string.IsNullOrEmpty(transactionRef) ? $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}" : transactionRef;
            booking.PaidAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(note))
            {
                booking.PaymentNote = note;
            }

            _unitOfWork.Bookings.Update(booking);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Xác nhận chuyển khoản ngân hàng ảo cho đơn {BookingId}, mã GD: {Ref}, ảnh: {Proof}", 
                bookingId, booking.PaymentTransactionRef, proofImageUrl);
            return true;
        }

        /// <summary>
        /// Hủy đơn đặt sân
        /// </summary>
        public async Task<bool> CancelBookingAsync(int bookingId, string userId)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null) return false;

            // Chỉ người đặt hoặc quản trị mới được hủy
            if (booking.CustomerId != userId)
            {
                var court = await _unitOfWork.Courts.Query()
                    .Include(c => c.Facility)
                    .FirstOrDefaultAsync(c => c.Id == booking.CourtId);

                if (court?.Facility?.OwnerId != userId)
                {
                    return false;
                }
            }

            booking.Status = BookingStatus.Cancelled;
            _unitOfWork.Bookings.Update(booking);
            await _unitOfWork.CompleteAsync();
            return true;
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
        /// Cập nhật trạng thái đơn đặt sân (dành cho Chủ sân duyệt / xác nhận thu tiền mặt)
        /// </summary>
        public async Task<bool> UpdateBookingStatusAsync(int bookingId, BookingStatus status, PaymentStatus? paymentStatus = null)
        {
            var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId);
            if (booking == null) return false;

            booking.Status = status;
            if (paymentStatus.HasValue)
            {
                booking.PaymentStatus = paymentStatus.Value;
            }

            _unitOfWork.Bookings.Update(booking);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Cập nhật đơn đặt sân {BookingId}: Status={Status}, Payment={Payment}", 
                bookingId, status, paymentStatus);
            return true;
        }
    }
}
