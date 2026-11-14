using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("NotificationConfigs")]
    [Index(nameof(FacilityId), IsUnique = true)]
    public class NotificationConfig
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Cụm sân")]
        public int FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [StringLength(100)]
        [Display(Name = "Telegram Chat ID")]
        public string? TelegramChatId { get; set; }

        [StringLength(200)]
        [Display(Name = "Telegram Bot Token")]
        public string? TelegramBotToken { get; set; }

        [StringLength(500)]
        [Display(Name = "Zalo Official Account Token")]
        public string? ZaloOaToken { get; set; }

        [Display(Name = "Bật thông báo khi có đơn mới")]
        public bool NotifyOnNewBooking { get; set; } = true;
    }
}
