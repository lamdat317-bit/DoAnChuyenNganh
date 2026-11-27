using System;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = AppRoles.Admin)]
    public class BankController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        public BankController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        // Cổng Quản Trị & Sổ Cái Ngân Hàng Ảo (Dành riêng cho Quản Trị Viên Admin)
        public async Task<IActionResult> Index()
        {
            var paidBookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Customer)
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Facility)
                .Where(b => b.PaymentStatus == PaymentStatus.Paid || !string.IsNullOrEmpty(b.PaymentTransactionRef))
                .OrderByDescending(b => b.PaidAt ?? b.CreatedAt)
                .Take(100)
                .ToListAsync();

            return View(paidBookings);
        }
    }
}

