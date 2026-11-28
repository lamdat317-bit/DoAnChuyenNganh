using DACN.Models;
using DACN.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DACN.Data
{
    /// <summary>
    /// Dữ liệu khởi tạo khi chạy THẬT (không phải Development): chỉ tạo danh mục cần thiết và 1 tài khoản Admin
    /// lấy từ cấu hình. Không tạo tài khoản mẫu có mật khẩu mặc định, không tạo sân / đơn mẫu.
    ///
    /// Cấu hình Admin (appsettings.Production.json, biến môi trường hoặc user-secrets):
    ///   "Seed": { "AdminEmail": "...", "AdminPassword": "..." }
    ///   hoặc biến môi trường Seed__AdminEmail / Seed__AdminPassword
    /// </summary>
    public static class ProductionSeeder
    {
        private const string DemoPassword = "Password@123";
        private static readonly string[] DemoEmails = { "admin@datsan247.vn", "owner@datsan247.vn", "customer@gmail.com" };

        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
            IConfiguration config, ILogger logger)
        {
            if (!await context.CityProvinces.AnyAsync())
            {
                context.CityProvinces.AddRange(new[] { "Hà Nội", "TP. Hồ Chí Minh", "Đà Nẵng", "Hải Phòng", "Bình Dương", "Cần Thơ" }
                    .Select(n => new CityProvince { Name = n }));
                await context.SaveChangesAsync();
            }

            if (!await context.SportTypes.AnyAsync())
            {
                context.SportTypes.AddRange(
                    new SportType { Name = "Bóng đá mini", IconCss = "bi bi-dribbble" },
                    new SportType { Name = "Cầu lông", IconCss = "bi bi-lightning-charge" },
                    new SportType { Name = "Quần vợt / Tennis", IconCss = "bi bi-circle" },
                    new SportType { Name = "Pickleball", IconCss = "bi bi-fire" });
                await context.SaveChangesAsync();
            }

            // Database chép từ máy dev sang: khóa các tài khoản mẫu còn dùng mật khẩu mặc định
            foreach (var email in DemoEmails)
            {
                var demo = await userManager.FindByEmailAsync(email);
                if (demo == null || !await userManager.CheckPasswordAsync(demo, DemoPassword)) continue;
                await userManager.SetLockoutEnabledAsync(demo, true);
                await userManager.SetLockoutEndDateAsync(demo, DateTimeOffset.MaxValue);
                logger.LogWarning("Đã khóa tài khoản mẫu {Email} vì vẫn dùng mật khẩu mặc định. Đổi mật khẩu rồi mở khóa nếu muốn dùng.", email);
            }

            if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin && (u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.UtcNow)))
                return;

            var adminEmail = config["Seed:AdminEmail"]?.Trim();
            var adminPassword = config["Seed:AdminPassword"];
            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                logger.LogWarning("Chưa có tài khoản Admin. Hãy cấu hình Seed:AdminEmail và Seed:AdminPassword rồi khởi động lại.");
                return;
            }

            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Quản Trị Viên Hệ Thống",
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                logger.LogInformation("Đã tạo tài khoản Admin {Email} từ cấu hình.", adminEmail);
            else
                logger.LogWarning("Không tạo được Admin {Email}: {Errors}", adminEmail, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
