using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DACN.Models.Enums;
using Microsoft.AspNetCore.Identity;

namespace DACN.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required(ErrorMessage = "Họ và tên không được để trống")]
        [StringLength(100, ErrorMessage = "Họ và tên không vượt quá 100 ký tự")]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Vai trò hệ thống")]
        public UserRole Role { get; set; } = UserRole.Customer;

        [Display(Name = "Ngày tạo tài khoản")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public virtual ICollection<Facility> Facilities { get; set; } = new List<Facility>();
        public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public virtual CustomerProfile? Profile { get; set; }
    }
}
