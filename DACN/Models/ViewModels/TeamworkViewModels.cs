using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DACN.Models;
using DACN.Models.Enums;

namespace DACN.Models.ViewModels
{
    public class MatchPostCardDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SportName { get; set; } = string.Empty;
        public string? SportIconCss { get; set; }
        public string VenueName { get; set; } = string.Empty;
        public string? VenueAddress { get; set; }
        public MatchPostType PostType { get; set; }
        public DateOnly MatchDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public MatchSkillLevel SkillLevel { get; set; }
        public int TotalSlots { get; set; }
        public int CurrentSlots { get; set; }
        public decimal CostPerPerson { get; set; }
        public string CostSharingNote { get; set; } = string.Empty;
        public MatchPostStatus Status { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public string CreatorPhone { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int MissingSlots => Math.Max(0, TotalSlots - CurrentSlots);
        public int ProgressPercent => TotalSlots > 0 ? (int)((float)CurrentSlots / TotalSlots * 100) : 0;
    }

    public class TeamworkIndexViewModel
    {
        public List<MatchPostCardDto> Posts { get; set; } = new();
        public List<SportType> SportTypes { get; set; } = new();
        public int? SelectedSportTypeId { get; set; }
        public MatchPostType? SelectedPostType { get; set; }
        public MatchSkillLevel? SelectedSkillLevel { get; set; }
        public string? Keyword { get; set; }

        public int TotalOpenMatches { get; set; }
        public int TotalTeammateRecruiting { get; set; }
        public int TotalOpponentSearching { get; set; }
    }

    public class CreateMatchPostViewModel
    {
        public int? BookingId { get; set; }
        public int? FacilityId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn môn thể thao")]
        [Display(Name = "Bộ môn thể thao")]
        public int SportTypeId { get; set; }

        [Required(ErrorMessage = "Tiêu đề không được để trống")]
        [StringLength(200, ErrorMessage = "Tiêu đề không quá 200 ký tự")]
        [Display(Name = "Tiêu đề bài đăng")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa điểm không được để trống")]
        [StringLength(255)]
        [Display(Name = "Tên sân / Địa điểm")]
        public string VenueName { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Địa chỉ chi tiết")]
        public string? VenueAddress { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mô tả chi tiết")]
        [Display(Name = "Mô tả & Yêu cầu")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Mục đích bài đăng")]
        public MatchPostType PostType { get; set; } = MatchPostType.FindTeammates;

        [Required(ErrorMessage = "Ngày thi đấu không được để trống")]
        [Display(Name = "Ngày thi đấu")]
        public DateOnly MatchDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        [Display(Name = "Giờ bắt đầu")]
        public TimeSpan StartTime { get; set; } = new TimeSpan(19, 0, 0);

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        [Display(Name = "Giờ kết thúc")]
        public TimeSpan EndTime { get; set; } = new TimeSpan(20, 30, 0);

        [Required]
        [Display(Name = "Trình độ yêu cầu")]
        public MatchSkillLevel SkillLevel { get; set; } = MatchSkillLevel.All;

        [Required(ErrorMessage = "Tổng số thành viên trong trận")]
        [Range(2, 50, ErrorMessage = "Số người từ 2 đến 50")]
        [Display(Name = "Tổng số thành viên của trận / đội")]
        public int TotalSlots { get; set; } = 7;

        [Required]
        [Range(1, 50, ErrorMessage = "Số thành viên hiện có")]
        [Display(Name = "Số thành viên hiện có")]
        public int CurrentSlots { get; set; } = 1;

        [Display(Name = "Chi phí / người (VNĐ)")]
        public decimal CostPerPerson { get; set; } = 50000;

        [StringLength(150)]
        [Display(Name = "Ghi chú chi phí")]
        public string CostSharingNote { get; set; } = "Chia đều tiền sân";

        [Required(ErrorMessage = "Số điện thoại / Zalo không được để trống")]
        [StringLength(20)]
        [Display(Name = "Số điện thoại / Zalo liên hệ")]
        public string ContactPhone { get; set; } = string.Empty;
    }

    public class MatchPostDetailViewModel
    {
        public MatchPost Post { get; set; } = null!;
        public bool IsCreator { get; set; }
        public bool HasJoined { get; set; }
        public bool HasPendingRequest { get; set; }
        public List<MatchJoinRequest> AcceptedMembers { get; set; } = new();
        public List<MatchJoinRequest> PendingRequests { get; set; } = new();
    }

    public class MyMatchesViewModel
    {
        public List<MatchPost> MyCreatedPosts { get; set; } = new();
        public List<MatchJoinRequest> MyJoinedRequests { get; set; } = new();
    }
}
