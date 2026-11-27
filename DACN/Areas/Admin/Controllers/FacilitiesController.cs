using System;
using System.Linq;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models.Enums;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Admin.Controllers
{
    /// <summary>Admin kiểm duyệt cụm sân do chủ sân đăng ký trước khi hiển thị công khai.</summary>
    [Area("Admin")]
    [Authorize(Roles = AppRoles.Admin)]
    public class FacilitiesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;

        public FacilitiesController(ApplicationDbContext context, INotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        public async Task<IActionResult> Index(string? status = "pending")
        {
            var query = _context.Facilities.AsNoTracking()
                .Include(f => f.Owner)
                .Include(f => f.SportType)
                .Include(f => f.City)
                .Include(f => f.Courts)
                .AsQueryable();

            query = status switch
            {
                "approved" => query.Where(f => f.ApprovalStatus == FacilityApprovalStatus.Approved),
                "rejected" => query.Where(f => f.ApprovalStatus == FacilityApprovalStatus.Rejected),
                "all" => query,
                _ => query.Where(f => f.ApprovalStatus == FacilityApprovalStatus.Pending)
            };

            ViewBag.Status = status is "approved" or "rejected" or "all" ? status : "pending";
            var counts = await _context.Facilities.GroupBy(f => f.ApprovalStatus)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            ViewBag.PendingCount = counts.FirstOrDefault(c => c.Key == FacilityApprovalStatus.Pending)?.Count ?? 0;
            ViewBag.ApprovedCount = counts.FirstOrDefault(c => c.Key == FacilityApprovalStatus.Approved)?.Count ?? 0;
            ViewBag.RejectedCount = counts.FirstOrDefault(c => c.Key == FacilityApprovalStatus.Rejected)?.Count ?? 0;

            return View(await query.OrderByDescending(f => f.Id).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int id, bool approve, string? note)
        {
            var facility = await _context.Facilities.FirstOrDefaultAsync(f => f.Id == id);
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            if (!approve && string.IsNullOrWhiteSpace(note))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập lý do từ chối để chủ sân biết cần sửa gì.";
                return RedirectToAction(nameof(Index), new { status = "pending" });
            }

            facility.ApprovalStatus = approve ? FacilityApprovalStatus.Approved : FacilityApprovalStatus.Rejected;
            facility.ApprovalNote = approve ? null : note!.Trim();
            facility.ApprovedAt = approve ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync();

            await _notifications.NotifyUserAsync(facility.OwnerId,
                approve ? $"Cụm sân \"{facility.Name}\" đã được duyệt" : $"Cụm sân \"{facility.Name}\" bị từ chối",
                approve ? "Cụm sân đã hiển thị công khai và bắt đầu nhận đặt sân online." : $"Lý do: {facility.ApprovalNote}. Hãy chỉnh sửa thông tin rồi gửi duyệt lại.",
                "/Owner/Facility", approve ? "bi-patch-check" : "bi-x-octagon", approve ? "green" : "red", sendEmail: true);

            TempData["SuccessMessage"] = approve ? $"Đã duyệt cụm sân \"{facility.Name}\"." : $"Đã từ chối cụm sân \"{facility.Name}\".";
            return RedirectToAction(nameof(Index), new { status = "pending" });
        }

        /// <summary>Hộp thư hệ thống: các email đã gửi / chưa gửi được (khi chưa cấu hình SMTP).</summary>
        public async Task<IActionResult> Emails(int? id)
        {
            var emails = await _context.EmailMessages.AsNoTracking()
                .OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync();
            ViewBag.Selected = id.HasValue ? emails.FirstOrDefault(e => e.Id == id) : emails.FirstOrDefault();
            ViewBag.SmtpConfigured = HttpContext.RequestServices.GetService(typeof(IAppEmailSender)) is IAppEmailSender s && s.IsConfigured;
            return View(emails);
        }
    }
}
