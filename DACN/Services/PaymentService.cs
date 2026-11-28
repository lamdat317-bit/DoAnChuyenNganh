using System.Data;
using System.Security.Cryptography;
using System.Text;
using DACN.Data;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DACN.Services
{
    public record PaymentResult(bool Success, string Message)
    {
        /// <summary>Thất bại vì ví không đủ tiền (giao diện gợi ý nạp thêm).</summary>
        public bool InsufficientFunds { get; init; }
        public long Shortfall { get; init; }
        /// <summary>Mã OTP giả lập để hiển thị trên màn hình (chỉ có khi xin mã).</summary>
        public string? Otp { get; init; }
        public DateTime? OtpExpiresAtUtc { get; init; }
        /// <summary>Số tiền hoàn vào ví (khi hủy).</summary>
        public long RefundAmount { get; init; }
        public long? TransactionId { get; init; }

        public static PaymentResult Fail(string message) => new(false, message);
    }

    /// <summary>Thông tin xem trước khi hủy: được hoàn bao nhiêu.</summary>
    public record CancelPreview(bool CanCancel, string? BlockReason, bool IsPaid, int RefundPercent, long PaidAmount, long RefundAmount);

    public interface IPaymentService
    {
        /// <summary>Sinh OTP 6 số (hết hạn sau <see cref="PaymentPolicy.OtpMinutes"/> phút) để xác nhận thanh toán đơn.</summary>
        Task<PaymentResult> RequestPaymentOtpAsync(int bookingId, string userId);

        /// <summary>Kiểm tra OTP rồi trừ ví khách, cộng ví chủ sân, chuyển đơn sang ĐÃ THANH TOÁN (1 transaction).</summary>
        Task<PaymentResult> ConfirmPaymentAsync(int bookingId, string userId, string? otp);

        /// <summary>Khách / chủ sân / Admin hủy đơn; đơn đã thanh toán bằng ví thì hoàn tiền tự động theo chính sách.</summary>
        Task<PaymentResult> CancelBookingAsync(int bookingId, string userId, string? reason);

        /// <summary>Tính trước số tiền được hoàn nếu hủy ngay bây giờ (cho popup xác nhận).</summary>
        Task<CancelPreview> PreviewCancelAsync(int bookingId, string userId);
    }

    public partial class PaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IBankService _bank;
        private readonly IMemoryCache _cache;
        private readonly INotificationService _notifications;
        private readonly ILogger<PaymentService> _logger;
        private readonly TimeProvider _clock;

        public PaymentService(ApplicationDbContext db, IBankService bank, IMemoryCache cache,
            INotificationService notifications, ILogger<PaymentService> logger, TimeProvider? clock = null)
        {
            _db = db;
            _bank = bank;
            _cache = cache;
            _notifications = notifications;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
        private DateTime LocalNow => _clock.GetLocalNow().DateTime;

        // ===================== OTP giả lập =====================

        private sealed class OtpEntry
        {
            public required byte[] Hash { get; init; }
            public required DateTime ExpiresAtUtc { get; init; }
            public int Attempts { get; set; }
        }

        private static string OtpKey(int bookingId, string userId) => $"pay-otp:{bookingId}:{userId}";
        private static byte[] HashOtp(string otp) => SHA256.HashData(Encoding.UTF8.GetBytes(otp));

        public async Task<PaymentResult> RequestPaymentOtpAsync(int bookingId, string userId)
        {
            var b = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookingId);
            var problem = PayBlockReason(b, userId);
            if (problem != null) return PaymentResult.Fail(problem);

            // Báo thiếu tiền ngay từ bước xin mã cho khách đỡ mất công
            var account = await _bank.EnsureAccountAsync(userId);
            var amount = Money.ToVnd(b!.TotalAmount);
            if (account.Balance < amount)
                return InsufficientResult(account.Balance, amount);

            var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var expires = UtcNow.AddMinutes(PaymentPolicy.OtpMinutes);
            _cache.Set(OtpKey(bookingId, userId), new OtpEntry { Hash = HashOtp(otp), ExpiresAtUtc = expires },
                absoluteExpiration: new DateTimeOffset(expires, TimeSpan.Zero));

            // OTP giả lập: in ra console (thay cho SMS) và trả về để hiện trên màn hình
            _logger.LogWarning("[OTP GIẢ LẬP] Đơn #BK-{BookingId}: mã {Otp}, hết hạn {Expires:HH:mm:ss} UTC", bookingId, otp, expires);
            return new PaymentResult(true, $"Mã OTP đã được gửi. Mã có hiệu lực {PaymentPolicy.OtpMinutes} phút.")
            {
                Otp = otp,
                OtpExpiresAtUtc = expires
            };
        }

        /// <summary>Kiểm tra OTP; đúng thì xóa luôn (mỗi mã dùng 1 lần).</summary>
        private string? VerifyOtp(int bookingId, string userId, string? otp)
        {
            var key = OtpKey(bookingId, userId);
            if (!_cache.TryGetValue(key, out OtpEntry? entry) || entry == null || entry.ExpiresAtUtc <= UtcNow)
            {
                _cache.Remove(key);
                return "Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm \"Gửi mã OTP\" để lấy mã mới.";
            }
            var input = (otp ?? "").Trim();
            if (input.Length != 6 || !input.All(char.IsDigit) || !CryptographicOperations.FixedTimeEquals(entry.Hash, HashOtp(input)))
            {
                entry.Attempts++;
                if (entry.Attempts >= PaymentPolicy.OtpMaxAttempts)
                {
                    _cache.Remove(key);
                    return $"Nhập sai OTP quá {PaymentPolicy.OtpMaxAttempts} lần. Vui lòng lấy mã mới.";
                }
                return $"Mã OTP không đúng (còn {PaymentPolicy.OtpMaxAttempts - entry.Attempts} lần thử).";
            }
            _cache.Remove(key);
            return null;
        }

        // ===================== Thanh toán =====================

        /// <summary>Lý do không thanh toán được (null = được).</summary>
        private string? PayBlockReason(Models.Booking? b, string userId)
        {
            if (b == null || b.CustomerId != userId || b.IsCounterBooking) return "Không tìm thấy đơn đặt sân của bạn.";
            if (b.PaymentStatus == PaymentStatus.Paid) return $"Đơn #BK-{b.Id} đã được thanh toán.";
            if (b.Status == BookingStatus.Cancelled) return $"Đơn #BK-{b.Id} đã bị hủy, không thể thanh toán.";
            if (b.Status != BookingStatus.Pending || b.PaymentStatus != PaymentStatus.Unpaid)
                return $"Đơn #BK-{b.Id} không ở trạng thái chờ thanh toán.";
            if (b.CreatedAt < UtcNow.AddMinutes(-PaymentPolicy.HoldMinutes))
                return $"Đơn #BK-{b.Id} đã quá {PaymentPolicy.HoldMinutes} phút giữ chỗ. Vui lòng đặt lại.";
            return null;
        }

        private static PaymentResult InsufficientResult(long balance, long amount) =>
            new(false, $"Số dư ví không đủ: còn {Money.Vnd(balance)}, cần {Money.Vnd(amount)}. Vui lòng nạp thêm {Money.Vnd(amount - balance)}.")
            {
                InsufficientFunds = true,
                Shortfall = amount - balance
            };

        public async Task<PaymentResult> ConfirmPaymentAsync(int bookingId, string userId, string? otp)
        {
            var otpError = VerifyOtp(bookingId, userId, otp);
            if (otpError != null) return PaymentResult.Fail(otpError);

            // Tạo ví trước (ngoài transaction) nếu chưa có
            var customerAccount = await _bank.EnsureAccountAsync(userId);

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                // Khóa dòng booking: bấm "Xác nhận" 2 lần / job hủy giữ chỗ chạy cùng lúc đều phải chờ
                var b = await LockBookingAsync(bookingId);
                var problem = PayBlockReason(b, userId);
                if (problem != null) return PaymentResult.Fail(problem);

                var ownerId = await _db.Courts.Where(c => c.Id == b!.CourtId).Select(c => c.Facility!.OwnerId).FirstAsync();
                var ownerAccount = await _bank.EnsureAccountAsync(ownerId);
                var amount = Money.ToVnd(b!.TotalAmount);

                var txn = await _bank.TransferAsync(customerAccount.Id, ownerAccount.Id, amount, BankTransactionType.Payment,
                    bookingId, $"Thanh toán đơn #BK-{bookingId}");

                var now = UtcNow;
                await _db.Bookings.Where(x => x.Id == bookingId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, BookingStatus.Confirmed)
                    .SetProperty(x => x.PaymentStatus, PaymentStatus.Paid)
                    .SetProperty(x => x.PaidAt, now)
                    .SetProperty(x => x.PaymentMethod, "Ví DatSan247")
                    .SetProperty(x => x.PaymentTransactionRef, $"GD{txn.Id}"));

                await tx.CommitAsync();

                await NotifySafeAsync(async () => await _notifications.NotifyFacilityOwnerAsync(await FacilityIdOf(b.CourtId),
                    $"Đơn #BK-{bookingId} đã thanh toán qua ví",
                    $"{b.CustomerName} đã thanh toán {Money.Vnd(amount)}. Tiền đã vào ví của bạn (GD #{txn.Id}).",
                    $"/Owner/Facility/Bookings?q={bookingId}", "bi-wallet2", "green"));

                return new PaymentResult(true, $"Thanh toán thành công {Money.Vnd(amount)}! Số dư còn lại: {Money.Vnd(txn.FromBalanceAfter ?? 0)}.")
                {
                    TransactionId = txn.Id
                };
            }
            catch (InsufficientFundsException ex)
            {
                return InsufficientResult(ex.Balance, ex.Required);
            }
            catch (BankException ex)
            {
                return PaymentResult.Fail(ex.Message);
            }
        }

        /// <summary>SELECT ... WITH (UPDLOCK, ROWLOCK): giữ khóa dòng booking tới hết transaction. Trả về dữ liệu mới nhất.</summary>
        private Task<Models.Booking?> LockBookingAsync(int bookingId) =>
            _db.Bookings
                .FromSqlInterpolated($"SELECT * FROM Bookings WITH (UPDLOCK, ROWLOCK) WHERE Id = {bookingId}")
                .AsNoTracking()
                .FirstOrDefaultAsync();

        /// <summary>Tiền đã chuyển xong (đã commit): lỗi gửi thông báo không được làm nghiệp vụ báo thất bại.</summary>
        private async Task NotifySafeAsync(Func<Task> send)
        {
            try { await send(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Không gửi được thông báo sau giao dịch ví."); }
        }

        private Task<int> FacilityIdOf(int courtId) =>
            _db.Courts.Where(c => c.Id == courtId).Select(c => c.FacilityId).FirstAsync();
    }
}
