namespace DACN.Models.Enums
{
    /// <summary>Chủ sở hữu tài khoản ngân hàng ảo.</summary>
    public enum BankOwnerType
    {
        User = 1,        // Khách hàng / Admin
        System = 2,      // Tài khoản của nền tảng DatSan247
        CourtOwner = 3   // Chủ sân: nhận tiền khách thanh toán, hoàn tiền khi hủy
    }

    /// <summary>Loại giao dịch.</summary>
    public enum BankTransactionType
    {
        Deposit = 1,  // Nạp tiền (giả lập)
        Payment = 2,  // Khách thanh toán đơn đặt sân → ví chủ sân
        Refund = 3    // Chủ sân hoàn tiền đơn bị hủy → ví khách
    }
}
