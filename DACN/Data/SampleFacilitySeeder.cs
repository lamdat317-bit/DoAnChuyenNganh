using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DACN.Data
{
    /// <summary>
    /// Thêm các cụm sân mẫu ở nhiều tỉnh / môn thể thao (kèm sân con, bảng giá, dịch vụ và đánh giá)
    /// để trang chủ, trang tìm sân và bản đồ có dữ liệu phong phú khi demo.
    /// Chạy an toàn nhiều lần: cụm sân nào đã có (theo tên) thì bỏ qua.
    /// </summary>
    public static class SampleFacilitySeeder
    {
        private record CourtSpec(string Name, decimal Price, decimal PeakPrice);
        private record ServiceSpec(string Name, decimal Price, string Unit);
        private record FacilitySpec(
            string Name, string Sport, string City, string District, string Address,
            double Lat, double Lng, TimeSpan Open, TimeSpan Close, string Description,
            CourtSpec[] Courts, ServiceSpec[] Services, int[] Ratings);

        private static readonly ServiceSpec Water = new("Nước khoáng 500ml", 10000m, "Chai");
        private static readonly ServiceSpec Isotonic = new("Nước điện giải Revive", 15000m, "Chai");
        private static readonly ServiceSpec Towel = new("Khăn lạnh", 5000m, "Cái");

        private static readonly FacilitySpec[] Samples =
        {
            new("Sân Bóng Đá Mini Thành Công", "Bóng đá", "Hà Nội", "Ba Đình",
                "Ngõ 12 Thành Công, Phường Thành Công, Ba Đình, Hà Nội", 21.0198, 105.8160,
                new(6, 0, 0), new(23, 0, 0),
                "Cụm 3 sân bóng 5 người cỏ nhân tạo thế hệ mới, đèn LED chống chói, có phòng thay đồ và tủ khóa.",
                new[] { new CourtSpec("Sân 5 số 1", 200000m, 350000m), new CourtSpec("Sân 5 số 2", 200000m, 350000m), new CourtSpec("Sân 7 (ghép)", 380000m, 600000m) },
                new[] { Water, Isotonic, new ServiceSpec("Thuê áo pitch (bộ 7 áo)", 30000m, "Bộ/Trận") },
                new[] { 5, 4, 5, 4, 5 }),

            new("Sân Cầu Lông Hoàng Mai Arena", "Cầu lông", "Hà Nội", "Hoàng Mai",
                "88 Tam Trinh, Phường Mai Động, Hoàng Mai, Hà Nội", 20.9935, 105.8636,
                new(5, 30, 0), new(22, 30, 0),
                "Nhà thi đấu 6 sân cầu lông thảm PVC chuẩn BWF, trần cao 9m, điều hòa và đèn không chói mắt.",
                new[] { new CourtSpec("Sân A1", 80000m, 120000m), new CourtSpec("Sân A2", 80000m, 120000m), new CourtSpec("Sân A3", 80000m, 120000m), new CourtSpec("Sân VIP", 110000m, 160000m) },
                new[] { Water, new ServiceSpec("Ống cầu lông thi đấu (12 quả)", 260000m, "Ống"), new ServiceSpec("Thuê vợt", 20000m, "Cây/Lượt") },
                new[] { 5, 5, 4, 4 }),

            new("Sân Tennis Hồ Tây Garden", "Tennis", "Hà Nội", "Tây Hồ",
                "25 Xuân Diệu, Phường Quảng An, Tây Hồ, Hà Nội", 21.0632, 105.8293,
                new(6, 0, 0), new(22, 0, 0),
                "2 sân tennis mặt cứng ven hồ, không gian xanh mát, có huấn luyện viên kèm theo yêu cầu.",
                new[] { new CourtSpec("Sân Hard 1", 250000m, 350000m), new CourtSpec("Sân Hard 2", 250000m, 350000m) },
                new[] { Water, Towel, new ServiceSpec("Thuê HLV 1 kèm 1", 300000m, "Giờ") },
                new[] { 5, 4, 5 }),

            new("Sân Bóng Đá Phú Nhuận Sports", "Bóng đá", "TP. Hồ Chí Minh", "Phú Nhuận",
                "145 Hoàng Văn Thụ, Phường 8, Phú Nhuận, TP.HCM", 10.7993, 106.6762,
                new(6, 0, 0), new(23, 30, 0),
                "Sân bóng 5 và 7 người ngay trung tâm, gần sân bay, bãi giữ xe máy rộng, có căng-tin phục vụ đến khuya.",
                new[] { new CourtSpec("Sân 5A", 250000m, 400000m), new CourtSpec("Sân 5B", 250000m, 400000m), new CourtSpec("Sân 7", 450000m, 700000m) },
                new[] { Water, Isotonic, new ServiceSpec("Thuê bóng thi đấu", 40000m, "Quả/Trận") },
                new[] { 4, 5, 4, 3, 5, 4 }),

            new("Nhà Thi Đấu Cầu Lông Tân Bình", "Cầu lông", "TP. Hồ Chí Minh", "Tân Bình",
                "210 Cộng Hòa, Phường 12, Tân Bình, TP.HCM", 10.8016, 106.6522,
                new(5, 0, 0), new(22, 0, 0),
                "8 sân cầu lông thảm Victor, quạt công nghiệp, phù hợp đánh phong trào và tổ chức giải nội bộ công ty.",
                new[] { new CourtSpec("Sân 1", 70000m, 110000m), new CourtSpec("Sân 2", 70000m, 110000m), new CourtSpec("Sân 3", 70000m, 110000m) },
                new[] { Water, new ServiceSpec("Cầu lông Thành Công (ống 12)", 220000m, "Ống") },
                new[] { 4, 4, 5 }),

            new("Pickleball Thảo Điền Club", "Pickleball", "TP. Hồ Chí Minh", "Thủ Đức",
                "30 Quốc Hương, Phường Thảo Điền, TP. Thủ Đức, TP.HCM", 10.8045, 106.7329,
                new(5, 30, 0), new(22, 0, 0),
                "4 sân pickleball có mái che, sàn acrylic chống trơn, khu nghỉ có quạt hơi nước và quầy nước ép.",
                new[] { new CourtSpec("Court 1", 150000m, 220000m), new CourtSpec("Court 2", 150000m, 220000m), new CourtSpec("Court 3", 150000m, 220000m) },
                new[] { Water, new ServiceSpec("Thuê vợt pickleball", 30000m, "Cây/Lượt"), new ServiceSpec("Bóng pickleball (hộp 3)", 90000m, "Hộp") },
                new[] { 5, 5, 4, 5 }),

            new("Sân Tennis Sky Court Quận 10", "Tennis", "TP. Hồ Chí Minh", "Quận 10",
                "60 Thành Thái, Phường 12, Quận 10, TP.HCM", 10.7724, 106.6644,
                new(6, 0, 0), new(22, 0, 0),
                "Sân tennis tiêu chuẩn thi đấu, đèn pha chiếu sáng ban đêm, có lớp học tennis cho trẻ em cuối tuần.",
                new[] { new CourtSpec("Sân trung tâm", 220000m, 320000m), new CourtSpec("Sân số 2", 200000m, 300000m) },
                new[] { Water, Towel },
                new[] { 4, 5, 4 }),

            new("Sân Bóng Đá Biển Xanh Đà Nẵng", "Bóng đá", "Đà Nẵng", "Sơn Trà",
                "75 Võ Nguyên Giáp, Phường Phước Mỹ, Sơn Trà, Đà Nẵng", 16.0617, 108.2460,
                new(6, 0, 0), new(23, 0, 0),
                "Sân bóng gần biển Mỹ Khê, gió mát buổi tối, cỏ nhân tạo mới thay năm 2026.",
                new[] { new CourtSpec("Sân 5 – Biển 1", 180000m, 300000m), new CourtSpec("Sân 5 – Biển 2", 180000m, 300000m) },
                new[] { Water, Isotonic },
                new[] { 5, 4, 5 }),

            new("Pickleball Sông Hàn", "Pickleball", "Đà Nẵng", "Hải Châu",
                "12 Bạch Đằng, Phường Thạch Thang, Hải Châu, Đà Nẵng", 16.0738, 108.2240,
                new(5, 30, 0), new(21, 30, 0),
                "Sân pickleball view sông Hàn, phù hợp người mới chơi, có lớp hướng dẫn cơ bản mỗi sáng thứ 7.",
                new[] { new CourtSpec("Sân 1", 120000m, 180000m), new CourtSpec("Sân 2", 120000m, 180000m) },
                new[] { Water, new ServiceSpec("Thuê vợt pickleball", 25000m, "Cây/Lượt") },
                new[] { 5, 4 }),

            new("Sân Bóng Đá Cần Thơ Riverside", "Bóng đá", "Cần Thơ", "Ninh Kiều",
                "18 Trần Văn Khéo, Phường Cái Khế, Ninh Kiều, Cần Thơ", 10.0452, 105.7880,
                new(6, 0, 0), new(22, 30, 0),
                "Cụm sân bóng ven sông, bãi đậu ô tô miễn phí, thường xuyên tổ chức giải phong trào hàng tháng.",
                new[] { new CourtSpec("Sân 1", 150000m, 250000m), new CourtSpec("Sân 2", 150000m, 250000m), new CourtSpec("Sân 3", 150000m, 250000m) },
                new[] { Water, Isotonic },
                new[] { 4, 4, 5 }),

            new("Sân Cầu Lông Hải Phòng Star", "Cầu lông", "Hải Phòng", "Ngô Quyền",
                "102 Lạch Tray, Phường Lạch Tray, Ngô Quyền, Hải Phòng", 20.8517, 106.6925,
                new(5, 30, 0), new(22, 0, 0),
                "Sân cầu lông có điều hòa, ánh sáng đều, phòng tắm nước nóng và chỗ để xe có mái che.",
                new[] { new CourtSpec("Sân 1", 70000m, 100000m), new CourtSpec("Sân 2", 70000m, 100000m) },
                new[] { Water, new ServiceSpec("Thuê vợt", 15000m, "Cây/Lượt") },
                new[] { 4, 5 }),

            new("Sân Bóng Đá Bình Dương Green", "Bóng đá", "Bình Dương", "Thủ Dầu Một",
                "Đường Yersin, Phường Phú Cường, Thủ Dầu Một, Bình Dương", 10.9804, 106.6519,
                new(6, 0, 0), new(23, 0, 0),
                "Sân 7 người rộng rãi, phù hợp đá giao hữu công ty, có khán đài nhỏ và loa thông báo.",
                new[] { new CourtSpec("Sân 7A", 350000m, 550000m), new CourtSpec("Sân 7B", 350000m, 550000m) },
                new[] { Water, Isotonic, new ServiceSpec("Thuê trọng tài", 150000m, "Trận") },
                new[] { 5, 4, 4 }),
        };

        private static readonly Dictionary<int, string[]> CommentsByRating = new()
        {
            [5] = new[] { "Sân đẹp, sạch sẽ, chủ sân thân thiện. Sẽ quay lại!", "Đặt online nhanh, tới nơi có sân ngay, rất tiện.", "Chất lượng tốt nhất khu vực, giá hợp lý." },
            [4] = new[] { "Sân ổn, giờ cao điểm hơi đông nên đặt trước.", "Mặt sân tốt, chỗ để xe hơi chật vào buổi tối.", "Giá hợp lý, nhân viên hỗ trợ nhiệt tình." },
            [3] = new[] { "Sân được nhưng đèn một góc hơi tối.", "Phòng thay đồ cần sạch hơn." },
        };

        public static async Task<int> SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            var owner = await userManager.FindByEmailAsync("owner@datsan247.vn");
            if (owner == null) return 0;

            var sports = await context.SportTypes.ToListAsync();
            if (sports.Count == 0) return 0;
            var cities = await context.CityProvinces.ToListAsync();

            var existingNames = await context.Facilities.Select(f => f.Name).ToListAsync();
            var todo = Samples.Where(s => !existingNames.Contains(s.Name)).ToList();
            if (todo.Count == 0) return 0;

            // Khách hàng mẫu để viết đánh giá (nếu có)
            var reviewerEmails = new[] { "customer@gmail.com", "minhanh@gmail.com", "quanghuy@gmail.com", "thuha@gmail.com", "ducthang@gmail.com" };
            var reviewers = await context.Users.Where(u => u.Email != null && reviewerEmails.Contains(u.Email)).ToListAsync();

            var today = DateOnly.FromDateTime(DateTime.Today);
            int added = 0, seq = 0;

            foreach (var spec in todo)
            {
                var sport = sports.FirstOrDefault(s => s.Name.Contains(spec.Sport, StringComparison.OrdinalIgnoreCase))
                            ?? sports.First();
                var city = cities.FirstOrDefault(c => c.Name == spec.City);
                if (city == null)
                {
                    city = new CityProvince { Name = spec.City };
                    context.CityProvinces.Add(city);
                    await context.SaveChangesAsync();
                    cities.Add(city);
                }

                var facility = new Facility
                {
                    OwnerId = owner.Id,
                    Name = spec.Name,
                    Address = spec.Address,
                    CityId = city.Id,
                    DistrictName = spec.District,
                    Latitude = spec.Lat,
                    Longitude = spec.Lng,
                    Description = spec.Description,
                    OpeningTime = spec.Open,
                    ClosingTime = spec.Close,
                    SportTypeId = sport.Id,
                    ApprovalStatus = FacilityApprovalStatus.Approved,
                    ApprovedAt = DateTime.UtcNow
                };
                context.Facilities.Add(facility);
                await context.SaveChangesAsync();

                // Sân con + bảng giá: giờ thường đến 17:00, giờ vàng 17:00 → đóng cửa
                var peakStart = new TimeSpan(17, 0, 0);
                var courts = new List<Court>();
                foreach (var cs in spec.Courts)
                {
                    var court = new Court { FacilityId = facility.Id, Name = cs.Name, IsActive = true };
                    context.Courts.Add(court);
                    courts.Add(court);
                }
                await context.SaveChangesAsync();

                for (int i = 0; i < courts.Count; i++)
                {
                    var cs = spec.Courts[i];
                    context.TimeSlots.Add(new TimeSlot { CourtId = courts[i].Id, StartTime = spec.Open, EndTime = peakStart, Price = cs.Price, IsPeakHour = false });
                    context.TimeSlots.Add(new TimeSlot { CourtId = courts[i].Id, StartTime = peakStart, EndTime = spec.Close, Price = cs.PeakPrice, IsPeakHour = true });
                }

                foreach (var sv in spec.Services)
                {
                    context.FacilityServices.Add(new FacilityService { FacilityId = facility.Id, Name = sv.Name, Price = sv.Price, Unit = sv.Unit, IsActive = true });
                }
                await context.SaveChangesAsync();

                // Đơn đã chơi + đánh giá (mỗi đánh giá gắn với 1 đơn hoàn thành)
                if (reviewers.Count > 0)
                {
                    for (int r = 0; r < spec.Ratings.Length; r++)
                    {
                        seq++;
                        var rating = spec.Ratings[r];
                        var reviewer = reviewers[(seq + r) % reviewers.Count];
                        var court = courts[r % courts.Count];
                        var cs = spec.Courts[r % courts.Count];
                        var date = today.AddDays(-(5 + r * 9 + seq % 7));
                        var start = new TimeSpan(17 + (r % 4), 0, 0);
                        var end = start.Add(TimeSpan.FromHours(1));

                        var booking = new Booking
                        {
                            CustomerId = reviewer.Id,
                            CustomerName = reviewer.FullName,
                            CustomerPhone = reviewer.PhoneNumber,
                            CourtId = court.Id,
                            BookingDate = date,
                            StartTime = start,
                            EndTime = end,
                            TotalAmount = cs.PeakPrice,
                            Status = BookingStatus.Completed,
                            PaymentStatus = PaymentStatus.Paid,
                            PaymentMethod = "VietQR",
                            PaidAt = date.ToDateTime(TimeOnly.MinValue).AddDays(-1).ToUniversalTime(),
                            PaymentNote = "Dữ liệu mẫu",
                            CreatedAt = date.ToDateTime(TimeOnly.MinValue).AddDays(-1).ToUniversalTime()
                        };
                        context.Bookings.Add(booking);
                        await context.SaveChangesAsync();

                        var comments = CommentsByRating.TryGetValue(rating, out var list) ? list : CommentsByRating[4];
                        context.Reviews.Add(new Review
                        {
                            FacilityId = facility.Id,
                            CustomerId = reviewer.Id,
                            BookingId = booking.Id,
                            Rating = rating,
                            Comment = comments[(seq + r) % comments.Length],
                            OwnerReply = rating <= 3 ? "Cảm ơn bạn đã góp ý, sân sẽ khắc phục sớm!" : null,
                            CreatedAt = date.ToDateTime(TimeOnly.FromTimeSpan(end)).AddHours(2).ToUniversalTime()
                        });
                    }
                    await context.SaveChangesAsync();
                }

                added++;
            }

            return added;
        }
    }
}
