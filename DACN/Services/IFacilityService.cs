using System.Collections.Generic;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.ViewModels;

namespace DACN.Services
{
    public interface IFacilityService
    {
        Task<HomeViewModel> GetHomeDataAsync();
        Task<List<FacilityCardDto>> SearchFacilitiesAsync(int? sportTypeId, string? keyword, int? cityId = null, string? sortBy = null);
        Task<List<Facility>> GetOwnerFacilitiesAsync(string ownerId);
        Task<Facility?> GetFacilityDetailAsync(int facilityId);
    }
}
