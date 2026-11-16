using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DACN.Models;
using DACN.Models.Enums;

namespace DACN.Services
{
    /// <summary>
    /// Quy tắc nghiệp vụ dùng chung cho toàn bộ luồng đặt sân:
    /// thời hạn giữ chỗ, định nghĩa "đơn còn hiệu lực" (đang chiếm khung giờ) và cách tính giá.
    /// Mọi nơi kiểm tra trùng lịch / hiển thị lịch phải dùng chung các quy tắc này để không lệch nhau.
    /// </summary>
    public static class BookingRules
    {
        /// <summary>Thời gian giữ chỗ tối đa cho đơn chưa thanh toán (phút).</summary>
        public const int HoldMinutes = 5;

        /// <summary>Đơn giá mặc định (VNĐ/giờ) cho phần thời gian chưa được cấu hình bảng giá.</summary>
        public const decimal DefaultPricePerHour = 150000m;

        /// <summary>Mốc thời gian (UTC): đơn Pending tạo trước mốc này coi như đã hết hạn giữ chỗ.</summary>
        public static DateTime HoldThreshold(DateTime utcNow) => utcNow.AddMinutes(-HoldMinutes);

        /// <summary>
        /// Đơn còn hiệu lực (đang chiếm khung giờ) — dùng được trong truy vấn EF Core:
        /// chưa bị hủy, và (đã được xác nhận / hoàn thành, hoặc đã thanh toán, hoặc vẫn còn trong thời hạn giữ chỗ).
        /// </summary>
        public static Expression<Func<Booking, bool>> IsActive(DateTime holdThresholdUtc) =>
            b => b.Status != BookingStatus.Cancelled
                 && (b.Status != BookingStatus.Pending
                     || b.PaymentStatus == PaymentStatus.Paid
                     || b.CreatedAt >= holdThresholdUtc);

        /// <summary>Đơn Pending chưa thanh toán đã quá thời hạn giữ chỗ.</summary>
        public static bool IsHoldExpired(Booking booking, DateTime utcNow) =>
            booking.Status == BookingStatus.Pending
            && booking.PaymentStatus != PaymentStatus.Paid
            && booking.CreatedAt < HoldThreshold(utcNow);

        /// <summary>Phiên bản chạy trong bộ nhớ của <see cref="IsActive"/>.</summary>
        public static bool IsActiveNow(Booking booking, DateTime utcNow) =>
            booking.Status != BookingStatus.Cancelled && !IsHoldExpired(booking, utcNow);

        /// <summary>
        /// Khách chỉ được TỰ hủy (miễn phí) đơn đã xác nhận / đã thanh toán khi còn ít nhất ngần này giờ trước giờ thi đấu.
        /// Sau mốc này phải liên hệ chủ sân. Đơn giữ chỗ chưa thanh toán thì hủy được bất cứ lúc nào trước giờ đá.
        /// </summary>
        public const int FreeCancelHours = 6;

        /// <summary>Thời điểm bắt đầu trận (giờ địa phương).</summary>
        public static DateTime StartsAt(Booking booking) =>
            booking.BookingDate.ToDateTime(TimeOnly.FromTimeSpan(booking.StartTime));

        /// <summary>
        /// Lý do khách KHÔNG được tự hủy đơn; trả về null nếu được phép hủy.
        /// (Chủ cụm sân không bị ràng buộc bởi quy tắc này.)
        /// </summary>
        public static string? GetCustomerCancelBlockReason(Booking booking, DateTime nowLocal)
        {
            if (booking.Status == BookingStatus.Cancelled)
            {
                return "Đơn đặt sân này đã được hủy trước đó.";
            }

            if (booking.Status == BookingStatus.Completed)
            {
                return "Đơn đặt sân đã hoàn thành, không thể hủy.";
            }

            var startAt = StartsAt(booking);
            if (startAt <= nowLocal)
            {
                return "Đã đến hoặc quá giờ thi đấu nên không thể tự hủy đơn. Vui lòng liên hệ chủ sân.";
            }

            bool isCommitted = booking.PaymentStatus == PaymentStatus.Paid || booking.Status == BookingStatus.Confirmed;
            if (isCommitted && startAt - nowLocal < TimeSpan.FromHours(FreeCancelHours))
            {
                return $"Đã quá hạn hủy miễn phí (trước giờ thi đấu ít nhất {FreeCancelHours} tiếng, tức trước {startAt.AddHours(-FreeCancelHours):HH:mm dd/MM}). Vui lòng liên hệ chủ sân để được hỗ trợ.";
            }

            return null;
        }

        /// <summary>Số giây giữ chỗ còn lại của đơn (0 nếu đã hết hạn).</summary>
        public static int HoldRemainingSeconds(Booking booking, DateTime utcNow)
        {
            var remaining = TimeSpan.FromMinutes(HoldMinutes) - (utcNow - booking.CreatedAt);
            return Math.Max(0, (int)remaining.TotalSeconds);
        }

        /// <summary>
        /// Tính tiền sân cho khoảng [start, end) theo bảng giá khung giờ (đơn giá tính theo giờ).
        /// - Mỗi đoạn thời gian chỉ được tính theo ĐÚNG MỘT khung giá (không cộng dồn khi các khung chồng nhau);
        ///   nếu có nhiều khung cùng phủ, khung được thêm sau cùng (Id lớn nhất) được ưu tiên.
        /// - Đoạn thời gian không có khung giá nào phủ được tính theo đơn giá mặc định.
        /// </summary>
        public static decimal CalculatePrice(IEnumerable<TimeSlot> timeSlots, TimeSpan start, TimeSpan end)
        {
            if (end <= start) return 0m;

            var slots = timeSlots
                .Where(s => s.StartTime < end && s.EndTime > start)
                .ToList();

            // Các mốc chia đoạn: điểm đầu/cuối của đơn + các điểm đầu/cuối khung giá nằm bên trong
            var points = new SortedSet<TimeSpan> { start, end };
            foreach (var s in slots)
            {
                if (s.StartTime > start && s.StartTime < end) points.Add(s.StartTime);
                if (s.EndTime > start && s.EndTime < end) points.Add(s.EndTime);
            }

            decimal total = 0m;
            TimeSpan? previous = null;
            foreach (var point in points)
            {
                if (previous.HasValue)
                {
                    var segStart = previous.Value;
                    var segEnd = point;
                    var covering = slots
                        .Where(s => s.StartTime <= segStart && s.EndTime >= segEnd)
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefault();

                    var pricePerHour = covering?.Price ?? DefaultPricePerHour;
                    total += (decimal)(segEnd - segStart).TotalHours * pricePerHour;
                }
                previous = point;
            }

            return Math.Round(total, 0, MidpointRounding.AwayFromZero);
        }
    }
}
