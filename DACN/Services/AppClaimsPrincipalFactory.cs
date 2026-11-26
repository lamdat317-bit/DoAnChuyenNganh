using System.Security.Claims;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DACN.Services
{
    /// <summary>
    /// Tên vai trò dùng cho [Authorize(Roles = ...)]. Khớp với tên của enum <see cref="UserRole"/>.
    /// </summary>
    public static class AppRoles
    {
        public const string Customer = nameof(UserRole.Customer);
        public const string Owner = nameof(UserRole.FacilityOwner);
        public const string Admin = nameof(UserRole.Admin);
        public const string OwnerOrAdmin = Owner + "," + Admin;
    }

    /// <summary>
    /// Thêm claim vai trò (Role) và họ tên vào cookie đăng nhập, dựa trên cột ApplicationUser.Role.
    /// Nhờ đó có thể dùng [Authorize(Roles = "...")] và User.IsInRole(...) trong view.
    /// </summary>
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>
    {
        public AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, optionsAccessor)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            identity.AddClaim(new Claim("FullName", user.FullName ?? string.Empty));
            return identity;
        }
    }
}
