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
    [Authorize(Roles = AppRoles.OwnerOrAdmin)]
    public class FacilityController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBookingService _bookingService;
        private readonly IFacilityService _facilityService;
        private readonly INotificationService _notifications;

        public FacilityController(
            IUnitOfWork unitOfWork,
            IBookingService bookingService,
            IFacilityService facilityService,
            INotificationService notifications)
        {
            _unitOfWork = unitOfWork;
            _bookingService = bookingService;
            _facilityService = facilityService;
            _notifications = notifications;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        }

        private bool IsAdmin => User.IsInRole(AppRoles.Admin);

        /// <summary>Các cụm sân mà người dùng hiện tại được phép quản lý (Admin: tất cả, Chủ sân: của mình).</summary>
        private IQueryable<Facility> ManagedFacilities()
        {
            var userId = GetCurrentUserId();
            var isAdmin = IsAdmin;
            return _unitOfWork.Facilities.Query().Where(f => isAdmin || f.OwnerId == userId);
        }

        private Task<bool> CanManageFacilityAsync(int facilityId)
            => ManagedFacilities().AnyAsync(f => f.Id == facilityId);

        private Task<bool> CanManageCourtAsync(int courtId)
            => ManagedFacilities().AnyAsync(f => f.Courts.Any(c => c.Id == courtId));

        private IActionResult DenyAccess()
        {
            TempData["ErrorMessage"] = "Bạn không có quyền quản lý cụm sân / sân con này.";
            return RedirectToAction(nameof(Index));
        }

        // 1. Danh sách cụm sân của Owner
        public async Task<IActionResult> Index()
        {
            var facilities = await ManagedFacilities()
                .Include(f => f.SportType)
                .Include(f => f.Courts)
                    .ThenInclude(c => c.TimeSlots)
                .Include(f => f.Services)
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
            // Không truyền facilityId (link từ menu) => mở cụm sân đầu tiên mà người dùng quản lý
            if (facilityId <= 0)
            {
                facilityId = await ManagedFacilities().OrderBy(f => f.Id).Select(f => f.Id).FirstOrDefaultAsync();
                if (facilityId == 0)
                {
                    TempData["ErrorMessage"] = "Bạn chưa có cụm sân nào. Hãy tạo cụm sân trước khi xem bảng lịch.";
                    return RedirectToAction(nameof(CreateFacility));
                }
            }
            else if (!await CanManageFacilityAsync(facilityId))
            {
                return DenyAccess();
            }

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
                ViewBag.AllFacilities = await ManagedFacilities()
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

            var facility = await ManagedFacilities().FirstOrDefaultAsync(f => f.Id == facilityId);
            if (facility == null)
            {
                return DenyAccess();
            }

            var court = new Court
            {
                FacilityId = facilityId,
                Name = name.Trim(),
                IsActive = true
            };

            await _unitOfWork.Courts.AddAsync(court);
            await _unitOfWork.CompleteAsync();

            // Tự động thêm khung giờ mặc định cho sân con (sân mở qua đêm: 2 khung)
            foreach (var slot in TimeSlotPlanner.DefaultSlots(court.Id, facility))
            {
                await _unitOfWork.TimeSlots.AddAsync(slot);
            }
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Thêm sân con \"{name}\" thành công với khung giờ hoạt động mặc định!";
            return RedirectToAction(nameof(Index));
        }

        // 4. Bật / Tắt trạng thái hoạt động của sân con (Active / Maintenance)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCourtStatus(int courtId)
        {
            if (!await CanManageCourtAsync(courtId)) return DenyAccess();
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
            if (!await CanManageCourtAsync(courtId)) return DenyAccess();
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
        public async Task<IActionResult> AddTimeSlot(int courtId, string? startTime, string? endTime, decimal price, bool isPeakHour)
        {
            if (!await CanManageCourtAsync(courtId)) return DenyAccess();
            if (!TryParseRange(startTime, endTime, out var start, out var end))
            {
                TempData["ErrorMessage"] = "Giờ bắt đầu / kết thúc không hợp lệ.";
                return RedirectToAction(nameof(ManageTimeSlots), new { courtId });
            }
            return await AddTimeSlotCore(courtId, start, end, price, isPeakHour);
        }

        /// <summary>
        /// Đọc khoảng giờ từ ô nhập giờ của trình duyệt. Ô type="time" không nhập được 24:00,
        /// nên giờ kết thúc 00:00 (khi giờ bắt đầu khác 00:00) được hiểu là nửa đêm cuối ngày.
        /// </summary>
        private static bool TryParseRange(string? startText, string? endText, out TimeSpan start, out TimeSpan end)
        {
            end = default;
            if (!BookingRules.TryParseHm(startText, out start) || !BookingRules.TryParseHm(endText, out end)) return false;
            if (end == TimeSpan.Zero && start > TimeSpan.Zero) end = BookingRules.EndOfDay;
            return true;
        }

        private async Task<IActionResult> AddTimeSlotCore(int courtId, TimeSpan startTime, TimeSpan endTime, decimal price, bool isPeakHour)
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

            if (court.Facility != null && !BookingRules.IsWithinOpenHours(court.Facility, startTime, endTime))
            {
                TempData["ErrorMessage"] = $"Khung giờ phải nằm trong giờ hoạt động của cụm sân ({BookingRules.OpenHoursText(court.Facility)}). Sân mở qua đêm: nhập riêng khung trước và sau 24:00.";
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
            if (slot != null && !await CanManageCourtAsync(slot.CourtId)) return DenyAccess();
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
        public async Task<IActionResult> Bookings(int? facilityId, string? status, string? date, string? q, int page = 1)
        {
            const int pageSize = 20;
            var facilities = await ManagedFacilities().ToListAsync();

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
                if (status == "verify")
                {
                    query = query.Where(b => b.PaymentStatus == PaymentStatus.AwaitingVerification && b.Status != BookingStatus.Cancelled);
                }
                else if (status == "refund")
                {
                    query = query.Where(b => b.PaymentStatus == PaymentStatus.RefundPending);
                }
                else if (Enum.TryParse<BookingStatus>(status, true, out var bookingStatus))
                {
                    query = query.Where(b => b.Status == bookingStatus);
                }
                ViewBag.SelectedStatus = status;
            }

            // Số đơn cần xử lý (hiện huy hiệu trên tab lọc)
            var scope = _unitOfWork.Bookings.Query().Where(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId));
            ViewBag.VerifyCount = await scope.CountAsync(b => b.PaymentStatus == PaymentStatus.AwaitingVerification && b.Status != BookingStatus.Cancelled);
            ViewBag.RefundCount = await scope.CountAsync(b => b.PaymentStatus == PaymentStatus.RefundPending);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                int.TryParse(kw.Replace("#", "").Replace("BK-", "", StringComparison.OrdinalIgnoreCase), out var idKw);
                query = query.Where(b => b.Id == idKw
                    || (b.CustomerName != null && b.CustomerName.Contains(kw))
                    || (b.CustomerPhone != null && b.CustomerPhone.Contains(kw)));
                ViewBag.Keyword = q;
            }

            var totalCount = await query.CountAsync();
            ViewBag.TotalAmount = await query.Where(b => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled).SumAsync(b => (decimal?)b.TotalAmount) ?? 0;
            ViewBag.UnpaidCount = await query.CountAsync(b => b.PaymentStatus == PaymentStatus.Unpaid && b.Status != BookingStatus.Cancelled);

            page = Math.Max(1, page);
            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.Facilities = facilities;
            ViewBag.SelectedFacilityId = facilityId;

            return View(bookings);
        }

        // 7. Cập nhật trạng thái đơn đặt sân (Duyệt / Xác nhận thanh toán tiền mặt / Hủy)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBookingStatus(int bookingId, BookingStatus status, PaymentStatus? paymentStatus, string? returnAction = "Bookings", int? facilityId = null, string? date = null)
        {
            var booking = await _unitOfWork.Bookings.Query().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null || !await CanManageCourtAsync(booking.CourtId)) return DenyAccess();

            var result = await _bookingService.UpdateBookingStatusAsync(bookingId, status, paymentStatus);

            TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.Message;

            if (returnAction == "ScheduleBoard" && facilityId.HasValue)
            {
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId = facilityId.Value, date });
            }

            // Quay lại đúng trang danh sách đang xem (giữ bộ lọc / phân trang)
            var referer = Request.Headers.Referer.ToString();
            if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri)
                && Url.IsLocalUrl(refUri.PathAndQuery) && refUri.Host == Request.Host.Host)
            {
                return LocalRedirect(refUri.PathAndQuery);
            }

            return RedirectToAction(nameof(Bookings));
        }

        // 7b. Đặt sân nhanh tại quầy (Dành cho khách vãng lai hoặc đặt trực tiếp qua điện thoại)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickBookCourt(int facilityId, int courtId, string date, string? startTime, string? endTime, string? customerName, string? customerPhone, decimal? totalAmount, bool isPaid)
        {
            if (!DateOnly.TryParse(date, out var bookingDate))
            {
                TempData["ErrorMessage"] = "Ngày đặt sân không hợp lệ.";
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }
            if (!TryParseRange(startTime, endTime, out var start, out var end))
            {
                TempData["ErrorMessage"] = "Giờ đặt sân không hợp lệ.";
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            if (!await CanManageFacilityAsync(facilityId)) return DenyAccess();

            var court = await _unitOfWork.Courts.GetByIdAsync(courtId);
            if (court == null || court.FacilityId != facilityId)
            {
                TempData["ErrorMessage"] = "Sân con không hợp lệ.";
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            // Đơn tại quầy được ghi dưới tài khoản người đang thao tác (chủ sân / admin)
            // Dùng chung luồng tạo đơn với khách online: kiểm tra giờ hợp lệ, giờ mở cửa,
            // sân đang hoạt động và chống trùng lịch trong Serializable Transaction.
            // Để trống / 0 ở ô tổng tiền => hệ thống tự tính theo bảng giá khung giờ.
            var dto = new BookingDto
            {
                CourtId = courtId,
                CustomerId = GetCurrentUserId(),
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Khách vãng lai" : customerName.Trim(),
                CustomerPhone = customerPhone?.Trim(),
                BookingDate = bookingDate,
                StartTime = start,
                EndTime = end
            };

            var result = await _bookingService.CreateCounterBookingAsync(dto, totalAmount, isPaid);

            if (!result.IsSuccess)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
            }

            TempData["SuccessMessage"] = $"Đặt sân nhanh thành công tại {court.Name} ({BookingRules.Hm(start)} - {BookingRules.Hm(end)}), tổng tiền {result.Booking!.TotalAmount:N0} đ! Đơn #BK-{result.BookingId}";
            return RedirectToAction(nameof(ScheduleBoard), new { facilityId, date });
        }

        // 8. Quản Lý Dịch Vụ & Tiện Ích Cụm Sân (Nước uống, thuê vợt, tủ đồ...)
        [HttpGet]
        public async Task<IActionResult> ManageServices(int facilityId)
        {
            var facility = await ManagedFacilities()
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
            if (!await CanManageFacilityAsync(facilityId)) return DenyAccess();
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
            if (service != null && !await CanManageFacilityAsync(service.FacilityId)) return DenyAccess();
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

        // 8b. Thống kê doanh thu (biểu đồ theo ngày / tháng / sân con / khung giờ)
        [HttpGet]
        public async Task<IActionResult> Statistics(int? facilityId, int range = 30)
        {
            if (range is not (7 or 30 or 90 or 365)) range = 30;

            var facilities = await ManagedFacilities().OrderBy(f => f.Name).ToListAsync();
            var facilityIds = facilities.Select(f => f.Id).ToList();
            if (facilityId.HasValue && facilityId.Value > 0)
            {
                if (!facilityIds.Contains(facilityId.Value)) return DenyAccess();
                facilityIds = new List<int> { facilityId.Value };
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var from = today.AddDays(-(range - 1));
            var prevFrom = from.AddDays(-range);
            var yearFrom = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            var earliest = prevFrom < yearFrom ? prevFrom : yearFrom;

            var bookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court)
                .Include(b => b.Customer)
                .Where(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId) && b.BookingDate >= earliest && b.BookingDate <= today)
                .ToListAsync();

            static bool IsRevenue(Booking b) => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled;

            var inRange = bookings.Where(b => b.BookingDate >= from).ToList();
            var revenueInRange = inRange.Where(IsRevenue).ToList();

            var vm = new RevenueStatisticsViewModel
            {
                SelectedFacilityId = facilityId,
                RangeDays = range,
                Facilities = facilities,
                TotalRevenue = revenueInRange.Sum(b => b.TotalAmount),
                PreviousRevenue = bookings.Where(b => b.BookingDate >= prevFrom && b.BookingDate < from).Where(IsRevenue).Sum(b => b.TotalAmount),
                TotalBookings = inRange.Count,
                PaidBookings = revenueInRange.Count,
                CancelledBookings = inRange.Count(b => b.Status == BookingStatus.Cancelled),
                PendingAmount = inRange.Where(b => b.Status != BookingStatus.Cancelled && b.PaymentStatus == PaymentStatus.Unpaid).Sum(b => b.TotalAmount)
            };
            vm.AverageBookingValue = vm.PaidBookings > 0 ? vm.TotalRevenue / vm.PaidBookings : 0;
            vm.CancellationRate = vm.TotalBookings > 0 ? (double)vm.CancelledBookings / vm.TotalBookings * 100 : 0;

            // Doanh thu theo ngày (với 365 ngày thì gom theo tuần cho dễ nhìn)
            if (range <= 90)
            {
                for (var d = from; d <= today; d = d.AddDays(1))
                {
                    var day = revenueInRange.Where(b => b.BookingDate == d).ToList();
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = day.Sum(b => b.TotalAmount), Count = day.Count });
                }
            }
            else
            {
                for (var d = from; d <= today; d = d.AddDays(7))
                {
                    var end = d.AddDays(7);
                    var week = revenueInRange.Where(b => b.BookingDate >= d && b.BookingDate < end).ToList();
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = week.Sum(b => b.TotalAmount), Count = week.Count });
                }
            }

            // Doanh thu 12 tháng gần nhất
            for (int i = 0; i < 12; i++)
            {
                var m = yearFrom.AddMonths(i);
                var month = bookings.Where(b => b.BookingDate.Year == m.Year && b.BookingDate.Month == m.Month).Where(IsRevenue).ToList();
                vm.MonthlyRevenue.Add(new ChartPoint { Label = m.ToString("MM/yyyy"), Value = month.Sum(b => b.TotalAmount), Count = month.Count });
            }

            vm.RevenueByCourt = revenueInRange
                .GroupBy(b => b.Court!.Name)
                .Select(g => new ChartPoint { Label = g.Key, Value = g.Sum(b => b.TotalAmount), Count = g.Count() })
                .OrderByDescending(p => p.Value)
                .ToList();

            vm.BookingsByHour = inRange
                .Where(b => b.Status != BookingStatus.Cancelled)
                .GroupBy(b => b.StartTime.Hours)
                .OrderBy(g => g.Key)
                .Select(g => new ChartPoint { Label = $"{g.Key:00}h", Count = g.Count(), Value = g.Sum(b => b.TotalAmount) })
                .ToList();

            vm.TopCustomers = revenueInRange
                .GroupBy(b => string.IsNullOrWhiteSpace(b.CustomerPhone) ? b.CustomerId : b.CustomerPhone)
                .Select(g => new TopCustomerDto
                {
                    Name = g.First().CustomerName ?? g.First().Customer?.FullName ?? "Khách hàng",
                    Phone = g.First().CustomerPhone,
                    Bookings = g.Count(),
                    Spent = g.Sum(b => b.TotalAmount)
                })
                .OrderByDescending(c => c.Spent)
                .Take(5)
                .ToList();

            return View(vm);
        }

        // 8c. Đánh giá của khách hàng & phản hồi của chủ sân
        [HttpGet]
        public async Task<IActionResult> Reviews(int? facilityId)
        {
            var facilities = await ManagedFacilities().OrderBy(f => f.Name).ToListAsync();
            var ids = facilities.Select(f => f.Id).ToList();
            if (facilityId.HasValue && facilityId.Value > 0 && ids.Contains(facilityId.Value))
            {
                ids = new List<int> { facilityId.Value };
            }

            var reviews = await _unitOfWork.Reviews.Query()
                .Include(r => r.Customer)
                .Include(r => r.Facility)
                .Include(r => r.Booking).ThenInclude(b => b!.Court)
                .Where(r => ids.Contains(r.FacilityId))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.Facilities = facilities;
            ViewBag.SelectedFacilityId = facilityId;
            return View(reviews);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyReview(int reviewId, string? reply, int? facilityId)
        {
            var review = await _unitOfWork.Reviews.GetByIdAsync(reviewId);
            if (review == null || !await CanManageFacilityAsync(review.FacilityId)) return DenyAccess();

            review.OwnerReply = string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();
            if (review.OwnerReply?.Length > 1000) review.OwnerReply = review.OwnerReply[..1000];
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = review.OwnerReply == null ? "Đã gỡ phản hồi." : "Đã gửi phản hồi cho khách hàng.";
            return RedirectToAction(nameof(Reviews), new { facilityId });
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
            // Giờ đóng cửa nhỏ hơn giờ mở cửa = mở qua nửa đêm (vd 16:00 → 02:00); bằng nhau = mở cửa 24/24.
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
                Longitude = model.Longitude,
                // Cụm sân của chủ sân phải chờ Admin duyệt; Admin tự tạo thì duyệt luôn
                ApprovalStatus = IsAdmin ? FacilityApprovalStatus.Approved : FacilityApprovalStatus.Pending,
                ApprovedAt = IsAdmin ? DateTime.UtcNow : null
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

            // Tự động tạo khung giờ mặc định (sân mở qua đêm: 2 khung trước / sau 24:00)
            foreach (var slot in TimeSlotPlanner.DefaultSlots(court.Id, facility, model.InitialPrice > 0 ? model.InitialPrice : TimeSlotPlanner.DefaultPrice))
            {
                await _unitOfWork.TimeSlots.AddAsync(slot);
            }

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

            if (facility.ApprovalStatus == FacilityApprovalStatus.Pending)
            {
                await _notifications.NotifyAdminsAsync($"Cụm sân mới chờ duyệt: {facility.Name}",
                    $"{User.FindFirstValue("FullName") ?? User.Identity?.Name} vừa đăng ký cụm sân tại {facility.Address}.",
                    "/Admin/Facilities?status=pending", "bi-building-add", "blue");
                TempData["SuccessMessage"] = $"Đã tạo cụm sân \"{facility.Name}\". Cụm sân sẽ hiển thị cho khách sau khi Admin duyệt.";
            }
            else
            {
                TempData["SuccessMessage"] = $"Tạo cụm sân \"{facility.Name}\" thành công kèm sân con và bảng giá ban đầu!";
            }
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

            if (!IsAdmin && facility.OwnerId != GetCurrentUserId())
            {
                return DenyAccess();
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
            // Đổi giờ hoạt động: không được làm "mồ côi" các đơn sắp tới nằm ngoài giờ mới
            if (ModelState.IsValid)
            {
                var newHours = new Facility { OpeningTime = model.OpeningTime, ClosingTime = model.ClosingTime };
                var today = DateOnly.FromDateTime(DateTime.Today);
                var holdThreshold = BookingRules.HoldThreshold(DateTime.UtcNow);
                var upcoming = await _unitOfWork.Bookings.Query()
                    .Where(BookingRules.IsActive(holdThreshold))
                    .Where(b => b.Court != null && b.Court.FacilityId == model.Id && b.BookingDate >= today)
                    .Select(b => new { b.Id, b.BookingDate, b.StartTime, b.EndTime })
                    .ToListAsync();
                var outside = upcoming.Where(b => !BookingRules.IsWithinOpenHours(newHours, b.StartTime, b.EndTime)).ToList();
                if (outside.Any())
                {
                    ModelState.AddModelError(nameof(model.ClosingTime),
                        $"Có {outside.Count} đơn sắp tới nằm ngoài giờ hoạt động mới (vd #BK-{outside[0].Id} ngày {outside[0].BookingDate:dd/MM} {BookingRules.Hm(outside[0].StartTime)}–{BookingRules.Hm(outside[0].EndTime)}). Hãy xử lý các đơn đó trước.");
                }
            }

            if (!ModelState.IsValid)
            {
                var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
                var cities = await _unitOfWork.CityProvinces.GetAllAsync();
                ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", model.SportTypeId);
                ViewBag.Cities = new SelectList(cities, "Id", "Name", model.CityId);
                return View(model);
            }

            var facility = await ManagedFacilities().FirstOrDefaultAsync(f => f.Id == model.Id);
            if (facility == null)
            {
                return DenyAccess();
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

            // Cụm sân bị từ chối: chỉnh sửa xong sẽ được gửi duyệt lại
            bool resubmitted = facility.ApprovalStatus == FacilityApprovalStatus.Rejected && !IsAdmin;
            if (resubmitted)
            {
                facility.ApprovalStatus = FacilityApprovalStatus.Pending;
                facility.ApprovalNote = null;
            }

            // Bảng giá các sân con tự khớp theo giờ hoạt động mới
            int trimmed = 0, added = 0;
            var courts = await _unitOfWork.Courts.Query().Include(c => c.TimeSlots).Where(c => c.FacilityId == facility.Id).ToListAsync();
            foreach (var court in courts)
            {
                var toRemove = new List<TimeSlot>();
                var toAdd = new List<TimeSlot>();
                var (t, a) = TimeSlotPlanner.Normalize(court.Id, court.TimeSlots, facility, toRemove, toAdd);
                trimmed += t; added += a;
                foreach (var s in toRemove) _unitOfWork.TimeSlots.Remove(s);
                foreach (var s in toAdd) await _unitOfWork.TimeSlots.AddAsync(s);
            }

            _unitOfWork.Facilities.Update(facility);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Cập nhật cụm sân \"{facility.Name}\" thành công!"
                + (trimmed + added > 0 ? $" Bảng giá đã tự điều chỉnh theo giờ mới ({trimmed} khung được cắt bớt, {added} khung được thêm — kiểm tra lại giá trong mục Khung giờ & giá)." : "");
            if (resubmitted)
            {
                await _notifications.NotifyAdminsAsync($"Cụm sân gửi duyệt lại: {facility.Name}",
                    "Chủ sân đã chỉnh sửa cụm sân bị từ chối trước đó.", "/Admin/Facilities?status=pending", "bi-arrow-repeat", "blue");
                TempData["SuccessMessage"] += " Cụm sân đã được gửi lại cho Admin duyệt.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 11. Xóa Cụm Sân (Delete Facility)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFacility(int id)
        {
            var facility = await ManagedFacilities()
                .Include(f => f.Courts)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (facility == null)
            {
                return DenyAccess();
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

        // 12. Xác nhận / từ chối biên lai chuyển khoản của khách
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPayment(int bookingId, bool approve, string? reason)
        {
            var booking = await _unitOfWork.Bookings.Query().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null || !await CanManageCourtAsync(booking.CourtId)) return DenyAccess();

            var result = await _bookingService.VerifyPaymentAsync(bookingId, approve, reason);
            TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return BackToReferer(nameof(Bookings));
        }

        // 13. Danh sách đơn bị hủy cần hoàn tiền / đã hoàn tiền
        [HttpGet]
        public async Task<IActionResult> Refunds(int? facilityId, bool done = false)
        {
            var facilities = await ManagedFacilities().ToListAsync();
            var ids = facilities.Select(f => f.Id).ToList();
            if (facilityId.HasValue && facilityId > 0 && ids.Contains(facilityId.Value)) ids = new List<int> { facilityId.Value };

            var targetStatus = done ? PaymentStatus.Refunded : PaymentStatus.RefundPending;
            var bookings = await _unitOfWork.Bookings.Query()
                .Include(b => b.Customer)
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.Court != null && ids.Contains(b.Court.FacilityId) && b.PaymentStatus == targetStatus)
                .OrderByDescending(b => done ? b.RefundedAt : b.CancelledAt)
                .Take(200)
                .ToListAsync();

            ViewBag.Facilities = facilities;
            ViewBag.SelectedFacilityId = facilityId;
            ViewBag.Done = done;
            ViewBag.PendingTotal = await _unitOfWork.Bookings.Query()
                .Where(b => b.Court != null && ids.Contains(b.Court.FacilityId) && b.PaymentStatus == PaymentStatus.RefundPending)
                .SumAsync(b => (decimal?)(b.RefundAmount ?? b.TotalAmount)) ?? 0;
            return View(bookings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRefunded(int bookingId, string? note)
        {
            var booking = await _unitOfWork.Bookings.Query().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null || !await CanManageCourtAsync(booking.CourtId)) return DenyAccess();

            var result = await _bookingService.MarkRefundedAsync(bookingId, note);
            TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return BackToReferer(nameof(Refunds));
        }

        // 14. Cài đặt thông báo Telegram cho cụm sân (bảng NotificationConfigs)
        [HttpGet]
        public async Task<IActionResult> NotificationSettings(int? facilityId)
        {
            var facilities = await ManagedFacilities().Include(f => f.NotificationConfig).OrderBy(f => f.Id).ToListAsync();
            if (!facilities.Any())
            {
                TempData["ErrorMessage"] = "Bạn chưa có cụm sân nào.";
                return RedirectToAction(nameof(CreateFacility));
            }
            var facility = facilities.FirstOrDefault(f => f.Id == facilityId) ?? facilities.First();
            ViewBag.Facilities = facilities;
            ViewBag.Facility = facility;
            return View(facility.NotificationConfig ?? new NotificationConfig { FacilityId = facility.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotificationSettings(int facilityId, string? telegramBotToken, string? telegramChatId, bool notifyOnNewBooking, string? submit)
        {
            if (!await CanManageFacilityAsync(facilityId)) return DenyAccess();

            var cfg = await _unitOfWork.Context.NotificationConfigs.FirstOrDefaultAsync(c => c.FacilityId == facilityId);
            if (cfg == null)
            {
                cfg = new NotificationConfig { FacilityId = facilityId };
                _unitOfWork.Context.NotificationConfigs.Add(cfg);
            }
            cfg.TelegramBotToken = string.IsNullOrWhiteSpace(telegramBotToken) ? null : telegramBotToken.Trim();
            cfg.TelegramChatId = string.IsNullOrWhiteSpace(telegramChatId) ? null : telegramChatId.Trim();
            cfg.NotifyOnNewBooking = notifyOnNewBooking;
            await _unitOfWork.CompleteAsync();

            if (submit == "test")
            {
                if (cfg.TelegramBotToken == null || cfg.TelegramChatId == null)
                {
                    TempData["ErrorMessage"] = "Nhập Bot Token và Chat ID trước khi gửi thử.";
                }
                else
                {
                    var error = await _notifications.SendTelegramAsync(cfg.TelegramBotToken, cfg.TelegramChatId,
                        "✅ SanBong.vn: kết nối Telegram thành công! Bạn sẽ nhận thông báo đơn đặt sân tại đây.");
                    TempData[error == null ? "SuccessMessage" : "ErrorMessage"] = error ?? "Đã gửi tin nhắn thử tới Telegram.";
                }
            }
            else
            {
                TempData["SuccessMessage"] = "Đã lưu cài đặt thông báo.";
            }
            return RedirectToAction(nameof(NotificationSettings), new { facilityId });
        }

        /// <summary>Quay lại trang trước (giữ bộ lọc) nếu là link nội bộ, không thì về action mặc định.</summary>
        private IActionResult BackToReferer(string fallbackAction)
        {
            var referer = Request.Headers.Referer.ToString();
            if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri)
                && Url.IsLocalUrl(refUri.PathAndQuery) && refUri.Host == Request.Host.Host)
            {
                return LocalRedirect(refUri.PathAndQuery);
            }
            return RedirectToAction(fallbackAction);
        }
    }
}
