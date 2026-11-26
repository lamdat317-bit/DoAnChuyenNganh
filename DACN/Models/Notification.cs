using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    /// <summary>Thông báo trong ứng dụng (chuông thông báo trên thanh menu).</summary>
    [Index(nameof(UserId), nameof(IsRead))]
    public class Notification
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [Required, StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Message { get; set; }

        /// <summary>Đường dẫn mở khi bấm vào thông báo.</summary>
        [StringLength(300)]
        public string? Url { get; set; }

        /// <summary>Tên icon Bootstrap Icons, vd "bi-calendar-check".</summary>
        [StringLength(50)]
        public string Icon { get; set; } = "bi-bell";

        /// <summary>Màu icon-bubble: green / amber / red / blue / violet / slate.</summary>
        [StringLength(20)]
        public string Tone { get; set; } = "green";

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
