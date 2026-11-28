using System.Data;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services
{
    /// <summary>
    /// Hủy đơn + hoàn tiền tự động vào ví.
    /// - Khách tự hủy: chỉ trước giờ chơi. Đơn chưa thanh toán: hủy để nhả sân. Đơn đã thanh toán bằng ví:
    ///   hoàn theo <see cref="PaymentPolicy.RefundPercent"/> (≥12h 100%, 6h–12h 50%, &lt;6h 0%).
    /// - Chủ sân / Admin hủy: hoàn 100% (lỗi không phải ở khách).
    /// - Đơn đã trả bằng hình thức cũ (chuyển khoản / tiền mặt, không có giao dịch ví): chuyển "Chờ hoàn tiền" để chủ sân hoàn tay như trước.
    /// </summary>
    public partial class PaymentService
    {
        private sealed record CancelContext(Booking Booking, string FacilityOwnerId, int FacilityId, string FacilityName,
            bool IsCustomer, bool IsOwnerOrAdmin, BankTransaction? WalletPayment);

        private async Task<CancelContext?> LoadCancelContextAsync(Booking? b, string userId)
        {
            if (b == null) return null;
            var facility = await _db.Courts.Where(c => c.Id == b.CourtId)
                .Select(c => new { c.FacilityId, c.Facility!.OwnerId, c.Facility.Name }).FirstAsync();
            bool isAdmin = await _db.Users.AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin);
            bool isOwnerOrAdmin = isAdmin || facility.OwnerId == userId;
            bool isCustomer = b.CustomerId == userId && !b.IsCounterBooking;
            var payment = await _db.BankTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.BookingId == b.Id && t.Type == BankTransactionType.Payment);
            return new CancelContext(b, facility.OwnerId, facility.FacilityId, facility.Name, isCustomer, isOwnerOrAdmin, payment);
        }

        /// <summary>Lý do không được hủy (null = được) và % hoàn tiền.</summary>
        private (string? Block, int Percent) EvaluateCancel(CancelContext ctx)
        {
            var b = ctx.Booking;
            if (!ctx.IsCustomer && !ctx.IsOwnerOrAdmin) return ("Bạn không có quyền hủy đơn đặt sân này.", 0);
            if (b.Status == BookingStatus.Cancelled) return ($"Đơn #BK-{b.Id} đã được hủy trước đó.", 0);
            if (b.Status == BookingStatus.Completed) return ($"Đơn #BK-{b.Id} đã hoàn thành, không thể hủy.", 0);
            if (ctx.IsOwnerOrAdmin) return (null, 100);

            var hoursBefore = (BookingRules.StartsAt(b) - LocalNow).TotalHours;
            if (hoursBefore <= 0) return ("Đã đến hoặc quá giờ chơi nên không thể tự hủy đơn. Vui lòng liên hệ chủ sân.", 0);
            return (null, PaymentPolicy.RefundPercent(hoursBefore));
        }

        public async Task<CancelPreview> PreviewCancelAsync(int bookingId, string userId)
        {
            var b = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookingId);
            var ctx = await LoadCancelContextAsync(b, userId);
            if (ctx == null) return new CancelPreview(false, "Đơn đặt sân không tồn tại.", false, 0, 0, 0);
            var (block, percent) = EvaluateCancel(ctx);
            // Trả bằng ví: số tiền thực đã trừ; trả theo hình thức cũ (chuyển khoản / tiền mặt): tổng tiền đơn
            bool legacyPaid = ctx.WalletPayment == null && b!.PaymentStatus is PaymentStatus.Paid or PaymentStatus.AwaitingVerification;
            long paid = ctx.WalletPayment?.Amount ?? (legacyPaid ? Money.ToVnd(b!.TotalAmount) : 0);
            return new CancelPreview(block == null, block, paid > 0, percent, paid, PaymentPolicy.RefundAmount(paid, percent));
        }

        public async Task<PaymentResult> CancelBookingAsync(int bookingId, string userId, string? reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (reason?.Length > 200) reason = reason[..200];

            CancelContext ctx;
            long refund;
            int percent;
            BankTransaction? refundTxn = null;

            await using (var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                try
                {
                    // Khóa dòng booking: bấm "Hủy" 2 lần thì lần sau phải chờ, rồi thấy đơn đã hủy
                    var b = await LockBookingAsync(bookingId);
                    var loaded = await LoadCancelContextAsync(b, userId);
                    if (loaded == null) return PaymentResult.Fail("Đơn đặt sân không tồn tại.");
                    ctx = loaded;
                    var (block, p) = EvaluateCancel(ctx);
                    if (block != null) return PaymentResult.Fail(block);
                    percent = p;

                    var now = UtcNow;
                    var byText = ctx.IsOwnerOrAdmin && !ctx.IsCustomer ? "Chủ sân hủy" : "Khách hủy";
                    var note = reason == null ? null : $"{byText}: {reason}";

                    if (ctx.WalletPayment != null)
                    {
                        // Đã thanh toán bằng ví → hoàn tự động từ ví chủ sân về ví khách
                        refund = PaymentPolicy.RefundAmount(ctx.WalletPayment.Amount, percent);
                        if (refund > 0)
                        {
                            refundTxn = await _bank.TransferAsync(ctx.WalletPayment.ToAccountId, ctx.WalletPayment.FromAccountId!.Value,
                                refund, BankTransactionType.Refund, bookingId,
                                $"Hoàn {percent}% đơn #BK-{bookingId} ({byText.ToLower()})");
                        }
                        var refundNote = refund > 0
                            ? $"Đã hoàn {percent}% ({Money.Vnd(refund)}) vào ví · GD #{refundTxn!.Id}"
                            : $"Hủy dưới {PaymentPolicy.PartialRefundHours} tiếng trước giờ chơi: không hoàn tiền";
                        var paymentStatus = refund > 0 ? PaymentStatus.Refunded : PaymentStatus.Paid;
                        await _db.Bookings.Where(x => x.Id == bookingId).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, BookingStatus.Cancelled)
                            .SetProperty(x => x.CancelledAt, now)
                            .SetProperty(x => x.PaymentStatus, paymentStatus)
                            .SetProperty(x => x.RefundAmount, (decimal)refund)
                            .SetProperty(x => x.RefundedAt, refund > 0 ? now : (DateTime?)null)
                            .SetProperty(x => x.RefundNote, refundNote)
                            .SetProperty(x => x.PaymentNote, x => note ?? x.PaymentNote));
                    }
                    else if (b!.PaymentStatus is PaymentStatus.Paid or PaymentStatus.AwaitingVerification)
                    {
                        // Đơn trả bằng hình thức cũ (không qua ví): giữ luồng hoàn tay của chủ sân
                        refund = Money.ToVnd(BookingRules.RefundAmountFor(b.TotalAmount, percent));
                        await _db.Bookings.Where(x => x.Id == bookingId).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, BookingStatus.Cancelled)
                            .SetProperty(x => x.CancelledAt, now)
                            .SetProperty(x => x.PaymentStatus, x => refund > 0 ? PaymentStatus.RefundPending : x.PaymentStatus)
                            .SetProperty(x => x.RefundAmount, (decimal)refund)
                            .SetProperty(x => x.PaymentNote, x => note ?? x.PaymentNote));
                    }
                    else
                    {
                        // Chưa thanh toán: chỉ hủy để nhả sân
                        refund = 0;
                        await _db.Bookings.Where(x => x.Id == bookingId).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, BookingStatus.Cancelled)
                            .SetProperty(x => x.CancelledAt, now)
                            .SetProperty(x => x.PaymentNote, x => note ?? x.PaymentNote));
                    }

                    // Kèo ghép đội tạo từ đơn này không còn sân để chơi
                    await _db.MatchPosts
                        .Where(mp => mp.BookingId == bookingId && (mp.Status == MatchPostStatus.Open || mp.Status == MatchPostStatus.Full))
                        .ExecuteUpdateAsync(s => s.SetProperty(mp => mp.Status, MatchPostStatus.Cancelled));

                    await tx.CommitAsync();
                }
                catch (InsufficientFundsException)
                {
                    return PaymentResult.Fail("Ví chủ sân không đủ số dư để hoàn tiền. Vui lòng liên hệ quản trị viên.");
                }
                catch (BankException ex)
                {
                    return PaymentResult.Fail(ex.Message);
                }
            }

            await NotifyCancelAsync(ctx, refund, percent, refundTxn);

            string message = ctx.WalletPayment != null
                ? refund > 0
                    ? $"Đã hủy đơn #BK-{bookingId}. Đã hoàn {Money.Vnd(refund)} ({percent}%) vào ví của khách."
                    : $"Đã hủy đơn #BK-{bookingId}. Hủy dưới {PaymentPolicy.PartialRefundHours} tiếng trước giờ chơi nên không được hoàn tiền."
                : refund > 0
                    ? $"Đã hủy đơn #BK-{bookingId}. Khoản {Money.Vnd(refund)} đang chờ chủ sân hoàn lại."
                    : $"Đã hủy đơn #BK-{bookingId}.";
            return new PaymentResult(true, message) { RefundAmount = refund, TransactionId = refundTxn?.Id };
        }

        private async Task NotifyCancelAsync(CancelContext ctx, long refund, int percent, BankTransaction? refundTxn)
        {
            var b = ctx.Booking;
            var where = $"{ctx.FacilityName} · {b.BookingDate:dd/MM} {BookingRules.Hm(b.StartTime)}–{BookingRules.Hm(b.EndTime)}";
            var refundText = refund > 0
                ? refundTxn != null ? $" Đã hoàn {Money.Vnd(refund)} ({percent}%) vào ví." : $" Cần hoàn {Money.Vnd(refund)}."
                : ctx.WalletPayment != null ? " Không hoàn tiền (hủy sát giờ)." : "";

            await NotifySafeAsync(async () =>
            {
                if (ctx.IsCustomer && !ctx.IsOwnerOrAdmin)
                {
                    await _notifications.NotifyFacilityOwnerAsync(ctx.FacilityId, $"Khách hủy đơn #BK-{b.Id}",
                        $"{b.CustomerName} đã hủy {where}.{refundText}",
                        $"/Owner/Facility/Bookings?facilityId={ctx.FacilityId}&q={b.Id}", "bi-calendar-x", refund > 0 ? "amber" : "red");
                }
                else if (!b.IsCounterBooking)
                {
                    await _notifications.NotifyUserAsync(b.CustomerId, $"Đơn #BK-{b.Id} đã bị chủ sân hủy",
                        $"{where}.{refundText}", $"/Booking/Confirmation?bookingId={b.Id}", "bi-calendar-x", "red", sendEmail: true);
                }
            });
        }
    }
}
