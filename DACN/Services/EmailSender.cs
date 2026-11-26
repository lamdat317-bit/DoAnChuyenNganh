using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DACN.Services
{
    public interface IAppEmailSender
    {
        /// <summary>True khi đã cấu hình SMTP trong appsettings.json (mục "Smtp").</summary>
        bool IsConfigured { get; }

        /// <summary>Lưu email vào bảng EmailMessages và gửi qua SMTP nếu đã cấu hình. Trả về true nếu gửi thành công.</summary>
        Task<bool> SendAsync(string to, string subject, string htmlBody);
    }

    /// <summary>
    /// Gửi email qua SMTP (vd Gmail: smtp.gmail.com, cổng 587, dùng "Mật khẩu ứng dụng").
    /// Cấu hình trong appsettings.json:
    /// "Smtp": { "Host": "smtp.gmail.com", "Port": 587, "User": "...", "Password": "...", "From": "...", "FromName": "SanBong.vn" }
    /// Chưa cấu hình thì email chỉ được lưu lại trong bảng EmailMessages (Admin xem tại Quản trị → Hộp thư hệ thống).
    /// </summary>
    public class SmtpEmailSender : IAppEmailSender
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(ApplicationDbContext context, IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["Smtp:Host"]);

        public async Task<bool> SendAsync(string to, string subject, string htmlBody)
        {
            var log = new EmailMessage { To = to, Subject = subject, HtmlBody = htmlBody };
            _context.EmailMessages.Add(log);

            if (IsConfigured)
            {
                try
                {
                    var from = _config["Smtp:From"] ?? _config["Smtp:User"] ?? "no-reply@sanbong.vn";
                    using var message = new MailMessage
                    {
                        From = new MailAddress(from, _config["Smtp:FromName"] ?? "SanBong.vn"),
                        Subject = subject,
                        Body = htmlBody,
                        IsBodyHtml = true
                    };
                    message.To.Add(to);

                    using var client = new SmtpClient(_config["Smtp:Host"], int.TryParse(_config["Smtp:Port"], out var port) ? port : 587)
                    {
                        EnableSsl = !string.Equals(_config["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase),
                        Credentials = string.IsNullOrEmpty(_config["Smtp:User"])
                            ? null
                            : new NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"]),
                        Timeout = 15000
                    };
                    await client.SendMailAsync(message);
                    log.Sent = true;
                }
                catch (Exception ex)
                {
                    log.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                    _logger.LogWarning(ex, "Không gửi được email tới {To}", to);
                }
            }
            else
            {
                log.Error = "Chưa cấu hình SMTP — email chỉ được lưu trong hệ thống.";
            }

            await _context.SaveChangesAsync();
            return log.Sent;
        }
    }
}
