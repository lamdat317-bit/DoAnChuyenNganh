using System;
using System.ComponentModel.DataAnnotations;

namespace DACN.Models.ViewModels
{
    public class CreateFacilityViewModel
    {
        [Required(ErrorMessage = "Tên cụm sân không được để trống")]
        [StringLength(200, ErrorMessage = "Tên cụm sân không quá 200 ký tự")]
        [Display(Name = "Tên cụm sân thể thao")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn môn thể thao")]
        [Display(Name = "Môn thể thao chính")]
        public int SportTypeId { get; set; }

        [Display(Name = "Tỉnh / Thành phố")]
        public int? CityId { get; set; }

        [StringLength(100)]
        [Display(Name = "Quận / Huyện")]
        public string? DistrictName { get; set; }

        [Required(ErrorMessage = "Địa chỉ chi tiết không được để trống")]
        [StringLength(300)]
        [Display(Name = "Địa chỉ chi tiết")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Giờ mở cửa không được để trống")]
        [Display(Name = "Giờ mở cửa")]
        public TimeSpan OpeningTime { get; set; } = new TimeSpan(6, 0, 0);

        [Required(ErrorMessage = "Giờ đóng cửa không được để trống")]
        [Display(Name = "Giờ đóng cửa")]
        public TimeSpan ClosingTime { get; set; } = new TimeSpan(23, 0, 0);

        [StringLength(2000)]
        [Display(Name = "Mô tả cụm sân & Tiện ích")]
        public string? Description { get; set; }

        [Display(Name = "Tọa độ Vĩ độ (Latitude)")]
        public double? Latitude { get; set; } = 21.0285;

        [Display(Name = "Tọa độ Kinh độ (Longitude)")]
        public double? Longitude { get; set; } = 105.8542;

        [Required(ErrorMessage = "Tên sân con ban đầu không được để trống")]
        [Display(Name = "Tên sân con khởi tạo")]
        public string InitialCourtName { get; set; } = "Sân 1 (Sân tiêu chuẩn)";

        [Display(Name = "Đơn giá mặc định (VNĐ/giờ)")]
        public decimal InitialPrice { get; set; } = 180000m;
    }

    public class EditFacilityViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên cụm sân không được để trống")]
        [StringLength(200)]
        [Display(Name = "Tên cụm sân")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn môn thể thao")]
        [Display(Name = "Môn thể thao chính")]
        public int SportTypeId { get; set; }

        [Display(Name = "Tỉnh / Thành phố")]
        public int? CityId { get; set; }

        [StringLength(100)]
        [Display(Name = "Quận / Huyện")]
        public string? DistrictName { get; set; }

        [Required(ErrorMessage = "Địa chỉ chi tiết không được để trống")]
        [StringLength(300)]
        [Display(Name = "Địa chỉ")]
        public string Address { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Giờ mở cửa")]
        public TimeSpan OpeningTime { get; set; }

        [Required]
        [Display(Name = "Giờ đóng cửa")]
        public TimeSpan ClosingTime { get; set; }

        [StringLength(2000)]
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        [Display(Name = "Tọa độ Vĩ độ (Latitude)")]
        public double? Latitude { get; set; }

        [Display(Name = "Tọa độ Kinh độ (Longitude)")]
        public double? Longitude { get; set; }
    }
}

