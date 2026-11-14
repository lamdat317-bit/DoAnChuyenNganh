using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("BookingServiceDetails")]
    [Index(nameof(BookingId))]
    [Index(nameof(FacilityServiceId))]
    public class BookingServiceDetail
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Đơn đặt sân")]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public virtual Booking? Booking { get; set; }

        [Required]
        [Display(Name = "Dịch vụ đính kèm")]
        public int FacilityServiceId { get; set; }

        [ForeignKey(nameof(FacilityServiceId))]
        public virtual FacilityService? FacilityService { get; set; }

        [Required]
        [Range(1, 1000, ErrorMessage = "Số lượng phải từ 1 trở lên")]
        [Display(Name = "Số lượng")]
        public int Quantity { get; set; } = 1;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Thành tiền (VNĐ)")]
        public decimal SubTotal { get; set; }
    }
}
