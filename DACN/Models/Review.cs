using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    /// <summary>
    /// Đánh giá của khách hàng cho một cụm sân. Mỗi đơn đặt sân đã hoàn thành chỉ được đánh giá 1 lần.
    /// </summary>
    [Table("Reviews")]
    [Index(nameof(FacilityId))]
    [Index(nameof(BookingId), IsUnique = true)]
    public class Review
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [Required]
        public string CustomerId { get; set; } = string.Empty;

        [ForeignKey(nameof(CustomerId))]
        public virtual ApplicationUser? Customer { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public virtual Booking? Booking { get; set; }

        [Range(1, 5, ErrorMessage = "Số sao phải từ 1 đến 5")]
        [Display(Name = "Số sao")]
        public int Rating { get; set; }

        [StringLength(1000, ErrorMessage = "Nhận xét không quá 1000 ký tự")]
        [Display(Name = "Nhận xét")]
        public string? Comment { get; set; }

        [StringLength(1000)]
        [Display(Name = "Phản hồi của chủ sân")]
        public string? OwnerReply { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
