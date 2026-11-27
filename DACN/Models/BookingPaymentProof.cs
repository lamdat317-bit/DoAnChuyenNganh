using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    /// <summary>
    /// Ảnh biên lai chuyển khoản của đơn đặt sân, lưu trực tiếp trong SQL Server (varbinary(max))
    /// thay vì file trong wwwroot. Tách bảng riêng để khi tải danh sách đơn không phải đọc kèm dữ liệu ảnh.
    /// Ảnh được phục vụ qua /Booking/ProofImage?bookingId=...
    /// </summary>
    public class BookingPaymentProof
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int BookingId { get; set; }

        [Required]
        public byte[] Data { get; set; } = Array.Empty<byte>();

        [Required]
        [StringLength(100)]
        public string ContentType { get; set; } = "image/png";

        [StringLength(255)]
        public string? OriginalFileName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Booking? Booking { get; set; }
    }
}
