namespace DACN.Services
{
    /// <summary>
    /// Các con số của ví / thanh toán / hoàn tiền, gom về 1 chỗ để dễ chỉnh.
    /// Tiền tính bằng đồng VND (số nguyên).
    /// </summary>
    public static class PaymentPolicy
    {
        // ----- Nạp tiền (giả lập) -----
        public const long MinDeposit = 10_000;
        public const long MaxDeposit = 50_000_000;

        // ----- Giữ chỗ / OTP -----
        /// <summary>Đơn PENDING chưa thanh toán quá ngần này phút thì tự hủy để nhả sân.</summary>
        public const int HoldMinutes = 15;
        /// <summary>Mã OTP xác nhận thanh toán hết hạn sau ngần này phút.</summary>
        public const int OtpMinutes = 2;
        /// <summary>Nhập sai OTP quá ngần này lần thì phải xin mã mới.</summary>
        public const int OtpMaxAttempts = 5;

        // ----- Hủy & hoàn tiền (khách tự hủy) -----
        /// <summary>Hủy trước giờ chơi ≥ ngần này tiếng: hoàn 100%.</summary>
        public const int FullRefundHours = 12;
        /// <summary>Hủy trước giờ chơi từ ngần này tiếng tới dưới <see cref="FullRefundHours"/>: hoàn <see cref="PartialRefundPercent"/>%.</summary>
        public const int PartialRefundHours = 6;
        public const int PartialRefundPercent = 50;
        // Dưới PartialRefundHours tiếng: không hoàn tiền (0%).

        /// <summary>% hoàn tiền khi khách tự hủy, theo số giờ còn lại tới giờ chơi.</summary>
        public static int RefundPercent(double hoursBeforeStart) =>
            hoursBeforeStart >= FullRefundHours ? 100
            : hoursBeforeStart >= PartialRefundHours ? PartialRefundPercent
            : 0;

        /// <summary>Số tiền hoàn (làm tròn xuống tới đồng).</summary>
        public static long RefundAmount(long paid, int percent) => paid * percent / 100;
    }
}
