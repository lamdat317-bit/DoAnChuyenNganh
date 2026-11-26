using System;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = AppRoles.Admin)]
    public class DashboardController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        // GET: /Admin/Dashboard
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.Select(u => u.Role).ToListAsync();

            var facilities = await _unitOfWork.Facilities.Query()
                .Include(f => f.Owner)
                .Include(f => f.SportType)
                .Include(f => f.Courts)
                .Include(f => f.Reviews)
                .OrderBy(f => f.Name)
                .ToListAsync();

            var bookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court)
                .ToListAsync();

            static bool IsRevenue(Booking b) => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled;

            var today = DateOnly.FromDateTime(DateTime.Today);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var reviews = facilities.SelectMany(f => f.Reviews).ToList();
            var facilityById = facilities.ToDictionary(f => f.Id);

            var vm = new AdminDashboardViewModel
            {
                TotalUsers = users.Count,
                TotalCustomers = users.Count(r => r == UserRole.Customer),
                TotalOwners = users.Count(r => r == UserRole.FacilityOwner),
                TotalFacilities = facilities.Count,
                TotalCourts = facilities.Sum(f => f.Courts.Count),
                TotalBookings = bookings.Count,
                BookingsThisMonth = bookings.Count(b => b.BookingDate >= monthStart),
                RevenueThisMonth = bookings.Where(b => b.BookingDate >= monthStart).Where(IsRevenue).Sum(b => b.TotalAmount),
                RevenueAllTime = bookings.Where(IsRevenue).Sum(b => b.TotalAmount),
                TotalReviews = reviews.Count,
                AverageRating = reviews.Count > 0 ? reviews.Average(r => r.Rating) : 0
            };

            for (int i = 11; i >= 0; i--)
            {
                var m = monthStart.AddMonths(-i);
                var month = bookings.Where(b => b.BookingDate.Year == m.Year && b.BookingDate.Month == m.Month).ToList();
                vm.MonthlyRevenue.Add(new ChartPoint
                {
                    Label = m.ToString("MM/yyyy"),
                    Value = month.Where(IsRevenue).Sum(b => b.TotalAmount),
                    Count = month.Count
                });
            }

            vm.BookingsBySport = bookings
                .Where(b => b.Court != null && facilityById.ContainsKey(b.Court.FacilityId))
                .GroupBy(b => facilityById[b.Court!.FacilityId].SportType?.Name ?? "Khác")
                .Select(g => new ChartPoint { Label = g.Key, Count = g.Count(), Value = g.Where(IsRevenue).Sum(b => b.TotalAmount) })
                .OrderByDescending(p => p.Count)
                .ToList();

            vm.Facilities = facilities.Select(f =>
            {
                var fb = bookings.Where(b => b.Court != null && b.Court.FacilityId == f.Id).ToList();
                return new AdminFacilityRow
                {
                    Id = f.Id,
                    Name = f.Name,
                    Address = f.Address,
                    SportName = f.SportType?.Name ?? "",
                    OwnerName = f.Owner?.FullName ?? "(không rõ)",
                    OwnerEmail = f.Owner?.Email,
                    CourtCount = f.Courts.Count,
                    BookingCount = fb.Count,
                    Revenue = fb.Where(IsRevenue).Sum(b => b.TotalAmount),
                    ReviewCount = f.Reviews.Count,
                    AverageRating = f.Reviews.Count > 0 ? f.Reviews.Average(r => r.Rating) : 0
                };
            }).OrderByDescending(r => r.Revenue).ToList();

            vm.RecentBookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Customer)
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .OrderByDescending(b => b.CreatedAt)
                .Take(8)
                .ToListAsync();

            return View(vm);
        }
    }
}
