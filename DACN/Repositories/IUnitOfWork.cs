using System;
using System.Data;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace DACN.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IBookingRepository Bookings { get; }
        IRepository<Facility> Facilities { get; }
        IRepository<Court> Courts { get; }
        IRepository<TimeSlot> TimeSlots { get; }
        IRepository<SportType> SportTypes { get; }
        IRepository<CityProvince> CityProvinces { get; }
        IRepository<FacilityService> FacilityServices { get; }
        IRepository<BookingServiceDetail> BookingServiceDetails { get; }
        IRepository<CustomerProfile> CustomerProfiles { get; }
        IRepository<NotificationConfig> NotificationConfigs { get; }
        IRepository<MatchPost> MatchPosts { get; }
        IRepository<MatchJoinRequest> MatchJoinRequests { get; }
        IRepository<Review> Reviews { get; }
        IRepository<BookingPaymentProof> PaymentProofs { get; }
        ApplicationDbContext Context { get; }

        Task<int> CompleteAsync();
        Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Serializable);
    }
}
