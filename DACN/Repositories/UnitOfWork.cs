using System;
using System.Data;
using System.Threading.Tasks;
using DACN.Data;
using DACN.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DACN.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private IBookingRepository? _bookingRepository;
        private IRepository<Facility>? _facilityRepository;
        private IRepository<Court>? _courtRepository;
        private IRepository<TimeSlot>? _timeSlotRepository;
        private IRepository<SportType>? _sportTypeRepository;
        private IRepository<CityProvince>? _cityProvinceRepository;
        private IRepository<FacilityService>? _facilityServiceRepository;
        private IRepository<BookingServiceDetail>? _bookingServiceDetailRepository;
        private IRepository<CustomerProfile>? _customerProfileRepository;
        private IRepository<NotificationConfig>? _notificationConfigRepository;
        private IRepository<MatchPost>? _matchPostRepository;
        private IRepository<MatchJoinRequest>? _matchJoinRequestRepository;

        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;
        }

        public ApplicationDbContext Context => _context;

        public IBookingRepository Bookings =>
            _bookingRepository ??= new BookingRepository(_context);

        public IRepository<Facility> Facilities =>
            _facilityRepository ??= new Repository<Facility>(_context);

        public IRepository<Court> Courts =>
            _courtRepository ??= new Repository<Court>(_context);

        public IRepository<TimeSlot> TimeSlots =>
            _timeSlotRepository ??= new Repository<TimeSlot>(_context);

        public IRepository<SportType> SportTypes =>
            _sportTypeRepository ??= new Repository<SportType>(_context);

        public IRepository<CityProvince> CityProvinces =>
            _cityProvinceRepository ??= new Repository<CityProvince>(_context);

        public IRepository<FacilityService> FacilityServices =>
            _facilityServiceRepository ??= new Repository<FacilityService>(_context);

        public IRepository<BookingServiceDetail> BookingServiceDetails =>
            _bookingServiceDetailRepository ??= new Repository<BookingServiceDetail>(_context);

        public IRepository<CustomerProfile> CustomerProfiles =>
            _customerProfileRepository ??= new Repository<CustomerProfile>(_context);

        public IRepository<NotificationConfig> NotificationConfigs =>
            _notificationConfigRepository ??= new Repository<NotificationConfig>(_context);

        public IRepository<MatchPost> MatchPosts =>
            _matchPostRepository ??= new Repository<MatchPost>(_context);

        public IRepository<MatchJoinRequest> MatchJoinRequests =>
            _matchJoinRequestRepository ??= new Repository<MatchJoinRequest>(_context);

        public async Task<int> CompleteAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Serializable)
        {
            return await _context.Database.BeginTransactionAsync(isolationLevel);
        }

        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
