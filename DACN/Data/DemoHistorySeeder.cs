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
    /// Tạo dữ liệu lịch sử mẫu (đơn đặt sân trong 4 tháng gần nhất + đánh giá) để trang Thống kê
    /// và phần Đánh giá có số liệu khi demo. Chỉ chạy 1 lần: khi CSDL chưa có đánh giá nào.
    /// </summary>
    public static class DemoHistorySeeder
    {
        private static readonly (string Email, string Name, string Phone)[] DemoCustomers =
        {
            ("minhanh@gmail.com", "Lê Minh Anh", "0912345678"),
            ("quanghuy@gmail.com", "Phạm Quang Huy", "0934567890"),
            ("thuha@gmail.com", "Nguyễn Thu Hà", "0978123456"),
            ("ducthang@gmail.com", "Võ Đức Thắng", "0965432187"),
        };

        private static readonly (int Rating, string Comment)[] SampleReviews =
        {
            (5, "Sân đẹp, mặt sân êm, đèn sáng. Chủ sân nhiệt tình, sẽ quay lại!"),
            (4, "Sân ổn, giá hợp lý. Giờ cao điểm hơi đông, nên đặt trước."),
            (5, "Đặt online rất nhanh, đến nơi là có sân ngay. Rất tiện."),
            (4, "Chỗ để xe rộng, có căng-tin. Phòng thay đồ nên sạch hơn chút."),
            (3, "Sân được, nhưng đèn một góc hơi tối vào buổi tối."),
            (5, "Nhân viên thân thiện, có cho thuê bóng và áo pitch. 10 điểm!"),
            (4, "Vị trí dễ tìm, thanh toán VietQR nhanh gọn."),
            (5, "Chất lượng sân tốt nhất khu vực, giá cũng mềm."),
        };

        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            if (await context.Reviews.AnyAsync()) return;

            var courts = await context.Courts.Include(c => c.TimeSlots).Where(c => c.IsActive).ToListAsync();
            if (courts.Count == 0) return;

            // Khách hàng mẫu (mật khẩu Password@123)
            var customers = new List<ApplicationUser>();
            var main = await userManager.FindByEmailAsync("customer@gmail.com");
            if (main != null) customers.Add(main);
            foreach (var (email, name, phone) in DemoCustomers)
            {
                var u = await userManager.FindByEmailAsync(email);
                if (u == null)
                {
                    u = new ApplicationUser
                    {
                        UserName = email, Email = email, FullName = name, PhoneNumber = phone,
                        Role = UserRole.Customer, EmailConfirmed = true, CreatedAt = DateTime.UtcNow.AddMonths(-4)
                    };
                    if (!(await userManager.CreateAsync(u, "Password@123")).Succeeded) continue;
                }
                customers.Add(u);
            }
            if (customers.Count == 0) return;

            var rnd = new Random(2026);
            var today = DateOnly.FromDateTime(DateTime.Today);
            int[] startHours = { 6, 7, 8, 9, 15, 16, 17, 17, 18, 18, 19, 19, 19, 20, 20, 21 };
            var newBookings = new List<Booking>();

            for (int dayOffset = 120; dayOffset >= 1; dayOffset--)
            {
                var date = today.AddDays(-dayOffset);
                bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                int perDay = rnd.Next(weekend ? 2 : 0, weekend ? 5 : 3);

                for (int i = 0; i < perDay; i++)
                {
                    var court = courts[rnd.Next(courts.Count)];
                    var start = new TimeSpan(startHours[rnd.Next(startHours.Length)], 0, 0);
                    var end = start.Add(TimeSpan.FromHours(rnd.Next(0, 3) == 0 ? 2 : 1));

                    // Bỏ qua nếu trùng giờ với đơn khác cùng sân cùng ngày
                    if (newBookings.Any(b => b.CourtId == court.Id && b.BookingDate == date && b.StartTime < end && b.EndTime > start))
                        continue;
                    if (await context.Bookings.AnyAsync(b => b.CourtId == court.Id && b.BookingDate == date && b.StartTime < end && b.EndTime > start))
                        continue;

                    var price = Services.BookingRules.CalculatePrice(court.TimeSlots, start, end);
                    if (price <= 0) price = 200000m * (decimal)(end - start).TotalHours;

                    var customer = customers[rnd.Next(customers.Count)];
                    bool cancelled = rnd.Next(100) < 8;
                    var createdAt = date.ToDateTime(TimeOnly.MinValue).AddDays(-rnd.Next(1, 4)).ToUniversalTime();

                    newBookings.Add(new Booking
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.FullName,
                        CustomerPhone = customer.PhoneNumber,
                        CourtId = court.Id,
                        BookingDate = date,
                        StartTime = start,
                        EndTime = end,
                        TotalAmount = price,
                        Status = cancelled ? BookingStatus.Cancelled : BookingStatus.Completed,
                        PaymentStatus = cancelled ? PaymentStatus.Unpaid : PaymentStatus.Paid,
                        PaymentMethod = cancelled ? null : "VietQR",
                        PaidAt = cancelled ? null : createdAt.AddMinutes(5),
                        PaymentTransactionRef = cancelled ? null : $"VTB-SEED-{date:yyyyMMdd}-{court.Id}{start.Hours:00}",
                        PaymentNote = cancelled ? "Khách hủy lịch" : "Dữ liệu mẫu",
                        CreatedAt = createdAt
                    });
                }
            }

            context.Bookings.AddRange(newBookings);
            await context.SaveChangesAsync();

            // Đánh giá cho khoảng 1/4 số đơn đã hoàn thành
            var courtFacility = courts.ToDictionary(c => c.Id, c => c.FacilityId);
            var reviewable = newBookings.Where(b => b.Status == BookingStatus.Completed).Where((_, idx) => idx % 4 == 0).ToList();
            foreach (var b in reviewable)
            {
                var (rating, comment) = SampleReviews[rnd.Next(SampleReviews.Length)];
                context.Reviews.Add(new Review
                {
                    BookingId = b.Id,
                    CustomerId = b.CustomerId,
                    FacilityId = courtFacility[b.CourtId],
                    Rating = rating,
                    Comment = comment,
                    OwnerReply = rating <= 3 ? "Cảm ơn bạn đã góp ý, sân sẽ cải thiện sớm nhất!" : null,
                    CreatedAt = b.BookingDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime)).AddHours(2).ToUniversalTime()
                });
            }
            await context.SaveChangesAsync();
        }
    }
}
