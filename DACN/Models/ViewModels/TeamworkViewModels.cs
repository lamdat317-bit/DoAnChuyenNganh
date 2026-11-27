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

        // ===== Kèo Tìm đối (đội gặp đội) =====
        public string? TeamName { get; set; }
        /// <summary>Tên đội đã được chốt làm đối (null nếu chưa chốt).</summary>
        public string? OpponentTeamName { get; set; }
        /// <summary>Số đội đang xin nhận kèo, chờ chủ kèo chốt.</summary>
        public int PendingTeamCount { get; set; }
        public bool IsOpponentSearch => PostType == MatchPostType.FindOpponent;
        public int OpponentSize => Math.Max(1, TotalSlots - CurrentSlots);
        public string HostTeamName => DACN.Services.MatchTeams.HostTeamName(TeamName, CreatorName);
    }

    public class TeamworkIndexViewModel
    {
        public List<MatchPostCardDto> Posts { get; set; } = new();
        public List<SportType> SportTypes { get; set; } = new();
        public int? SelectedSportTypeId { get; set; }
        public MatchPostType? SelectedPostType { get; set; }
        public MatchSkillLevel? SelectedSkillLevel { get; set; }
        public DateOnly? SelectedDate { get; set; }
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

        /// <summary>Bắt buộc khi Tìm đối; không bắt buộc khi Tìm đồng đội.</summary>
        [StringLength(100, ErrorMessage = "Tên đội không quá 100 ký tự")]
        [Display(Name = "Tên đội của bạn")]
        public string? TeamName { get; set; }

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
        /// <summary>Yêu cầu của người đang xem (nếu có), để rút lại.</summary>
        public MatchJoinRequest? MyRequest { get; set; }

        public bool IsOpponentSearch => Post.PostType == MatchPostType.FindOpponent;
        /// <summary>Kèo Tìm đối: đội đã được chốt làm đối thủ.</summary>
        public MatchJoinRequest? AcceptedOpponent => IsOpponentSearch ? AcceptedMembers.FirstOrDefault() : null;
    }

    public class MyMatchesViewModel
    {
        public List<MatchPost> MyCreatedPosts { get; set; } = new();
        public List<MatchJoinRequest> MyJoinedRequests { get; set; } = new();
    }
}
