using System;
using System.ComponentModel.DataAnnotations;

namespace DACN.Models
{
    /// <summary>
    /// Nhật ký email hệ thống gửi đi (quên mật khẩu, nhắc lịch...). Nếu chưa cấu hình SMTP,
    /// email vẫn được lưu ở đây với trạng thái "chưa gửi" để Admin / người chấm có thể xem.
    /// </summary>
    public class EmailMessage
    {
        public int Id { get; set; }

        [Required, StringLength(256)]
        public string To { get; set; } = string.Empty;

        [Required, StringLength(250)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string HtmlBody { get; set; } = string.Empty;

        public bool Sent { get; set; }

        [StringLength(500)]
        public string? Error { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
