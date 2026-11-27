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
<<<<<<< HEAD
            ViewBag.TodayRevenue = todayBookings.Sum(BookingRules.RevenueOf);
=======
            ViewBag.TodayRevenue = todayBookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.TotalAmount);
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70

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

<<<<<<< HEAD
            var query = FilterBookings(facilityIds, facilityId, status, date, q);
            if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out _)) ViewBag.SelectedDate = date;
            if (!string.IsNullOrEmpty(status)) ViewBag.SelectedStatus = status;
            if (!string.IsNullOrWhiteSpace(q)) ViewBag.Keyword = q;
=======
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
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70

            // Số đơn cần xử lý (hiện huy hiệu trên tab lọc)
            var scope = _unitOfWork.Bookings.Query().Where(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId));
            ViewBag.VerifyCount = await scope.CountAsync(b => b.PaymentStatus == PaymentStatus.AwaitingVerification && b.Status != BookingStatus.Cancelled);
            ViewBag.RefundCount = await scope.CountAsync(b => b.PaymentStatus == PaymentStatus.RefundPending);

<<<<<<< HEAD
            var totalCount = await query.CountAsync();
            // Doanh thu thực: đơn đã trả còn hiệu lực + phần tiền giữ lại của đơn hủy chỉ hoàn một phần (khớp BookingRules.RevenueOf)
            ViewBag.TotalAmount = await query.SumAsync(b => (decimal?)(
                b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled ? b.TotalAmount
                : (b.PaymentStatus == PaymentStatus.RefundPending || b.PaymentStatus == PaymentStatus.Refunded) && b.RefundAmount != null && b.TotalAmount > b.RefundAmount ? b.TotalAmount - b.RefundAmount.Value
                : 0m)) ?? 0;
=======
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
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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

<<<<<<< HEAD
        /// <summary>Lọc đơn đặt sân theo cụm sân, trạng thái, ngày và từ khóa (dùng chung cho danh sách và xuất file).</summary>
        private IQueryable<Booking> FilterBookings(List<int> facilityIds, int? facilityId, string? status, string? date, string? q)
        {
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
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                int.TryParse(kw.Replace("#", "").Replace("BK-", "", StringComparison.OrdinalIgnoreCase), out var idKw);
                var codeKw = TicketCode.Normalize(kw) ?? "";
                query = query.Where(b => b.Id == idKw
                    || (b.CustomerName != null && b.CustomerName.Contains(kw))
                    || (b.CustomerPhone != null && b.CustomerPhone.Contains(kw))
                    || (codeKw != "" && b.CheckInCode == codeKw));
            }

            return query;
        }

        // 6b. Xuất danh sách đơn đặt sân (theo bộ lọc hiện tại) ra file CSV mở được bằng Excel
        [HttpGet]
        public async Task<IActionResult> ExportBookings(int? facilityId, string? status, string? date, string? q)
        {
            var facilityIds = await ManagedFacilities().Select(f => f.Id).ToListAsync();
            var bookings = await FilterBookings(facilityIds, facilityId, status, date, q)
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .Take(10000)
                .ToListAsync();

            static string Csv(string? v)
            {
                v ??= string.Empty;
                // Chặn công thức Excel (CSV injection) và escape dấu nháy
                if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }
            static string StatusText(BookingStatus s) => s switch
            {
                BookingStatus.Pending => "Chờ xác nhận",
                BookingStatus.Confirmed => "Đã xác nhận",
                BookingStatus.Completed => "Đã hoàn thành",
                BookingStatus.Cancelled => "Đã hủy",
                _ => s.ToString()
            };
            static string PayText(PaymentStatus s) => s switch
            {
                PaymentStatus.Unpaid => "Chưa thanh toán",
                PaymentStatus.Paid => "Đã thanh toán",
                PaymentStatus.AwaitingVerification => "Chờ kiểm tra biên lai",
                PaymentStatus.RefundPending => "Chờ hoàn tiền",
                PaymentStatus.Refunded => "Đã hoàn tiền",
                _ => s.ToString()
            };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Mã đơn,Cụm sân,Sân,Ngày chơi,Giờ bắt đầu,Giờ kết thúc,Khách hàng,Số điện thoại,Email,Tổng tiền (VNĐ),Trạng thái,Thanh toán,Phương thức,Ngày tạo");
            foreach (var b in bookings)
            {
                sb.AppendJoin(',', new[]
                {
                    Csv($"BK-{b.Id}"),
                    Csv(b.Court?.Facility?.Name),
                    Csv(b.Court?.Name),
                    Csv(b.BookingDate.ToString("dd/MM/yyyy")),
                    Csv(BookingRules.Hm(b.StartTime)),
                    Csv(BookingRules.Hm(b.EndTime)),
                    Csv(b.CustomerName ?? b.Customer?.FullName),
                    Csv(b.CustomerPhone ?? b.Customer?.PhoneNumber),
                    Csv(b.Customer?.Email),
                    b.TotalAmount.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    Csv(StatusText(b.Status)),
                    Csv(PayText(b.PaymentStatus)),
                    Csv(b.PaymentMethod),
                    Csv(b.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                });
                sb.AppendLine();
            }

            // BOM UTF-8 để Excel hiển thị đúng tiếng Việt
            var bytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"don-dat-san-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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

<<<<<<< HEAD
        // ===== Soát vé tại sân: quét QR hoặc nhập mã vé =====
        [HttpGet]
        public async Task<IActionResult> CheckIn(string? code)
        {
            var facilityIds = await ManagedFacilities().Select(f => f.Id).ToListAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);

            ViewBag.Code = code;
            if (!string.IsNullOrWhiteSpace(code))
            {
                var normalized = TicketCode.Normalize(code);
                Booking? booking = null;
                if (normalized != null)
                {
                    booking = await _unitOfWork.Bookings.Query()
                        .Include(b => b.Court).ThenInclude(c => c!.Facility)
                        .Include(b => b.Customer)
                        .Include(b => b.ServiceDetails).ThenInclude(d => d.FacilityService)
                        .FirstOrDefaultAsync(b => b.CheckInCode == normalized
                                                  && b.Court != null && facilityIds.Contains(b.Court.FacilityId));
                }
                ViewBag.Booking = booking;
                ViewBag.NotFound = booking == null;
                ViewBag.BadFormat = normalized == null;
            }

            ViewBag.TodayCheckIns = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId)
                            && b.BookingDate == today && b.CheckedInAt != null)
                .OrderByDescending(b => b.CheckedInAt)
                .Take(30)
                .ToListAsync();
            ViewBag.TodayExpected = await _unitOfWork.Bookings.Query()
                .CountAsync(b => b.Court != null && facilityIds.Contains(b.Court.FacilityId)
                                 && b.BookingDate == today && b.Status != BookingStatus.Cancelled);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmCheckIn(int bookingId)
        {
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null || booking.Court == null || !await CanManageFacilityAsync(booking.Court.FacilityId)) return DenyAccess();

            var pretty = TicketCode.Pretty(booking.CheckInCode);
            if (booking.Status == BookingStatus.Cancelled)
            {
                TempData["ErrorMessage"] = $"Vé #BK-{booking.Id} đã bị hủy, không thể check-in.";
            }
            else if (booking.CheckedInAt != null)
            {
                TempData["ErrorMessage"] = $"Vé #BK-{booking.Id} đã check-in lúc {booking.CheckedInAt.Value.ToLocalTime():HH:mm dd/MM}.";
            }
            else
            {
                booking.CheckedInAt = DateTime.UtcNow;
                booking.CheckedInBy = GetCurrentUserId();
                try
                {
                    await _unitOfWork.CompleteAsync();
                    TempData["SuccessMessage"] = $"Đã check-in vé #BK-{booking.Id} · {booking.CustomerName ?? "Khách"} · {booking.Court.Name} {BookingRules.Hm(booking.StartTime)}–{BookingRules.Hm(booking.EndTime)}.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    TempData["ErrorMessage"] = "Vé vừa được cập nhật ở nơi khác. Vui lòng quét lại.";
                }
            }
            return RedirectToAction(nameof(CheckIn), new { code = booking.CheckInCode });
        }

        // ===== Ảnh cụm sân (chủ sân tải ảnh thật lên) =====
        private const int MaxPhotosPerFacility = 12;
        private const long MaxPhotoBytes = 5 * 1024 * 1024;

        /// <summary>Nhận diện định dạng ảnh theo chữ ký file (không tin phần mở rộng / Content-Type do trình duyệt gửi).</summary>
        private static string? DetectImageType(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> ManagePhotos(int facilityId)
        {
            var facility = await ManagedFacilities().Include(f => f.SportType).FirstOrDefaultAsync(f => f.Id == facilityId);
            if (facility == null) return DenyAccess();

            ViewBag.Photos = await _unitOfWork.Context.FacilityImages
                .Where(i => i.FacilityId == facilityId)
                .OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => new FacilityPhotoItem(i.Id, i.IsCover, i.OriginalFileName, i.Data.Length))
                .ToListAsync();
            ViewBag.MaxPhotos = MaxPhotosPerFacility;
            return View(facility);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(80 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 80 * 1024 * 1024)]
        public async Task<IActionResult> UploadPhotos(int facilityId, List<IFormFile>? files)
        {
            if (!await CanManageFacilityAsync(facilityId)) return DenyAccess();
            var db = _unitOfWork.Context;

            files = (files ?? new()).Where(f => f != null && f.Length > 0).ToList();
            if (files.Count == 0)
            {
                TempData["ErrorMessage"] = "Vui lòng chọn ít nhất 1 ảnh.";
                return RedirectToAction(nameof(ManagePhotos), new { facilityId });
            }

            var existing = await db.FacilityImages.Where(i => i.FacilityId == facilityId)
                .Select(i => new { i.SortOrder, i.IsCover }).ToListAsync();
            int slots = MaxPhotosPerFacility - existing.Count;
            if (slots <= 0)
            {
                TempData["ErrorMessage"] = $"Mỗi cụm sân tối đa {MaxPhotosPerFacility} ảnh. Hãy xóa bớt ảnh cũ trước.";
                return RedirectToAction(nameof(ManagePhotos), new { facilityId });
            }

            int nextOrder = existing.Count == 0 ? 0 : existing.Max(e => e.SortOrder) + 1;
            bool hasCover = existing.Any(e => e.IsCover);
            int added = 0;
            var skipped = new List<string>();

            foreach (var file in files)
            {
                if (added >= slots) { skipped.Add($"{file.FileName} (vượt quá {MaxPhotosPerFacility} ảnh)"); continue; }
                if (file.Length > MaxPhotoBytes) { skipped.Add($"{file.FileName} (lớn hơn 5MB)"); continue; }

                using var ms = new System.IO.MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var type = DetectImageType(bytes);
                if (type == null) { skipped.Add($"{file.FileName} (không phải ảnh JPG/PNG/WebP)"); continue; }

                db.FacilityImages.Add(new FacilityImage
                {
                    FacilityId = facilityId,
                    Data = bytes,
                    ContentType = type,
                    OriginalFileName = System.IO.Path.GetFileName(file.FileName).Length > 200
                        ? System.IO.Path.GetFileName(file.FileName)[..200] : System.IO.Path.GetFileName(file.FileName),
                    SortOrder = nextOrder++,
                    IsCover = !hasCover && added == 0
                });
                added++;
            }
            await db.SaveChangesAsync();

            if (added > 0) TempData["SuccessMessage"] = $"Đã tải lên {added} ảnh." + (skipped.Count > 0 ? $" Bỏ qua {skipped.Count} file." : "");
            if (skipped.Count > 0) TempData["ErrorMessage"] = "Không tải được: " + string.Join("; ", skipped.Take(5)) + (skipped.Count > 5 ? "…" : "");
            return RedirectToAction(nameof(ManagePhotos), new { facilityId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCoverPhoto(int photoId)
        {
            var db = _unitOfWork.Context;
            var photo = await db.FacilityImages.Where(i => i.Id == photoId).Select(i => new { i.Id, i.FacilityId }).FirstOrDefaultAsync();
            if (photo == null || !await CanManageFacilityAsync(photo.FacilityId)) return DenyAccess();

            await db.FacilityImages.Where(i => i.FacilityId == photo.FacilityId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsCover, i => i.Id == photo.Id));
            TempData["SuccessMessage"] = "Đã đặt làm ảnh bìa.";
            return RedirectToAction(nameof(ManagePhotos), new { facilityId = photo.FacilityId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MovePhoto(int photoId, int direction)
        {
            var db = _unitOfWork.Context;
            var photo = await db.FacilityImages.Where(i => i.Id == photoId).Select(i => new { i.Id, i.FacilityId }).FirstOrDefaultAsync();
            if (photo == null || !await CanManageFacilityAsync(photo.FacilityId)) return DenyAccess();

            // Đánh lại thứ tự liên tục rồi đổi chỗ với ảnh bên cạnh (không đọc dữ liệu ảnh)
            var order = await db.FacilityImages.Where(i => i.FacilityId == photo.FacilityId)
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Id).ToListAsync();
            int idx = order.IndexOf(photoId), target = idx + (direction < 0 ? -1 : 1);
            if (idx >= 0 && target >= 0 && target < order.Count)
            {
                (order[idx], order[target]) = (order[target], order[idx]);
                for (int k = 0; k < order.Count; k++)
                {
                    int id = order[k], pos = k;
                    await db.FacilityImages.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.SortOrder, pos));
                }
            }
            return RedirectToAction(nameof(ManagePhotos), new { facilityId = photo.FacilityId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePhoto(int photoId)
        {
            var db = _unitOfWork.Context;
            var photo = await db.FacilityImages.Where(i => i.Id == photoId).Select(i => new { i.Id, i.FacilityId, i.IsCover }).FirstOrDefaultAsync();
            if (photo == null || !await CanManageFacilityAsync(photo.FacilityId)) return DenyAccess();

            await db.FacilityImages.Where(i => i.Id == photo.Id).ExecuteDeleteAsync();
            if (photo.IsCover)
            {
                // Ảnh bìa bị xóa → ảnh đầu tiên còn lại thành ảnh bìa
                var next = await db.FacilityImages.Where(i => i.FacilityId == photo.FacilityId)
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync();
                if (next.HasValue)
                    await db.FacilityImages.Where(i => i.Id == next.Value).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsCover, true));
            }
            TempData["SuccessMessage"] = "Đã xóa ảnh.";
            return RedirectToAction(nameof(ManagePhotos), new { facilityId = photo.FacilityId });
        }

=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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

<<<<<<< HEAD
            static bool IsRevenue(Booking b) => BookingRules.RevenueOf(b) > 0;
=======
            static bool IsRevenue(Booking b) => b.PaymentStatus == PaymentStatus.Paid && b.Status != BookingStatus.Cancelled;
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70

            var inRange = bookings.Where(b => b.BookingDate >= from).ToList();
            var revenueInRange = inRange.Where(IsRevenue).ToList();

            var vm = new RevenueStatisticsViewModel
            {
                SelectedFacilityId = facilityId,
                RangeDays = range,
                Facilities = facilities,
<<<<<<< HEAD
                TotalRevenue = revenueInRange.Sum(BookingRules.RevenueOf),
                PreviousRevenue = bookings.Where(b => b.BookingDate >= prevFrom && b.BookingDate < from).Where(IsRevenue).Sum(BookingRules.RevenueOf),
=======
                TotalRevenue = revenueInRange.Sum(b => b.TotalAmount),
                PreviousRevenue = bookings.Where(b => b.BookingDate >= prevFrom && b.BookingDate < from).Where(IsRevenue).Sum(b => b.TotalAmount),
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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
<<<<<<< HEAD
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = day.Sum(BookingRules.RevenueOf), Count = day.Count });
=======
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = day.Sum(b => b.TotalAmount), Count = day.Count });
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                }
            }
            else
            {
                for (var d = from; d <= today; d = d.AddDays(7))
                {
                    var end = d.AddDays(7);
                    var week = revenueInRange.Where(b => b.BookingDate >= d && b.BookingDate < end).ToList();
<<<<<<< HEAD
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = week.Sum(BookingRules.RevenueOf), Count = week.Count });
=======
                    vm.DailyRevenue.Add(new ChartPoint { Label = d.ToString("dd/MM"), Value = week.Sum(b => b.TotalAmount), Count = week.Count });
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
                }
            }

            // Doanh thu 12 tháng gần nhất
            for (int i = 0; i < 12; i++)
            {
                var m = yearFrom.AddMonths(i);
                var month = bookings.Where(b => b.BookingDate.Year == m.Year && b.BookingDate.Month == m.Month).Where(IsRevenue).ToList();
<<<<<<< HEAD
                vm.MonthlyRevenue.Add(new ChartPoint { Label = m.ToString("MM/yyyy"), Value = month.Sum(BookingRules.RevenueOf), Count = month.Count });
=======
                vm.MonthlyRevenue.Add(new ChartPoint { Label = m.ToString("MM/yyyy"), Value = month.Sum(b => b.TotalAmount), Count = month.Count });
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            }

            vm.RevenueByCourt = revenueInRange
                .GroupBy(b => b.Court!.Name)
<<<<<<< HEAD
                .Select(g => new ChartPoint { Label = g.Key, Value = g.Sum(BookingRules.RevenueOf), Count = g.Count() })
=======
                .Select(g => new ChartPoint { Label = g.Key, Value = g.Sum(b => b.TotalAmount), Count = g.Count() })
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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
<<<<<<< HEAD
                    Spent = g.Sum(BookingRules.RevenueOf)
=======
                    Spent = g.Sum(b => b.TotalAmount)
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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
<<<<<<< HEAD
                        "✅ DatSan247: kết nối Telegram thành công! Bạn sẽ nhận thông báo đơn đặt sân tại đây.");
=======
                        "✅ SanBong.vn: kết nối Telegram thành công! Bạn sẽ nhận thông báo đơn đặt sân tại đây.");
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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
