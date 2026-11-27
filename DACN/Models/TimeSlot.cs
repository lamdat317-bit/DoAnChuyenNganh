using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("TimeSlots")]
    [Index(nameof(CourtId))]
    [Index(nameof(CourtId), nameof(StartTime), nameof(EndTime))]
    public class TimeSlot
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Mã sân không được để trống")]
        [Display(Name = "Sân áp dụng")]
        public int CourtId { get; set; }

        [ForeignKey(nameof(CourtId))]
        public virtual Court? Court { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        [Display(Name = "Giờ bắt đầu")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        [Display(Name = "Giờ kết thúc")]
        public TimeSpan EndTime { get; set; }

        [Required(ErrorMessage = "Đơn giá không được để trống")]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 100000000, ErrorMessage = "Giá tiền phải từ 0 trở lên")]
        [Display(Name = "Đơn giá / Khung giờ (VNĐ)")]
        public decimal Price { get; set; }

        [Display(Name = "Khung giờ cao điểm")]
        public bool IsPeakHour { get; set; } = false;
    }
}
