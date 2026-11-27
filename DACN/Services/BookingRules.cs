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
                     || b.PaymentStatus != PaymentStatus.Unpaid
                     || b.CreatedAt >= holdThresholdUtc);

        /// <summary>Đơn Pending chưa thanh toán đã quá thời hạn giữ chỗ.</summary>
        public static bool IsHoldExpired(Booking booking, DateTime utcNow) =>
            booking.Status == BookingStatus.Pending
            && booking.PaymentStatus == PaymentStatus.Unpaid
            && booking.CreatedAt < HoldThreshold(utcNow);

        /// <summary>Phiên bản chạy trong bộ nhớ của <see cref="IsActive"/>.</summary>
        public static bool IsActiveNow(Booking booking, DateTime utcNow) =>
            booking.Status != BookingStatus.Cancelled && !IsHoldExpired(booking, utcNow);

        /// <summary>
        /// Khách tự hủy đơn đã thanh toán khi còn ít nhất ngần này giờ trước giờ thi đấu thì được hoàn 100%.
        /// Hủy sát giờ hơn (nhưng chưa tới giờ) vẫn được, hoàn <see cref="LateCancelRefundPercent"/>%.
        /// Chủ sân / Admin hủy thì luôn hoàn 100%.
        /// </summary>
        public const int FreeCancelHours = 6;

        /// <summary>Phần trăm tiền hoàn khi khách hủy trong vòng <see cref="FreeCancelHours"/> tiếng trước giờ thi đấu.</summary>
        public const int LateCancelRefundPercent = 50;

        /// <summary>Phần trăm tiền được hoàn nếu khách tự hủy vào thời điểm <paramref name="nowLocal"/>.</summary>
        public static int CustomerRefundPercent(Booking booking, DateTime nowLocal) =>
            StartsAt(booking) - nowLocal >= TimeSpan.FromHours(FreeCancelHours) ? 100 : LateCancelRefundPercent;

        /// <summary>
        /// Doanh thu thực chủ sân giữ lại từ một đơn:
        /// đơn đã thanh toán còn hiệu lực → toàn bộ tiền; đơn hủy có hoàn tiền một phần → phần không hoàn lại;
        /// còn lại (chưa trả tiền, hoàn 100%) → 0.
        /// </summary>
        public static decimal RevenueOf(Booking b)
        {
            if (b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled) return b.TotalAmount;
            if (b.PaymentStatus is PaymentStatus.RefundPending or PaymentStatus.Refunded && b.RefundAmount.HasValue)
                return Math.Max(0, b.TotalAmount - b.RefundAmount.Value);
            return 0;
        }

        /// <summary>Số tiền hoàn theo phần trăm, làm tròn đến nghìn đồng.</summary>
        public static decimal RefundAmountFor(decimal total, int percent) =>
            percent >= 100 ? total : Math.Round(total * percent / 100m / 1000m, MidpointRounding.AwayFromZero) * 1000m;

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

            // Trước giờ thi đấu khách luôn được tự hủy; hủy sát giờ chỉ bị giảm phần tiền hoàn (xem CustomerRefundPercent).
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

        // ================== GIỜ HOẠT ĐỘNG (hỗ trợ mở qua nửa đêm) ==================

        /// <summary>Mốc 24:00 — kết thúc ngày.</summary>
        public static readonly TimeSpan EndOfDay = TimeSpan.FromDays(1);

        /// <summary>
        /// Các đoạn giờ hoạt động trong MỘT ngày lịch của cụm sân.
        /// - Bình thường (06:00 → 23:00): một đoạn [06:00, 23:00).
        /// - Mở qua đêm (16:00 → 02:00): hai đoạn [00:00, 02:00) (phần kéo dài của đêm hôm trước) và [16:00, 24:00).
        /// - Đóng cửa lúc 00:00 (16:00 → 00:00): một đoạn [16:00, 24:00).
        /// Đơn đặt sân không vắt qua nửa đêm: khung 23:00–01:00 được đặt thành 2 đơn của 2 ngày.
        /// </summary>
        public static List<(TimeSpan Start, TimeSpan End)> OpenSegments(TimeSpan opening, TimeSpan closing)
        {
            var segments = new List<(TimeSpan, TimeSpan)>();
            if (closing >= EndOfDay) closing = EndOfDay;
            if (closing > opening)
            {
                segments.Add((opening, closing));
            }
            else
            {
                if (closing > TimeSpan.Zero) segments.Add((TimeSpan.Zero, closing));
                segments.Add((opening, EndOfDay));
            }
            return segments;
        }

        public static List<(TimeSpan Start, TimeSpan End)> OpenSegments(Facility facility)
            => OpenSegments(facility.OpeningTime, facility.ClosingTime);

        /// <summary>Khoảng [start, end) có nằm trọn trong một đoạn giờ hoạt động không.</summary>
        public static bool IsWithinOpenHours(Facility facility, TimeSpan start, TimeSpan end)
            => end > start && OpenSegments(facility).Any(s => start >= s.Start && end <= s.End);

        /// <summary>Các block 1 tiếng trong ngày theo giờ hoạt động (block cuối mỗi đoạn có thể ngắn hơn 1 tiếng).</summary>
        public static List<(TimeSpan Start, TimeSpan End)> HourBlocks(Facility facility)
        {
            var blocks = new List<(TimeSpan, TimeSpan)>();
            foreach (var (segStart, segEnd) in OpenSegments(facility))
            {
                for (var t = segStart; t < segEnd; t = t.Add(TimeSpan.FromHours(1)))
                {
                    var e = t.Add(TimeSpan.FromHours(1));
                    blocks.Add((t, e > segEnd ? segEnd : e));
                }
            }
            return blocks;
        }


        /// <summary>Đọc giờ dạng "HH:mm", chấp nhận "24:00" (nửa đêm cuối ngày).</summary>
        public static bool TryParseHm(string? s, out TimeSpan value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s == "24:00" || s == "24:00:00") { value = EndOfDay; return true; }
            return TimeSpan.TryParse(s, out value) && value >= TimeSpan.Zero && value < EndOfDay;
        }
        /// <summary>Định dạng giờ, hiển thị mốc 24:00 thay vì 1.00:00.</summary>
        public static string Hm(TimeSpan t) => t >= EndOfDay ? "24:00" : t.ToString(@"hh\:mm");

        /// <summary>Chuỗi giờ mở cửa, vd "06:00 – 23:00" hoặc "16:00 – 02:00 (hôm sau)".</summary>
        public static string OpenHoursText(Facility facility)
            => $"{Hm(facility.OpeningTime)} – {Hm(facility.ClosingTime)}{(facility.IsOvernight && facility.ClosingTime > TimeSpan.Zero ? " (hôm sau)" : "")}";

        // ================== HIỂN THỊ TRẠNG THÁI ĐƠN ==================

        /// <summary>Nhãn trạng thái dùng chung cho mọi màn hình: (lớp badge-soft, icon, chữ).</summary>
        public static (string Tone, string Icon, string Text) StatusBadge(Booking b)
        {
            if (b.Status == BookingStatus.Cancelled)
            {
                return b.PaymentStatus switch
                {
                    PaymentStatus.RefundPending => ("warning", "bi-arrow-counterclockwise", "Đã hủy · Chờ hoàn tiền"),
                    PaymentStatus.Refunded => ("neutral", "bi-check2-circle", "Đã hủy · Đã hoàn tiền"),
                    _ => ("danger", "bi-x-circle", "Đã hủy")
                };
            }
            if (b.Status == BookingStatus.Completed) return ("info", "bi-flag", "Đã hoàn thành");
            return b.PaymentStatus switch
            {
                PaymentStatus.Paid => ("success", "bi-check-circle", "Đã thanh toán"),
                PaymentStatus.AwaitingVerification => ("violet", "bi-shield-check", "Chờ xác nhận chuyển khoản"),
                _ => b.Status == BookingStatus.Confirmed
                    ? ("accent", "bi-cash-coin", "Đã giữ chỗ · Trả tại sân")
                    : ("warning", "bi-hourglass-split", "Chờ thanh toán")
            };
        }
    }
}
