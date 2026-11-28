using System.Security.Claims;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DACN.Controllers
{
    /// <summary>
    /// "Internet Banking" của người dùng: xem số tài khoản ảo, số dư, lịch sử giao dịch và nạp tiền (giả lập).
    /// Controller chỉ gọi <see cref="IBankService"/>, không tự đụng tới số dư.
    /// </summary>
    [Authorize]
    public class WalletController : Controller
    {
        private const int PageSize = 10;
        private readonly IBankService _bank;

        public WalletController(IBankService bank)
        {
            _bank = bank;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET /Wallet?page=2
        [HttpGet]
        public async Task<IActionResult> Index(int page = 1)
        {
            var account = await _bank.EnsureAccountAsync(CurrentUserId);
            var statement = await _bank.GetStatementAsync(account.Id, page, PageSize);
            ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Query["returnUrl"]) ? Request.Query["returnUrl"].ToString() : null;
            return View(statement);
        }

        // POST /Wallet/Deposit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deposit(long? amount, string? returnUrl)
        {
            if (amount is null)
            {
                TempData["ErrorMessage"] = "Vui lòng nhập số tiền cần nạp.";
                return RedirectToAction(nameof(Index), new { returnUrl });
            }
            try
            {
                var txn = await _bank.DepositAsync(CurrentUserId, amount.Value);
                TempData["SuccessMessage"] = $"Đã nạp {Money.Vnd(txn.Amount)} vào ví. Số dư mới: {Money.Vnd(txn.ToBalanceAfter)}.";
            }
            catch (BankException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index), new { returnUrl });
            }

            // Nạp tiền từ trang thanh toán (thiếu số dư) thì quay lại trang đó
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        // GET /Wallet/Account → JSON số tài khoản + số dư (dùng cho trang khác hiển thị nhanh)
        [HttpGet]
        public async Task<IActionResult> Account()
        {
            var a = await _bank.EnsureAccountAsync(CurrentUserId);
            return Json(new { accountNo = a.AccountNo, balance = a.Balance, balanceText = Money.Vnd(a.Balance) });
        }
    }
}
