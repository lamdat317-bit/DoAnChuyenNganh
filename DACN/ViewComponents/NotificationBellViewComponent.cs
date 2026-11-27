using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Services;
using Microsoft.AspNetCore.Mvc;

namespace DACN.ViewComponents
{
    /// <summary>Chuông thông báo trên thanh menu: số chưa đọc + 6 thông báo mới nhất.</summary>
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly INotificationService _notifications;

        public NotificationBellViewComponent(INotificationService notifications) => _notifications = notifications;

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            ViewBag.Unread = await _notifications.CountUnreadAsync(userId);
            return View(await _notifications.GetLatestAsync(userId, 6));
        }
    }
}
