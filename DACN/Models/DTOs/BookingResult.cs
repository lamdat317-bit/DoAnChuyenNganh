namespace DACN.Models.DTOs
{
    public class BookingResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public Booking? Booking { get; set; }
        public int? BookingId => Booking?.Id;

        public static BookingResult Success(Booking booking, string message = "Đặt sân thành công!")
        {
            return new BookingResult
            {
                IsSuccess = true,
                Message = message,
                Booking = booking
            };
        }

        public static BookingResult Failure(string message, Booking? booking = null)
        {
            return new BookingResult
            {
                IsSuccess = false,
                Message = message,
                Booking = booking
            };
        }
    }
}
