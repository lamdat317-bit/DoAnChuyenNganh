using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services
{
    /// <summary>
    /// Các việc tự động theo thời gian của đơn đặt sân (chạy định kỳ bởi <see cref="ExpiredBookingCleanupService"/>):
    /// 1. Đơn đã xác nhận + đã thanh toán, đã qua giờ kết thúc  → tự chuyển "Hoàn thành".
    /// 2. Đơn còn hiệu lực sắp tới giờ chơi (trong <see cref="ReminderHours"/> tiếng) → gửi nhắc lịch cho khách (1 lần).
    /// </summary>
    public class BookingLifecycleService
    {
        public const int ReminderHours = 2;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;

        public BookingLifecycleService(ApplicationDbContext context, INotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        /// <summary>
        /// Đơn khách đã gửi biên lai nhưng tới hết giờ chơi chủ sân vẫn chưa xác nhận (và khách chưa check-in):
        /// tự hủy để đơn không treo mãi ở "Chờ xác nhận", chuyển sang "Chờ hoàn tiền" toàn bộ
        /// (chủ sân kiểm tra tài khoản, nếu đã nhận tiền thì hoàn lại cho khách), báo cho cả khách và chủ sân.
        /// Đơn đã check-in nghĩa là chủ sân đã cho khách vào sân, nên để chủ sân tự xác nhận sau.
        /// </summary>
        public async Task<int> ExpireUnverifiedTransfersAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);

            var candidates = await _context.Bookings
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.PaymentStatus == PaymentStatus.AwaitingVerification
                         && b.Status != BookingStatus.Cancelled
                         && b.CheckedInAt == null
                         && b.BookingDate <= today)
                .ToListAsync(ct);

            var expired = candidates.Where(b => BookingRules.EndsAt(b) <= now).ToList();
            if (expired.Count == 0) return 0;

            foreach (var b in expired)
            {
                b.Status = BookingStatus.Cancelled;
                b.CancelledAt = DateTime.UtcNow;
                b.PaymentStatus = PaymentStatus.RefundPending;
                b.RefundAmount = b.TotalAmount;
                b.PaymentNote = "Hệ thống tự hủy: đã hết giờ chơi mà chủ sân chưa xác nhận chuyển khoản.";
            }
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Chủ sân vừa xử lý đơn cùng lúc: bỏ qua, lần chạy sau sẽ xét lại
                _context.ChangeTracker.Clear();
                return 0;
            }

            foreach (var b in expired)
            {
                var where = $"{b.Court?.Facility?.Name} · {b.Court?.Name} · {b.BookingDate:dd/MM} {BookingRules.Hm(b.StartTime)}–{BookingRules.Hm(b.EndTime)}";
                await _notifications.NotifyUserAsync(b.CustomerId, $"Đơn #BK-{b.Id} chưa được xác nhận thanh toán",
                    $"Chủ sân chưa xác nhận chuyển khoản của bạn trước khi hết giờ chơi ({where}) nên đơn đã được hủy. Nếu bạn đã chuyển tiền, chủ sân sẽ hoàn {b.TotalAmount:N0}đ.",
                    $"/Booking/Confirmation?bookingId={b.Id}", "bi-hourglass-bottom", "amber", sendEmail: true);
                if (b.Court != null)
                {
                    await _notifications.NotifyFacilityOwnerAsync(b.Court.FacilityId, $"Đơn #BK-{b.Id} bị tự hủy vì chưa xác nhận chuyển khoản",
                        $"{b.CustomerName} đã gửi biên lai {b.TotalAmount:N0}đ cho {where} nhưng bạn chưa xác nhận. Kiểm tra tài khoản, nếu đã nhận tiền hãy hoàn lại cho khách.",
                        $"/Owner/Facility/Refunds?facilityId={b.Court.FacilityId}", "bi-hourglass-bottom", "amber");
                }
            }
            return expired.Count;
        }

        public async Task<int> AutoCompleteAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            var nowTime = now.TimeOfDay;

            return await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed
                         && b.PaymentStatus == PaymentStatus.Paid
                         && (b.BookingDate < today || (b.BookingDate == today && b.EndTime <= nowTime)))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Completed), ct);
        }

        public async Task<int> SendRemindersAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            var tomorrow = today.AddDays(1);
            var horizon = now.AddHours(ReminderHours);

            var candidates = await _context.Bookings
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.ReminderSentAt == null
                         && (b.BookingDate == today || b.BookingDate == tomorrow)
                         && b.Status != BookingStatus.Cancelled
                         && b.Status != BookingStatus.Completed
                         && (b.Status == BookingStatus.Confirmed || b.PaymentStatus != PaymentStatus.Unpaid))
                .ToListAsync(ct);

            var due = candidates
                .Where(b => { var start = BookingRules.StartsAt(b); return start > now && start <= horizon; })
                .ToList();
            if (due.Count == 0) return 0;

            foreach (var b in due) b.ReminderSentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            foreach (var b in due)
            {
                var minutes = (int)Math.Round((BookingRules.StartsAt(b) - now).TotalMinutes);
                await _notifications.NotifyUserAsync(b.CustomerId,
                    $"Sắp tới giờ chơi: {b.Court?.Facility?.Name}",
                    $"{b.Court?.Name} · {BookingRules.Hm(b.StartTime)}–{BookingRules.Hm(b.EndTime)} hôm nay (còn khoảng {minutes} phút). Địa chỉ: {b.Court?.Facility?.Address}",
                    $"/Booking/Confirmation?bookingId={b.Id}", "bi-alarm", "amber", sendEmail: true);
            }
            return due.Count;
        }
    }
}
