using DACN.Data;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Admin.Controllers
{
    public class AdminBankViewModel
    {
        public BankAccount System { get; set; } = null!;
        public List<BankAccount> TopAccounts { get; set; } = new();
        public long TotalUserBalance { get; set; }
        public long TotalOwnerBalance { get; set; }
        public int AccountCount { get; set; }
        public long TotalDeposits { get; set; }
        public long TotalPayments { get; set; }
        public long TotalRefunds { get; set; }
        public List<BankTransaction> Transactions { get; set; } = new();
        public int TransactionCount { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public BankTransactionType? Type { get; set; }
        public string? Query { get; set; }
    }

    /// <summary>Sổ cái ngân hàng ảo: số dư tài khoản hệ thống, các ví và TOÀN BỘ giao dịch (chỉ Admin).</summary>
    [Area("Admin")]
    [Authorize(Roles = AppRoles.Admin)]
    public class BankController : Controller
    {
        private const int PageSize = 20;
        private readonly ApplicationDbContext _db;
        private readonly IBankService _bank;

        public BankController(ApplicationDbContext db, IBankService bank)
        {
            _db = db;
            _bank = bank;
        }

        // GET /Admin/Bank?type=Payment&q=9704...&page=2
        public async Task<IActionResult> Index(BankTransactionType? type, string? q, int page = 1)
        {
            var vm = new AdminBankViewModel { Type = type, Query = q?.Trim() };
            vm.System = await _bank.GetSystemAccountAsync();

            var accounts = _db.BankAccounts.AsNoTracking();
            vm.AccountCount = await accounts.CountAsync();
            vm.TotalUserBalance = await accounts.Where(a => a.OwnerType == BankOwnerType.User).SumAsync(a => (long?)a.Balance) ?? 0;
            vm.TotalOwnerBalance = await accounts.Where(a => a.OwnerType == BankOwnerType.CourtOwner).SumAsync(a => (long?)a.Balance) ?? 0;
            vm.TopAccounts = await accounts.Include(a => a.Owner)
                .Where(a => a.OwnerType != BankOwnerType.System)
                .OrderByDescending(a => a.Balance).Take(8).ToListAsync();

            var all = _db.BankTransactions.AsNoTracking();
            vm.TotalDeposits = await all.Where(t => t.Type == BankTransactionType.Deposit).SumAsync(t => (long?)t.Amount) ?? 0;
            vm.TotalPayments = await all.Where(t => t.Type == BankTransactionType.Payment).SumAsync(t => (long?)t.Amount) ?? 0;
            vm.TotalRefunds = await all.Where(t => t.Type == BankTransactionType.Refund).SumAsync(t => (long?)t.Amount) ?? 0;

            var query = all;
            if (type.HasValue) query = query.Where(t => t.Type == type.Value);
            if (!string.IsNullOrEmpty(vm.Query))
            {
                var s = vm.Query.Replace(" ", "").Replace("#BK-", "", StringComparison.OrdinalIgnoreCase);
                int.TryParse(s, out var bookingId);
                query = query.Where(t => t.BookingId == bookingId
                    || (t.FromAccount != null && (t.FromAccount.AccountNo == s || t.FromAccount.Owner!.Email!.Contains(s)))
                    || t.ToAccount!.AccountNo == s || t.ToAccount.Owner!.Email!.Contains(s));
            }

            vm.TransactionCount = await query.CountAsync();
            vm.TotalPages = Math.Max(1, (int)Math.Ceiling(vm.TransactionCount / (double)PageSize));
            vm.Page = Math.Clamp(page, 1, vm.TotalPages);
            vm.Transactions = await query
                .Include(t => t.FromAccount).ThenInclude(a => a!.Owner)
                .Include(t => t.ToAccount).ThenInclude(a => a!.Owner)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Skip((vm.Page - 1) * PageSize).Take(PageSize)
                .ToListAsync();
            return View(vm);
        }
    }
}
