using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using Microsoft.EntityFrameworkCore;

namespace DACN.Data
{
    /// <summary>
    /// Chuyển các ảnh biên lai cũ (file trong wwwroot/uploads/payment_proofs) vào bảng SQL BookingPaymentProofs.
    /// Chạy mỗi lần khởi động nhưng chỉ xử lý những đơn còn trỏ tới file -> an toàn khi chạy lại.
    /// </summary>
    public static class PaymentProofImporter
    {
        private const string LegacyPrefix = "/uploads/payment_proofs/";

        public static async Task<int> ImportAsync(ApplicationDbContext context, string webRootPath)
        {
            var legacy = await context.Bookings
                .Where(b => b.PaymentProofImage != null && b.PaymentProofImage.StartsWith(LegacyPrefix))
                .ToListAsync();
            if (legacy.Count == 0) return 0;

            var existingIds = await context.BookingPaymentProofs
                .Select(p => p.BookingId).ToListAsync();

            var imported = 0;
            foreach (var booking in legacy)
            {
                var fileName = Path.GetFileName(booking.PaymentProofImage!);
                var filePath = Path.Combine(webRootPath, "uploads", "payment_proofs", fileName);
                if (!File.Exists(filePath)) continue; // file đã mất -> giữ nguyên đường dẫn cũ

                if (!existingIds.Contains(booking.Id))
                {
                    context.BookingPaymentProofs.Add(new BookingPaymentProof
                    {
                        BookingId = booking.Id,
                        Data = await File.ReadAllBytesAsync(filePath),
                        ContentType = ContentTypeOf(fileName),
                        OriginalFileName = fileName,
                        CreatedAt = booking.PaidAt ?? File.GetCreationTimeUtc(filePath)
                    });
                    existingIds.Add(booking.Id);
                }
                booking.PaymentProofImage = $"/Booking/ProofImage?bookingId={booking.Id}";
                imported++;
            }

            await context.SaveChangesAsync();
            return imported;
        }

        private static string ContentTypeOf(string fileName) =>
            Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "image/png"
            };
    }
}
