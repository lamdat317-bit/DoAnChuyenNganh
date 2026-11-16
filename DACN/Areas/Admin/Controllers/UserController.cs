using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUnitOfWork _unitOfWork;

        public UserController(UserManager<ApplicationUser> userManager, IUnitOfWork unitOfWork)
        {
            _userManager = userManager;
            _unitOfWork = unitOfWork;
        }

        // GET: /Admin/User/Index
        public async Task<IActionResult> Index(string? role, string? search)
        {
            var query = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(s) || 
                                         (u.Email != null && u.Email.ToLower().Contains(s)) ||
                                         (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
                ViewBag.Search = search;
            }

            if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var parsedRole))
            {
                query = query.Where(u => u.Role == parsedRole);
                ViewBag.SelectedRole = role;
            }

            var allUsers = await _userManager.Users.ToListAsync();
            ViewBag.TotalUsers = allUsers.Count;
            ViewBag.TotalManagers = allUsers.Count(u => u.Role == UserRole.FacilityOwner);
            ViewBag.TotalCustomers = allUsers.Count(u => u.Role == UserRole.Customer);
            ViewBag.TotalAdmins = allUsers.Count(u => u.Role == UserRole.Admin);

            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();
            return View(users);
        }

        // POST: /Admin/User/CreateManager (Chỉ Admin mới có quyền tạo tài khoản Quản lý / Chủ sân)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManager(CreateManagerViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Thông tin tạo tài khoản không hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction(nameof(Index));
            }

            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
                TempData["ErrorMessage"] = $"Email '{model.Email}' đã được đăng ký trong hệ thống.";
                return RedirectToAction(nameof(Index));
            }

            var user = new ApplicationUser
            {
                UserName = model.Email.Trim(),
                Email = model.Email.Trim(),
                FullName = model.FullName.Trim(),
                PhoneNumber = model.PhoneNumber.Trim(),
                Role = model.Role, // Quản lý cơ sở (FacilityOwner) hoặc Quản trị viên (Admin)
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                var roleName = model.Role == UserRole.FacilityOwner ? "Chủ Sân / Quản Lý Cơ Sở" : "Quản Trị Viên (Admin)";
                TempData["SuccessMessage"] = $"Đã cấp tài khoản [{roleName}] thành công cho: {user.FullName} ({user.Email})!";
            }
            else
            {
                TempData["ErrorMessage"] = "Lỗi khi tạo tài khoản: " + string.Join("; ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/User/DeleteUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Tài khoản không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            // Ngăn chặn xóa tài khoản Admin mặc định
            if (user.Email == "admin@sanbong.vn")
            {
                TempData["ErrorMessage"] = "Không thể xóa tài khoản Quản Trị Viên mặc định của hệ thống.";
                return RedirectToAction(nameof(Index));
            }

            // Đơn đặt sân, cụm sân, bài đăng kèo, yêu cầu ghép đội đều tham chiếu tới tài khoản bằng khóa ngoại Restrict
            // => tài khoản đã phát sinh dữ liệu thì không xóa được (tránh lỗi 500 và mất lịch sử giao dịch).
            bool hasRelatedData =
                await _unitOfWork.Bookings.AnyAsync(b => b.CustomerId == id)
                || await _unitOfWork.Facilities.AnyAsync(f => f.OwnerId == id)
                || await _unitOfWork.MatchPosts.AnyAsync(p => p.CreatorId == id)
                || await _unitOfWork.MatchJoinRequests.AnyAsync(r => r.UserId == id);

            if (hasRelatedData)
            {
                TempData["ErrorMessage"] = $"Không thể xóa tài khoản {user.FullName} ({user.Email}) vì đã phát sinh đơn đặt sân, cụm sân hoặc bài đăng ghép đội.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Đã xóa tài khoản {user.FullName} ({user.Email}) thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể xóa tài khoản này.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

