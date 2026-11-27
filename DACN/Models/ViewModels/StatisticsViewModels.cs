using System.Collections.Generic;

namespace DACN.Models.ViewModels
{
    public class ChartPoint
    {
        public string Label { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public int Count { get; set; }
    }

    public class TopCustomerDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public int Bookings { get; set; }
        public decimal Spent { get; set; }
    }

    /// <summary>Thống kê doanh thu cho Chủ sân (hoặc Admin xem toàn hệ thống).</summary>
    public class RevenueStatisticsViewModel
    {
        public int? SelectedFacilityId { get; set; }
        public int RangeDays { get; set; } = 30;
        public List<Facility> Facilities { get; set; } = new();

        public decimal TotalRevenue { get; set; }
        public decimal PreviousRevenue { get; set; }
        public int TotalBookings { get; set; }
        public int PaidBookings { get; set; }
        public int CancelledBookings { get; set; }
        public decimal AverageBookingValue { get; set; }
        public double CancellationRate { get; set; }
        public decimal PendingAmount { get; set; }

        public List<ChartPoint> DailyRevenue { get; set; } = new();
        public List<ChartPoint> MonthlyRevenue { get; set; } = new();
        public List<ChartPoint> RevenueByCourt { get; set; } = new();
        public List<ChartPoint> BookingsByHour { get; set; } = new();
        public List<TopCustomerDto> TopCustomers { get; set; } = new();

        public double RevenueGrowthPercent =>
            PreviousRevenue == 0 ? (TotalRevenue > 0 ? 100 : 0) : (double)((TotalRevenue - PreviousRevenue) / PreviousRevenue * 100);
    }

    /// <summary>Trang tổng quan của Admin.</summary>
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalCustomers { get; set; }
        public int TotalOwners { get; set; }
        public int TotalFacilities { get; set; }
        public int TotalCourts { get; set; }
        public int TotalBookings { get; set; }
        public int BookingsThisMonth { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public decimal RevenueAllTime { get; set; }
        public int TotalReviews { get; set; }
        public double AverageRating { get; set; }

        public List<ChartPoint> MonthlyRevenue { get; set; } = new();
        public List<ChartPoint> BookingsBySport { get; set; } = new();
        public List<AdminFacilityRow> Facilities { get; set; } = new();
        public List<Booking> RecentBookings { get; set; } = new();
    }

    public class AdminFacilityRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string SportName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerEmail { get; set; }
        public int CourtCount { get; set; }
        public int BookingCount { get; set; }
        public decimal Revenue { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    public class ReviewFormViewModel
    {
        public int BookingId { get; set; }
        public int Rating { get; set; } = 5;
        public string? Comment { get; set; }
    }
}
