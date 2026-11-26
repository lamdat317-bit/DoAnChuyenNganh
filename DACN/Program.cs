using DACN.Data;
using DACN.Models;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Database Connection & Entity Framework Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=(localdb)\\mssqllocaldb;Database=SportBookingDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2. Cấu hình ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

// Đường dẫn đăng nhập / từ chối truy cập khi chưa đủ quyền
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// Cookie cũ (tạo trước khi có claim Role) sẽ được kiểm tra lại ngay để nhận quyền mới
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// 3. Đăng ký Dependency Injection theo kiến trúc Repository Pattern & Services
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IFacilityService, DACN.Services.FacilityService>();

// Thông báo (trong app + Telegram chủ sân + email SMTP) và vòng đời đơn (tự hoàn thành, nhắc lịch)
builder.Services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<BookingLifecycleService>();

// Link đặt lại mật khẩu có hiệu lực 2 giờ
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));

// Job nền: hủy giữ chỗ quá hạn, tự hoàn thành đơn đã chơi, nhắc lịch trước giờ chơi
builder.Services.AddHostedService<ExpiredBookingCleanupService>();

// 4. Thêm MVC Controllers & Views
builder.Services.AddControllersWithViews();

var app = builder.Build();

// 5. Cấu hình HTTP Request Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// 6. Định tuyến Areas (Admin / Owner)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Facility}/{action=Index}/{id?}");

// 7. Định tuyến mặc định (Customer / Public)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Tự động kiểm tra hoặc Seed dữ liệu mẫu khi khởi động
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        
        // Tự động tạo / cập nhật database qua EF Core Migrations
        await context.Database.MigrateAsync();

        // Nạp dữ liệu mẫu
        await DbInitializer.SeedAsync(context, userManager);

        // Chuyển ảnh biên lai cũ (file trong wwwroot/uploads) vào SQL
        var importedProofs = await PaymentProofImporter.ImportAsync(context, app.Environment.WebRootPath);
        if (importedProofs > 0)
        {
            services.GetRequiredService<ILogger<Program>>()
                .LogInformation("Đã chuyển {Count} ảnh biên lai vào SQL.", importedProofs);
        }

        // Lịch sử đặt sân & đánh giá mẫu cho trang Thống kê (chỉ ở môi trường Development)
        if (app.Environment.IsDevelopment())
        {
            await DemoHistorySeeder.SeedAsync(context, userManager);
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Chưa thể kết nối Database để Seed. Vui lòng chạy migrations trước.");
    }
}

app.Run();
