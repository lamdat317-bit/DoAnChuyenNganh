using DACN.Models.Enums;
using DACN.Services;
using Microsoft.EntityFrameworkCore;

namespace DACN.Data
{
    /// <summary>
    /// Dữ liệu mẫu cho ví (chỉ Development): nạp sẵn tiền cho các tài khoản khách mẫu để demo thanh toán ngay.
    /// Chỉ nạp cho ví chưa có giao dịch nào, nên chạy lại nhiều lần không bị cộng dồn.
    /// </summary>
    public static class DemoWalletSeeder
    {
        private static readonly (string Email, long Amount)[] Wallets =
        {
            ("customer@gmail.com", 2_000_000),
            ("minhanh@gmail.com", 1_000_000),
            ("thuha@gmail.com", 500_000),
        };

        public static async Task<int> SeedAsync(ApplicationDbContext db, IBankService bank)
        {
            int funded = 0;
            foreach (var (email, amount) in Wallets)
            {
                var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstOrDefaultAsync();
                if (userId == null) continue;
                var account = await bank.EnsureAccountAsync(userId);
                bool hasHistory = await db.BankTransactions.AnyAsync(t => t.FromAccountId == account.Id || t.ToAccountId == account.Id);
                if (hasHistory) continue;
                await bank.TransferAsync(null, account.Id, amount, BankTransactionType.Deposit, null, "Số dư ban đầu (dữ liệu mẫu)");
                funded++;
            }
            return funded;
        }
    }
}
