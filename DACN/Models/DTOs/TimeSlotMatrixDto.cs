using System;
using DACN.Models.Enums;

namespace DACN.Models.DTOs
{
    public enum SlotAvailability
    {
        Available,
        Booked,
        PendingPayment,
        PastTime
    }

    public class TimeSlotMatrixDto
    {
        public int SlotIndex { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string TimeLabel => $"{StartTime:hh\\:mm} - {EndTime:hh\\:mm}";
        public decimal Price { get; set; }
        public bool IsPeakHour { get; set; }
        public SlotAvailability Availability { get; set; } = SlotAvailability.Available;
        public int? BookingId { get; set; }
        public string? BookedByCustomerName { get; set; }
        public BookingStatus? BookingStatus { get; set; }
    }
}
