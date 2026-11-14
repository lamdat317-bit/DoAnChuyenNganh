using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("CustomerProfiles")]
    [Index(nameof(ApplicationUserId), IsUnique = true)]
    public class CustomerProfile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Mã tài khoản")]
        public string ApplicationUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(ApplicationUserId))]
        public virtual ApplicationUser? Customer { get; set; }

        [Display(Name = "Tổng số lượt đặt sân")]
        public int TotalBookingsCount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Tổng số tiền đã chi tiêu (VNĐ)")]
        public decimal TotalSpentAmount { get; set; } = 0;

        [Required]
        [Display(Name = "Hạng thành viên")]
        public VipTier VipTier { get; set; } = VipTier.Standard;

        [Display(Name = "Cập nhật lần cuối")]
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
