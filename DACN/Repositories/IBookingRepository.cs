using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DACN.Models;

namespace DACN.Repositories
{
    public interface IBookingRepository : IRepository<Booking>
    {
        Task<bool> HasOverlappingBookingAsync(int courtId, DateOnly bookingDate, TimeSpan newStart, TimeSpan newEnd, int? excludeBookingId = null);
        Task<List<Booking>> GetBookingsByCourtAndDateAsync(int courtId, DateOnly bookingDate);
        Task<List<Booking>> GetBookingsByFacilityAndDateAsync(int facilityId, DateOnly bookingDate);
        Task<List<Booking>> GetCustomerBookingsAsync(string customerId);
        Task<Booking?> GetBookingDetailAsync(int bookingId);
    }
}
