using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DACN.Models
{
    /// <summary>Cụm sân khách hàng đã lưu vào danh sách yêu thích.</summary>
    [Table("FavoriteFacilities")]
    public class FavoriteFacility
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

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
