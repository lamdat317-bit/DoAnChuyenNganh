using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;

        public NotificationsController(ApplicationDbContext context, INotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        public async Task<IActionResult> Index()
        {
            var items = await _notifications.GetLatestAsync(UserId, 100);
            return View(items);
        }

        /// <summary>Bấm vào 1 thông báo: đánh dấu đã đọc rồi mở trang liên quan.</summary>
        public async Task<IActionResult> Open(int id)
        {
            var n = await _context.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
            if (n == null) return RedirectToAction(nameof(Index));
            if (!n.IsRead)
            {
                n.IsRead = true;
                await _context.SaveChangesAsync();
            }
            return !string.IsNullOrEmpty(n.Url) && Url.IsLocalUrl(n.Url) ? LocalRedirect(n.Url) : RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead(string? returnUrl)
        {
            await _notifications.MarkAllReadAsync(UserId);
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
        }

        /// <summary>Số thông báo chưa đọc (trang tự cập nhật huy hiệu chuông mỗi 30 giây).</summary>
        [HttpGet]
        public async Task<IActionResult> UnreadCount()
            => Json(new { count = await _notifications.CountUnreadAsync(UserId) });
    }
}
