using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.AspNetCore.Identity;

namespace DACN.Data
{
    public static class DbInitializer
    {
<<<<<<< HEAD
        /// <summary>
        /// Đổi thương hiệu: chuyển email tài khoản demo cũ (@sanbong.vn) sang @datsan247.vn, giữ nguyên mật khẩu và dữ liệu.
        /// </summary>
        public static async Task RenameLegacyDemoAccountsAsync(UserManager<ApplicationUser> userManager)
        {
            foreach (var prefix in new[] { "owner", "admin" })
            {
                var oldEmail = $"{prefix}@sanbong.vn";
                var newEmail = $"{prefix}@datsan247.vn";
                var user = await userManager.FindByEmailAsync(oldEmail);
                if (user == null || await userManager.FindByEmailAsync(newEmail) != null) continue;

                user.Email = newEmail;
                user.UserName = newEmail;
                await userManager.UpdateAsync(user); // tự cập nhật NormalizedEmail / NormalizedUserName
            }
        }

        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            await RenameLegacyDemoAccountsAsync(userManager);

=======
        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            // Kiểm tra và seed CityProvinces nếu chưa có
            if (!context.CityProvinces.Any())
            {
                var hanoi = new CityProvince { Name = "Hà Nội" };
                var hcm = new CityProvince { Name = "TP. Hồ Chí Minh" };
                var danang = new CityProvince { Name = "Đà Nẵng" };
                var haiphong = new CityProvince { Name = "Hải Phòng" };
                var binhduong = new CityProvince { Name = "Bình Dương" };
                var cantho = new CityProvince { Name = "Cần Thơ" };

                context.CityProvinces.AddRange(hanoi, hcm, danang, haiphong, binhduong, cantho);
                await context.SaveChangesAsync();

                // Gán CityId cho các facility hiện có nếu chưa có
                var facs = context.Facilities.ToList();
                foreach (var f in facs)
                {
                    if (f.CityId == null)
                    {
                        if (f.Address.Contains("Hà Nội", StringComparison.OrdinalIgnoreCase))
                        {
                            f.CityId = hanoi.Id;
                            f.DistrictName = "Cầu Giấy";
                        }
                        else
                        {
                            f.CityId = hcm.Id;
                            f.DistrictName = "Quận 7";
                        }
                    }
                }
                await context.SaveChangesAsync();
            }

            // Kiểm tra nếu SportTypes đã có thì kiểm tra seed MatchPosts & Services
            if (context.SportTypes.Any())
            {
                if (!context.FacilityServices.Any())
                {
                    var fac1 = context.Facilities.FirstOrDefault();
                    var fac2 = context.Facilities.Skip(1).FirstOrDefault() ?? fac1;
                    if (fac1 != null)
                    {
                        context.FacilityServices.AddRange(
                            new FacilityService { FacilityId = fac1.Id, Name = "Nước khoáng Lavie 500ml", Price = 10000m, Unit = "Chai", IsActive = true },
                            new FacilityService { FacilityId = fac1.Id, Name = "Bộ áo pitch phân đội (7 áo)", Price = 30000m, Unit = "Bộ/Trận", IsActive = true },
                            new FacilityService { FacilityId = fac1.Id, Name = "Thuê bóng đá thi đấu FIFA", Price = 50000m, Unit = "Quả/Trận", IsActive = true }
                        );
                    }
                    if (fac2 != null)
                    {
                        context.FacilityServices.AddRange(
                            new FacilityService { FacilityId = fac2.Id, Name = "Thuê vợt Pickleball Carbon Pro", Price = 30000m, Unit = "Cây/Trận", IsActive = true },
                            new FacilityService { FacilityId = fac2.Id, Name = "Hộp bóng Pickleball thi đấu 3 quả", Price = 80000m, Unit = "Hộp", IsActive = true }
                        );
                    }
                    await context.SaveChangesAsync();
                }

                if (!context.Bookings.Any(b => b.PaymentStatus == PaymentStatus.Unpaid))
                {
                    var customerUser = await userManager.FindByIdAsync("demo-customer-id") ?? context.Users.FirstOrDefault();
                    var court2 = context.Courts.Skip(1).FirstOrDefault() ?? context.Courts.FirstOrDefault();
                    if (customerUser != null && court2 != null)
                    {
                        context.Bookings.Add(new Booking
                        {
                            CustomerId = customerUser.Id,
                            CourtId = court2.Id,
                            BookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                            StartTime = new TimeSpan(19, 0, 0),
                            EndTime = new TimeSpan(20, 0, 0),
                            TotalAmount = 250000m,
                            Status = BookingStatus.Pending,
                            PaymentStatus = PaymentStatus.Unpaid,
                            CreatedAt = DateTime.UtcNow
                        });
                        await context.SaveChangesAsync();
                    }
                }

                if (!context.MatchPosts.Any())
                {
                    var existingCustomer = await userManager.FindByIdAsync("demo-customer-id") ?? context.Users.FirstOrDefault();
                    var existingOwner = await userManager.FindByIdAsync("demo-owner-id") ?? context.Users.FirstOrDefault();
                    var fb = context.SportTypes.FirstOrDefault(s => s.Name.Contains("Bóng đá")) ?? context.SportTypes.First();
                    var pb = context.SportTypes.FirstOrDefault(s => s.Name.Contains("Pickleball")) ?? context.SportTypes.First();
                    var bm = context.SportTypes.FirstOrDefault(s => s.Name.Contains("Cầu lông")) ?? context.SportTypes.First();
                    var fac1 = context.Facilities.FirstOrDefault() ?? new Facility { Name = "Sân Bóng Chảo Lửa Cầu Giấy", Address = "Hà Nội" };
                    var fac2 = context.Facilities.Skip(1).FirstOrDefault() ?? fac1;
                    var bk = context.Bookings.FirstOrDefault();

                    if (existingCustomer != null && existingOwner != null)
                    {
                        await SeedMatchPostsAsync(context, existingCustomer, existingOwner, fb, pb, bm, fac1, fac2, bk);
                    }
                }

<<<<<<< HEAD
                var existingAdmin = await userManager.FindByEmailAsync("admin@datsan247.vn");
=======
                var existingAdmin = await userManager.FindByEmailAsync("admin@sanbong.vn");
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                if (existingAdmin == null)
                {
                    var adminUser = new ApplicationUser
                    {
                        Id = "demo-admin-id",
<<<<<<< HEAD
                        UserName = "admin@datsan247.vn",
                        Email = "admin@datsan247.vn",
=======
                        UserName = "admin@sanbong.vn",
                        Email = "admin@sanbong.vn",
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                        FullName = "Quản Trị Viên Hệ Thống",
                        PhoneNumber = "0999888777",
                        Role = UserRole.Admin,
                        CreatedAt = DateTime.UtcNow,
                        EmailConfirmed = true
                    };
                    await userManager.CreateAsync(adminUser, "Password@123");
                }

                return;
            }

            // 1. Tạo Users mẫu
            var owner = new ApplicationUser
            {
                Id = "demo-owner-id",
<<<<<<< HEAD
                UserName = "owner@datsan247.vn",
                Email = "owner@datsan247.vn",
=======
                UserName = "owner@sanbong.vn",
                Email = "owner@sanbong.vn",
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                FullName = "Nguyễn Văn Chủ Sân",
                PhoneNumber = "0901234567",
                Role = UserRole.FacilityOwner,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var customer = new ApplicationUser
            {
                Id = "demo-customer-id",
                UserName = "customer@gmail.com",
                Email = "customer@gmail.com",
                FullName = "Trần Đăng Khoa",
                PhoneNumber = "0987654321",
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var admin = new ApplicationUser
            {
                Id = "demo-admin-id",
<<<<<<< HEAD
                UserName = "admin@datsan247.vn",
                Email = "admin@datsan247.vn",
=======
                UserName = "admin@sanbong.vn",
                Email = "admin@sanbong.vn",
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                FullName = "Quản Trị Viên Hệ Thống",
                PhoneNumber = "0999888777",
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            await userManager.CreateAsync(owner, "Password@123");
            await userManager.CreateAsync(customer, "Password@123");
            await userManager.CreateAsync(admin, "Password@123");

            // 2. Tạo SportTypes mẫu
            var football = new SportType { Name = "Bóng đá mini", IconCss = "bi bi-dribbble" };
            var badminton = new SportType { Name = "Cầu lông", IconCss = "bi bi-lightning-charge" };
            var tennis = new SportType { Name = "Quần vợt / Tennis", IconCss = "bi bi-circle" };
            var pickleball = new SportType { Name = "Pickleball", IconCss = "bi bi-fire" };

            var sportTypes = new List<SportType> { football, badminton, tennis, pickleball };
            await context.SportTypes.AddRangeAsync(sportTypes);
            await context.SaveChangesAsync();

            // 3. Tạo Cụm sân mẫu (Facilities)
            var f1 = new Facility
            {
                ApprovalStatus = FacilityApprovalStatus.Approved, ApprovedAt = DateTime.UtcNow,
                OwnerId = owner.Id,
                Name = "Sân Bóng Đá Chảo Lửa Cầu Giấy",
                Address = "Số 18 Hoàng Quán Chi, Dịch Vọng Hậu, Cầu Giấy, Hà Nội",
                Latitude = 21.0315,
                Longitude = 105.7892,
                Description = "Mặt cỏ nhân tạo chuẩn FIFA, hệ thống đèn LED cao cấp, bãi đỗ xe ô tô và căng-tin rộng rãi.",
                OpeningTime = new TimeSpan(6, 0, 0),
                ClosingTime = new TimeSpan(23, 0, 0),
                SportTypeId = football.Id
            };

            var f2 = new Facility
            {
                ApprovalStatus = FacilityApprovalStatus.Approved, ApprovedAt = DateTime.UtcNow,
                OwnerId = owner.Id,
                Name = "CLB Pickleball & Tennis Star",
                Address = "120 Nguyễn Thị Thập, Phường Tân Hưng, Quận 7, TP.HCM",
                Latitude = 10.7412,
                Longitude = 106.7025,
                Description = "Cụm 6 sân pickleball có mái che chống nóng, sơn sàn acrylic tiêu chuẩn thi đấu quốc tế.",
                OpeningTime = new TimeSpan(5, 30, 0),
                ClosingTime = new TimeSpan(22, 30, 0),
                SportTypeId = pickleball.Id
            };

            await context.Facilities.AddRangeAsync(f1, f2);
            await context.SaveChangesAsync();

            // 4. Tạo Sân con (Courts)
            var c1 = new Court { FacilityId = f1.Id, Name = "Sân 5A (Cỏ nhân tạo)", IsActive = true };
            var c2 = new Court { FacilityId = f1.Id, Name = "Sân 5B (Cỏ nhân tạo)", IsActive = true };
            var c3 = new Court { FacilityId = f1.Id, Name = "Sân 7A (Sân lớn)", IsActive = true };

            var c4 = new Court { FacilityId = f2.Id, Name = "Sân Pickleball Pro 01", IsActive = true };
            var c5 = new Court { FacilityId = f2.Id, Name = "Sân Pickleball Pro 02", IsActive = true };

            await context.Courts.AddRangeAsync(c1, c2, c3, c4, c5);
            await context.SaveChangesAsync();

            // 5. Cấu hình bảng giá theo khung giờ (TimeSlots) cho Sân 5A
            var timeSlots = new List<TimeSlot>
            {
                // Khung sáng bình thường
                new TimeSlot { CourtId = c1.Id, StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(16, 0, 0), Price = 180000m, IsPeakHour = false },
                // Khung giờ vàng / cao điểm chiều tối
                new TimeSlot { CourtId = c1.Id, StartTime = new TimeSpan(16, 0, 0), EndTime = new TimeSpan(20, 0, 0), Price = 300000m, IsPeakHour = true },
                // Khung đêm
                new TimeSlot { CourtId = c1.Id, StartTime = new TimeSpan(20, 0, 0), EndTime = new TimeSpan(23, 0, 0), Price = 220000m, IsPeakHour = false },

                // Pickleball
                new TimeSlot { CourtId = c4.Id, StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(17, 0, 0), Price = 120000m, IsPeakHour = false },
                new TimeSlot { CourtId = c4.Id, StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(22, 0, 0), Price = 200000m, IsPeakHour = true }
            };

            await context.TimeSlots.AddRangeAsync(timeSlots);
            await context.SaveChangesAsync();

            // 6. Tạo 1 đơn đặt sân mẫu
            var sampleBooking = new Booking
            {
                CustomerId = customer.Id,
                CourtId = c1.Id,
                BookingDate = DateOnly.FromDateTime(DateTime.Today),
                StartTime = new TimeSpan(17, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                TotalAmount = 300000m,
                Status = BookingStatus.Confirmed,
                PaymentStatus = PaymentStatus.Paid,
                CreatedAt = DateTime.UtcNow
            };

            await context.Bookings.AddAsync(sampleBooking);
            await context.SaveChangesAsync();

            // 7. Tạo bài đăng Teamwork / Tìm kèo / Ghép đội mẫu
            await SeedMatchPostsAsync(context, customer, owner, football, pickleball, badminton, f1, f2, sampleBooking);
        }

        public static async Task SeedMatchPostsAsync(
            ApplicationDbContext context,
            ApplicationUser customer,
            ApplicationUser owner,
            SportType football,
            SportType pickleball,
            SportType badminton,
            Facility f1,
            Facility f2,
            Booking? sampleBooking)
        {
            if (context.MatchPosts.Any()) return;

            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            var weekend = DateOnly.FromDateTime(DateTime.Today.AddDays(3));

            var post1 = new MatchPost
            {
                CreatorId = customer.Id,
                SportTypeId = football.Id,
                FacilityId = f1.Id,
                BookingId = sampleBooking?.Id,
                Title = "⚽ Kèo đá bóng sân 7 thiếu 2 chân cánh - Giao lưu vui vẻ",
                VenueName = f1.Name,
                VenueAddress = f1.Address,
                Description = "Đội mình lập nhóm đá dưỡng sinh thứ 5 hàng tuần, hiện đang có 5 người, cần tuyển thêm 2 bạn đá vị trí tiền vệ cánh hoặc thòng. Tinh thần fair-play, giao lưu bia bọt sau trận!",
                PostType = MatchPostType.FindTeammates,
                MatchDate = tomorrow,
                StartTime = new TimeSpan(19, 0, 0),
                EndTime = new TimeSpan(20, 30, 0),
                SkillLevel = MatchSkillLevel.Beginner,
                TotalSlots = 7,
                CurrentSlots = 5,
                CostPerPerson = 50000m,
                CostSharingNote = "Chia đều tiền sân 50k/người (đã bao gồm nước đá)",
                ContactPhone = "0987654321",
                Status = MatchPostStatus.Open,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            };

            var post2 = new MatchPost
            {
                CreatorId = customer.Id,
                SportTypeId = pickleball.Id,
                FacilityId = f2.Id,
                Title = "🏓 Tìm đôi nam/nữ giao lưu Pickleball cuối tuần cọ sát",
                VenueName = f2.Name,
                VenueAddress = f2.Address,
                Description = "Mình và bạn gái đánh DUPR tầm 3.0 - 3.5, muốn tìm 1 đôi nam nữ hoặc đôi nam cùng trình độ giao lưu 2 tiếng cuối tuần. Sân đẹp có mái che, quạt mát.",
                PostType = MatchPostType.FindOpponent,
                MatchDate = weekend,
                StartTime = new TimeSpan(17, 30, 0),
                EndTime = new TimeSpan(19, 30, 0),
                SkillLevel = MatchSkillLevel.Intermediate,
                TotalSlots = 4,
                CurrentSlots = 2,
                CostPerPerson = 60000m,
                CostSharingNote = "Kèo cưa đôi 50/50 tiền sân",
                ContactPhone = "0901234567",
                Status = MatchPostStatus.Open,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            };

            var post3 = new MatchPost
            {
                CreatorId = owner.Id,
                SportTypeId = badminton.Id,
                Title = "🏸 Tuyển 1 thành viên đánh đôi Cầu Lông chiều mai (16h - 18h)",
                VenueName = "Nhà thi đấu Cầu Giấy, Số 35 Trần Quý Kiên, Hà Nội",
                VenueAddress = "35 Trần Quý Kiên, Cầu Giấy, Hà Nội",
                Description = "Nhóm 3 người đang thiếu 1 bạn đánh đôi cho đủ 2 cặp. Trình độ trung bình, cầu lông Yonex chuẩn. Bạn nào rảnh tham gia cùng cho vui nhé!",
                PostType = MatchPostType.FindTeammates,
                MatchDate = tomorrow,
                StartTime = new TimeSpan(16, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                SkillLevel = MatchSkillLevel.Intermediate,
                TotalSlots = 4,
                CurrentSlots = 3,
                CostPerPerson = 45000m,
                CostSharingNote = "Chia đều tiền sân + tiền cầu",
                ContactPhone = "0912345678",
                Status = MatchPostStatus.Open,
                CreatedAt = DateTime.UtcNow.AddMinutes(-40)
            };

            var post4 = new MatchPost
            {
                CreatorId = customer.Id,
                SportTypeId = football.Id,
                FacilityId = f1.Id,
                Title = "🔥 FC Anh Em tìm đối tác đá sân 7 tối thứ 6 - Cược nước ngọt",
                VenueName = f1.Name,
                VenueAddress = f1.Address,
                Description = "Đội mình toàn dân văn phòng U30, đá lành mạnh, không chặt chém, muốn tìm 1 đội đối tác đá cọ sát 90 phút. Sân đã bao trước, chia đôi chi phí.",
                PostType = MatchPostType.FindOpponent,
                MatchDate = tomorrow.AddDays(1),
                StartTime = new TimeSpan(20, 0, 0),
                EndTime = new TimeSpan(21, 30, 0),
                SkillLevel = MatchSkillLevel.Intermediate,
                TotalSlots = 14,
                CurrentSlots = 7,
                CostPerPerson = 50000m,
                CostSharingNote = "Cưa đôi tiền sân + cược két nước ngọt",
                ContactPhone = "0987654321",
                Status = MatchPostStatus.Open,
                CreatedAt = DateTime.UtcNow.AddHours(-5)
            };

            await context.MatchPosts.AddRangeAsync(post1, post2, post3, post4);
            await context.SaveChangesAsync();

            // Thêm 1 request mẫu
            var joinRequest = new MatchJoinRequest
            {
                MatchPostId = post1.Id,
                UserId = owner.Id,
                PreferredPosition = "Tiền vệ cánh phải",
                ContactPhone = "0901234567",
                Message = "Chào đội trưởng, mình đá cánh phải được 3 năm rồi, cho mình xin 1 slot nhé!",
                Status = JoinRequestStatus.Accepted,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            };

            await context.MatchJoinRequests.AddAsync(joinRequest);
            await context.SaveChangesAsync();
        }
    }
}
