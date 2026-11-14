using System;
using System.Collections.Generic;

namespace DACN.Models.DTOs
{
    public class BookingDto
    {
        public int CourtId { get; set; }

        public string? CustomerId { get; set; }

        public string? CustomerName { get; set; }

        public string? CustomerPhone { get; set; }

        public DateOnly BookingDate { get; set; }

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }

        public string? Note { get; set; }

        public List<int>? SelectedServiceIds { get; set; } = new();
    }
}
