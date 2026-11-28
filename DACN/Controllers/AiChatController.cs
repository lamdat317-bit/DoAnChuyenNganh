using System.Security.Claims;
using DACN.Services.Assistant;
using Microsoft.AspNetCore.Mvc;

namespace DACN.Controllers
{
    /// <summary>API trợ lý đặt sân (khung chat góc màn hình). Toàn bộ xử lý nằm ở <see cref="SportAssistant"/>.</summary>
    [ApiController]
    [Route("api/ai")]
    public class AiChatController : ControllerBase
    {
        private readonly SportAssistant _assistant;

        public AiChatController(SportAssistant assistant)
        {
            _assistant = assistant;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest? request)
        {
            // Khách chưa đăng nhập vẫn hỏi được; đã đăng nhập thì trả lời cả về đơn / ví của chính họ
            var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
            var response = await _assistant.AskAsync(request?.Message, request?.Context, userId);
            return Ok(response);
        }
    }
}
