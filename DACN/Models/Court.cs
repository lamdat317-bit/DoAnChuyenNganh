using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("Courts")]
    [Index(nameof(FacilityId))]
    public class Court
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Mã cụm sân không được để trống")]
        [Display(Name = "Cụm sân")]
        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [Required(ErrorMessage = "Tên sân không được để trống")]
        [StringLength(100, ErrorMessage = "Tên sân không vượt quá 100 ký tự")]
        [Display(Name = "Tên sân con")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Trạng thái hoạt động")]
        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public virtual ICollection<TimeSlot> TimeSlots { get; set; } = new List<TimeSlot>();
        public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}
