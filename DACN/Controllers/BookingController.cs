using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models.DTOs;
using DACN.Repositories;
using DACN.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DACN.Controllers
{
    public class BookingController : Controller
    {
        private readonly IBookingService _bookingService;
        private readonly IFacilityService _facilityService;
        private readonly IUnitOfWork _unitOfWork;

        public BookingController(
            IBookingService bookingService,
            IFacilityService facilityService,
            IUnitOfWork unitOfWork)
        {
            _bookingService = bookingService;
            _facilityService = facilityService;
            _unitOfWork = unitOfWork;
        }

        // 1. Tìm kiếm sân theo môn thể thao, từ khóa địa điểm, tỉnh thành và sắp xếp
        public async Task<IActionResult> Index(int? sportTypeId, string? keyword, int? cityId, string? sortBy)
        {
            var facilities = await _facilityService.SearchFacilitiesAsync(sportTypeId, keyword, cityId, sortBy);
            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            var cities = await _unitOfWork.CityProvinces.GetAllAsync();

            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", sportTypeId);
            ViewBag.Cities = new SelectList(cities, "Id", "Name", cityId);
            ViewBag.Keyword = keyword;
            ViewBag.SelectedSportTypeId = sportTypeId;
            ViewBag.SelectedCityId = cityId;
            ViewBag.SelectedSortBy = sortBy;

            return View(facilities);
        }

        // 2. Xem ma trận lịch trống thời gian thực của 1 sân trong ngày
        [HttpGet]
        public async Task<IActionResult> Matrix(int courtId, string? date)
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
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    var currentUser = await _unitOfWork.Context.Users.FindAsync(currentUserId);
                    ViewBag.CurrentUserFullName = currentUser?.FullName;
                    ViewBag.CurrentUserPhone = currentUser?.PhoneNumber;
                }

                var matrixViewModel = await _bookingService.GetCourtScheduleMatrixAsync(courtId, selectedDate);
                return View(matrixViewModel);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // 3. Đặt sân - Xử lý Race Condition / Concurrency
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingDto dto, string? bookingDate, string? startTime, string? endTime)
        {
            // Fallback parse nếu model binder không nhận chuỗi trực tiếp
            if (dto.BookingDate == default && !string.IsNullOrEmpty(bookingDate) && DateOnly.TryParse(bookingDate, out var pDate))
            {
                dto.BookingDate = pDate;
            }
            if (dto.StartTime == default && !string.IsNullOrEmpty(startTime) && TimeSpan.TryParse(startTime, out var pStart))
            {
                dto.StartTime = pStart;
            }
            if (dto.EndTime == default && !string.IsNullOrEmpty(endTime) && TimeSpan.TryParse(endTime, out var pEnd))
            {
                dto.EndTime = pEnd;
            }

            // Đảm bảo CustomerId luôn trỏ tới một ApplicationUser có thật trong CSDL
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                var defaultUser = await _unitOfWork.Context.Users.FirstOrDefaultAsync(u => u.Id == "demo-customer-id")
                    ?? await _unitOfWork.Context.Users.FirstOrDefaultAsync(u => u.Role == DACN.Models.Enums.UserRole.Customer)
                    ?? await _unitOfWork.Context.Users.FirstOrDefaultAsync();

                dto.CustomerId = defaultUser?.Id ?? "demo-customer-id";
            }
            else
            {
                dto.CustomerId = currentUserId;
            }

            // Gán hoặc chuẩn hóa Họ tên & SĐT người đặt sân
            if (string.IsNullOrWhiteSpace(dto.CustomerName))
            {
                var userObj = await _unitOfWork.Context.Users.FindAsync(dto.CustomerId);
                dto.CustomerName = userObj?.FullName ?? "Khách đặt sân";
            }
            if (string.IsNullOrWhiteSpace(dto.CustomerPhone))
            {
                var userObj = await _unitOfWork.Context.Users.FindAsync(dto.CustomerId);
                dto.CustomerPhone = userObj?.PhoneNumber ?? "Chưa cung cấp";
            }

            // Kiểm tra các thông tin đặt sân bắt buộc
            if (dto.CourtId <= 0)
            {
                TempData["ErrorMessage"] = "Vui lòng chọn sân thi đấu hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            if (dto.BookingDate == default)
            {
                dto.BookingDate = DateOnly.FromDateTime(DateTime.Today);
            }

            if (dto.StartTime == default || dto.EndTime == default || dto.StartTime >= dto.EndTime)
            {
                TempData["ErrorMessage"] = "Vui lòng nhấp chọn một khung giờ trống trước khi bấm Giữ Chỗ.";
                return RedirectToAction(nameof(Matrix), new { courtId = dto.CourtId, date = dto.BookingDate.ToString("yyyy-MM-dd") });
            }

            var result = await _bookingService.CreateBookingAsync(dto);

            if (!result.IsSuccess)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Matrix), new { courtId = dto.CourtId, date = dto.BookingDate.ToString("yyyy-MM-dd") });
            }

            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(Checkout), new { bookingId = result.BookingId });
        }

        // 4. Màn hình thanh toán đơn đặt sân
        [HttpGet]
        public async Task<IActionResult> Checkout(int bookingId)
        {
            var booking = await _bookingService.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                return NotFound("Đơn đặt sân không tồn tại.");
            }

            // Kiểm tra thời hạn giữ chỗ: Tối đa 5 phút
            var elapsed = DateTime.UtcNow - booking.CreatedAt;
            if (booking.PaymentStatus != DACN.Models.Enums.PaymentStatus.Paid && elapsed.TotalMinutes >= 5)
            {
                booking.Status = DACN.Models.Enums.BookingStatus.Cancelled;
                booking.PaymentNote = (booking.PaymentNote ?? "") + " [Đã quá hạn 5 phút giữ chỗ]";
                _unitOfWork.Bookings.Update(booking);
                await _unitOfWork.CompleteAsync();

                TempData["ErrorMessage"] = $"Đơn giữ chỗ #BK-{bookingId} đã quá thời hạn 5 phút. Khung giờ này đã được mở lại cho người khác.";
                return RedirectToAction(nameof(Matrix), new { courtId = booking.CourtId, date = booking.BookingDate.ToString("yyyy-MM-dd") });
            }

            return View(booking);
        }

        // 5. Xử lý thanh toán thông thường (Chỉ chấp nhận chuyển khoản điện tử, không nhận tiền mặt)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int bookingId, string paymentMethod)
        {
            if (string.Equals(paymentMethod, "CashOnCourt", StringComparison.OrdinalIgnoreCase) || paymentMethod?.ToLower().Contains("cash") == true)
            {
                TempData["ErrorMessage"] = "Hệ thống chỉ chấp nhận thanh toán chuyển khoản qua mã VietQR Napas 24/7 để đảm bảo giữ chỗ chắc chắn và chống bùng sân.";
                return RedirectToAction(nameof(Checkout), new { bookingId });
            }

            var success = await _bookingService.ProcessPaymentAsync(bookingId, paymentMethod ?? "VietQR");

            if (success)
            {
                TempData["SuccessMessage"] = "Thanh toán thành công! Sân đã được xác nhận giữ chỗ cho bạn.";
                return RedirectToAction(nameof(Confirmation), new { bookingId });
            }

            TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại.";
            return RedirectToAction(nameof(Checkout), new { bookingId });
        }

        // 5a. Hỗ trợ GET cho SubmitVirtualTransfer (chống lỗi 405 Method Not Allowed khi F5 hoặc gõ URL trực tiếp)
        [HttpGet]
        public IActionResult SubmitVirtualTransfer(int? bookingId)
        {
            if (bookingId.HasValue && bookingId.Value > 0)
            {
                return RedirectToAction(nameof(Checkout), new { bookingId = bookingId.Value });
            }
            return RedirectToAction(nameof(MyBookings));
        }

        // 5b. Xử lý chuyển khoản Ngân Hàng Ảo kèm tải ảnh minh chứng chuyển khoản (Proof of Payment)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitVirtualTransfer(
            int bookingId,
            IFormFile? proofImage,
            string? senderAccount,
            string? transactionRef,
            string? note,
            string? sampleProofBill)
        {
            try
            {
                var booking = await _bookingService.GetBookingByIdAsync(bookingId);
                if (booking == null)
                {
                    TempData["ErrorMessage"] = "Đơn đặt sân không tồn tại.";
                    return RedirectToAction(nameof(Index));
                }

                // Kiểm tra thời hạn 5 phút giữ chỗ
                var elapsed = DateTime.UtcNow - booking.CreatedAt;
                if (booking.PaymentStatus != DACN.Models.Enums.PaymentStatus.Paid && elapsed.TotalMinutes >= 5)
                {
                    booking.Status = DACN.Models.Enums.BookingStatus.Cancelled;
                    booking.PaymentNote = (booking.PaymentNote ?? "") + " [Đã quá hạn 5 phút giữ chỗ]";
                    _unitOfWork.Bookings.Update(booking);
                    await _unitOfWork.CompleteAsync();

                    TempData["ErrorMessage"] = "Đơn giữ chỗ đã quá thời hạn 5 phút và đã bị hủy. Khung giờ này đã được mở lại cho người chơi khác.";
                    return RedirectToAction(nameof(Matrix), new { courtId = booking.CourtId, date = booking.BookingDate.ToString("yyyy-MM-dd") });
                }

                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsDir = Path.Combine(webRoot, "uploads", "payment_proofs");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                // Kiểm tra xem khách đã gửi ảnh minh chứng chưa
                if ((proofImage == null || proofImage.Length == 0) && string.IsNullOrEmpty(sampleProofBill))
                {
                    TempData["ErrorMessage"] = "Vui lòng đính kèm ảnh chụp màn hình biên lai chuyển khoản thành công trước khi xác nhận.";
                    return RedirectToAction(nameof(Checkout), new { bookingId });
                }

                string? savedProofPath = null;

                // 1. Nếu khách tải file ảnh minh chứng thật từ điện thoại / máy tính
                if (proofImage != null && proofImage.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
                    var ext = Path.GetExtension(proofImage.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(ext))
                    {
                        TempData["ErrorMessage"] = "Định dạng file ảnh không hợp lệ (chỉ chấp nhận JPG, PNG, WEBP).";
                        return RedirectToAction(nameof(Checkout), new { bookingId });
                    }

                    var fileName = $"proof_bk{bookingId}_{DateTime.UtcNow.Ticks}{ext}";
                    var filePath = Path.Combine(uploadsDir, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await proofImage.CopyToAsync(stream);
                    }

                    savedProofPath = $"/uploads/payment_proofs/{fileName}";
                }
                // 2. Dự phòng hỗ trợ dữ liệu ảnh Base64 / SVG từ API hoặc kiểm thử
                else if (!string.IsNullOrEmpty(sampleProofBill) && sampleProofBill.StartsWith("data:image/svg+xml"))
                {
                    var fileName = $"bill_bk{bookingId}_{DateTime.UtcNow.Ticks}.svg";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    var svgContent = Uri.UnescapeDataString(sampleProofBill.Replace("data:image/svg+xml;charset=utf-8,", ""));
                    await System.IO.File.WriteAllTextAsync(filePath, svgContent);
                    savedProofPath = $"/uploads/payment_proofs/{fileName}";
                }
                else if (!string.IsNullOrEmpty(sampleProofBill) && sampleProofBill.StartsWith("data:image/"))
                {
                    var fileName = $"bill_bk{bookingId}_{DateTime.UtcNow.Ticks}.png";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    var parts = sampleProofBill.Split(',');
                    var base64 = parts.Length > 1 ? parts[1] : parts[0];
                    var bytes = Convert.FromBase64String(base64);
                    await System.IO.File.WriteAllBytesAsync(filePath, bytes);
                    savedProofPath = $"/uploads/payment_proofs/{fileName}";
                }

                var txnRef = string.IsNullOrEmpty(transactionRef)
                    ? $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}"
                    : transactionRef;

                var fullNote = !string.IsNullOrEmpty(senderAccount)
                    ? $"Người chuyển: {senderAccount}. {note}"
                    : (string.IsNullOrEmpty(note) ? "Chuyển khoản VietQR" : note);

                var success = await _bookingService.SubmitBankTransferProofAsync(
                    bookingId,
                    savedProofPath,
                    txnRef,
                    "VietQR (VietinBank 24/7)",
                    fullNote);

                if (success)
                {
                    TempData["SuccessMessage"] = "Thanh toán chuyển khoản thành công! Sân đã được xác nhận giữ chỗ cho bạn.";
                    return RedirectToAction(nameof(Confirmation), new { bookingId });
                }

                TempData["ErrorMessage"] = "Không thể ghi nhận giao dịch. Vui lòng thử lại.";
                return RedirectToAction(nameof(Checkout), new { bookingId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi xử lý chuyển khoản: {ex.Message}";
                return RedirectToAction(nameof(Checkout), new { bookingId });
            }
        }

        // 5c. API chuyển tiền tức thì từ Ứng dụng Ngân Hàng Ảo (VietBank Mobile Sandbox 1-Click)
        [HttpPost]
        public async Task<IActionResult> ApiSimulateBankTransfer(
            [FromForm] int bookingId,
            [FromForm] string? senderName,
            [FromForm] string? senderBank,
            [FromForm] string? generatedBillImage)
        {
            try
            {
                var booking = await _bookingService.GetBookingByIdAsync(bookingId);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Đơn đặt sân không tồn tại." });
                }

                // Kiểm tra thời hạn 5 phút giữ chỗ
                var elapsed = DateTime.UtcNow - booking.CreatedAt;
                if (booking.PaymentStatus != DACN.Models.Enums.PaymentStatus.Paid && elapsed.TotalMinutes >= 5)
                {
                    booking.Status = DACN.Models.Enums.BookingStatus.Cancelled;
                    booking.PaymentNote = (booking.PaymentNote ?? "") + " [Đã quá hạn 5 phút giữ chỗ]";
                    _unitOfWork.Bookings.Update(booking);
                    await _unitOfWork.CompleteAsync();

                    return Json(new { success = false, message = "Đơn giữ chỗ đã quá hạn 5 phút. Khung giờ này đã được mở lại cho người khác." });
                }

                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsDir = Path.Combine(webRoot, "uploads", "payment_proofs");
                if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

                string? savedBillPath = null;
                if (!string.IsNullOrEmpty(generatedBillImage) && generatedBillImage.StartsWith("data:image/svg+xml"))
                {
                    var fileName = $"sim_bk{bookingId}_{DateTime.UtcNow.Ticks}.svg";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    var svgContent = Uri.UnescapeDataString(generatedBillImage.Replace("data:image/svg+xml;charset=utf-8,", ""));
                    await System.IO.File.WriteAllTextAsync(filePath, svgContent);
                    savedBillPath = $"/uploads/payment_proofs/{fileName}";
                }

                var txnRef = $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}";
                var note = $"Giao dịch qua VietBank Sandbox | Người gửi: {senderName ?? "Khách hàng"} ({senderBank ?? "VietBank Digital"})";

                var success = await _bookingService.SubmitBankTransferProofAsync(
                    bookingId,
                    savedBillPath,
                    txnRef,
                    "VietBank_Sandbox",
                    note);

                if (success)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Tiền đã vào tài khoản SANBONG VN thành công! Đã xác nhận giữ chỗ.",
                        transactionRef = txnRef,
                        redirectUrl = Url.Action(nameof(Confirmation), new { bookingId })
                    });
                }

                return Json(new { success = false, message = "Giao dịch không thành công." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi xử lý: {ex.Message}" });
            }
        }

        // 5d. Cổng Sao Kê / Quản Lý Ngân Hàng Ảo (Chỉ dành riêng cho Quản Trị Viên Admin)
        [HttpGet]
        public IActionResult VirtualBankLedger()
        {
            return RedirectToAction("Index", "Bank", new { area = "Admin" });
        }

        // 6. Xác nhận đặt sân thành công
        [HttpGet]
        public async Task<IActionResult> Confirmation(int bookingId)
        {
            var booking = await _bookingService.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        // 7. Lịch sử đặt sân & Quản lý vé sân của khách hàng
        [HttpGet]
        public async Task<IActionResult> MyBookings(string? filter = "all")
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "demo-customer-id";
            var allBookings = await _bookingService.GetCustomerBookingsAsync(userId);

            var today = DateOnly.FromDateTime(DateTime.Today);

            var filteredBookings = filter?.ToLower() switch
            {
                "upcoming" => allBookings.Where(b => b.BookingDate >= today && b.Status != Models.Enums.BookingStatus.Cancelled).ToList(),
                "completed" => allBookings.Where(b => (b.BookingDate < today || b.Status == Models.Enums.BookingStatus.Completed) && b.Status != Models.Enums.BookingStatus.Cancelled).ToList(),
                "unpaid" => allBookings.Where(b => b.PaymentStatus == Models.Enums.PaymentStatus.Unpaid && b.Status != Models.Enums.BookingStatus.Cancelled).ToList(),
                "cancelled" => allBookings.Where(b => b.Status == Models.Enums.BookingStatus.Cancelled).ToList(),
                _ => allBookings
            };

            ViewBag.ActiveFilter = filter?.ToLower() ?? "all";
            ViewBag.TotalCount = allBookings.Count;
            ViewBag.UpcomingCount = allBookings.Count(b => b.BookingDate >= today && b.Status != Models.Enums.BookingStatus.Cancelled);
            ViewBag.CompletedCount = allBookings.Count(b => (b.BookingDate < today || b.Status == Models.Enums.BookingStatus.Completed) && b.Status != Models.Enums.BookingStatus.Cancelled);
            ViewBag.UnpaidCount = allBookings.Count(b => b.PaymentStatus == Models.Enums.PaymentStatus.Unpaid && b.Status != Models.Enums.BookingStatus.Cancelled);
            ViewBag.CancelledCount = allBookings.Count(b => b.Status == Models.Enums.BookingStatus.Cancelled);

            return View(filteredBookings);
        }

        // 8. Hủy đơn đặt sân
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int bookingId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "demo-customer-id";
            var success = await _bookingService.CancelBookingAsync(bookingId, userId);

            if (success)
            {
                TempData["SuccessMessage"] = $"Đã hủy đơn đặt sân #BK-{bookingId} thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy đơn đặt sân này hoặc bạn không có quyền thực hiện.";
            }

            return RedirectToAction(nameof(MyBookings));
        }

        // 9. Chi tiết cụm sân thể thao (Giới thiệu, sân con, bảng giá, dịch vụ tiện ích)
        [HttpGet]
        public async Task<IActionResult> FacilityDetail(int id)
        {
            var facility = await _facilityService.GetFacilityDetailAsync(id);
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy cụm sân được yêu cầu.";
                return RedirectToAction(nameof(Index));
            }

            return View(facility);
        }
    }
}
