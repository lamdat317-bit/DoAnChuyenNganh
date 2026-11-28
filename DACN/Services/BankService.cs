using System.Data;
using System.Security.Cryptography;
using DACN.Data;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services
{
    /// <summary>Lỗi nghiệp vụ khi chuyển tiền (không đủ số dư, số tiền sai...). Ném ra để transaction rollback.</summary>
    public class BankException : Exception
    {
        public BankException(string message) : base(message) { }
    }

    /// <summary>Tài khoản nguồn không đủ số dư.</summary>
    public class InsufficientFundsException : BankException
    {
        public long Balance { get; }
        public long Required { get; }
        public InsufficientFundsException(long balance, long required)
            : base($"Số dư không đủ: còn {balance:N0} ₫, cần {required:N0} ₫.")
        {
            Balance = balance;
            Required = required;
        }
    }

    public record BankStatement(BankAccount Account, List<BankTransaction> Items, int TotalCount, int Page, int PageSize)
    {
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }

    public interface IBankService
    {
        /// <summary>Lấy (tạo nếu chưa có) tài khoản của người dùng.</summary>
        Task<BankAccount> EnsureAccountAsync(string userId);
        Task<BankAccount> GetSystemAccountAsync();
        /// <summary>Tạo bù tài khoản cho mọi người dùng chưa có. Trả về số tài khoản đã tạo.</summary>
        Task<int> BackfillAccountsAsync();

        /// <summary>Nạp tiền giả lập vào ví người dùng.</summary>
        Task<BankTransaction> DepositAsync(string userId, long amount);

        /// <summary>
        /// Chuyển tiền dùng chung cho mọi nghiệp vụ. Khóa dòng 2 tài khoản (UPDLOCK), kiểm tra số dư, trừ/cộng và ghi giao dịch.
        /// Nếu đang có transaction (vd thanh toán còn cập nhật booking) thì chạy trong transaction đó; nếu không tự mở transaction riêng.
        /// Ném <see cref="BankException"/> khi lỗi nghiệp vụ.
        /// </summary>
        Task<BankTransaction> TransferAsync(int? fromAccountId, int toAccountId, long amount, BankTransactionType type, int? bookingId, string? note);

        Task<BankStatement> GetStatementAsync(int accountId, int page, int pageSize);
    }

    public class BankService : IBankService
    {
        public const string SystemAccountNo = "970400000001";

        private readonly ApplicationDbContext _db;
        private readonly ILogger<BankService> _logger;

        public BankService(ApplicationDbContext db, ILogger<BankService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ===================== Tài khoản =====================

        public async Task<BankAccount> EnsureAccountAsync(string userId)
        {
            var existing = await _db.BankAccounts.FirstOrDefaultAsync(a => a.OwnerId == userId);
            var role = await _db.Users.Where(u => u.Id == userId).Select(u => (UserRole?)u.Role).FirstOrDefaultAsync()
                       ?? throw new BankException("Người dùng không tồn tại.");
            var type = role == UserRole.FacilityOwner ? BankOwnerType.CourtOwner : BankOwnerType.User;

            if (existing != null)
            {
                // Admin đổi vai trò người dùng (vd khách → chủ sân): cập nhật loại tài khoản cho khớp
                if (existing.OwnerType != type)
                {
                    existing.OwnerType = type;
                    await _db.SaveChangesAsync();
                }
                return existing;
            }

            for (int attempt = 0; ; attempt++)
            {
                var account = new BankAccount { AccountNo = NewAccountNo(), OwnerType = type, OwnerId = userId, Balance = 0 };
                _db.BankAccounts.Add(account);
                try
                {
                    await _db.SaveChangesAsync();
                    return account;
                }
                catch (DbUpdateException) when (attempt < 5)
                {
                    // Trùng số tài khoản (rất hiếm) hoặc 2 request cùng tạo cho 1 người: thử lại
                    _db.Entry(account).State = EntityState.Detached;
                    var created = await _db.BankAccounts.FirstOrDefaultAsync(a => a.OwnerId == userId);
                    if (created != null) return created;
                }
            }
        }

        public async Task<BankAccount> GetSystemAccountAsync()
        {
            var sys = await _db.BankAccounts.FirstOrDefaultAsync(a => a.OwnerType == BankOwnerType.System);
            if (sys != null) return sys;
            sys = new BankAccount { AccountNo = SystemAccountNo, OwnerType = BankOwnerType.System, OwnerId = null, Balance = 0 };
            _db.BankAccounts.Add(sys);
            await _db.SaveChangesAsync();
            return sys;
        }

        public async Task<int> BackfillAccountsAsync()
        {
            await GetSystemAccountAsync();
            var missing = await _db.Users
                .Where(u => !_db.BankAccounts.Any(a => a.OwnerId == u.Id))
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var id in missing) await EnsureAccountAsync(id);
            return missing.Count;
        }

        /// <summary>Số tài khoản ảo 12 chữ số dạng 9704xxxxxxxx (ngẫu nhiên, kiểm tra trùng).</summary>
        private string NewAccountNo()
        {
            while (true)
            {
                var no = "9704" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
                if (no != SystemAccountNo && !_db.BankAccounts.Any(a => a.AccountNo == no)) return no;
            }
        }

        // ===================== Nghiệp vụ =====================

        public async Task<BankTransaction> DepositAsync(string userId, long amount)
        {
            if (amount < PaymentPolicy.MinDeposit || amount > PaymentPolicy.MaxDeposit)
                throw new BankException($"Số tiền nạp phải từ {PaymentPolicy.MinDeposit:N0} ₫ đến {PaymentPolicy.MaxDeposit:N0} ₫.");
            var account = await EnsureAccountAsync(userId);
            return await TransferAsync(null, account.Id, amount, BankTransactionType.Deposit, null, "Nạp tiền vào ví (giả lập)");
        }

        public async Task<BankTransaction> TransferAsync(int? fromAccountId, int toAccountId, long amount,
            BankTransactionType type, int? bookingId, string? note)
        {
            if (amount <= 0) throw new BankException("Số tiền giao dịch phải lớn hơn 0.");
            if (fromAccountId == toAccountId) throw new BankException("Không thể chuyển tiền cho chính tài khoản nguồn.");
            if (fromAccountId == null && type != BankTransactionType.Deposit) throw new BankException("Chỉ giao dịch nạp tiền mới không có tài khoản nguồn.");

            // Dùng transaction của nghiệp vụ gọi tới (nếu có); không thì tự mở
            var ownTx = _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;
            BankTransaction? txn = null;
            try
            {
                // Idempotent: mỗi đơn chỉ 1 giao dịch mỗi loại (DB còn chặn thêm bằng UNIQUE(BookingId, Type))
                if (bookingId.HasValue && await _db.BankTransactions.AnyAsync(t => t.BookingId == bookingId && t.Type == type))
                    throw new BankException(type == BankTransactionType.Payment
                        ? $"Đơn #BK-{bookingId} đã được thanh toán trước đó."
                        : $"Đơn #BK-{bookingId} đã được hoàn tiền trước đó.");

                // Khóa dòng theo thứ tự Id tăng dần để 2 giao dịch ngược chiều không deadlock
                var ids = new[] { fromAccountId, toAccountId }.Where(i => i.HasValue).Select(i => i!.Value).Distinct().OrderBy(i => i).ToList();
                var locked = new Dictionary<int, long>();
                foreach (var id in ids)
                {
                    var row = await _db.BankAccounts
                        .FromSqlInterpolated($"SELECT * FROM BankAccounts WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}")
                        .AsNoTracking()
                        .Select(a => new { a.Id, a.Balance })
                        .FirstOrDefaultAsync()
                        ?? throw new BankException("Tài khoản không tồn tại.");
                    locked[row.Id] = row.Balance;
                }

                long? fromAfter = null;
                if (fromAccountId.HasValue)
                {
                    var balance = locked[fromAccountId.Value];
                    if (balance < amount) throw new InsufficientFundsException(balance, amount);
                    fromAfter = balance - amount;
                    await _db.BankAccounts.Where(a => a.Id == fromAccountId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount));
                }
                var toAfter = checked(locked[toAccountId] + amount);
                await _db.BankAccounts.Where(a => a.Id == toAccountId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + amount));

                txn = new BankTransaction
                {
                    FromAccountId = fromAccountId,
                    ToAccountId = toAccountId,
                    Amount = amount,
                    Type = type,
                    BookingId = bookingId,
                    Note = note?.Length > 300 ? note[..300] : note,
                    FromBalanceAfter = fromAfter,
                    ToBalanceAfter = toAfter,
                    CreatedAt = DateTime.UtcNow
                };
                _db.BankTransactions.Add(txn);
                await _db.SaveChangesAsync();

                // Số dư đã đổi bằng ExecuteUpdate (bỏ qua bộ nhớ đệm của EF): cập nhật lại entity đang được theo dõi nếu có
                foreach (var entry in _db.ChangeTracker.Entries<BankAccount>().Where(e => ids.Contains(e.Entity.Id)).ToList())
                {
                    entry.Entity.Balance = entry.Entity.Id == toAccountId ? toAfter : fromAfter!.Value;
                    entry.State = EntityState.Unchanged;
                }

                if (ownTx != null) await ownTx.CommitAsync();
                _logger.LogInformation("Giao dịch {Type} #{Id}: {From} → {To}, {Amount} ₫, đơn {Booking}",
                    type, txn.Id, fromAccountId, toAccountId, amount, bookingId);
                return txn;
            }
            catch (DbUpdateException ex) when (bookingId.HasValue && IsUniqueViolation(ex))
            {
                // 2 request cùng lúc vượt qua bước kiểm tra: UNIQUE(BookingId, Type) chặn lại
                Detach(txn);
                throw new BankException($"Đơn #BK-{bookingId} đã có giao dịch {(type == BankTransactionType.Payment ? "thanh toán" : "hoàn tiền")}.");
            }
            catch
            {
                Detach(txn);
                throw;
            }
            finally
            {
                if (ownTx != null) await ownTx.DisposeAsync(); // chưa Commit thì tự Rollback
            }
        }

        /// <summary>Giao dịch lỗi: gỡ khỏi bộ theo dõi để lần SaveChanges sau không cố ghi lại.</summary>
        private void Detach(BankTransaction? txn)
        {
            if (txn != null) _db.Entry(txn).State = EntityState.Detached;
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

        // ===================== Sao kê =====================

        public async Task<BankStatement> GetStatementAsync(int accountId, int page, int pageSize)
        {
            var account = await _db.BankAccounts.AsNoTracking().Include(a => a.Owner).FirstOrDefaultAsync(a => a.Id == accountId)
                          ?? throw new BankException("Tài khoản không tồn tại.");
            var query = _db.BankTransactions.AsNoTracking()
                .Where(t => t.FromAccountId == accountId || t.ToAccountId == accountId);
            var total = await query.CountAsync();
            page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
            var items = await query
                .Include(t => t.FromAccount).ThenInclude(a => a!.Owner)
                .Include(t => t.ToAccount).ThenInclude(a => a!.Owner)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return new BankStatement(account, items, total, page, pageSize);
        }
    }
}
