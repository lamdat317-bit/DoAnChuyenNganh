using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    /// <summary>
    /// Ảnh thật của cụm sân do chủ sân tải lên, lưu trong SQL Server (varbinary(max)).
    /// Ảnh được phục vụ qua /Booking/FacilityImage/{id}. Khi truy vấn danh sách, chỉ chọn Id / FacilityId
    /// (không đọc cột Data) để không tải dữ liệu ảnh thừa.
    /// </summary>
    [Table("FacilityImages")]
    public class FacilityImage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [Required]
        public byte[] Data { get; set; } = Array.Empty<byte>();

        [Required]
        [StringLength(50)]
        public string ContentType { get; set; } = "image/jpeg";

        [StringLength(255)]
        public string? OriginalFileName { get; set; }

        /// <summary>Ảnh bìa hiển thị trên thẻ sân và đầu trang chi tiết.</summary>
        public bool IsCover { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

namespace DACN.Models
{
    /// <summary>Thông tin ảnh hiển thị ở trang quản lý (không kèm dữ liệu ảnh).</summary>
    public record FacilityPhotoItem(int Id, bool IsCover, string? FileName, long Size);
}
