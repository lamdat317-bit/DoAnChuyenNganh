using DACN.Data;
using DACN.Models;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Định dạng số kiểu Việt Nam cho tiền: 180.000đ (dấu chấm ngăn cách hàng nghìn).
// Giữ dấu chấm cho phần thập phân như cũ để không ảnh hưởng tọa độ GPS / ô nhập số.
var appCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("en-US").Clone();
appCulture.NumberFormat.NumberGroupSeparator = ".";
appCulture.NumberFormat.CurrencyGroupSeparator = ".";
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = appCulture;
System.Globalization.CultureInfo.CurrentCulture = appCulture;

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

    // Chống dò mật khẩu: sai 5 lần liên tiếp thì tạm khóa đăng nhập 5 phút
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
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

// Ngân hàng ảo: ví người dùng, chuyển tiền có khóa dòng + transaction
builder.Services.AddScoped<IBankService, BankService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddMemoryCache(); // lưu mã OTP giả lập
builder.Services.AddSingleton(TimeProvider.System);

// Trợ lý đặt sân trong khung chat (chạy offline, trả lời bằng dữ liệu thật trong DB)
builder.Services.AddScoped<DACN.Services.Assistant.SportAssistant>();

// Thông báo (trong app + Telegram chủ sân + email SMTP) và vòng đời đơn (tự hoàn thành, nhắc lịch)
builder.Services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<BookingLifecycleService>();

// Link đặt lại mật khẩu có hiệu lực 2 giờ
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));

// Job nền: hủy giữ chỗ quá hạn, tự hoàn thành đơn đã chơi, nhắc lịch trước giờ chơi
builder.Services.AddHostedService<ExpiredBookingCleanupService>();

// Job nền: tự đổi địa chỉ cụm sân sang tọa độ GPS (OpenStreetMap Nominatim) để hiện ghim trên bản đồ Tìm sân
builder.Services.AddHttpClient(FacilityGeocodingService.HttpClientName, c =>
{
    c.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    c.Timeout = TimeSpan.FromSeconds(10);
    // Nominatim yêu cầu User-Agent nhận diện ứng dụng
    c.DefaultRequestHeaders.UserAgent.ParseAdd("DatSan247/1.0 (facility-map)");
});
builder.Services.AddHostedService<FacilityGeocodingService>();

// 4. Thêm MVC Controllers & Views
builder.Services.AddControllersWithViews();

var app = builder.Build();

// 5. Cấu hình HTTP Request Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Trang lỗi thân thiện cho 404 / 403 / 500... (thay vì trang trắng)
app.UseStatusCodePagesWithReExecute("/Home/HttpStatus", "?code={0}");

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

        var seedLogger = services.GetRequiredService<ILogger<Program>>();

        if (app.Environment.IsDevelopment())
        {
            // Dữ liệu mẫu (tài khoản demo mật khẩu Password@123, sân, đơn, kèo): CHỈ ở Development (demo / chấm đồ án)
            await DbInitializer.SeedAsync(context, userManager);

            // Lịch sử đặt sân & đánh giá mẫu cho trang Thống kê
            await DemoHistorySeeder.SeedAsync(context, userManager);

            // Cụm sân mẫu ở nhiều tỉnh / môn (bỏ qua sân đã có, chạy lại an toàn)
            var addedFacilities = await SampleFacilitySeeder.SeedAsync(context, userManager);
            if (addedFacilities > 0) seedLogger.LogInformation("Đã thêm {Count} cụm sân mẫu.", addedFacilities);
        }
        else
        {
            // Chạy thật: chỉ tạo danh mục + Admin từ cấu hình, không có tài khoản mật khẩu mặc định
            await ProductionSeeder.SeedAsync(context, userManager, app.Configuration, seedLogger);
        }

        // Ngân hàng ảo: tạo tài khoản SYSTEM + tạo bù ví cho người dùng cũ chưa có
        var bank = services.GetRequiredService<IBankService>();
        var newWallets = await bank.BackfillAccountsAsync();
        if (newWallets > 0) seedLogger.LogInformation("Đã tạo {Count} ví ngân hàng ảo cho người dùng cũ.", newWallets);
        if (app.Environment.IsDevelopment())
        {
            var funded = await DemoWalletSeeder.SeedAsync(context, bank);
            if (funded > 0) seedLogger.LogInformation("Đã nạp tiền mẫu cho {Count} ví.", funded);
        }

        // Chuyển ảnh biên lai cũ (file trong wwwroot/uploads) vào SQL
        var importedProofs = await PaymentProofImporter.ImportAsync(context, app.Environment.WebRootPath);
        if (importedProofs > 0) seedLogger.LogInformation("Đã chuyển {Count} ảnh biên lai vào SQL.", importedProofs);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Chưa thể kết nối Database để Seed. Vui lòng chạy migrations trước.");
    }
}

app.Run();
