using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IUnitOfWork _unitOfWork;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IUnitOfWork unitOfWork)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _unitOfWork = unitOfWork;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.EmailOrUsername)
                       ?? await _userManager.FindByNameAsync(model.EmailOrUsername);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Đăng nhập thành công! Chào mừng {user.FullName} quay trở lại.";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                if (user.Role == UserRole.Admin)
                {
                    return RedirectToAction("Index", "User", new { area = "Admin" });
                }

                if (user.Role == UserRole.FacilityOwner)
                {
                    return RedirectToAction("Index", "Facility", new { area = "Owner" });
                }

                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "Mật khẩu không chính xác.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "Địa chỉ Email này đã được đăng ký.");
                return View(model);
            }

            // Đăng ký tài khoản công khai: LUÔN LUÔN LÀ KHÁCH HÀNG (Customer)
            // Chỉ có Admin mới có quyền tạo tài khoản Quản lý / Chủ sân (FacilityOwner)
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, isPersistent: true);
                TempData["SuccessMessage"] = "Đăng ký tài khoản Khách Hàng thành công! Bạn đã đăng nhập vào hệ thống.";

                return RedirectToAction("Index", "Home");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            TempData["SuccessMessage"] = "Đã đăng xuất khỏi hệ thống an toàn.";
            return RedirectToAction("Index", "Home");
        }

        // Action đặc biệt 1-Click dành cho buổi thuyết trình/chấm điểm Đồ Án
        [HttpGet]
        public async Task<IActionResult> QuickLogin(string role)
        {
            string email = role.ToLower() switch
            {
                "owner" => "owner@sanbong.vn",
                "admin" => "admin@sanbong.vn",
                _ => "customer@gmail.com"
            };

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = await _userManager.Users.FirstOrDefaultAsync();
            }

            if (user != null)
            {
                await _signInManager.SignInAsync(user, isPersistent: true);
                TempData["SuccessMessage"] = $"Đã chuyển nhanh sang vai trò: [{user.FullName} - {user.Role}]";

                if (user.Role == UserRole.FacilityOwner)
                {
                    return RedirectToAction("Index", "Facility", new { area = "Owner" });
                }
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "demo-customer-id";
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var bookings = await _unitOfWork.Bookings.Query()
                .Where(b => b.CustomerId == userId)
                .ToListAsync();

            var matchesJoined = await _unitOfWork.MatchJoinRequests.Query()
                .CountAsync(r => r.UserId == userId && r.Status == JoinRequestStatus.Accepted);

            decimal totalSpent = bookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.TotalAmount);

            var vm = new ProfileViewModel
            {
                User = user,
                TotalBookings = bookings.Count,
                TotalSpent = totalSpent,
                TotalMatchesJoined = matchesJoined,
                VipTier = totalSpent > 2000000 ? VipTier.Gold : (totalSpent > 500000 ? VipTier.Silver : VipTier.Standard)
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(UpdateProfileViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "demo-customer-id";
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Thông tin cập nhật không hợp lệ.";
                return RedirectToAction(nameof(Profile));
            }

            user.FullName = model.FullName.Trim();
            user.PhoneNumber = model.PhoneNumber.Trim();

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Cập nhật thông tin hồ sơ thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Lỗi cập nhật: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Profile));
        }
    }
}
