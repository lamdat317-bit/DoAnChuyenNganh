using DACN.Models.Enums;

namespace DACN.Services
{
    /// <summary>Định dạng tiền VND và nhãn giao dịch dùng chung cho các màn hình ví.</summary>
    public static class Money
    {
        /// <summary>150000 → "150.000 ₫"</summary>
        public static string Vnd(long amount) => $"{amount:N0} ₫";

        public static string Vnd(decimal amount) => $"{amount:N0} ₫";

        /// <summary>Tiền đặt sân lưu decimal(18,2) nhưng luôn là số đồng chẵn: đổi sang số nguyên để chuyển tiền.</summary>
        public static long ToVnd(decimal amount)
        {
            if (amount < 0 || amount != decimal.Truncate(amount))
                throw new BankException($"Số tiền {amount} không hợp lệ (phải là số đồng nguyên, không âm).");
            return (long)amount;
        }

        public static (string Label, string Tone, string Icon) TypeBadge(BankTransactionType type) => type switch
        {
            BankTransactionType.Deposit => ("Nạp tiền", "success", "bi-box-arrow-in-down"),
            BankTransactionType.Payment => ("Thanh toán", "info", "bi-credit-card"),
            BankTransactionType.Refund => ("Hoàn tiền", "warning", "bi-arrow-counterclockwise"),
            _ => (type.ToString(), "neutral", "bi-dot")
        };

        public static string OwnerTypeLabel(BankOwnerType type) => type switch
        {
            BankOwnerType.System => "Hệ thống",
            BankOwnerType.CourtOwner => "Chủ sân",
            _ => "Người dùng"
        };
    }
}

namespace DACN.Services
{
    /// <summary>Dữ liệu cho partial _Pager: UrlTemplate chứa "__page__" để thay bằng số trang.</summary>
    public record PagerModel(int Page, int TotalPages, string UrlTemplate)
    {
        public string UrlFor(int page) => UrlTemplate.Replace("__page__", page.ToString());
    }
}
