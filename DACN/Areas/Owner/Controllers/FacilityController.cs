using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.DTOs;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DACN.Areas.Owner.Controllers
{
    [Area("Owner")]
    // Uncomment for production role authorization: [Authorize(Roles = "FacilityOwner,Admin")]
    public class FacilityController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBookingService _bookingService;
        private readonly IFacilityService _facilityService;

        public FacilityController(
            IUnitOfWork unitOfWork,
            IBookingService bookingService,
            IFacilityService facilityService)
        {
            _unitOfWork = unitOfWork;
            _bookingService = bookingService;
            _facilityService = facilityService;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "demo-owner-id";
        }

        // 1. Danh sách cụm sân của Owner
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            var facilities = await _unitOfWork.Facilities.Query()
                .Include(f => f.SportType)
                .Include(f => f.Courts)
                    .ThenInclude(c => c.TimeSlots)
                .Include(f => f.Services)
                .Where(f => f.OwnerId == userId || f.OwnerId == "demo-owner-id")
                .ToListAsync();

            var facilityIds = facilities.Select(f => f.Id).ToList();
            var courtIds = facilities.SelectMany(f => f.Courts).Select(c => c.Id).ToList();

            var today = DateOnly.FromDateTime(DateTime.Today);
            var todayBookings = await _unitOfWork.Bookings.Query()
                .Where(b => courtIds.Contains(b.CourtId) && b.BookingDate == today)
                .ToListAsync();

            ViewBag.TotalFacilities = facilities.Count;
            ViewBag.TotalCourts = courtIds.Count;
            ViewBag.TodayBookingsCount = todayBookings.Count(b => b.Status != BookingStatus.Cancelled);
            ViewBag.TodayRevenue = todayBookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.TotalAmount);

            return View(facilities);
        }

        // 2. Timeline Schedule Board - Ma trận lịch đặt sân theo thời gian thực của toàn bộ sân con
        public async Task<IActionResult> ScheduleBoard(int facilityId, string? date)
        {
            DateOnly selectedDate;
            if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsedDate))
            {
                selectedDate = parsedDate;
            }
            else
            {
                selectedDate = DateOnly.FromDateTime(DateTime.Today);
            }

            try
            {
                var scheduleModel = await _bookingService.GetFacilityTimelineScheduleAsync(facilityId, selectedDate);
                ViewBag.AllFacilities = await _unitOfWork.Facilities.Query()
                    .Select(f => new Facility { Id = f.Id, Name = f.Name, Address = f.Address })
                    .ToListAsync();
                ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
                ViewBag.PrevDate = selectedDate.AddDays(-1).ToString("yyyy-MM-dd");
                ViewBag.NextDate = selectedDate.AddDays(1).ToString("yyyy-MM-dd");
                ViewBag.TodayDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
                return View(scheduleModel);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // 3. Thêm Sân Con Mới vào Cụm Sân
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCourt(int facilityId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Tên sân không được để trống.";
                return RedirectToAction(nameof(Index));
            }

            var facility = await _unitOfWork.Facilities.GetByIdAsync(facilityId);
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            var court = new Court
            {
                FacilityId = facilityId,
                Name = name.Trim(),
                IsActive = true
            };

            await _unitOfWork.Courts.AddAsync(court);
            await _unitOfWork.CompleteAsync();

            // Tự động thêm khung giờ mặc định cho sân con
            var defaultSlot = new TimeSlot
            {
                CourtId = court.Id,
                StartTime = facility.OpeningTime,
                EndTime = facility.ClosingTime,
                Price = 180000m,
                IsPeakHour = false
            };

            await _unitOfWork.TimeSlots.AddAsync(defaultSlot);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Thêm sân con \"{name}\" thành công với khung giờ hoạt động mặc định!";
            return RedirectToAction(nameof(Index));
        }

        // 4. Bật / Tắt trạng thái hoạt động của sân con (Active / Maintenance)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCourtStatus(int courtId)
        {
            var court = await _unitOfWork.Courts.GetByIdAsync(courtId);
            if (court == null)
            {
                TempData["ErrorMessage"] = "Sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            court.IsActive = !court.IsActive;
            _unitOfWork.Courts.Update(court);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Đã chuyển trạng thái sân \"{court.Name}\" sang: [{(court.IsActive ? "Đang hoạt động" : "Tạm ngưng / Bảo trì")}].";
            return RedirectToAction(nameof(Index));
        }

        // 5. Quản lý khung giờ & Bảng giá của Sân con (TimeSlots)
        public async Task<IActionResult> ManageTimeSlots(int courtId)
        {
            var court = await _unitOfWork.Courts.Query()
                .Include(c => c.Facility)
                .Include(c => c.TimeSlots)
                .FirstOrDefaultAsync(c => c.Id == courtId);

            if (court == null)
            {
                return NotFound("Không tìm thấy sân yêu cầu.");
            }

            return View(court);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTimeSlot(int courtId, TimeSpan startTime, TimeSpan endTime, decimal price, bool isPeakHour)
        {
            if (startTime >= endTime)
            {
                TempData["ErrorMessage"] = "Giờ bắt đầu phải nhỏ hơn giờ kết thúc.";
                return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
            }

            if (price < 0)
            {
                TempData["ErrorMessage"] = "Đơn giá không được âm.";
                return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
            }

            var court = await _unitOfWork.Courts.Query()
                .Include(c => c.Facility)
                .Include(c => c.TimeSlots)
                .FirstOrDefaultAsync(c => c.Id == courtId);

            if (court == null)
            {
                TempData["ErrorMessage"] = "Sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            if (court.Facility != null && (startTime < court.Facility.OpeningTime || endTime > court.Facility.ClosingTime))
            {
                TempData["ErrorMessage"] = $"Khung giờ phải nằm trong giờ hoạt động của cụm sân ({court.Facility.OpeningTime:hh\\:mm} - {court.Facility.ClosingTime:hh\\:mm}).";
                return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
            }

            // Khung giờ mới được ưu tiên: các khung cũ bị chồng lấn sẽ được cắt bớt / tách đôi / xóa,
            // để mỗi thời điểm chỉ thuộc đúng 1 khung giá (tránh tính tiền 2 lần).
            int adjustedCount = 0;
            foreach (var old in court.TimeSlots.Where(s => s.StartTime < endTime && s.EndTime > startTime).ToList())
            {
                adjustedCount++;
                bool keepsLeft = old.StartTime < startTime;
                bool keepsRight = old.EndTime > endTime;

                if (keepsLeft && keepsRight)
                {
                    // Khung cũ bao trùm khung mới => tách làm 2 phần trái / phải
                    await _unitOfWork.TimeSlots.AddAsync(new TimeSlot
                    {
                        CourtId = courtId,
                        StartTime = endTime,
                        EndTime = old.EndTime,
                        Price = old.Price,
                        IsPeakHour = old.IsPeakHour
                    });
                    old.EndTime = startTime;
                }
                else if (keepsLeft)
                {
                    old.EndTime = startTime;
                }
                else if (keepsRight)
                {
                    old.StartTime = endTime;
                }
                else
                {
                    // Khung cũ nằm trọn trong khung mới => bỏ
                    _unitOfWork.TimeSlots.Remove(old);
                }
            }

            await _unitOfWork.TimeSlots.AddAsync(new TimeSlot
            {
                CourtId = courtId,
                StartTime = startTime,
                EndTime = endTime,
                Price = price,
                IsPeakHour = isPeakHour
            });
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = adjustedCount > 0
                ? $"Thêm khung giờ và bảng giá thành công! Đã tự động điều chỉnh {adjustedCount} khung giờ cũ bị chồng lấn."
                : "Thêm khung giờ và bảng giá thành công!";
            return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTimeSlot(int slotId, int courtId)
        {
            var slot = await _unitOfWork.TimeSlots.GetByIdAsync(slotId);
            if (slot != null)
            {
                _unitOfWork.TimeSlots.Remove(slot);
                await _unitOfWork.CompleteAsync();
                TempData["SuccessMessage"] = "Đã xóa khung giờ thành công!";
            }

            return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
        }

        // 6. Quản Lý Đơn Đặt Sân của Cơ Sở (Bookings Manager)
        [HttpGet]
        public async Task<IActionResult> Bookings(int? facilityId, string? status, string? date)
        {
            var userId = GetCurrentUserId();
            var facilities = await _unitOfWork.Facilities.Query()
                .Where(f => f.OwnerId == userId || f.OwnerId == "demo-owner-id")
                .ToListAsync();

            var facilityIds = facilities.Select(f => f.Id).ToList();

            var query = _unitOfWork.Bookings.Query()
                .Include(b => b.Customer)
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Facility)
                .Where(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId))
                .AsQueryable();

            if (facilityId.HasValue && facilityId.Value > 0)
            {
                query = query.Where(b => b.Court != null && b.Court.FacilityId == facilityId.Value);
            }

            if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsedDate))
            {
                query = query.Where(b => b.BookingDate == parsedDate);
                ViewBag.SelectedDate = date;
            }

            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<BookingStatus>(status, true, out var bookingStatus))
                {
                    query = query.Where(b => b.Status == bookingStatus);
                }
                ViewBag.SelectedStatus = status;
            }

            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            ViewBag.Facilities = facilities;
            ViewBag.SelectedFacilityId = facilityId;

            return View(bookings);
        }

        // 7. Cập nhật trạng thái đơn đặt sân (Duyệt / Xác nhận thanh toán tiền mặt / Hủy)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBookingStatus(int bookingId, BookingStatus status, PaymentStatus? paymentStatus, string? returnAction = "Bookings", int? facilityId = null, string? date = null)
        {
            var result = await _bookingService.UpdateBookingStatusAsync(bookingId, status, paymentStatus);

            TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.Message;

            if (returnAction == "ScheduleBoard" && facilityId.HasValue)
            {
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId = facilityId.Value, date });
            }

            return RedirectToAction(returnAction ?? nameof(Bookings));
        }

        // 7b. Đặt sân nhanh tại quầy (Dành cho khách vãng lai hoặc đặt trực tiếp qua điện thoại)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickBookCourt(int facilityId, int courtId, string date, TimeSpan startTime, TimeSpan endTime, string? customerName, string? customerPhone, decimal? totalAmount, bool isPaid)
        {
            if (!DateOnly.TryParse(date, out var bookingDate))
            {
                TempData["ErrorMessage"] = "Ngày đặt sân không hợp lệ.";
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            var court = await _unitOfWork.Courts.GetByIdAsync(courtId);
            if (court == null || court.FacilityId != facilityId)
            {
                TempData["ErrorMessage"] = "Sân con không hợp lệ.";
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            var ownerUser = await _unitOfWork.Context.Users.FirstOrDefaultAsync(u => u.Id == GetCurrentUserId())
                            ?? await _unitOfWork.Context.Users.FirstOrDefaultAsync();

            // Dùng chung luồng tạo đơn với khách online: kiểm tra giờ hợp lệ, giờ mở cửa,
            // sân đang hoạt động và chống trùng lịch trong Serializable Transaction.
            // Để trống / 0 ở ô tổng tiền => hệ thống tự tính theo bảng giá khung giờ.
            var dto = new BookingDto
            {
                CourtId = courtId,
                CustomerId = ownerUser?.Id ?? "demo-customer-id",
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Khách vãng lai" : customerName.Trim(),
                CustomerPhone = customerPhone?.Trim(),
                BookingDate = bookingDate,
                StartTime = startTime,
                EndTime = endTime
            };

            var result = await _bookingService.CreateCounterBookingAsync(dto, totalAmount, isPaid);

            if (!result.IsSuccess)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            TempData["SuccessMessage"] = $"Đặt sân nhanh thành công tại {court.Name} ({startTime:hh\\:mm} - {endTime:hh\\:mm}), tổng tiền {result.Booking!.TotalAmount:N0} đ! Đơn #BK-{result.BookingId}";
            return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
        }

        // 8. Quản Lý Dịch Vụ & Tiện Ích Cụm Sân (Nước uống, thuê vợt, tủ đồ...)
        [HttpGet]
        public async Task<IActionResult> ManageServices(int facilityId)
        {
            var facility = await _unitOfWork.Facilities.Query()
                .Include(f => f.Services)
                .FirstOrDefaultAsync(f => f.Id == facilityId);

            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            return View(facility);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddService(int facilityId, string name, decimal price, string unit)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Tên dịch vụ không được để trống.";
                return RedirectToAction(nameof(ManageServices), new { facilityId });
            }

            var service = new DACN.Models.FacilityService
            {
                FacilityId = facilityId,
                Name = name.Trim(),
                Price = price,
                Unit = string.IsNullOrWhiteSpace(unit) ? "Lần" : unit.Trim(),
                IsActive = true
            };

            await _unitOfWork.FacilityServices.AddAsync(service);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Thêm dịch vụ \"{name}\" thành công!";
            return RedirectToAction(nameof(ManageServices), new { facilityId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteService(int serviceId, int facilityId)
        {
            var service = await _unitOfWork.FacilityServices.GetByIdAsync(serviceId);
            if (service != null)
            {
                // Dịch vụ đã từng được đặt kèm trong đơn thì không thể xóa (khóa ngoại Restrict, cần giữ lịch sử hóa đơn)
                // => chuyển sang "Tạm ngưng" để khách không chọn được nữa.
                bool usedInBookings = await _unitOfWork.BookingServiceDetails.AnyAsync(d => d.FacilityServiceId == serviceId);
                if (usedInBookings)
                {
                    service.IsActive = false;
                    await _unitOfWork.CompleteAsync();
                    TempData["SuccessMessage"] = $"Dịch vụ \"{service.Name}\" đã có trong lịch sử đơn đặt sân nên được chuyển sang Tạm ngưng thay vì xóa.";
                }
                else
                {
                    _unitOfWork.FacilityServices.Remove(service);
                    await _unitOfWork.CompleteAsync();
                    TempData["SuccessMessage"] = "Đã xóa dịch vụ thành công!";
                }
            }

            return RedirectToAction(nameof(ManageServices), new { facilityId });
        }

        // 9. Thêm Cụm Sân Mới (Create Facility)
        [HttpGet]
        public async Task<IActionResult> CreateFacility()
        {
            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            var cities = await _unitOfWork.CityProvinces.GetAllAsync();

            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name");
            ViewBag.Cities = new SelectList(cities, "Id", "Name");

            return View(new CreateFacilityViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFacility(CreateFacilityViewModel model)
        {
            if (model.OpeningTime >= model.ClosingTime)
            {
                ModelState.AddModelError(nameof(model.ClosingTime), "Giờ đóng cửa phải sau giờ mở cửa.");
            }

            if (!ModelState.IsValid)
            {
                var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
                var cities = await _unitOfWork.CityProvinces.GetAllAsync();
                ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", model.SportTypeId);
                ViewBag.Cities = new SelectList(cities, "Id", "Name", model.CityId);
                return View(model);
            }

            var userId = GetCurrentUserId();

            var facility = new Facility
            {
                OwnerId = userId,
                Name = model.Name.Trim(),
                SportTypeId = model.SportTypeId,
                CityId = model.CityId,
                DistrictName = model.DistrictName?.Trim(),
                Address = model.Address.Trim(),
                OpeningTime = model.OpeningTime,
                ClosingTime = model.ClosingTime,
                Description = model.Description?.Trim(),
                Latitude = model.Latitude,
                Longitude = model.Longitude
            };

            await _unitOfWork.Facilities.AddAsync(facility);
            await _unitOfWork.CompleteAsync();

            // Tự động tạo 1 sân con ban đầu cho cụm sân
            var court = new Court
            {
                FacilityId = facility.Id,
                Name = string.IsNullOrWhiteSpace(model.InitialCourtName) ? "Sân 1" : model.InitialCourtName.Trim(),
                IsActive = true
            };

            await _unitOfWork.Courts.AddAsync(court);
            await _unitOfWork.CompleteAsync();

            // Tự động tạo khung giờ mặc định
            var defaultSlot = new TimeSlot
            {
                CourtId = court.Id,
                StartTime = model.OpeningTime,
                EndTime = model.ClosingTime,
                Price = model.InitialPrice > 0 ? model.InitialPrice : 180000m,
                IsPeakHour = false
            };

            await _unitOfWork.TimeSlots.AddAsync(defaultSlot);

            // Thêm các dịch vụ cơ bản
            var defaultWater = new DACN.Models.FacilityService
            {
                FacilityId = facility.Id,
                Name = "Nước suối đóng chai",
                Price = 10000m,
                Unit = "Chai",
                IsActive = true
            };
            var defaultBibs = new DACN.Models.FacilityService
            {
                FacilityId = facility.Id,
                Name = "Bộ áo pitch phân đội",
                Price = 30000m,
                Unit = "Bộ/Trận",
                IsActive = true
            };
            await _unitOfWork.FacilityServices.AddAsync(defaultWater);
            await _unitOfWork.FacilityServices.AddAsync(defaultBibs);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Tạo cụm sân \"{facility.Name}\" thành công kèm sân con và bảng giá ban đầu!";
            return RedirectToAction(nameof(Index));
        }

        // 10. Chỉnh sửa thông tin Cụm Sân (Edit Facility)
        [HttpGet]
        public async Task<IActionResult> EditFacility(int id)
        {
            var facility = await _unitOfWork.Facilities.GetByIdAsync(id);
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            var userId = GetCurrentUserId();
            if (facility.OwnerId != userId && facility.OwnerId != "demo-owner-id")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền chỉnh sửa cụm sân này.";
                return RedirectToAction(nameof(Index));
            }

            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            var cities = await _unitOfWork.CityProvinces.GetAllAsync();

            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", facility.SportTypeId);
            ViewBag.Cities = new SelectList(cities, "Id", "Name", facility.CityId);

            var model = new EditFacilityViewModel
            {
                Id = facility.Id,
                Name = facility.Name,
                SportTypeId = facility.SportTypeId,
                CityId = facility.CityId,
                DistrictName = facility.DistrictName,
                Address = facility.Address,
                OpeningTime = facility.OpeningTime,
                ClosingTime = facility.ClosingTime,
                Description = facility.Description,
                Latitude = facility.Latitude,
                Longitude = facility.Longitude
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFacility(EditFacilityViewModel model)
        {
            if (model.OpeningTime >= model.ClosingTime)
            {
                ModelState.AddModelError(nameof(model.ClosingTime), "Giờ đóng cửa phải sau giờ mở cửa.");
            }

            if (!ModelState.IsValid)
            {
                var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
                var cities = await _unitOfWork.CityProvinces.GetAllAsync();
                ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", model.SportTypeId);
                ViewBag.Cities = new SelectList(cities, "Id", "Name", model.CityId);
                return View(model);
            }

            var facility = await _unitOfWork.Facilities.GetByIdAsync(model.Id);
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            facility.Name = model.Name.Trim();
            facility.SportTypeId = model.SportTypeId;
            facility.CityId = model.CityId;
            facility.DistrictName = model.DistrictName?.Trim();
            facility.Address = model.Address.Trim();
            facility.OpeningTime = model.OpeningTime;
            facility.ClosingTime = model.ClosingTime;
            facility.Description = model.Description?.Trim();
            facility.Latitude = model.Latitude;
            facility.Longitude = model.Longitude;

            _unitOfWork.Facilities.Update(facility);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Cập nhật cụm sân \"{facility.Name}\" thành công!";
            return RedirectToAction(nameof(Index));
        }

        // 11. Xóa Cụm Sân (Delete Facility)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFacility(int id)
        {
            var facility = await _unitOfWork.Facilities.Query()
                .Include(f => f.Courts)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (facility == null)
            {
                TempData["ErrorMessage"] = "Cụm sân không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var holdThreshold = BookingRules.HoldThreshold(DateTime.UtcNow);
            var facilityBookings = _unitOfWork.Bookings.Query()
                .Where(b => b.Court != null && b.Court.FacilityId == id);

            var upcomingBookings = await facilityBookings
                .Where(BookingRules.IsActive(holdThreshold))
                .CountAsync(b => b.BookingDate >= today);

            if (upcomingBookings > 0)
            {
                TempData["ErrorMessage"] = $"Không thể xóa cụm sân này vì đang có {upcomingBookings} lịch đặt sân sắp diễn ra.";
                return RedirectToAction(nameof(Index));
            }

            // Đơn đặt sân (kể cả đơn cũ / đã hủy) tham chiếu tới sân con bằng khóa ngoại Restrict
            // và là lịch sử doanh thu => không xóa được cụm sân, hướng dẫn chủ sân tạm ngưng các sân con.
            if (await facilityBookings.AnyAsync())
            {
                TempData["ErrorMessage"] = "Cụm sân đã có lịch sử đặt sân nên không thể xóa (cần giữ lại hóa đơn, doanh thu). Hãy chuyển các sân con sang trạng thái Tạm ngưng.";
                return RedirectToAction(nameof(Index));
            }

            _unitOfWork.Facilities.Remove(facility);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Đã xóa cụm sân \"{facility.Name}\" thành công!";
            return RedirectToAction(nameof(Index));
        }
    }
}
