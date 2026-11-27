using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using DACN.Models.ViewModels;
using DACN.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services
{
    public class FacilityService : IFacilityService
    {
        private readonly IUnitOfWork _unitOfWork;

        public FacilityService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<HomeViewModel> GetHomeDataAsync()
        {
            var sportTypes = await _unitOfWork.SportTypes.Query()
                .Include(s => s.Facilities)
                    .ThenInclude(f => f.Courts)
                .ToListAsync();

            var sportTypeDtos = sportTypes.Select(s => new SportTypeSummaryDto
            {
                Id = s.Id,
                Name = s.Name,
                IconCss = s.IconCss ?? "bi bi-activity",
                FacilityCount = s.Facilities.Count,
                CourtCount = s.Facilities.SelectMany(f => f.Courts).Count()
            }).ToList();

            var facilities = await _unitOfWork.Facilities.Query()
                .Where(f => f.ApprovalStatus == FacilityApprovalStatus.Approved)
                .Include(f => f.SportType)
                .Include(f => f.City)
                .Include(f => f.Courts)
                    .ThenInclude(c => c.TimeSlots)
                .Include(f => f.Reviews)
                .OrderByDescending(f => f.Reviews.Count)
                .ThenBy(f => f.Id)
                .Take(6)
                .ToListAsync();

            var covers = await GetCoverMapAsync(facilities.Select(f => f.Id));

            var facilityCards = facilities.Select(f =>
            {
                var allSlots = f.Courts.SelectMany(c => c.TimeSlots).ToList();
                decimal minPrice = allSlots.Any() ? allSlots.Min(s => s.Price) : 100000m;
                decimal maxPrice = allSlots.Any() ? allSlots.Max(s => s.Price) : 250000m;

                return new FacilityCardDto
                {
                    Id = f.Id,
                    Name = f.Name,
                    Address = f.Address,
                    SportTypeName = f.SportType?.Name ?? "Thể thao",
                    IconCss = f.SportType?.IconCss,
                    CityName = f.City?.Name,
                    DistrictName = f.DistrictName,
                    ImageUrl = covers.TryGetValue(f.Id, out var coverId) ? PhotoUrl(coverId) : GetSportCoverImage(f.SportType?.Name, f.Id),
                    CourtCount = f.Courts.Count,
                    FirstCourtId = f.Courts.FirstOrDefault(c => c.IsActive)?.Id ?? f.Courts.FirstOrDefault()?.Id ?? 0,
                    MinPrice = minPrice,
                    MaxPrice = maxPrice,
                    OpeningHours = BookingRules.OpenHoursText(f),
                    Latitude = f.Latitude,
                    Longitude = f.Longitude,
                    ReviewCount = f.Reviews.Count,
                    AverageRating = f.Reviews.Count > 0 ? Math.Round(f.Reviews.Average(r => r.Rating), 1) : 0
                };
            }).ToList();

            var totalCourts = await _unitOfWork.Courts.Query().CountAsync(c => c.IsActive);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var todayBookings = await _unitOfWork.Bookings.Query().CountAsync(b => b.BookingDate == today);

            var hotMatches = await _unitOfWork.MatchPosts.Query()
                .Include(p => p.SportType)
                .Include(p => p.Creator)
                // Cùng điều kiện với trang Ghép đội: kèo đã qua giờ bắt đầu tự ẩn
                .NotStarted()
                .Where(p => p.Status == Models.Enums.MatchPostStatus.Open)
                .OrderByDescending(p => p.CreatedAt)
                .Take(4)
                .Select(p => new MatchPostCardDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    SportName = p.SportType != null ? p.SportType.Name : "Thể thao",
                    SportIconCss = p.SportType != null ? p.SportType.IconCss : "bi bi-activity",
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
                    CreatorName = p.Creator != null ? p.Creator.FullName : "Đội trưởng",
                    CreatorPhone = p.ContactPhone,
                    CreatedAt = p.CreatedAt,
                    TeamName = p.TeamName
                })
                .ToListAsync();

            return new HomeViewModel
            {
                SportTypes = sportTypeDtos,
                FeaturedFacilities = facilityCards,
                HotMatches = hotMatches,
                TotalCourtsAvailable = totalCourts,
                TotalBookingsToday = todayBookings
            };
        }

        // Bộ ảnh bìa theo môn (Unsplash). Mỗi cụm sân chọn 1 ảnh theo Id để danh sách không bị trùng ảnh.
        private static readonly string[] FootballCovers =
        {
            "1574629810360-7efbbe195018", "1551958219-acbc608c6377", "1431324155629-1a6deb1dec8d",
            "1579952363873-27f3bade9f55", "1459865264687-595d652de67e", "1575361204480-aadea25e6e68", "1529900748604-07564a03e7a6"
        };
        private static readonly string[] PickleballCovers = { "1622163642998-1ea32b0bbc67", "1693142518820-78d7a05f1546" };
        private static readonly string[] BadmintonCovers = { "1626224583764-f87db24ac4ea", "1521537634581-0dced2fee2ef", "1599474924187-334a4ae5bd3c", "1626721105368-a69248e93b32", "1617696618050-b0fef0c666af", "1559369064-c4d65141e408" };
        private static readonly string[] TennisCovers =
        {
            "1595435934249-5df7ed86e1c0", "1554068865-24cecd4e34b8", "1622279457486-62dcc4a431d6", "1542144582-1ba00456b5e3", "1560012057-4372e14c5085", "1617883861744-13b534e3b928", "1558365849-6ebd8b0454b2"
        };

        /// <summary>Đường dẫn ảnh do chủ sân tải lên.</summary>
        public static string PhotoUrl(int imageId) => $"/Booking/FacilityImage/{imageId}";

        /// <summary>
        /// Ảnh bìa (Id ảnh) của từng cụm sân: ưu tiên ảnh được đánh dấu bìa, sau đó ảnh đầu tiên.
        /// Chỉ đọc Id, không đọc dữ liệu ảnh.
        /// </summary>
        private async Task<Dictionary<int, int>> GetCoverMapAsync(IEnumerable<int> facilityIds)
        {
            var ids = facilityIds.ToList();
            if (ids.Count == 0) return new Dictionary<int, int>();
            var rows = await _unitOfWork.Context.FacilityImages
                .Where(i => ids.Contains(i.FacilityId))
                .Select(i => new { i.Id, i.FacilityId, i.IsCover, i.SortOrder })
                .ToListAsync();
            return rows
                .GroupBy(r => r.FacilityId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.IsCover).ThenBy(r => r.SortOrder).ThenBy(r => r.Id).First().Id);
        }

        public static string GetSportCoverImage(string? sportName, int seed = 0)
        {
            static string Url(string id) => $"https://images.unsplash.com/photo-{id}?auto=format&fit=crop&w=900&q=80";
            static string Pick(string[] arr, int s) => Url(arr[Math.Abs(s) % arr.Length]);

            if (string.IsNullOrEmpty(sportName)) return Url("1529900748604-07564a03e7a6");

            var lower = sportName.ToLower();
            if (lower.Contains("bóng đá") || lower.Contains("football") || lower.Contains("soccer")) return Pick(FootballCovers, seed);
            if (lower.Contains("pickleball")) return Pick(PickleballCovers, seed);
            if (lower.Contains("cầu lông") || lower.Contains("badminton")) return Pick(BadmintonCovers, seed);
            if (lower.Contains("tennis") || lower.Contains("quần vợt")) return Pick(TennisCovers, seed);

            return Url("1529900748604-07564a03e7a6");
        }

        public async Task<List<FacilityCardDto>> SearchFacilitiesAsync(int? sportTypeId, string? keyword, int? cityId = null, string? sortBy = null)
        {
            var query = _unitOfWork.Facilities.Query()
                .Where(f => f.ApprovalStatus == FacilityApprovalStatus.Approved)
                .Include(f => f.SportType)
                .Include(f => f.City)
                .Include(f => f.Courts)
                    .ThenInclude(c => c.TimeSlots)
                .Include(f => f.Reviews)
                .AsQueryable();

            if (sportTypeId.HasValue && sportTypeId.Value > 0)
            {
                query = query.Where(f => f.SportTypeId == sportTypeId.Value);
            }

            if (cityId.HasValue && cityId.Value > 0)
            {
                query = query.Where(f => f.CityId == cityId.Value);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lower = keyword.Trim().ToLower();
                query = query.Where(f => f.Name.ToLower().Contains(lower) || f.Address.ToLower().Contains(lower));
            }

            var list = await query.ToListAsync();
            var covers = await GetCoverMapAsync(list.Select(f => f.Id));

            var dtos = list.Select(f =>
            {
                var allSlots = f.Courts.SelectMany(c => c.TimeSlots).ToList();
                decimal minPrice = allSlots.Any() ? allSlots.Min(s => s.Price) : 100000m;
                decimal maxPrice = allSlots.Any() ? allSlots.Max(s => s.Price) : 250000m;

                return new FacilityCardDto
                {
                    Id = f.Id,
                    Name = f.Name,
                    Address = f.Address,
                    SportTypeName = f.SportType?.Name ?? "Thể thao",
                    IconCss = f.SportType?.IconCss,
                    CityName = f.City?.Name,
                    DistrictName = f.DistrictName,
                    ImageUrl = covers.TryGetValue(f.Id, out var coverId) ? PhotoUrl(coverId) : GetSportCoverImage(f.SportType?.Name, f.Id),
                    CourtCount = f.Courts.Count,
                    FirstCourtId = f.Courts.FirstOrDefault(c => c.IsActive)?.Id ?? f.Courts.FirstOrDefault()?.Id ?? 0,
                    MinPrice = minPrice,
                    MaxPrice = maxPrice,
                    OpeningHours = BookingRules.OpenHoursText(f),
                    Latitude = f.Latitude,
                    Longitude = f.Longitude,
                    ReviewCount = f.Reviews.Count,
                    AverageRating = f.Reviews.Count > 0 ? Math.Round(f.Reviews.Average(r => r.Rating), 1) : 0
                };
            }).ToList();

            if (!string.IsNullOrEmpty(sortBy))
            {
                dtos = sortBy.ToLower() switch
                {
                    "price_asc" => dtos.OrderBy(d => d.MinPrice).ToList(),
                    "price_desc" => dtos.OrderByDescending(d => d.MinPrice).ToList(),
                    "courts" => dtos.OrderByDescending(d => d.CourtCount).ToList(),
                    "rating" => dtos.OrderByDescending(d => d.AverageRating).ThenByDescending(d => d.ReviewCount).ToList(),
                    _ => dtos
                };
            }

            return dtos;
        }

        public async Task<List<Facility>> GetOwnerFacilitiesAsync(string ownerId)
        {
            return await _unitOfWork.Facilities.Query()
                .Include(f => f.SportType)
                .Include(f => f.Courts)
                .Where(f => f.OwnerId == ownerId)
                .ToListAsync();
        }

        public async Task<Facility?> GetFacilityDetailAsync(int facilityId)
        {
            return await _unitOfWork.Facilities.Query()
                .Include(f => f.SportType)
                .Include(f => f.Courts)
                    .ThenInclude(c => c.TimeSlots)
                .Include(f => f.Services)
                .Include(f => f.Owner)
                .Include(f => f.City)
                .Include(f => f.Reviews)
                    .ThenInclude(r => r.Customer)
                .AsSplitQuery()
                .FirstOrDefaultAsync(f => f.Id == facilityId);
        }
    }
}
