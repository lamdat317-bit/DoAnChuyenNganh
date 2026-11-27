using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.AspNetCore.Authorization;
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
        private readonly IWebHostEnvironment _env;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IUnitOfWork unitOfWork,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _unitOfWork = unitOfWork;
            _env = env;
        }

        private IActionResult RedirectByRole(ApplicationUser user)
        {
            return user.Role switch
            {
                UserRole.Admin => RedirectToAction("Index", "Dashboard", new { area = "Admin" }),
                UserRole.FacilityOwner => RedirectToAction("Index", "Facility", new { area = "Owner" }),
                _ => RedirectToAction("Index", "Home")
            };
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewBag.ShowDemoAccounts = _env.IsDevelopment();
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewBag.ShowDemoAccounts = _env.IsDevelopment();
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
                return RedirectByRole(user);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản này đã bị khóa. Vui lòng liên hệ quản trị viên.");
                return View(model);
            }

            ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
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
                if (await _userManager.IsLockedOutAsync(user))
            {
                TempData["ErrorMessage"] = "Tài khoản mẫu này đang bị khóa.";
                return RedirectToAction(nameof(Login));
            }

            await _signInManager.SignInAsync(user, isPersistent: true);
                TempData["SuccessMessage"] = "Đăng ký tài khoản thành công! Bạn đã được đăng nhập.";
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


        // ===== Quên mật khẩu: gửi link đặt lại qua email (token của ASP.NET Identity, hiệu lực 2 giờ) =====
        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model,
            [FromServices] DACN.Services.IAppEmailSender emailSender)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var link = Url.Action(nameof(ResetPassword), "Account", new { email = user.Email, token }, Request.Scheme)!;
                var enc = System.Net.WebUtility.HtmlEncode(link);
                var sent = await emailSender.SendAsync(user.Email!, "Đặt lại mật khẩu DatSan247",
                    $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;border:1px solid #e5e9f0;border-radius:12px;overflow:hidden"">
<div style=""background:#16a34a;color:#fff;padding:16px 20px;font-weight:700;font-size:18px"">DatSan247</div>
<div style=""padding:20px;color:#0f172a""><p>Xin chào {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
<p>Bạn (hoặc ai đó) vừa yêu cầu đặt lại mật khẩu. Bấm nút dưới đây để tạo mật khẩu mới — link có hiệu lực trong 2 giờ:</p>
<p style=""text-align:center;margin:24px 0""><a href=""{enc}"" style=""background:#16a34a;color:#fff;padding:12px 22px;border-radius:8px;text-decoration:none;font-weight:600"">Đặt lại mật khẩu</a></p>
<p style=""color:#64748b;font-size:13px"">Nếu bạn không yêu cầu, hãy bỏ qua email này. Mật khẩu hiện tại vẫn giữ nguyên.</p></div></div>");

                // Môi trường demo chưa cấu hình SMTP: hiện link ngay trên màn hình để thử chức năng
                if (!sent && _env.IsDevelopment())
                {
                    TempData["DevResetLink"] = link;
                }
            }

            // Luôn trả cùng một thông báo để không lộ email nào đã đăng ký
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation()
        {
            ViewBag.DevResetLink = TempData["DevResetLink"] as string;
            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword(string? email, string? token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                TempData["ErrorMessage"] = "Link đặt lại mật khẩu không hợp lệ.";
                return RedirectToAction(nameof(ForgotPassword));
            }
            return View(new ResetPasswordViewModel { Email = email, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            var result = user == null
                ? IdentityResult.Failed(new IdentityError { Description = "invalid" })
                : await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);

            if (!result.Succeeded)
            {
                bool tokenProblem = user == null || result.Errors.Any(e => e.Code == "InvalidToken");
                ModelState.AddModelError(string.Empty, tokenProblem
                    ? "Link đặt lại mật khẩu đã hết hạn hoặc đã được dùng. Vui lòng yêu cầu link mới."
                    : string.Join(" ", result.Errors.Select(e => e.Description)));
                return View(model);
            }

            await _userManager.UpdateSecurityStampAsync(user!);
            TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công! Hãy đăng nhập bằng mật khẩu mới.";
            return RedirectToAction(nameof(Login));
        }
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // Đăng nhập nhanh 1-Click bằng tài khoản mẫu: CHỈ bật ở môi trường Development (demo / chấm đồ án)
        [HttpGet]
        public async Task<IActionResult> QuickLogin(string role)
        {
            if (!_env.IsDevelopment())
            {
                return NotFound();
            }

            string email = role?.ToLower() switch
            {
                "owner" => "owner@datsan247.vn",
                "admin" => "admin@datsan247.vn",
                _ => "customer@gmail.com"
            };

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["ErrorMessage"] = $"Chưa có tài khoản mẫu {email} trong cơ sở dữ liệu.";
                return RedirectToAction(nameof(Login));
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                TempData["ErrorMessage"] = "Tài khoản mẫu này đang bị khóa.";
                return RedirectToAction(nameof(Login));
            }

            await _signInManager.SignInAsync(user, isPersistent: true);
            TempData["SuccessMessage"] = $"Đã đăng nhập nhanh với vai trò: {user.FullName}";
            return RedirectByRole(user);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction(nameof(Login));
            }

            var bookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.CustomerId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var matchesJoined = await _unitOfWork.MatchJoinRequests.Query()
                .CountAsync(r => r.UserId == userId && r.Status == JoinRequestStatus.Accepted);

            var reviewsCount = await _unitOfWork.Reviews.Query().CountAsync(r => r.CustomerId == userId);

            decimal totalSpent = bookings
                .Where(b => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled)
                .Sum(b => b.TotalAmount);

            var vm = new ProfileViewModel
            {
                User = user,
                TotalBookings = bookings.Count,
                TotalSpent = totalSpent,
                TotalMatchesJoined = matchesJoined,
                TotalReviews = reviewsCount,
                RecentBookings = bookings.Take(5).ToList(),
                VipTier = totalSpent > 2000000 ? VipTier.Gold : (totalSpent > 500000 ? VipTier.Silver : VipTier.Standard)
            };

            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(UpdateProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
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
                await _signInManager.RefreshSignInAsync(user);
                TempData["SuccessMessage"] = "Cập nhật thông tin hồ sơ thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Lỗi cập nhật: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Profile));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Profile), null, "password");
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                var msg = result.Errors.Any(e => e.Code == "PasswordMismatch")
                    ? "Mật khẩu hiện tại không đúng."
                    : string.Join(" ", result.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = "Đổi mật khẩu thất bại: " + msg;
                return RedirectToAction(nameof(Profile), null, "password");
            }

            await _signInManager.RefreshSignInAsync(user);
            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction(nameof(Profile));
        }
    }
}
