using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DACN.Services
{
    /// <summary>
    /// Job chạy nền: định kỳ hủy các đơn giữ chỗ đã quá <see cref="BookingRules.HoldMinutes"/> phút mà chưa thanh toán,
    /// để danh sách "Vé của tôi", bảng lịch chủ sân và các số liệu thống kê không còn đếm những đơn bỏ dở.
    /// (Việc chống trùng lịch không phụ thuộc vào job này — truy vấn trùng lịch đã tự bỏ qua đơn quá hạn.)
    /// </summary>
    public class ExpiredBookingCleanupService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpiredBookingCleanupService> _logger;

        public ExpiredBookingCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExpiredBookingCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            try
            {
                do
                {
                    await ReleaseOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Ứng dụng đang tắt
            }
        }

        private async Task ReleaseOnceAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var released = await bookingService.ReleaseExpiredHoldsAsync(stoppingToken);
                if (released > 0)
                {
                    _logger.LogInformation("Đã tự động hủy {Count} đơn giữ chỗ quá hạn chưa thanh toán.", released);
                }

                var lifecycle = scope.ServiceProvider.GetRequiredService<BookingLifecycleService>();
                var completed = await lifecycle.AutoCompleteAsync(stoppingToken);
                if (completed > 0)
                {
                    _logger.LogInformation("Đã tự chuyển {Count} đơn đã chơi xong sang Hoàn thành.", completed);
                }
                await lifecycle.SendRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể dọn các đơn giữ chỗ quá hạn (sẽ thử lại sau {Seconds}s).", Interval.TotalSeconds);
            }
        }
    }
}
