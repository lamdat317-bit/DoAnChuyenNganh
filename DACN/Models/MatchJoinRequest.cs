using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;

namespace DACN.Models
{
    [Table("MatchJoinRequests")]
    public class MatchJoinRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Bài đăng kèo")]
        public int MatchPostId { get; set; }

        [ForeignKey(nameof(MatchPostId))]
        public virtual MatchPost? MatchPost { get; set; }

        [Required]
        [Display(Name = "Người đăng ký")]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }

        [StringLength(100)]
        [Display(Name = "Vị trí sở trường")]
        public string? PreferredPosition { get; set; }

        [StringLength(20)]
        [Display(Name = "Số điện thoại liên hệ")]
        public string? ContactPhone { get; set; }

        /// <summary>Kèo Tìm đối: tên đội đang xin nhận kèo (cả đội đăng ký, không phải từng người).</summary>
        [StringLength(100)]
        [Display(Name = "Tên đội")]
        public string? TeamName { get; set; }

        /// <summary>Kèo Tìm đối: số người của đội nhận kèo.</summary>
        [Range(1, 50)]
        [Display(Name = "Số người của đội")]
        public int? TeamSize { get; set; }

        [StringLength(300)]
        [Display(Name = "Lời nhắn gửi đội trưởng")]
        public string? Message { get; set; }

        [Required]
        [Display(Name = "Trạng thái phê duyệt")]
        public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;

        [Display(Name = "Thời gian gửi")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
