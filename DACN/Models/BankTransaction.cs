using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;

namespace DACN.Models
{
    /// <summary>
    /// Một giao dịch chuyển tiền giữa 2 tài khoản ảo (nạp tiền thì không có tài khoản nguồn).
    /// UNIQUE(BookingId, Type): mỗi đơn chỉ thanh toán 1 lần và hoàn tiền 1 lần.
    /// </summary>
    [Table("BankTransactions")]
    public class BankTransaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>Tài khoản bị trừ tiền; null khi nạp tiền.</summary>
        public int? FromAccountId { get; set; }

        [ForeignKey(nameof(FromAccountId))]
        public virtual BankAccount? FromAccount { get; set; }

        /// <summary>Tài khoản được cộng tiền.</summary>
        public int ToAccountId { get; set; }

        [ForeignKey(nameof(ToAccountId))]
        public virtual BankAccount? ToAccount { get; set; }

        /// <summary>Số tiền (VND, số nguyên, &gt; 0).</summary>
        public long Amount { get; set; }

        public BankTransactionType Type { get; set; }

        public int? BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public virtual Booking? Booking { get; set; }

        [StringLength(300)]
        public string? Note { get; set; }

        /// <summary>Số dư tài khoản nguồn ngay sau giao dịch (để in sao kê).</summary>
        public long? FromBalanceAfter { get; set; }

        /// <summary>Số dư tài khoản đích ngay sau giao dịch.</summary>
        public long ToBalanceAfter { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
