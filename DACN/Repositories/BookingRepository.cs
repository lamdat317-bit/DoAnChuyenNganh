using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Repositories
{
    public class BookingRepository : Repository<Booking>, IBookingRepository
    {
        public BookingRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<bool> HasOverlappingBookingAsync(int courtId, DateOnly bookingDate, TimeSpan newStart, TimeSpan newEnd, int? excludeBookingId = null)
        {
            var query = _dbSet.AsNoTracking()
                .Where(b => b.CourtId == courtId
                         && b.BookingDate == bookingDate
                         && b.Status != BookingStatus.Cancelled
                         && newStart < b.EndTime
                         && newEnd > b.StartTime);

            if (excludeBookingId.HasValue)
            {
                query = query.Where(b => b.Id != excludeBookingId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<List<Booking>> GetBookingsByCourtAndDateAsync(int courtId, DateOnly bookingDate)
        {
            return await _dbSet.AsNoTracking()
                .Include(b => b.Customer)
                .Where(b => b.CourtId == courtId
                         && b.BookingDate == bookingDate
                         && b.Status != BookingStatus.Cancelled)
                .OrderBy(b => b.StartTime)
                .ToListAsync();
        }

        public async Task<List<Booking>> GetBookingsByFacilityAndDateAsync(int facilityId, DateOnly bookingDate)
        {
            return await _dbSet.AsNoTracking()
                .Include(b => b.Court)
                .Include(b => b.Customer)
                .Where(b => b.Court != null
                         && b.Court.FacilityId == facilityId
                         && b.BookingDate == bookingDate
                         && b.Status != BookingStatus.Cancelled)
                .OrderBy(b => b.StartTime)
                .ToListAsync();
        }

        public async Task<List<Booking>> GetCustomerBookingsAsync(string customerId)
        {
            return await _dbSet.AsNoTracking()
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Facility)
                        .ThenInclude(f => f!.SportType)
                .Include(b => b.ServiceDetails)
                    .ThenInclude(sd => sd.FacilityService)
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();
        }

        public async Task<Booking?> GetBookingDetailAsync(int bookingId)
        {
            return await _dbSet
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Facility)
                        .ThenInclude(f => f!.SportType)
                .Include(b => b.Customer)
                .Include(b => b.ServiceDetails)
                    .ThenInclude(sd => sd.FacilityService)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
        }
    }
}
