using System.Globalization;
using DACN.Models;

namespace DACN.Services
{
    /// <summary>
    /// Tạo link Google Maps cho cụm sân. Chủ sân chỉ cần nhập địa chỉ: nếu không có tọa độ hợp lệ
    /// thì Google Maps tự tìm theo chuỗi địa chỉ (số nhà, đường + quận/huyện + tỉnh/thành).
    /// </summary>
    public static class FacilityMap
    {
        // Tọa độ mặc định cũ của form "Thêm cụm sân" (trung tâm Hà Nội): không phải vị trí thật của sân
        public const double LegacyDefaultLat = 21.0285, LegacyDefaultLng = 105.8542;

        public static bool HasCoordinates(double? lat, double? lng) =>
            lat.HasValue && lng.HasValue && lat.Value != 0 && lng.Value != 0
            && !(Math.Abs(lat.Value - LegacyDefaultLat) < 1e-6 && Math.Abs(lng.Value - LegacyDefaultLng) < 1e-6);

        public static bool HasCoordinates(Facility f) => HasCoordinates(f.Latitude, f.Longitude);

        /// <summary>Địa chỉ đầy đủ để tìm trên bản đồ, bỏ phần quận/tỉnh nếu địa chỉ đã ghi sẵn.</summary>
        public static string FullAddress(string? address, string? district, string? city)
        {
            var parts = new List<string>();
            var addr = address?.Trim() ?? "";
            if (addr.Length > 0) parts.Add(addr);
            foreach (var extra in new[] { district, city })
            {
                var e = extra?.Trim();
                if (!string.IsNullOrEmpty(e) && addr.IndexOf(e, StringComparison.OrdinalIgnoreCase) < 0) parts.Add(e);
            }
            return string.Join(", ", parts);
        }

        public static string FullAddress(Facility f) => FullAddress(f.Address, f.DistrictName, f.City?.Name);

        // Link Google Maps ưu tiên địa chỉ chủ sân nhập (Google tìm địa chỉ VN chính xác hơn tọa độ tự dò);
        // tọa độ chỉ dùng khi không có địa chỉ, và để cắm ghim trên bản đồ "Tìm sân".
        private static string Target(Facility f)
        {
            var address = FullAddress(f);
            if (!string.IsNullOrWhiteSpace(address) || !HasCoordinates(f)) return address;
            return $"{f.Latitude!.Value.ToString(CultureInfo.InvariantCulture)},{f.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>Mở vị trí cụm sân trên Google Maps.</summary>
        public static string SearchUrl(Facility f) =>
            "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(Target(f));

        /// <summary>Chỉ đường tới cụm sân.</summary>
        public static string DirectionsUrl(Facility f) =>
            "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(Target(f));

        /// <summary>Bản đồ nhúng (iframe) không cần API key.</summary>
        public static string EmbedUrl(Facility f) =>
            "https://maps.google.com/maps?q=" + Uri.EscapeDataString(Target(f)) + "&hl=vi&z=16&output=embed";
    }
}
