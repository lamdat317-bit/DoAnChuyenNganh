using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DACN.Controllers
{
    [Authorize]
    public class TeamworkController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        private readonly DACN.Services.INotificationService _notifications;

        public TeamworkController(IUnitOfWork unitOfWork, DACN.Services.INotificationService notifications)
        {
            _unitOfWork = unitOfWork;
            _notifications = notifications;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        }

        /// <summary>Kèo (chưa bị hủy) đang gắn với đơn đặt sân này, nếu có — mỗi đơn chỉ 1 kèo.</summary>
        private async Task<int?> FindActivePostIdForBookingAsync(int bookingId)
        {
            return await _unitOfWork.MatchPosts.Query()
                .Where(p => p.BookingId == bookingId && p.Status != MatchPostStatus.Cancelled)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync();
        }

        // 1. Bảng tin Kèo Thể Thao / Ghép Đội
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(int? sportTypeId, MatchPostType? postType, MatchSkillLevel? skillLevel, string? keyword)
        {
            var query = _unitOfWork.MatchPosts.Query()
                .Include(p => p.SportType)
                .Include(p => p.Creator)
                .AsQueryable();

            // Tự ẩn các kèo đã qua giờ bắt đầu (không cần xóa thủ công)
            query = DACN.Services.MatchPostQueries.NotStarted(query)
                .Where(p => p.Status != MatchPostStatus.Cancelled);

            if (sportTypeId.HasValue && sportTypeId.Value > 0)
            {
                query = query.Where(p => p.SportTypeId == sportTypeId.Value);
            }

            if (postType.HasValue)
            {
                query = query.Where(p => p.PostType == postType.Value);
            }

            if (skillLevel.HasValue && skillLevel.Value != MatchSkillLevel.All)
            {
                query = query.Where(p => p.SkillLevel == skillLevel.Value || p.SkillLevel == MatchSkillLevel.All);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lower = keyword.Trim().ToLower();
                query = query.Where(p => p.Title.ToLower().Contains(lower) ||
                                         p.VenueName.ToLower().Contains(lower) ||
                                         (p.VenueAddress != null && p.VenueAddress.ToLower().Contains(lower)));
            }

            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();

            var postDtos = posts.Select(p => new MatchPostCardDto
            {
                Id = p.Id,
                Title = p.Title,
                SportName = p.SportType?.Name ?? "Thể thao",
                SportIconCss = p.SportType?.IconCss,
                VenueName = p.VenueName,
                VenueAddress = p.VenueAddress,
                PostType = p.PostType,
                MatchDate = p.MatchDate,
                StartTime = p.StartTime,
                EndTime = p.EndTime,
                SkillLevel = p.SkillLevel,
                TotalSlots = p.TotalSlots,
                CurrentSlots = p.CurrentSlots,
                CostPerPerson = p.CostPerPerson,
                CostSharingNote = p.CostSharingNote,
                Status = p.Status,
                CreatorName = p.Creator?.FullName ?? "Đội trưởng",
                CreatorPhone = p.ContactPhone,
                CreatedAt = p.CreatedAt
            }).ToList();

            var vm = new TeamworkIndexViewModel
            {
                Posts = postDtos,
                SportTypes = sportTypes.ToList(),
                SelectedSportTypeId = sportTypeId,
                SelectedPostType = postType,
                SelectedSkillLevel = skillLevel,
                Keyword = keyword,
                TotalOpenMatches = posts.Count(p => p.Status == MatchPostStatus.Open),
                TotalTeammateRecruiting = posts.Count(p => p.PostType == MatchPostType.FindTeammates && p.Status == MatchPostStatus.Open),
                TotalOpponentSearching = posts.Count(p => p.PostType == MatchPostType.FindOpponent && p.Status == MatchPostStatus.Open)
            };

            return View(vm);
        }

        // 2. Chi tiết Kèo & Sơ đồ Đội hình
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Detail(int id)
        {
            var post = await _unitOfWork.MatchPosts.Query()
                .Include(p => p.SportType)
                .Include(p => p.Creator)
                .Include(p => p.Facility)
                .Include(p => p.Booking)
                .Include(p => p.JoinRequests)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                TempData["ErrorMessage"] = "Kèo thi đấu không tồn tại hoặc đã bị gỡ.";
                return RedirectToAction(nameof(Index));
            }

            var currentUserId = GetCurrentUserId();
            bool isCreator = post.CreatorId == currentUserId;
            bool hasJoined = post.JoinRequests.Any(r => r.UserId == currentUserId && r.Status == JoinRequestStatus.Accepted);
            bool hasPendingRequest = post.JoinRequests.Any(r => r.UserId == currentUserId && r.Status == JoinRequestStatus.Pending);

            var acceptedMembers = post.JoinRequests
                .Where(r => r.Status == JoinRequestStatus.Accepted)
                .ToList();

            var pendingRequests = post.JoinRequests
                .Where(r => r.Status == JoinRequestStatus.Pending)
                .ToList();

            var vm = new MatchPostDetailViewModel
            {
                Post = post,
                IsCreator = isCreator,
                HasJoined = hasJoined,
                HasPendingRequest = hasPendingRequest,
                AcceptedMembers = acceptedMembers,
                PendingRequests = pendingRequests
            };

            return View(vm);
        }

        // 3. Màn hình Đăng Kèo Mới (Hỗ trợ 1-Click từ Đơn Đặt Sân)
        [HttpGet]
        public async Task<IActionResult> Create(int? bookingId)
        {
            var model = new CreateMatchPostViewModel();
            var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
            ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name");

            var me = GetCurrentUserId();
            if (bookingId.HasValue)
            {
                var booking = await _unitOfWork.Bookings.Query()
                    .Include(b => b.Court)
                        .ThenInclude(c => c!.Facility)
                            .ThenInclude(f => f!.SportType)
                    .FirstOrDefaultAsync(b => b.Id == bookingId.Value && b.CustomerId == me);

                if (booking == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy đơn đặt sân của bạn.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var blockReason = DACN.Services.BookingRules.GetMatchPostBlockReason(booking, DateTime.Now);
                if (blockReason != null)
                {
                    TempData["ErrorMessage"] = blockReason;
                    return RedirectToAction("MyBookings", "Booking");
                }

                var existingPostId = await FindActivePostIdForBookingAsync(booking.Id);
                if (existingPostId.HasValue)
                {
                    TempData["ErrorMessage"] = "Đơn đặt sân này đã có kèo rồi. Mỗi đơn chỉ tạo được 1 kèo.";
                    return RedirectToAction(nameof(Detail), new { id = existingPostId.Value });
                }

                {
                    model.BookingId = booking.Id;
                    model.FacilityId = booking.Court?.FacilityId;
                    model.SportTypeId = booking.Court?.Facility?.SportTypeId ?? 1;
                    model.VenueName = $"{booking.Court?.Name} - {booking.Court?.Facility?.Name}";
                    model.VenueAddress = booking.Court?.Facility?.Address;
                    model.MatchDate = booking.BookingDate;
                    model.StartTime = booking.StartTime;
                    model.EndTime = booking.EndTime;
                    model.Title = $"Kèo {booking.Court?.Facility?.SportType?.Name ?? "thể thao"} tại {booking.Court?.Facility?.Name} ngày {booking.BookingDate:dd/MM}";
                    model.CostPerPerson = Math.Round(booking.TotalAmount / 7, 0);
                    model.CostSharingNote = $"Tổng tiền sân {booking.TotalAmount:N0} đ chia đều";
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateMatchPostViewModel model)
        {
            var currentUser = GetCurrentUserId();
            if (model.MatchDate < DateOnly.FromDateTime(DateTime.Today))
                ModelState.AddModelError(nameof(model.MatchDate), "Ngày thi đấu không được ở quá khứ.");
            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "Giờ kết thúc phải sau giờ bắt đầu.");
            if (model.CurrentSlots >= model.TotalSlots)
                ModelState.AddModelError(nameof(model.CurrentSlots), "Số người hiện có phải nhỏ hơn tổng số người cần.");
            // Chỉ được gắn đơn đặt sân của chính mình, đã thanh toán, chưa hủy và chưa có kèo
            if (model.BookingId.HasValue)
            {
                var booking = await _unitOfWork.Bookings.Query()
                    .FirstOrDefaultAsync(b => b.Id == model.BookingId.Value && b.CustomerId == currentUser);
                if (booking == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy đơn đặt sân của bạn.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var blockReason = DACN.Services.BookingRules.GetMatchPostBlockReason(booking, DateTime.Now);
                if (blockReason != null)
                {
                    TempData["ErrorMessage"] = blockReason;
                    return RedirectToAction("MyBookings", "Booking");
                }

                var existingPostId = await FindActivePostIdForBookingAsync(booking.Id);
                if (existingPostId.HasValue)
                {
                    TempData["ErrorMessage"] = "Đơn đặt sân này đã có kèo rồi. Mỗi đơn chỉ tạo được 1 kèo.";
                    return RedirectToAction(nameof(Detail), new { id = existingPostId.Value });
                }

                // Lấy cụm sân từ chính đơn, không tin giá trị gửi lên từ form
                model.FacilityId = (await _unitOfWork.Courts.GetByIdAsync(booking.CourtId))?.FacilityId;
            }

            if (!ModelState.IsValid)
            {
                var sportTypes = await _unitOfWork.SportTypes.GetAllAsync();
                ViewBag.SportTypes = new SelectList(sportTypes, "Id", "Name", model.SportTypeId);
                return View(model);
            }

            var currentUserId = GetCurrentUserId();

            var post = new MatchPost
            {
                CreatorId = currentUserId,
                SportTypeId = model.SportTypeId,
                FacilityId = model.FacilityId,
                BookingId = model.BookingId,
                Title = model.Title,
                VenueName = model.VenueName,
                VenueAddress = model.VenueAddress,
                Description = model.Description,
                PostType = model.PostType,
                MatchDate = model.MatchDate,
                StartTime = model.StartTime,
                EndTime = model.EndTime,
                SkillLevel = model.SkillLevel,
                TotalSlots = model.TotalSlots,
                CurrentSlots = model.CurrentSlots,
                CostPerPerson = model.CostPerPerson,
                CostSharingNote = model.CostSharingNote,
                ContactPhone = model.ContactPhone,
                Status = MatchPostStatus.Open,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.MatchPosts.AddAsync(post);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = "Đăng bài tìm kèo thành công! Hãy chia sẻ để bạn bè và các tay chơi khác cùng tham gia.";
            return RedirectToAction(nameof(Detail), new { id = post.Id });
        }

        // 4. Xin Tham Gia Kèo (Join Request)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int matchPostId, string? preferredPosition, string? contactPhone, string? message)
        {
            var post = await _unitOfWork.MatchPosts.Query()
                .Include(p => p.JoinRequests)
                .FirstOrDefaultAsync(p => p.Id == matchPostId);

            if (post == null)
            {
                TempData["ErrorMessage"] = "Kèo không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            if (post.Status != MatchPostStatus.Open || post.CurrentSlots >= post.TotalSlots)
            {
                TempData["ErrorMessage"] = "Rất tiếc! Kèo này hiện đã đủ người hoặc tạm đóng.";
                return RedirectToAction(nameof(Detail), new { id = matchPostId });
            }

            if (post.MatchDate.ToDateTime(TimeOnly.FromTimeSpan(post.StartTime)) <= DateTime.Now)
            {
                TempData["ErrorMessage"] = "Kèo này đã diễn ra, không thể xin tham gia nữa.";
                return RedirectToAction(nameof(Detail), new { id = matchPostId });
            }

            var currentUserId = GetCurrentUserId();

            if (post.CreatorId == currentUserId)
            {
                TempData["ErrorMessage"] = "Bạn chính là Đội trưởng / Người tạo kèo này.";
                return RedirectToAction(nameof(Detail), new { id = matchPostId });
            }

            var existingRequest = post.JoinRequests.FirstOrDefault(r => r.UserId == currentUserId);
            if (existingRequest != null)
            {
                TempData["ErrorMessage"] = "Bạn đã gửi yêu cầu gia nhập kèo này rồi.";
                return RedirectToAction(nameof(Detail), new { id = matchPostId });
            }

            // Không nhập SĐT thì lấy số trong hồ sơ tài khoản (không tự điền số giả)
            if (string.IsNullOrWhiteSpace(contactPhone))
                contactPhone = (await _unitOfWork.Context.Users.FindAsync(currentUserId))?.PhoneNumber;

            var request = new MatchJoinRequest
            {
                MatchPostId = matchPostId,
                UserId = currentUserId,
                PreferredPosition = preferredPosition,
                ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim(),
                Message = message ?? "Xin chào Đội trưởng, cho mình đăng ký 1 slot nhé!",
                Status = JoinRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.MatchJoinRequests.AddAsync(request);
            await _unitOfWork.CompleteAsync();
            await _notifications.NotifyUserAsync(post.CreatorId, $"Có người xin vào kèo \"{post.Title}\"",
                $"{User.FindFirstValue("FullName") ?? "Một người chơi"}{(string.IsNullOrWhiteSpace(preferredPosition) ? "" : $" (vị trí {preferredPosition})")}: {request.Message}",
                $"/Teamwork/Detail/{post.Id}", "bi-person-plus", "blue");

            TempData["SuccessMessage"] = "Gửi yêu cầu tham gia thành công! Đội trưởng sẽ nhận được thông báo và duyệt slot của bạn.";
            return RedirectToAction(nameof(Detail), new { id = matchPostId });
        }

        // 5. Duyệt Yêu Cầu Tham Gia (Dành cho Đội trưởng)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRequest(int requestId, bool accept)
        {
            var request = await _unitOfWork.MatchJoinRequests.Query()
                .Include(r => r.MatchPost)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null || request.MatchPost == null)
            {
                TempData["ErrorMessage"] = "Yêu cầu không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            var currentUserId = GetCurrentUserId();
            if (request.MatchPost.CreatorId != currentUserId)
            {
                TempData["ErrorMessage"] = "Chỉ Đội trưởng tạo kèo mới có quyền duyệt thành viên.";
                return RedirectToAction(nameof(Detail), new { id = request.MatchPostId });
            }

            // Chỉ duyệt yêu cầu đang chờ — tránh bấm 2 lần làm cộng slot 2 lần
            if (request.Status != JoinRequestStatus.Pending)
            {
                TempData["ErrorMessage"] = "Yêu cầu này đã được xử lý trước đó.";
                return RedirectToAction(nameof(Detail), new { id = request.MatchPostId });
            }

            if (accept)
            {
                if (request.MatchPost.CurrentSlots >= request.MatchPost.TotalSlots)
                {
                    TempData["ErrorMessage"] = "Kèo đã đủ số lượng thành viên, không thể nhận thêm.";
                    return RedirectToAction(nameof(Detail), new { id = request.MatchPostId });
                }

                request.Status = JoinRequestStatus.Accepted;
                request.MatchPost.CurrentSlots += 1;

                if (request.MatchPost.CurrentSlots >= request.MatchPost.TotalSlots)
                {
                    request.MatchPost.Status = MatchPostStatus.Full;
                }

                TempData["SuccessMessage"] = "Đã chấp nhận thành viên vào đội!";
            }
            else
            {
                request.Status = JoinRequestStatus.Rejected;
                TempData["SuccessMessage"] = "Đã từ chối yêu cầu tham gia.";
            }

            await _unitOfWork.CompleteAsync();
            await _notifications.NotifyUserAsync(request.UserId,
                accept ? $"Bạn đã được nhận vào kèo \"{request.MatchPost.Title}\"" : $"Yêu cầu vào kèo \"{request.MatchPost.Title}\" bị từ chối",
                accept ? $"Hẹn gặp ngày {request.MatchPost.MatchDate:dd/MM} lúc {DACN.Services.BookingRules.Hm(request.MatchPost.StartTime)} tại {request.MatchPost.VenueName}. Liên hệ đội trưởng: {request.MatchPost.ContactPhone}."
                       : "Đội trưởng đã chọn đủ người phù hợp. Hãy thử tìm kèo khác nhé!",
                $"/Teamwork/Detail/{request.MatchPostId}", accept ? "bi-people-fill" : "bi-person-x", accept ? "green" : "slate");
            return RedirectToAction(nameof(Detail), new { id = request.MatchPostId });
        }

        // 6. Quản Lý Kèo Của Tôi
        [HttpGet]
        public async Task<IActionResult> MyMatches()
        {
            var currentUserId = GetCurrentUserId();

            var myPosts = await _unitOfWork.MatchPosts.Query()
                .Include(p => p.SportType)
                .Include(p => p.JoinRequests)
                    .ThenInclude(r => r.User)
                .Where(p => p.CreatorId == currentUserId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var myJoinedRequests = await _unitOfWork.MatchJoinRequests.Query()
                .Include(r => r.MatchPost)
                    .ThenInclude(p => p!.SportType)
                .Include(r => r.MatchPost)
                    .ThenInclude(p => p!.Creator)
                .Where(r => r.UserId == currentUserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var vm = new MyMatchesViewModel
            {
                MyCreatedPosts = myPosts,
                MyJoinedRequests = myJoinedRequests
            };

            return View(vm);
        }

        // 7. Đổi trạng thái bài đăng Kèo (Đóng / Mở tuyển thêm)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePostStatus(int id)
        {
            var post = await _unitOfWork.MatchPosts.GetByIdAsync(id);
            if (post == null)
            {
                TempData["ErrorMessage"] = "Kèo không tồn tại.";
                return RedirectToAction(nameof(MyMatches));
            }

            var currentUserId = GetCurrentUserId();
            if (post.CreatorId != currentUserId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền quản lý kèo này.";
                return RedirectToAction(nameof(Detail), new { id });
            }

            post.Status = post.Status == MatchPostStatus.Open ? MatchPostStatus.Full : MatchPostStatus.Open;
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = $"Đã cập nhật trạng thái kèo sang: [{(post.Status == MatchPostStatus.Open ? "Đang mở tuyển" : "Đã chốt danh sách")}]";
            return RedirectToAction(nameof(Detail), new { id });
        }

        // 8. Hủy yêu cầu xin gia nhập kèo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int requestId)
        {
            var request = await _unitOfWork.MatchJoinRequests.Query()
                .Include(r => r.MatchPost)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (request == null)
            {
                TempData["ErrorMessage"] = "Yêu cầu không tồn tại.";
                return RedirectToAction(nameof(MyMatches));
            }

            var currentUserId = GetCurrentUserId();
            if (request.UserId != currentUserId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền hủy yêu cầu này.";
                return RedirectToAction(nameof(MyMatches));
            }

            // Thành viên đã được nhận mà rút lui -> trả lại 1 slot cho kèo
            if (request.Status == JoinRequestStatus.Accepted && request.MatchPost != null)
            {
                request.MatchPost.CurrentSlots = Math.Max(1, request.MatchPost.CurrentSlots - 1);
                if (request.MatchPost.Status == MatchPostStatus.Full && request.MatchPost.CurrentSlots < request.MatchPost.TotalSlots)
                {
                    request.MatchPost.Status = MatchPostStatus.Open;
                }
            }

            _unitOfWork.MatchJoinRequests.Remove(request);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = "Đã hủy yêu cầu tham gia kèo thành công.";
            return RedirectToAction(nameof(MyMatches));
        }

        // 9. Xóa bài đăng kèo (Dành cho Đội trưởng)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePost(int id)
        {
            var post = await _unitOfWork.MatchPosts.GetByIdAsync(id);
            if (post == null)
            {
                TempData["ErrorMessage"] = "Kèo không tồn tại.";
                return RedirectToAction(nameof(MyMatches));
            }

            var currentUserId = GetCurrentUserId();
            if (post.CreatorId != currentUserId)
            {
                TempData["ErrorMessage"] = "Chỉ Đội trưởng tạo kèo mới có quyền xóa bài đăng này.";
                return RedirectToAction(nameof(MyMatches));
            }

            _unitOfWork.MatchPosts.Remove(post);
            await _unitOfWork.CompleteAsync();

            TempData["SuccessMessage"] = "Đã xóa bài đăng tìm kèo thành công.";
            return RedirectToAction(nameof(MyMatches));
        }
    }
}
