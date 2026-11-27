using System.Diagnostics;
using System.Threading.Tasks;
using DACN.Models;
using DACN.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DACN.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IFacilityService _facilityService;

        public HomeController(ILogger<HomeController> logger, IFacilityService facilityService)
        {
            _logger = logger;
            _facilityService = facilityService;
        }

        public async Task<IActionResult> Index()
        {
            var homeData = await _facilityService.GetHomeDataAsync();
            return View(homeData);
        }

        public IActionResult Privacy()
        {
            return View();
        }

<<<<<<< HEAD
        /// <summary>Trang hiển thị cho mã lỗi HTTP (404 không tìm thấy, 403 bị chặn...).</summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult HttpStatus(int code = 404)
        {
            Response.StatusCode = code;
            ViewBag.Code = code;
            return View();
        }

=======
>>>>>>> ffbb85a62983d539c74f270981df849bea20ac70
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
