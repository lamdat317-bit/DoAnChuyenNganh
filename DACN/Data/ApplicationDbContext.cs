using DACN.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DACN.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<SportType> SportTypes => Set<SportType>();
        public DbSet<CityProvince> CityProvinces => Set<CityProvince>();
        public DbSet<Facility> Facilities => Set<Facility>();
        public DbSet<FacilityService> FacilityServices => Set<FacilityService>();
        public DbSet<Court> Courts => Set<Court>();
        public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<BookingServiceDetail> BookingServiceDetails => Set<BookingServiceDetail>();
        public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
        public DbSet<NotificationConfig> NotificationConfigs => Set<NotificationConfig>();
        public DbSet<MatchPost> MatchPosts => Set<MatchPost>();
        public DbSet<MatchJoinRequest> MatchJoinRequests => Set<MatchJoinRequest>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<BookingPaymentProof> BookingPaymentProofs => Set<BookingPaymentProof>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

        /// <summary>
        /// Cột SQL kiểu time chỉ lưu tối đa 23:59:59.9999999 nên mốc 24:00 (nửa đêm, dùng cho sân mở tới khuya)
        /// được lưu thành 23:59:59.9999999 và đọc ra lại thành 24:00. Thứ tự so sánh vẫn đúng vì không có giá trị nào nằm giữa.
        /// </summary>
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<TimeSpan>().HaveConversion<DACN.Data.EndOfDayTimeSpanConverter>();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Facility relationships
            builder.Entity<Facility>(entity =>
            {
                entity.HasOne(f => f.Owner)
                      .WithMany(u => u.Facilities)
                      .HasForeignKey(f => f.OwnerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(f => f.SportType)
                      .WithMany(s => s.Facilities)
                      .HasForeignKey(f => f.SportTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(f => f.City)
                      .WithMany(c => c.Facilities)
                      .HasForeignKey(f => f.CityId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(f => f.Name);
                entity.HasIndex(f => f.SportTypeId);
                entity.HasIndex(f => f.CityId);
                entity.HasIndex(f => f.OwnerId);
            });

            // Configure FacilityService
            builder.Entity<FacilityService>(entity =>
            {
                entity.HasOne(s => s.Facility)
                      .WithMany(f => f.Services)
                      .HasForeignKey(s => s.FacilityId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(s => s.Price)
                      .HasPrecision(18, 2);

                entity.HasIndex(s => s.FacilityId);
            });

            // Configure Court relationships
            builder.Entity<Court>(entity =>
            {
                entity.HasOne(c => c.Facility)
                      .WithMany(f => f.Courts)
                      .HasForeignKey(c => c.FacilityId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(c => c.FacilityId);
            });

            // Configure TimeSlot relationships
            builder.Entity<TimeSlot>(entity =>
            {
                entity.HasOne(t => t.Court)
                      .WithMany(c => c.TimeSlots)
                      .HasForeignKey(t => t.CourtId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(t => t.Price)
                      .HasPrecision(18, 2);

                entity.HasIndex(t => new { t.CourtId, t.StartTime, t.EndTime });
            });

            // Configure Booking relationships & Concurrency
            builder.Entity<Booking>(entity =>
            {
                entity.HasOne(b => b.Customer)
                      .WithMany(u => u.Bookings)
                      .HasForeignKey(b => b.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(b => b.Court)
                      .WithMany(c => c.Bookings)
                      .HasForeignKey(b => b.CourtId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Property(b => b.TotalAmount)
                      .HasPrecision(18, 2);

                // RowVersion for Optimistic Concurrency check
                entity.Property(b => b.RowVersion)
                      .IsRowVersion();

                // Composite Index to accelerate interval overlap checks
                entity.HasIndex(b => new { b.CourtId, b.BookingDate, b.StartTime, b.EndTime })
                      .HasDatabaseName("IX_Bookings_OverlapCheck");

                entity.HasIndex(b => b.CustomerId);
                entity.HasIndex(b => b.Status);
            });

            // Configure BookingServiceDetail
            builder.Entity<BookingServiceDetail>(entity =>
            {
                entity.HasOne(d => d.Booking)
                      .WithMany(b => b.ServiceDetails)
                      .HasForeignKey(d => d.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.FacilityService)
                      .WithMany(s => s.BookingServiceDetails)
                      .HasForeignKey(d => d.FacilityServiceId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Property(d => d.SubTotal)
                      .HasPrecision(18, 2);

                entity.HasIndex(d => d.BookingId);
                entity.HasIndex(d => d.FacilityServiceId);
            });

            // Ảnh biên lai chuyển khoản lưu trong SQL (1-1 với Booking)
            builder.Entity<BookingPaymentProof>(entity =>
            {
                entity.Property(p => p.Data).HasColumnType("varbinary(max)");
                entity.HasOne(p => p.Booking)
                      .WithOne()
                      .HasForeignKey<BookingPaymentProof>(p => p.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure Review (Đánh giá cụm sân)
            builder.Entity<Review>(entity =>
            {
                entity.HasOne(r => r.Facility)
                      .WithMany(f => f.Reviews)
                      .HasForeignKey(r => r.FacilityId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.Customer)
                      .WithMany()
                      .HasForeignKey(r => r.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Booking)
                      .WithMany()
                      .HasForeignKey(r => r.BookingId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure CustomerProfile
            builder.Entity<CustomerProfile>(entity =>
            {
                entity.HasOne(p => p.Customer)
                      .WithOne(u => u.Profile)
                      .HasForeignKey<CustomerProfile>(p => p.ApplicationUserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(p => p.TotalSpentAmount)
                      .HasPrecision(18, 2);

                entity.HasIndex(p => p.ApplicationUserId)
                      .IsUnique();
            });

            // Configure NotificationConfig
            builder.Entity<NotificationConfig>(entity =>
            {
                entity.HasOne(n => n.Facility)
                      .WithOne(f => f.NotificationConfig)
                      .HasForeignKey<NotificationConfig>(n => n.FacilityId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(n => n.FacilityId)
                      .IsUnique();
            });

            // Configure MatchPost
            builder.Entity<MatchPost>(entity =>
            {
                entity.HasOne(m => m.Creator)
                      .WithMany()
                      .HasForeignKey(m => m.CreatorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.SportType)
                      .WithMany()
                      .HasForeignKey(m => m.SportTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.Facility)
                      .WithMany()
                      .HasForeignKey(m => m.FacilityId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(m => m.Booking)
                      .WithMany()
                      .HasForeignKey(m => m.BookingId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.Property(m => m.CostPerPerson)
                      .HasPrecision(18, 2);

                entity.HasIndex(m => m.SportTypeId);
                entity.HasIndex(m => m.MatchDate);
                entity.HasIndex(m => m.PostType);
                entity.HasIndex(m => m.Status);
            });

            // Configure MatchJoinRequest
            builder.Entity<MatchJoinRequest>(entity =>
            {
                entity.HasOne(r => r.MatchPost)
                      .WithMany(m => m.JoinRequests)
                      .HasForeignKey(r => r.MatchPostId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.User)
                      .WithMany()
                      .HasForeignKey(r => r.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(r => new { r.MatchPostId, r.UserId });
            });
        }
    }
}
