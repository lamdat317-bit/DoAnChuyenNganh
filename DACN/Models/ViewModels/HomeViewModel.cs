using System.Collections.Generic;

namespace DACN.Models.ViewModels
{
    public class HomeViewModel
    {
        public List<SportTypeSummaryDto> SportTypes { get; set; } = new List<SportTypeSummaryDto>();
        public List<FacilityCardDto> FeaturedFacilities { get; set; } = new List<FacilityCardDto>();
        public List<MatchPostCardDto> HotMatches { get; set; } = new List<MatchPostCardDto>();
        public int TotalCourtsAvailable { get; set; }
        public int TotalBookingsToday { get; set; }
    }

    public class SportTypeSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IconCss { get; set; } = string.Empty;
        public int FacilityCount { get; set; }
        public int CourtCount { get; set; }
    }

    public class FacilityCardDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string SportTypeName { get; set; } = string.Empty;
        public string? IconCss { get; set; }
        public string? CityName { get; set; }
        public string? DistrictName { get; set; }
        public string? ImageUrl { get; set; }
        public int CourtCount { get; set; }
        public int FirstCourtId { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public string OpeningHours { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
