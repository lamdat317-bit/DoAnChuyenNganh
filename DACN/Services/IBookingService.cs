using System;
using System.Collections.Generic;
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
        /// Lấy ma trận khung giờ còn trống / đã đặt của 1 sân con trong ngày được chọn (dành cho Customer).
        /// </summary>
        Task<CourtScheduleMatrixViewModel> GetCourtScheduleMatrixAsync(int courtId, DateOnly date);

        /// <summary>
        /// Lấy bảng biểu thời gian (Timeline Schedule Board) của toàn bộ các sân trong cụm (dành cho Owner/Admin).
        /// </summary>
        Task<FacilityTimelineScheduleViewModel> GetFacilityTimelineScheduleAsync(int facilityId, DateOnly date);

        /// <summary>
        /// Xử lý thanh toán giả lập đơn đặt sân.
        /// </summary>
        Task<bool> ProcessPaymentAsync(int bookingId, string paymentMethod = "DemoGateway");

        /// <summary>
        /// Xác nhận chuyển khoản ngân hàng ảo kèm ảnh minh chứng giao dịch.
        /// </summary>
        Task<bool> SubmitBankTransferProofAsync(int bookingId, string? proofImageUrl, string? transactionRef, string? paymentMethod = "VirtualBank", string? note = null);

        /// <summary>
        /// Hủy đơn đặt sân.
        /// </summary>
        Task<bool> CancelBookingAsync(int bookingId, string userId);

        /// <summary>
        /// Cập nhật trạng thái đơn đặt sân (dành cho Chủ sân duyệt / xác nhận thanh toán tiền mặt).
        /// </summary>
        Task<bool> UpdateBookingStatusAsync(int bookingId, BookingStatus status, PaymentStatus? paymentStatus = null);

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
