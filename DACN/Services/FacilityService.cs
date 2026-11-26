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
                .Take(8)
                .ToListAsync();

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
                    ImageUrl = GetSportCoverImage(f.SportType?.Name),
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
                    CreatedAt = p.CreatedAt
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

        public static string GetSportCoverImage(string? sportName)
        {
            if (string.IsNullOrEmpty(sportName))
                return "https://images.unsplash.com/photo-1529900748604-07564a03e7a6?auto=format&fit=crop&w=800&q=80";

            var lower = sportName.ToLower();
            if (lower.Contains("bóng đá") || lower.Contains("football") || lower.Contains("soccer"))
                return "https://images.unsplash.com/photo-1574629810360-7efbbe195018?auto=format&fit=crop&w=800&q=80";
            if (lower.Contains("pickleball"))
                return "https://images.unsplash.com/photo-1622163642998-1ea32b0bbc67?auto=format&fit=crop&w=800&q=80";
            if (lower.Contains("cầu lông") || lower.Contains("badminton"))
                return "https://images.unsplash.com/photo-1626224583764-f87db24ac4ea?auto=format&fit=crop&w=800&q=80";
            if (lower.Contains("tennis") || lower.Contains("quần vợt"))
                return "https://images.unsplash.com/photo-1595435934249-5df7ed86e1c0?auto=format&fit=crop&w=800&q=80";

            return "https://images.unsplash.com/photo-1529900748604-07564a03e7a6?auto=format&fit=crop&w=800&q=80";
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
                    ImageUrl = GetSportCoverImage(f.SportType?.Name),
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
