using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    /// <summary>Cụm sân tài khoản đã xem gần đây (mỗi tài khoản 1 danh sách riêng, dùng được trên mọi máy).</summary>
    [Table("RecentlyViewedFacilities")]
    public class RecentlyViewedFacility
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }

        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}
