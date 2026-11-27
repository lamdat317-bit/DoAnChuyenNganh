using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models.DTOs;
using DACN.Models.Enums;
using DACN.Repositories;
using DACN.Models;
using DACN.Models.ViewModels;
using DACN.Services;
using Microsoft.AspNetCore.Authorization;
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
        private readonly INotificationService _notifications;

        public BookingController(
            IBookingService bookingService,
            IFacilityService facilityService,
            IUnitOfWork unitOfWork,
            INotificationService notifications)
        {
            _bookingService = bookingService;
            _facilityService = facilityService;
            _unitOfWork = unitOfWork;
            _notifications = notifications;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>Cụm sân chưa được duyệt chỉ chủ sân đó và Admin xem được.</summary>
        private bool CanViewFacility(Facility? facility)
            => facility != null && (facility.ApprovalStatus == FacilityApprovalStatus.Approved
                || User.IsInRole(AppRoles.Admin)
                || facility.OwnerId == CurrentUserId);

        /// <summary>
        /// Chỉ khách đặt đơn, chủ cụm sân chứa đơn đó, hoặc Admin mới được xem / thanh toán đơn.
        /// </summary>
        private async Task<bool> CanAccessBookingAsync(int bookingId)
        {
            if (User.IsInRole(AppRoles.Admin)) return true;
            var userId = CurrentUserId;
            return await _unitOfWork.Bookings.Query().AnyAsync(b => b.Id == bookingId
                && (b.CustomerId == userId || (b.Court != null && b.Court.Facility != null && b.Court.Facility.OwnerId == userId)));
        }

        // ===== Ảnh biên lai chuyển khoản: lưu trong SQL (bảng BookingPaymentProofs) =====
        private const long MaxProofBytes = 5 * 1024 * 1024;

        /// <summary>Ghi (hoặc thay) ảnh biên lai của đơn vào SQL, trả về URL để hiển thị ảnh.</summary>
        private async Task<string> SaveProofToDbAsync(int bookingId, byte[] data, string contentType, string? fileName)
        {
            var proof = await _unitOfWork.PaymentProofs.FirstOrDefaultAsync(p => p.BookingId == bookingId);
            if (proof == null)
            {
                proof = new BookingPaymentProof { BookingId = bookingId };
                await _unitOfWork.PaymentProofs.AddAsync(proof);
            }
            proof.Data = data;
            proof.ContentType = contentType;
            proof.OriginalFileName = fileName;
            proof.CreatedAt = DateTime.UtcNow;
            await _unitOfWork.CompleteAsync();
            return Url.Action(nameof(ProofImage), new { bookingId, v = DateTime.Now.Ticks })!;
        }

        /// <summary>Giải mã "data:image/...;base64,..." hoặc "data:image/svg+xml;charset=utf-8,..." thành byte[].</summary>
        private static bool TryDecodeDataUri(string? dataUri, out byte[] bytes, out string contentType)
        {
            bytes = Array.Empty<byte>();
            contentType = "image/png";
            if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:image/")) return false;
            var comma = dataUri.IndexOf(',');
            if (comma < 0) return false;
            var header = dataUri.Substring(5, comma - 5); // vd: image/png;base64
            var payload = dataUri[(comma + 1)..];
            contentType = header.Split(';')[0];
            try
            {
                bytes = header.Contains(";base64")
                    ? Convert.FromBase64String(payload)
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }
            catch (FormatException) { return false; }
            return bytes.Length > 0 && bytes.Length <= MaxProofBytes;
        }

        // Trả ảnh biên lai từ SQL — chỉ người đặt, chủ sân hoặc Admin xem được
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ProofImage(int bookingId)
        {
            if (!await CanAccessBookingAsync(bookingId)) return Forbid();
            var proof = await _unitOfWork.PaymentProofs.FirstOrDefaultAsync(p => p.BookingId == bookingId);
            if (proof == null) return NotFound();
            Response.Headers.CacheControl = "private, max-age=86400";
            // Ảnh SVG do người dùng gửi: chặn script nếu ai đó mở trực tiếp link ảnh
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(proof.Data, proof.ContentType);
        }

        // 1. Tìm kiếm sân theo môn thể thao, từ khóa địa điểm, tỉnh thành và sắp xếp
<<<<<<< HEAD
        public async Task<IActionResult> Index(int? sportTypeId, string? keyword, int? cityId, string? sortBy,
            decimal? minPrice, decimal? maxPrice, int page = 1, string? sport = null)
        {
            const int pageSize = 12;

            // Cho phép lọc theo tên môn (vd ?sport=tennis) — dùng cho link ở footer / chia sẻ
            if (!sportTypeId.HasValue && !string.IsNullOrWhiteSpace(sport))
            {
                var s = sport.Trim().ToLower();
                var match = (await _unitOfWork.SportTypes.GetAllAsync())
                    .FirstOrDefault(t => t.Name.ToLower().Contains(s));
                sportTypeId = match?.Id;
            }
            var facilities = await _facilityService.SearchFacilitiesAsync(sportTypeId, keyword, cityId, sortBy);

            // Lọc theo khoảng giá (theo giá thấp nhất của cụm sân)
            if (minPrice.HasValue && minPrice.Value > 0) facilities = facilities.Where(f => f.MinPrice >= minPrice.Value).ToList();
            if (maxPrice.HasValue && maxPrice.Value > 0) facilities = facilities.Where(f => f.MinPrice <= maxPrice.Value).ToList();

            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            var cities = await _unitOfWork.CityProvinces.GetAllAsync();

            var totalCount = facilities.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", sportTypeId);
            ViewBag.SportTypeList = sportTypes.ToList();
=======
        public async Task<IActionResult> Index(int? sportTypeId, string? keyword, int? cityId, string? sortBy)
        {
            var facilities = await _facilityService.SearchFacilitiesAsync(sportTypeId, keyword, cityId, sortBy);
            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            var cities = await _unitOfWork.CityProvinces.GetAllAsync();

            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", sportTypeId);
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
            ViewBag.Cities = new SelectList(cities, "Id", "Name", cityId);
            ViewBag.Keyword = keyword;
            ViewBag.SelectedSportTypeId = sportTypeId;
            ViewBag.SelectedCityId = cityId;
            ViewBag.SelectedSortBy = sortBy;
<<<<<<< HEAD
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            // Dữ liệu cho chế độ xem bản đồ: toàn bộ kết quả lọc có tọa độ GPS
            ViewBag.MapData = facilities
                .Where(f => f.Latitude.HasValue && f.Longitude.HasValue && f.Latitude.Value != 0)
                .Select(f => new
                {
                    id = f.Id,
                    name = f.Name,
                    addr = f.Address,
                    sport = f.SportTypeName,
                    price = f.MinPrice,
                    rating = f.ReviewCount > 0 ? f.AverageRating : (double?)null,
                    lat = f.Latitude!.Value,
                    lng = f.Longitude!.Value,
                    courtId = f.FirstCourtId
                })
                .ToList();

            return View(facilities.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        // ===== Sân yêu thích =====

        /// <summary>Danh sách Id cụm sân mà người dùng đã lưu (để tô trái tim trên thẻ sân).</summary>
        [HttpGet]
        public async Task<IActionResult> FavoriteIds()
        {
            if (User.Identity?.IsAuthenticated != true) return Json(Array.Empty<int>());
            var uid = CurrentUserId;
            var ids = await _unitOfWork.Context.FavoriteFacilities
                .Where(f => f.UserId == uid)
                .Select(f => f.FacilityId)
                .ToListAsync();
            return Json(ids);
        }

        /// <summary>Thêm / bỏ cụm sân khỏi danh sách yêu thích (gọi bằng AJAX).</summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFavorite(int facilityId)
        {
            var uid = CurrentUserId;
            var facility = await _unitOfWork.Facilities.Query().FirstOrDefaultAsync(f => f.Id == facilityId);
            if (!CanViewFacility(facility)) return NotFound();

            var db = _unitOfWork.Context;
            var existing = await db.FavoriteFacilities.FirstOrDefaultAsync(f => f.UserId == uid && f.FacilityId == facilityId);
            bool isFavorite;
            if (existing != null)
            {
                db.FavoriteFacilities.Remove(existing);
                isFavorite = false;
            }
            else
            {
                db.FavoriteFacilities.Add(new FavoriteFacility { UserId = uid, FacilityId = facilityId });
                isFavorite = true;
            }
            await db.SaveChangesAsync();
            var count = await db.FavoriteFacilities.CountAsync(f => f.UserId == uid);
            return Json(new { isFavorite, count });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Favorites()
        {
            var uid = CurrentUserId;
            var ids = await _unitOfWork.Context.FavoriteFacilities
                .Where(f => f.UserId == uid)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.FacilityId)
                .ToListAsync();
            var all = await _facilityService.SearchFacilitiesAsync(null, null);
            var model = ids.Select(id => all.FirstOrDefault(f => f.Id == id))
                .Where(f => f != null)
                .Select(f => f!)
                .ToList();
            return View(model);
=======

            return View(facilities);
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
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

                var facilityOfCourt = await _unitOfWork.Courts.Query().Where(c => c.Id == courtId).Select(c => c.Facility).FirstOrDefaultAsync();
                if (!CanViewFacility(facilityOfCourt))
                {
                    TempData["ErrorMessage"] = "Sân không tồn tại hoặc chưa được duyệt.";
                    return RedirectToAction(nameof(Index));
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
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingDto dto, string? bookingDate, string? startTime, string? endTime)
        {
            // Fallback parse nếu model binder không nhận chuỗi trực tiếp (vd "24:00" cho sân mở tới nửa đêm)
            if (dto.BookingDate == default && !string.IsNullOrEmpty(bookingDate) && DateOnly.TryParse(bookingDate, out var pDate))
            {
                dto.BookingDate = pDate;
            }
            var rawStart = Request.Form["StartTime"].FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? startTime;
            var rawEnd = Request.Form["EndTime"].FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? endTime;
            bool hasTimes = BookingRules.TryParseHm(rawStart, out var pStart) & BookingRules.TryParseHm(rawEnd, out var pEnd);
            if (hasTimes)
            {
                dto.StartTime = pStart;
                dto.EndTime = pEnd;
            }

            // Đơn luôn thuộc về tài khoản đang đăng nhập (không tin CustomerId gửi lên từ form)
            dto.CustomerId = CurrentUserId;

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

            if (!hasTimes || dto.StartTime >= dto.EndTime)
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
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout(int bookingId)
        {
            if (!await CanAccessBookingAsync(bookingId)) return Forbid();
            var booking = await _bookingService.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                return NotFound("Đơn đặt sân không tồn tại.");
            }

            // Đơn đã thanh toán thì chuyển thẳng sang vé xác nhận
            if (booking.PaymentStatus != PaymentStatus.Unpaid)
            {
                return RedirectToAction(nameof(Confirmation), new { bookingId });
            }

            // Đơn đã hủy / đã hoàn thành / quá hạn giữ chỗ (sẽ bị hủy ngay) thì không cho thanh toán nữa
            var check = await _bookingService.CheckPayableAsync(bookingId);
            if (!check.IsSuccess)
            {
                TempData["ErrorMessage"] = check.Message;
                return RedirectToAction(nameof(Matrix), new { courtId = booking.CourtId, date = booking.BookingDate.ToString("yyyy-MM-dd") });
            }

            return View(booking);
        }

        /// <summary>
        /// Điều hướng khi đơn không thể thanh toán: đã trả tiền → vé xác nhận; không tồn tại → trang tìm sân;
        /// còn lại (đã hủy / quá hạn giữ chỗ) → về ma trận giờ để khách chọn lại khung giờ.
        /// </summary>
        private IActionResult RedirectForUnpayable(BookingResult check, int bookingId)
        {
            if (check.Booking == null)
            {
                TempData["ErrorMessage"] = check.Message;
                return RedirectToAction(nameof(Index));
            }

            if (check.Booking.PaymentStatus != PaymentStatus.Unpaid)
            {
                TempData["SuccessMessage"] = check.Message;
                return RedirectToAction(nameof(Confirmation), new { bookingId });
            }

            TempData["ErrorMessage"] = check.Message;
            return RedirectToAction(nameof(Matrix), new { courtId = check.Booking.CourtId, date = check.Booking.BookingDate.ToString("yyyy-MM-dd") });
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
        [Authorize]
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
            if (!await CanAccessBookingAsync(bookingId)) return Forbid();
            try
            {
                // Kiểm tra đơn còn thanh toán được (chưa trả tiền, chưa hủy, còn hạn giữ chỗ) TRƯỚC khi lưu file
                var check = await _bookingService.CheckPayableAsync(bookingId);
                if (!check.IsSuccess)
                {
                    return RedirectForUnpayable(check, bookingId);
                }

                // Kiểm tra xem khách đã gửi ảnh minh chứng chưa
                if ((proofImage == null || proofImage.Length == 0) && string.IsNullOrEmpty(sampleProofBill))
                {
                    TempData["ErrorMessage"] = "Vui lòng đính kèm ảnh chụp màn hình biên lai chuyển khoản thành công trước khi xác nhận.";
                    return RedirectToAction(nameof(Checkout), new { bookingId });
                }

                string? savedProofPath = null;

                // 1. Nếu khách tải file ảnh minh chứng thật từ điện thoại / máy tính -> lưu thẳng vào SQL
                if (proofImage != null && proofImage.Length > 0)
                {
                    var allowedTypes = new Dictionary<string, string>
                    {
                        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
                        [".webp"] = "image/webp", [".gif"] = "image/gif"
                    };
                    var ext = Path.GetExtension(proofImage.FileName).ToLowerInvariant();
                    if (!allowedTypes.TryGetValue(ext, out var contentType))
                    {
                        TempData["ErrorMessage"] = "Định dạng file ảnh không hợp lệ (chỉ chấp nhận JPG, PNG, WEBP).";
                        return RedirectToAction(nameof(Checkout), new { bookingId });
                    }
                    if (proofImage.Length > MaxProofBytes)
                    {
                        TempData["ErrorMessage"] = "Ảnh biên lai quá lớn (tối đa 5 MB).";
                        return RedirectToAction(nameof(Checkout), new { bookingId });
                    }

                    using var ms = new MemoryStream();
                    await proofImage.CopyToAsync(ms);
                    savedProofPath = await SaveProofToDbAsync(bookingId, ms.ToArray(), contentType, Path.GetFileName(proofImage.FileName));
                }
                // 2. Dự phòng hỗ trợ dữ liệu ảnh Base64 / SVG từ API hoặc kiểm thử
                else if (TryDecodeDataUri(sampleProofBill, out var billBytes, out var billType))
                {
                    savedProofPath = await SaveProofToDbAsync(bookingId, billBytes, billType, null);
                }

                var txnRef = string.IsNullOrEmpty(transactionRef)
                    ? $"VTB-{DateTime.UtcNow:yyyyMMddHHmmss}-{bookingId}"
                    : transactionRef;

                var fullNote = !string.IsNullOrEmpty(senderAccount)
                    ? $"Người chuyển: {senderAccount}. {note}"
                    : (string.IsNullOrEmpty(note) ? "Chuyển khoản VietQR" : note);

                var result = await _bookingService.SubmitBankTransferProofAsync(
                    bookingId,
                    savedProofPath,
                    txnRef,
                    "VietQR (VietinBank 24/7)",
                    fullNote);

                if (result.IsSuccess)
                {
                    TempData["SuccessMessage"] = result.Message;
                    return RedirectToAction(nameof(Confirmation), new { bookingId });
                }

                return RedirectForUnpayable(result, bookingId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi xử lý chuyển khoản: {ex.Message}";
                return RedirectToAction(nameof(Checkout), new { bookingId });
            }
        }

        // 5d. Cổng Sao Kê / Quản Lý Ngân Hàng Ảo (Chỉ dành riêng cho Quản Trị Viên Admin)
        [HttpGet]
        public IActionResult VirtualBankLedger()
        {
            return RedirectToAction("Index", "Bank", new { area = "Admin" });
        }

        // 6. Xác nhận đặt sân thành công
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Confirmation(int bookingId)
        {
            if (!await CanAccessBookingAsync(bookingId)) return Forbid();
            var booking = await _bookingService.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        // 7. Lịch sử đặt sân & Quản lý vé sân của khách hàng
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> MyBookings(string? filter = "all")
        {
            var userId = CurrentUserId;
            var allBookings = await _bookingService.GetCustomerBookingsAsync(userId);

            // Những đơn đã được đánh giá (để ẩn nút "Đánh giá")
            var bookingIds = allBookings.Select(b => b.Id).ToList();
            ViewBag.ReviewedBookingIds = (await _unitOfWork.Reviews.Query()
                .Where(r => bookingIds.Contains(r.BookingId))
                .Select(r => r.BookingId)
                .ToListAsync()).ToHashSet();

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
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int bookingId, string? reason)
        {
            var result = await _bookingService.CancelBookingAsync(bookingId, CurrentUserId, reason);

            TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.Message;

            return RedirectToAction(nameof(MyBookings));
        }

        // 8b. Khách hàng đánh giá cụm sân sau khi đã chơi xong (mỗi đơn 1 lần)
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReview(ReviewFormViewModel model)
        {
            var userId = CurrentUserId;
            var booking = await _unitOfWork.Bookings.Query()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == userId);

            if (booking == null || booking.Court == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt sân của bạn.";
                return RedirectToAction(nameof(MyBookings));
            }

            var endsAt = booking.BookingDate.ToDateTime(TimeOnly.FromTimeSpan(booking.EndTime));
            bool played = booking.Status == BookingStatus.Completed
                || (booking.Status == BookingStatus.Confirmed && booking.PaymentStatus == PaymentStatus.Paid && endsAt <= DateTime.Now);

            if (!played)
            {
                TempData["ErrorMessage"] = "Bạn chỉ có thể đánh giá sau khi đã thanh toán và kết thúc giờ chơi.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (model.Rating < 1 || model.Rating > 5)
            {
                TempData["ErrorMessage"] = "Vui lòng chọn từ 1 đến 5 sao.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (await _unitOfWork.Reviews.AnyAsync(r => r.BookingId == booking.Id))
            {
                TempData["ErrorMessage"] = "Đơn đặt sân này đã được đánh giá rồi.";
                return RedirectToAction(nameof(MyBookings));
            }

            var comment = model.Comment?.Trim();
            if (comment?.Length > 1000) comment = comment[..1000];

            await _unitOfWork.Reviews.AddAsync(new Review
            {
                BookingId = booking.Id,
                FacilityId = booking.Court.FacilityId,
                CustomerId = userId,
                Rating = model.Rating,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
                CreatedAt = DateTime.UtcNow
            });
            await _unitOfWork.CompleteAsync();

            await _notifications.NotifyFacilityOwnerAsync(booking.Court.FacilityId,
                $"Đánh giá mới {model.Rating}★ cho {booking.Court.Facility?.Name}",
                string.IsNullOrWhiteSpace(comment) ? $"{booking.CustomerName} chấm {model.Rating} sao." : $"{booking.CustomerName}: \"{(comment.Length > 140 ? comment[..140] + "…" : comment)}\"",
                $"/Owner/Facility/Reviews?facilityId={booking.Court.FacilityId}", "bi-star-fill", model.Rating <= 3 ? "amber" : "green");

            TempData["SuccessMessage"] = "Cảm ơn bạn đã đánh giá! Nhận xét của bạn đã được đăng lên trang cụm sân.";
            return RedirectToAction(nameof(FacilityDetail), new { id = booking.Court.FacilityId });
        }

        // 9. Chi tiết cụm sân thể thao (Giới thiệu, sân con, bảng giá, dịch vụ tiện ích)
        [HttpGet]
        public async Task<IActionResult> FacilityDetail(int id)
        {
            var facility = await _facilityService.GetFacilityDetailAsync(id);
            if (facility != null && !CanViewFacility(facility)) facility = null;
            if (facility == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy cụm sân được yêu cầu.";
                return RedirectToAction(nameof(Index));
            }

<<<<<<< HEAD
            // Danh sách ảnh thật của sân (chỉ lấy Id, ảnh bìa lên đầu)
            ViewBag.PhotoIds = await _unitOfWork.Context.FacilityImages
                .Where(i => i.FacilityId == facility.Id)
                .OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => i.Id)
                .ToListAsync();

            return View(facility);
        }

        /// <summary>Ảnh cụm sân lưu trong SQL. Cụm sân chưa duyệt thì chỉ chủ sân / Admin xem được.</summary>
        [HttpGet("Booking/FacilityImage/{id:int}")]
        public async Task<IActionResult> FacilityImage(int id)
        {
            var meta = await _unitOfWork.Context.FacilityImages
                .Where(i => i.Id == id)
                .Select(i => new { i.Facility!.ApprovalStatus, i.Facility.OwnerId })
                .FirstOrDefaultAsync();
            if (meta == null) return NotFound();

            bool isPublic = meta.ApprovalStatus == FacilityApprovalStatus.Approved;
            if (!isPublic && !User.IsInRole(AppRoles.Admin) && meta.OwnerId != CurrentUserId) return NotFound();

            var img = await _unitOfWork.Context.FacilityImages
                .Where(i => i.Id == id)
                .Select(i => new { i.Data, i.ContentType })
                .FirstAsync();

            // Mỗi ảnh có Id riêng và không bị sửa nội dung → cho trình duyệt cache lâu
            Response.Headers.CacheControl = isPublic ? "public, max-age=604800, immutable" : "private, max-age=3600";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(img.Data, img.ContentType);
        }
=======
            return View(facility);
        }
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
    }
}
