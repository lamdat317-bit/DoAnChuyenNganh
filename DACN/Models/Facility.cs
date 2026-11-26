using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("Facilities")]
    [Index(nameof(OwnerId))]
    [Index(nameof(SportTypeId))]
    [Index(nameof(CityId))]
    [Index(nameof(Name))]
    public class Facility
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Mã chủ sân không được để trống")]
        [Display(Name = "Chủ sân")]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey(nameof(OwnerId))]
        public virtual ApplicationUser? Owner { get; set; }

        [Required(ErrorMessage = "Tên cụm sân không được để trống")]
        [StringLength(200, ErrorMessage = "Tên cụm sân không quá 200 ký tự")]
        [Display(Name = "Tên cụm sân")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa chỉ cụm sân không được để trống")]
        [StringLength(300, ErrorMessage = "Địa chỉ không vượt quá 300 ký tự")]
        [Display(Name = "Địa chỉ chi tiết")]
        public string Address { get; set; } = string.Empty;

        [Display(Name = "Tỉnh / Thành phố")]
        public int? CityId { get; set; }

        [ForeignKey(nameof(CityId))]
        public virtual CityProvince? City { get; set; }

        [StringLength(100)]
        [Display(Name = "Quận / Huyện")]
        public string? DistrictName { get; set; }

        [Display(Name = "Vĩ độ (GPS Latitude)")]
        [Range(-90.0, 90.0, ErrorMessage = "Vĩ độ phải trong khoảng từ -90 đến 90")]
        public double? Latitude { get; set; }

        [Display(Name = "Kinh độ (GPS Longitude)")]
        [Range(-180.0, 180.0, ErrorMessage = "Kinh độ phải trong khoảng từ -180 đến 180")]
        public double? Longitude { get; set; }

        [StringLength(2000)]
        [Display(Name = "Mô tả chi tiết và tiện ích")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Giờ mở cửa không được để trống")]
        [Display(Name = "Giờ mở cửa")]
        public TimeSpan OpeningTime { get; set; } = new TimeSpan(6, 0, 0); // 06:00

        [Required(ErrorMessage = "Giờ đóng cửa không được để trống")]
        [Display(Name = "Giờ đóng cửa")]
        public TimeSpan ClosingTime { get; set; } = new TimeSpan(23, 0, 0); // 23:00

        [Required(ErrorMessage = "Loại môn thể thao không được để trống")]
        [Display(Name = "Môn thể thao")]
        public int SportTypeId { get; set; }

        [ForeignKey(nameof(SportTypeId))]
        public virtual SportType? SportType { get; set; }

        // Navigation Properties
        /// <summary>Kiểm duyệt của Admin: chỉ cụm sân Approved mới hiển thị cho khách.</summary>
        [Display(Name = "Trạng thái duyệt")]
        public FacilityApprovalStatus ApprovalStatus { get; set; } = FacilityApprovalStatus.Pending;

        [Display(Name = "Lý do từ chối")]
        [StringLength(500)]
        public string? ApprovalNote { get; set; }

        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Mở qua nửa đêm khi giờ đóng cửa nhỏ hơn hoặc bằng giờ mở cửa (vd 16:00 → 02:00).
        /// Khi đó mỗi ngày gồm 2 đoạn hoạt động: 00:00 → giờ đóng và giờ mở → 24:00.
        /// </summary>
        [NotMapped]
        public bool IsOvernight => ClosingTime <= OpeningTime;

        public virtual ICollection<Court> Courts { get; set; } = new List<Court>();
        public virtual ICollection<FacilityService> Services { get; set; } = new List<FacilityService>();
        public virtual NotificationConfig? NotificationConfig { get; set; }
        public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();
    }
}
