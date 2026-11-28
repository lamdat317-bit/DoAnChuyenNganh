using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DACN.Data;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services
{
    /// <summary>
    /// Job nền: tự đổi địa chỉ cụm sân thành tọa độ GPS (OpenStreetMap Nominatim, miễn phí, không cần API key)
    /// để cụm sân hiện ghim trên "Xem bản đồ" ở trang Tìm sân. Chủ sân không phải nhập tọa độ.
    /// Nominatim giới hạn 1 yêu cầu/giây nên job chạy tuần tự, có nghỉ giữa các lần gọi.
    /// </summary>
    public class FacilityGeocodingService : BackgroundService
    {
        public const string HttpClientName = "nominatim";
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(1100);
        private const int BatchSize = 20;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<FacilityGeocodingService> _logger;
        // Địa chỉ đã thử mà không tìm ra: không gọi lại cho tới khi chủ sân sửa địa chỉ (hoặc app khởi động lại)
        private readonly HashSet<string> _notFound = new();

        public FacilityGeocodingService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, ILogger<FacilityGeocodingService> logger)
        {
            _scopeFactory = scopeFactory;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // để app khởi động xong (seed dữ liệu)
                using var timer = new PeriodicTimer(Interval);
                do
                {
                    await RunOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Ứng dụng đang tắt
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var candidates = await db.Facilities
                    .Include(f => f.City)
                    .Where(f => f.Address != "" && (f.Latitude == null || f.Longitude == null || f.Latitude == 0 || f.Longitude == 0
                                || (f.Latitude == FacilityMap.LegacyDefaultLat && f.Longitude == FacilityMap.LegacyDefaultLng)))
                    .OrderByDescending(f => f.Id)
                    .Take(BatchSize * 3)
                    .ToListAsync(ct);

                int updated = 0, calls = 0;
                foreach (var facility in candidates)
                {
                    if (calls >= BatchSize) break;
                    var full = FacilityMap.FullAddress(facility);
                    if (string.IsNullOrWhiteSpace(full) || _notFound.Contains(full)) continue;

                    (double Lat, double Lng)? point = null;
                    foreach (var query in CandidateQueries(facility))
                    {
                        if (calls > 0) await Task.Delay(RequestGap, ct);
                        calls++;
                        point = await LookupAsync(query, ct);
                        if (point != null) break;
                    }

                    if (point == null)
                    {
                        _notFound.Add(full);
                        continue;
                    }
                    facility.Latitude = Math.Round(point.Value.Lat, 6);
                    facility.Longitude = Math.Round(point.Value.Lng, 6);
                    updated++;
                }

                if (updated > 0)
                {
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("Đã tự lấy tọa độ GPS từ địa chỉ cho {Count} cụm sân.", updated);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể lấy tọa độ từ địa chỉ cụm sân (sẽ thử lại sau {Minutes} phút).", Interval.TotalMinutes);
            }
        }

        /// <summary>Địa chỉ đầy đủ trước; không ra thì bỏ số nhà/ngõ (OpenStreetMap ở VN hay thiếu số nhà).</summary>
        private static IEnumerable<string> CandidateQueries(Models.Facility f)
        {
            var full = FacilityMap.FullAddress(f);
            yield return full;
            var noNumber = Regex.Replace(f.Address ?? "", @"^\s*(số\s*)?[\d/\-A-Za-z]*\d[\d/\-A-Za-z]*\s*,?\s*", "", RegexOptions.IgnoreCase);
            noNumber = Regex.Replace(noNumber, @"^\s*(ngõ|ngách|hẻm|kiệt)\s*[\d/]+\s*,?\s*", "", RegexOptions.IgnoreCase);
            var street = FacilityMap.FullAddress(noNumber, f.DistrictName, f.City?.Name);
            if (!string.IsNullOrWhiteSpace(noNumber) && !string.Equals(street, full, StringComparison.OrdinalIgnoreCase))
                yield return street;
        }

        private async Task<(double, double)?> LookupAsync(string query, CancellationToken ct)
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            var url = "search?format=jsonv2&limit=1&countrycodes=vn&accept-language=vi&q=" + Uri.EscapeDataString(query);
            var results = await client.GetFromJsonAsync<List<NominatimResult>>(url, ct);
            var first = results?.FirstOrDefault();
            if (first == null) return null;
            if (double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                return (lat, lng);
            return null;
        }

        private sealed class NominatimResult
        {
            [JsonPropertyName("lat")] public string? Lat { get; set; }
            [JsonPropertyName("lon")] public string? Lon { get; set; }
        }
    }
}
