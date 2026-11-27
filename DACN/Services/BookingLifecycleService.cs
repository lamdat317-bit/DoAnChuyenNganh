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
