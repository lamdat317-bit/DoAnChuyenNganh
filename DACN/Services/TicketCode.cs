using System;
using System.Security.Cryptography;

namespace DACN.Services
{
    /// <summary>
    /// Mã vé check-in: 8 ký tự ngẫu nhiên (bỏ các ký tự dễ nhầm 0/O, 1/I/L).
    /// Nội dung mã QR trên vé có dạng "DATSAN247:XXXXXXXX".
    /// </summary>
    public static class TicketCode
    {
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const string QrPrefix = "DATSAN247:";
        public const int Length = 8;

        public static string New()
        {
            Span<char> chars = stackalloc char[Length];
            for (int i = 0; i < Length; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            return new string(chars);
        }

        /// <summary>Nội dung mã hóa vào QR.</summary>
        public static string QrPayload(string code) => QrPrefix + code;

        /// <summary>Hiển thị dễ đọc: ABCD-EFGH.</summary>
        public static string Pretty(string? code) =>
            string.IsNullOrEmpty(code) ? "" : code.Length == Length ? code[..4] + "-" + code[4..] : code;

        /// <summary>
        /// Chuẩn hóa dữ liệu nhân viên quét / gõ: bỏ tiền tố QR, khoảng trắng, dấu gạch, chuyển chữ hoa.
        /// Trả về null nếu không đúng định dạng.
        /// </summary>
        public static string? Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim();
            if (s.StartsWith(QrPrefix, StringComparison.OrdinalIgnoreCase)) s = s[QrPrefix.Length..];
            s = s.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            // Hỗ trợ cả mã cũ được sinh từ NEWID (hex) cho các đơn tạo trước khi có tính năng này
            if (s.Length != Length) return null;
            foreach (var c in s)
                if (!char.IsLetterOrDigit(c) || c > 127) return null;
            return s;
        }
    }
}
