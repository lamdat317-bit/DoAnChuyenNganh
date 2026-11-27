using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;

namespace DACN.Models
{
    [Table("MatchPosts")]
    public class MatchPost
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Người tạo kèo")]
        public string CreatorId { get; set; } = string.Empty;

        [ForeignKey(nameof(CreatorId))]
        public virtual ApplicationUser? Creator { get; set; }

        [Required]
        [Display(Name = "Môn thể thao")]
        public int SportTypeId { get; set; }

        [ForeignKey(nameof(SportTypeId))]
        public virtual SportType? SportType { get; set; }

        [Display(Name = "Cụm sân")]
        public int? FacilityId { get; set; }

        [ForeignKey(nameof(FacilityId))]
        public virtual Facility? Facility { get; set; }

        [Display(Name = "Đơn đặt sân liên kết")]
        public int? BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public virtual Booking? Booking { get; set; }

        [Required(ErrorMessage = "Tiêu đề không được để trống")]
        [StringLength(200, ErrorMessage = "Tiêu đề không quá 200 ký tự")]
        [Display(Name = "Tiêu đề bài đăng")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa điểm thi đấu không được để trống")]
        [StringLength(255)]
        [Display(Name = "Địa điểm / Tên sân")]
        public string VenueName { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Địa chỉ chi tiết")]
        public string? VenueAddress { get; set; }

        [Required(ErrorMessage = "Mô tả không được để trống")]
        [Display(Name = "Mô tả & Yêu cầu")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Loại kèo")]
        public MatchPostType PostType { get; set; } = MatchPostType.FindTeammates;

        [Required(ErrorMessage = "Ngày thi đấu không được để trống")]
        [Display(Name = "Ngày thi đấu")]
        public DateOnly MatchDate { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        [Display(Name = "Giờ bắt đầu")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        [Display(Name = "Giờ kết thúc")]
        public TimeSpan EndTime { get; set; }

        [Required]
        [Display(Name = "Trình độ yêu cầu")]
        public MatchSkillLevel SkillLevel { get; set; } = MatchSkillLevel.All;

        [Required]
        [Range(1, 50, ErrorMessage = "Tổng số người cần tuyển/tham gia không hợp lệ")]
        [Display(Name = "Tổng số thành viên")]
        public int TotalSlots { get; set; } = 7;

        [Required]
        [Display(Name = "Số thành viên hiện có")]
        public int CurrentSlots { get; set; } = 1;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Chi phí / người (VNĐ)")]
        public decimal CostPerPerson { get; set; } = 0;

        [StringLength(150)]
        [Display(Name = "Hình thức chia sẻ chi phí")]
        public string CostSharingNote { get; set; } = "Chia đều tiền sân";

        [Required(ErrorMessage = "Số điện thoại / Zalo liên hệ không được để trống")]
        [StringLength(20)]
        [Display(Name = "Số điện thoại / Zalo")]
        public string ContactPhone { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Trạng thái kèo")]
        public MatchPostStatus Status { get; set; } = MatchPostStatus.Open;

        [Display(Name = "Ngày tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public virtual ICollection<MatchJoinRequest> JoinRequests { get; set; } = new List<MatchJoinRequest>();
    }
}
