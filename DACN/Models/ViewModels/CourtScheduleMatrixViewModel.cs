using System;
using System.Collections.Generic;
using DACN.Models.DTOs;

namespace DACN.Models.ViewModels
{
    public class CourtScheduleMatrixViewModel
    {
        public int FacilityId { get; set; }
        public string FacilityName { get; set; } = string.Empty;
        public string FacilityAddress { get; set; } = string.Empty;
        public int CourtId { get; set; }
        public string CourtName { get; set; } = string.Empty;
        public string SportTypeName { get; set; } = string.Empty;
        public DateOnly SelectedDate { get; set; }
        public List<TimeSlotMatrixDto> Slots { get; set; } = new List<TimeSlotMatrixDto>();
        public List<Court> OtherCourtsInFacility { get; set; } = new List<Court>();
        public List<FacilityService> AvailableServices { get; set; } = new List<FacilityService>();
    }

    public class FacilityTimelineScheduleViewModel
    {
        public Facility Facility { get; set; } = null!;
        public DateOnly SelectedDate { get; set; }
        public List<TimeSpan> TimelineHours { get; set; } = new List<TimeSpan>();
        public Dictionary<int, List<Booking>> CourtBookings { get; set; } = new Dictionary<int, List<Booking>>();
    }
}
