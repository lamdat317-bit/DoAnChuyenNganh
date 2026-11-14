using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Models
{
    [Table("Bookings")]
    [Index(nameof(CourtId), nameof(BookingDate), nameof(StartTime), nameof(EndTime))]
    [Index(nameof(CustomerId))]
    [Index(nameof(Status))]
    public class Booking
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required(ErrorMessage = "Mã khách hàng không được để trống")]
        [Display(Name = "Khách hàng")]
        public string CustomerId { get; set; } = string.Empty;

        [ForeignKey(nameof(CustomerId))]
        public virtual ApplicationUser? Customer { get; set; }

        [Required(ErrorMessage = "Mã sân không được để trống")]
        [Display(Name = "Sân")]
        public int CourtId { get; set; }

        [ForeignKey(nameof(CourtId))]
        public virtual Court? Court { get; set; }

        [Required(ErrorMessage = "Ngày đặt sân không được để trống")]
        [Display(Name = "Ngày đặt sân")]
        public DateOnly BookingDate { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        [Display(Name = "Giờ bắt đầu")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        [Display(Name = "Giờ kết thúc")]
        public TimeSpan EndTime { get; set; }

        [Required(ErrorMessage = "Tổng tiền không được để trống")]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 1000000000, ErrorMessage = "Tổng tiền không hợp lệ")]
        [Display(Name = "Tổng số tiền (VNĐ)")]
        public decimal TotalAmount { get; set; }

        [Required]
        [Display(Name = "Trạng thái đặt sân")]
        public BookingStatus Status { get; set; } = BookingStatus.Pending;

        [Required]
        [Display(Name = "Trạng thái thanh toán")]
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

        [Display(Name = "Họ và tên người đặt")]
        [StringLength(100)]
        public string? CustomerName { get; set; }

        [Display(Name = "Số điện thoại liên hệ")]
        [StringLength(20)]
        public string? CustomerPhone { get; set; }

        [Display(Name = "Ghi chú đặt sân")]
        [StringLength(500)]
        public string? Note { get; set; }

        [Display(Name = "Phương thức thanh toán")]
        [StringLength(50)]
        public string? PaymentMethod { get; set; }

        [Display(Name = "Ảnh minh chứng chuyển khoản")]
        [Column(TypeName = "nvarchar(max)")]
        public string? PaymentProofImage { get; set; }

        [Display(Name = "Mã giao dịch ngân hàng ảo")]
        [StringLength(100)]
        public string? PaymentTransactionRef { get; set; }

        [Display(Name = "Thời gian thanh toán")]
        public DateTime? PaidAt { get; set; }

        [Display(Name = "Ghi chú giao dịch")]
        [StringLength(500)]
        public string? PaymentNote { get; set; }

        [Display(Name = "Thời gian tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Timestamp]
        [Display(Name = "Khóa phiên đồng thời (Optimistic Concurrency)")]
        public byte[]? RowVersion { get; set; }

        // Navigation Properties
        public virtual ICollection<BookingServiceDetail> ServiceDetails { get; set; } = new List<BookingServiceDetail>();
    }
}
