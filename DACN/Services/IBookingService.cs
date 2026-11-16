using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.DTOs;
using DACN.Models.Enums;
using DACN.Models.ViewModels;

namespace DACN.Services
{
    public interface IBookingService
    {
        /// <summary>
        /// Tạo đơn đặt sân với cơ chế bảo vệ Race Condition & Double Booking bằng Serializable Transaction.
        /// </summary>
        Task<BookingResult> CreateBookingAsync(BookingDto dto);

        /// <summary>
        /// Chủ sân đặt sân nhanh tại quầy (đơn được xác nhận ngay, có thể đã/chưa thu tiền).
        /// Dùng chung cơ chế chống trùng lịch với khách đặt online.
        /// </summary>
        Task<BookingResult> CreateCounterBookingAsync(BookingDto dto, decimal? totalAmountOverride, bool isPaid);

        /// <summary>
        /// Lấy ma trận khung giờ còn trống / đã đặt của 1 sân con trong ngày được chọn (dành cho Customer).
        /// </summary>
        Task<CourtScheduleMatrixViewModel> GetCourtScheduleMatrixAsync(int courtId, DateOnly date);

        /// <summary>
        /// Lấy bảng biểu thời gian (Timeline Schedule Board) của toàn bộ các sân trong cụm (dành cho Owner/Admin).
        /// </summary>
        Task<FacilityTimelineScheduleViewModel> GetFacilityTimelineScheduleAsync(int facilityId, DateOnly date);

        /// <summary>
        /// Kiểm tra đơn còn được phép thanh toán hay không (tự hủy nếu đã quá hạn giữ chỗ).
        /// Khi thất bại, <see cref="BookingResult.Booking"/> vẫn được gán (nếu đơn tồn tại) để controller điều hướng phù hợp.
        /// </summary>
        Task<BookingResult> CheckPayableAsync(int bookingId);

        /// <summary>
        /// Xử lý thanh toán giả lập đơn đặt sân.
        /// </summary>
        Task<BookingResult> ProcessPaymentAsync(int bookingId, string paymentMethod = "DemoGateway");

        /// <summary>
        /// Xác nhận chuyển khoản ngân hàng ảo kèm ảnh minh chứng giao dịch.
        /// </summary>
        Task<BookingResult> SubmitBankTransferProofAsync(int bookingId, string? proofImageUrl, string? transactionRef, string? paymentMethod = "VirtualBank", string? note = null);

        /// <summary>
        /// Hủy đơn đặt sân (khách đặt hoặc chủ cụm sân).
        /// </summary>
        Task<BookingResult> CancelBookingAsync(int bookingId, string userId);

        /// <summary>
        /// Cập nhật trạng thái đơn đặt sân (dành cho Chủ sân duyệt / xác nhận thanh toán tiền mặt).
        /// </summary>
        Task<BookingResult> UpdateBookingStatusAsync(int bookingId, BookingStatus status, PaymentStatus? paymentStatus = null);

        /// <summary>
        /// Hủy toàn bộ đơn giữ chỗ đã quá hạn mà chưa thanh toán. Trả về số đơn bị hủy.
        /// </summary>
        Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách đơn đặt sân của khách hàng.
        /// </summary>
        Task<List<Booking>> GetCustomerBookingsAsync(string customerId);

        /// <summary>
        /// Chi tiết đơn đặt sân.
        /// </summary>
        Task<Booking?> GetBookingByIdAsync(int bookingId);
    }
}
