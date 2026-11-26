using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("FacilityServices")]
    [Index(nameof(FacilityId))]
    public class FacilityService
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Cụm sân")]
        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [Required(ErrorMessage = "Tên dịch vụ/tiện ích không được để trống")]
        [StringLength(150)]
        [Display(Name = "Tên dịch vụ/tiện ích")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 10000000, ErrorMessage = "Đơn giá không hợp lệ")]
        [Display(Name = "Đơn giá (VNĐ)")]
        public decimal Price { get; set; } = 0;

        [StringLength(50)]
        [Display(Name = "Đơn vị tính")]
        public string Unit { get; set; } = "Lần";

        [Display(Name = "Đang áp dụng")]
        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public virtual ICollection<BookingServiceDetail> BookingServiceDetails { get; set; } = new List<BookingServiceDetail>();
    }
}
