using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DACN.Services
{
    public interface INotificationService
    {
        /// <summary>Tạo thông báo trong ứng dụng cho 1 người dùng (tùy chọn gửi kèm email).</summary>
        Task NotifyUserAsync(string userId, string title, string? message, string? url,
            string icon = "bi-bell", string tone = "green", bool sendEmail = false);

        /// <summary>Thông báo cho chủ cụm sân (kèm Telegram nếu chủ sân đã cấu hình).</summary>
        Task NotifyFacilityOwnerAsync(int facilityId, string title, string? message, string? url,
            string icon = "bi-bell", string tone = "green", bool isNewBooking = false);

        /// <summary>Thông báo cho tất cả Admin.</summary>
        Task NotifyAdminsAsync(string title, string? message, string? url, string icon = "bi-bell", string tone = "blue");

        Task<int> CountUnreadAsync(string userId);
        Task<List<Notification>> GetLatestAsync(string userId, int take);
        Task MarkAllReadAsync(string userId);

        /// <summary>Gửi thử tin Telegram, trả về null nếu thành công hoặc thông báo lỗi.</summary>
        Task<string?> SendTelegramAsync(string botToken, string chatId, string text);
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAppEmailSender _email;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(ApplicationDbContext context, IAppEmailSender email,
            IHttpClientFactory httpFactory, ILogger<NotificationService> logger)
        {
            _context = context;
            _email = email;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task NotifyUserAsync(string userId, string title, string? message, string? url,
            string icon = "bi-bell", string tone = "green", bool sendEmail = false)
        {
            if (string.IsNullOrEmpty(userId)) return;
            try
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Title = Cut(title, 150),
                    Message = message == null ? null : Cut(message, 1000),
                    Url = url,
                    Icon = icon,
                    Tone = tone
                });
                await _context.SaveChangesAsync();

                if (sendEmail)
                {
                    var email = await _context.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
                    if (!string.IsNullOrEmpty(email))
                    {
                        await _email.SendAsync(email, title, EmailTemplate(title, message, url));
                    }
                }
            }
            catch (Exception ex)
            {
                // Thông báo lỗi không được làm hỏng nghiệp vụ chính (đặt sân, thanh toán...)
                _logger.LogWarning(ex, "Không tạo được thông báo cho {UserId}", userId);
            }
        }

        public async Task NotifyFacilityOwnerAsync(int facilityId, string title, string? message, string? url,
            string icon = "bi-bell", string tone = "green", bool isNewBooking = false)
        {
            var facility = await _context.Facilities.AsNoTracking()
                .Include(f => f.NotificationConfig)
                .Where(f => f.Id == facilityId)
                .Select(f => new { f.OwnerId, f.Name, f.NotificationConfig })
                .FirstOrDefaultAsync();
            if (facility == null) return;

            await NotifyUserAsync(facility.OwnerId, title, message, url, icon, tone);

            var cfg = facility.NotificationConfig;
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.TelegramBotToken) && !string.IsNullOrWhiteSpace(cfg.TelegramChatId)
                && (!isNewBooking || cfg.NotifyOnNewBooking))
            {
                await SendTelegramAsync(cfg.TelegramBotToken!, cfg.TelegramChatId!, $"🏟 {facility.Name}\n{title}\n{message}");
            }
        }

        public async Task NotifyAdminsAsync(string title, string? message, string? url, string icon = "bi-bell", string tone = "blue")
        {
            var adminIds = await _context.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToListAsync();
            foreach (var id in adminIds)
            {
                await NotifyUserAsync(id, title, message, url, icon, tone);
            }
        }

        public Task<int> CountUnreadAsync(string userId)
            => _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

        public Task<List<Notification>> GetLatestAsync(string userId, int take)
            => _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync();

        public Task MarkAllReadAsync(string userId)
            => _context.Notifications.Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        public async Task<string?> SendTelegramAsync(string botToken, string chatId, string text)
        {
            try
            {
                var client = _httpFactory.CreateClient("telegram");
                var res = await client.PostAsJsonAsync($"https://api.telegram.org/bot{botToken.Trim()}/sendMessage",
                    new { chat_id = chatId.Trim(), text });
                if (res.IsSuccessStatusCode) return null;
                var body = await res.Content.ReadAsStringAsync();
                return $"Telegram trả lỗi {(int)res.StatusCode}: {Cut(body, 200)}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không gửi được tin Telegram");
                return "Không kết nối được tới Telegram: " + ex.Message;
            }
        }

        private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

        private static string EmailTemplate(string title, string? message, string? url)
        {
            static string enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            return $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;border:1px solid #e5e9f0;border-radius:12px;overflow:hidden"">
<div style=""background:#16a34a;color:#fff;padding:16px 20px;font-weight:700;font-size:18px"">SanBong.vn</div>
<div style=""padding:20px;color:#0f172a""><h2 style=""font-size:18px;margin:0 0 8px"">{enc(title)}</h2>
<p style=""color:#475569;line-height:1.6"">{enc(message ?? "")}</p>
{(string.IsNullOrEmpty(url) ? "" : $@"<p style=""color:#64748b;font-size:13px"">Đăng nhập SanBong.vn để xem chi tiết.</p>")}
</div></div>";
        }
    }
}
