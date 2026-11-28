using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DACN.Models.Enums;

namespace DACN.Models
{
    /// <summary>
    /// Tài khoản ngân hàng ảo (ví). Mỗi người dùng có đúng 1 tài khoản; thêm 1 tài khoản SYSTEM của nền tảng.
    /// Số dư lưu bằng SỐ NGUYÊN (đồng VND), không âm (CHECK constraint trong DB).
    /// </summary>
    [Table("BankAccounts")]
    public class BankAccount
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Số tài khoản ảo, vd 970412345678 (duy nhất).</summary>
        [Required]
        [StringLength(20)]
        public string AccountNo { get; set; } = string.Empty;

        public BankOwnerType OwnerType { get; set; }

        /// <summary>Id người dùng sở hữu; null với tài khoản SYSTEM.</summary>
        [StringLength(450)]
        public string? OwnerId { get; set; }

        [ForeignKey(nameof(OwnerId))]
        public virtual ApplicationUser? Owner { get; set; }

        /// <summary>Số dư (VND, số nguyên, ≥ 0).</summary>
        public long Balance { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
