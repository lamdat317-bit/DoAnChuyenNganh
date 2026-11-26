namespace DACN.Models.Enums
{
    public enum PaymentStatus
    {
        Unpaid = 1,               // Chưa thanh toán
        Paid = 2,                 // Đã thanh toán (đã được xác nhận)
        AwaitingVerification = 3, // Khách đã gửi biên lai chuyển khoản, chờ chủ sân kiểm tra
        RefundPending = 4,        // Đơn đã trả tiền bị hủy, chờ chủ sân hoàn tiền
        Refunded = 5              // Đã hoàn tiền cho khách
    }
}
