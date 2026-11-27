using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DACN.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DACN.Controllers
{
    [ApiController]
    [Route("api/ai")]
    public class AiChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AiChatController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class ChatRequest
        {
            public string? Message { get; set; }
        }

        public class ChatResponse
        {
            public string Reply { get; set; } = string.Empty;
            public string? ActionUrl { get; set; }
            public string? ActionText { get; set; }
            public List<string>? QuickSuggestions { get; set; }
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC)
                .Replace("đ", "d")
                .Replace("Đ", "D");
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                return Ok(new ChatResponse 
                { 
                    Reply = "Xin chào bạn! Tôi là **Trợ Lý Thể Thao AI** của DatSan247. Tôi có thể hỗ trợ bạn tìm sân, kiểm tra khung giờ trống, hướng dẫn sử dụng, dẫn đường Google Maps đến sân hoặc tìm kèo ghép đội thi đấu. Bạn cần tôi hỗ trợ điều gì hôm nay?" 
                });
            }

            var q = request.Message.Trim().ToLower();
            var cleanQ = RemoveDiacritics(q);

            bool Has(params string[] keywords)
            {
                foreach (var kw in keywords)
                {
                    var cleanKw = RemoveDiacritics(kw.ToLower());
                    if (q.Contains(kw.ToLower()) || cleanQ.Contains(cleanKw))
                        return true;
                }
                return false;
            }

            var allFacilities = await _context.Facilities
                .Where(f => f.ApprovalStatus == DACN.Models.Enums.FacilityApprovalStatus.Approved)
                .Include(f => f.Courts)
                .Include(f => f.SportType)
                .Include(f => f.City)
                .ToListAsync();

            // =========================================================================
            // 1. HƯỚNG DẪN CÁCH DÙNG / QUY TRÌNH ĐẶT SÂN TỪ A - Z
            // =========================================================================
            if (Has("cách dùng", "hướng dẫn", "sử dụng", "làm sao để đặt", "cách đặt", "quy trình", "bước đặt", "chỉ cách", "cach dung", "huong dan"))
            {
                var guide = "📖 **HƯỚNG DẪN CHI TIẾT CÁCH SỬ DỤNG DATSAN247 (TỪ A - Z):**\n\n" +
                    "**Bước 1: Tìm kiếm cụm sân phù hợp**\n" +
                    "• Vào mục **'Tìm & Đặt sân'** trên thanh điều hướng.\n" +
                    "• Dùng bộ lọc để chọn môn thể thao (*Bóng đá mini, Pickleball, Cầu lông, Tennis*), chọn Tỉnh/Thành phố (*Hà Nội, TP.HCM, Đà Nẵng...*) hoặc sắp xếp theo mức giá từ thấp đến cao.\n" +
                    "• Nhấp vào cụm sân để xem hình ảnh thực tế, tiện ích, bảng giá và bản đồ vị trí.\n\n" +
                    "**Bước 2: Xem ma trận lịch trống & Chọn khung giờ**\n" +
                    "• Chọn ngày bạn muốn thi đấu (*Hôm nay, Ngày mai hoặc ngày bất kỳ*).\n" +
                    "• Hệ thống hiển thị ma trận giờ trực quan:\n" +
                    "   - 🟢 **Ô xanh ngọc**: Khung giờ còn trống, nhấp chuột để chọn.\n" +
                    "   - 🟡 **Giờ vàng**: Khung giờ cao điểm nhiều người chơi.\n" +
                    "   - ⚪ **Ô xám mờ**: Đã có người đặt trước.\n" +
                    "   - 🟠 **Ô vàng cam**: Đang có khách khác tạm giữ chỗ chờ thanh toán.\n\n" +
                    "**Bước 3: Chọn thêm dịch vụ tiện ích đi kèm (Tùy chọn)**\n" +
                    "• Tại cột bên phải, bạn có thể tick chọn thêm: *Thuê áo bib, bóng thi đấu FIFA, thuê vợt Pickleball Carbon, nước suối ướp lạnh...*\n" +
                    "• Hệ thống tự động tính tổng tiền minh bạch theo thời gian thực.\n\n" +
                    "**Bước 4: Thanh toán giữ chỗ tức thì qua VietQR**\n" +
                    "• Nhấn nút **'Giữ Chỗ & Thanh Toán Ngay'**.\n" +
                    "• Hệ thống tự động khóa khung giờ trong **5 phút** chờ thanh toán (cam kết chống trùng lịch 100%).\n" +
                    "• Mở bất kỳ ứng dụng ngân hàng nào (*VietinBank, VCB, MB, Techcombank...*) hoặc ví MoMo, chọn **'Quét QR'** để thanh toán tự động trong 3 giây không cần nhập tay.\n\n" +
                    "**Bước 5: Nhận Vé Điện Tử E-Ticket Check-in**\n" +
                    "• Nhận ngay mã **Stadium Pass (Vé điện tử)** có mã QR check-in tại quầy sân.\n" +
                    "• Trên vé có sẵn nút **'Dẫn đường Google Maps'** để bạn bật GPS định vị đi thẳng đến sân thi đấu!\n\n" +
                    "💡 *Mẹo thêm:* Nếu thiếu người thi đấu, bạn hãy vào mục **'Ghép Đội & Tìm Kèo'** để đăng bài tuyển slot hoặc xin giao lưu với các đội gần bạn nhé!";

                return Ok(new ChatResponse
                {
                    Reply = guide,
                    ActionUrl = "/Booking/Index",
                    ActionText = "Bắt đầu tìm sân ngay",
                    QuickSuggestions = new List<string> { "Sân bóng đá gần đây", "Sân Pickleball", "Chỉ đường đến sân" }
                });
            }

            // =========================================================================
            // 2. CHỈ ĐƯỜNG ĐẾN SÂN / GOOGLE MAPS ĐỊNH VỊ GPS
            // =========================================================================
            if (Has("đường", "bản đồ", "map", "định vị", "ở đâu", "vị trí", "chỉ đường", "đi đến", "địa chỉ", "duong", "ban do", "chi duong", "dia chi"))
            {
                // Kiểm tra xem người dùng đã nêu cụm sân cụ thể nào chưa
                var matchedFacility = allFacilities.FirstOrDefault(f => 
                {
                    var fNameClean = RemoveDiacritics(f.Name.ToLower());
                    var fDistClean = RemoveDiacritics((f.DistrictName ?? "").ToLower());
                    var fAddrClean = RemoveDiacritics((f.Address ?? "").ToLower());

                    if (!string.IsNullOrEmpty(fNameClean) && cleanQ.Contains(fNameClean)) return true;
                    if (!string.IsNullOrEmpty(fDistClean) && cleanQ.Contains(fDistClean)) return true;
                    if (!string.IsNullOrEmpty(fAddrClean) && cleanQ.Contains(fAddrClean)) return true;

                    // Match specific keywords
                    if (fNameClean.Contains("chao lua") && (cleanQ.Contains("chao lua") || cleanQ.Contains("cau giay") || cleanQ.Contains("hoang quan chi") || cleanQ.Contains("ha noi"))) return true;
                    if (fNameClean.Contains("star") && (cleanQ.Contains("star") || cleanQ.Contains("quan 7") || cleanQ.Contains("nguyen thi thap") || cleanQ.Contains("ho chi minh") || cleanQ.Contains("tphcm"))) return true;

                    return false;
                });

                // Trường hợp 2A: Người dùng ĐÃ NÊU TÊN SÂN CỤ THỂ
                if (matchedFacility != null)
                {
                    var hasGps = matchedFacility.Latitude.HasValue && matchedFacility.Longitude.HasValue && matchedFacility.Latitude.Value != 0;
                    var mapDirectionUrl = hasGps
                        ? $"https://www.google.com/maps/dir/?api=1&destination={matchedFacility.Latitude!.Value.ToString(CultureInfo.InvariantCulture)},{matchedFacility.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}"
                        : $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(matchedFacility.Address)}";

                    var coordsText = hasGps 
                        ? $"\n- **Tọa độ GPS:** `{matchedFacility.Latitude!.Value:F4}, {matchedFacility.Longitude!.Value:F4}`" 
                        : "";

                    var replyMsg = $"📍 **HƯỚNG DẪN ĐƯỜNG ĐI ĐẾN: {matchedFacility.Name.ToUpper()}**\n\n" +
                        $"- **Địa chỉ cụ thể:** {matchedFacility.Address}\n" +
                        $"- **Bộ môn:** {matchedFacility.SportType?.Name ?? "Thể thao"}\n" +
                        $"- **Quy mô:** {matchedFacility.Courts.Count} sân thi đấu đạt chuẩn\n" +
                        $"- **Giờ hoạt động:** {DACN.Services.BookingRules.OpenHoursText(matchedFacility)}{coordsText}\n\n" +
                        $"🚗 **Cách di chuyển:**\n" +
                        $"Bạn hãy nhấp vào nút **'Mở Google Maps chỉ đường'** bên dưới. Ứng dụng Google Maps sẽ tự động lấy vị trí hiện tại của bạn và bật chế độ **dẫn đường GPS Turn-by-turn bằng giọng nói** (xe máy, ô tô hoặc đi bộ) trực tiếp đến tận cổng sân thi đấu!";

                    return Ok(new ChatResponse
                    {
                        Reply = replyMsg,
                        ActionUrl = mapDirectionUrl,
                        ActionText = $"Mở Google Maps dẫn đường đến {matchedFacility.Name}",
                        QuickSuggestions = new List<string> { "Xem lịch trống sân này", "Sân Pickleball", "Cách đặt sân" }
                    });
                }

                // Trường hợp 2B: Người dùng HỎI CHUNG CHUNG ("chỉ đường đến sân", "sân ở đâu") -> AI HỎI LẠI SÂN NÀO
                var facilityListText = string.Join("\n", allFacilities.Select((f, idx) => 
                    $"**{idx + 1}. {f.Name}**\n   📍 *Địa chỉ:* {f.Address} ({f.SportType?.Name})"));

                var askBackReply = "🧭 **BẠN MUỐN TÌM ĐƯỜNG ĐẾN CỤM SÂN NÀO TRONG HỆ THỐNG?**\n\n" +
                    "Hiện tại hệ thống DatSan247 đang có các cụm sân thi đấu sau:\n\n" +
                    facilityListText + "\n\n" +
                    "👉 **Bạn hãy nhắn tên cụm sân bạn muốn đến** (hoặc bấm vào các nút gợi ý bên dưới) để tôi mở bản đồ và gửi đường link dẫn đường GPS chính xác nhất cho bạn nhé!";

                var quickSugg = allFacilities.Take(3).Select(f => f.Name).ToList();

                return Ok(new ChatResponse
                {
                    Reply = askBackReply,
                    ActionUrl = "/Booking/Index",
                    ActionText = "Xem tất cả sân trên bản đồ",
                    QuickSuggestions = quickSugg
                });
            }

            // =========================================================================
            // 3. TRA CỨU SÂN BÓNG ĐÁ
            // =========================================================================
            // Câu hỏi về kèo / ghép đội ưu tiên trả lời phần Ghép đội (dù có nhắc tên môn)
            bool wantsTeam = Has("ghép đội", "tìm kèo", "kèo", "tìm đội", "đối thủ", "ghep doi", "tim keo", "tim doi", "doi thu");

            if (!wantsTeam && Has("bóng đá", "football", "sân banh", "sân bóng", "đá banh", "đá bóng", "bong da", "da banh", "da bong", "san banh"))
            {
                var fbFacilities = allFacilities.Where(f => f.SportType != null && RemoveDiacritics(f.SportType.Name).ToLower().Contains("bong da")).ToList();
                if (fbFacilities.Any())
                {
                    var f = fbFacilities.First();
                    return Ok(new ChatResponse
                    {
                        Reply = $"⚽ **THÔNG TIN SÂN BÓNG ĐÁ:**\n\n" +
                            $"Hệ thống hiện có cụm sân **{f.Name}**:\n" +
                            $"• **Địa chỉ:** {f.Address}\n" +
                            $"• **Quy mô:** {f.Courts.Count} sân bóng cỏ nhân tạo chất lượng cao đạt chuẩn FIFA, có dàn đèn LED chống chói thi đấu đêm.\n" +
                            $"• **Giờ mở cửa:** {DACN.Services.BookingRules.OpenHoursText(f)} hàng ngày.\n" +
                            $"• **Dịch vụ phụ trợ:** Cho thuê áo bib phân đội, bóng thi đấu FIFA, căng tin nước uống, bãi đỗ xe rộng rãi.\n\n" +
                            $"Bạn có thể nhấp vào nút bên dưới để xem ma trận lịch trống và giữ chỗ ngay tức thì!",
                        ActionUrl = $"/Booking/FacilityDetail/{f.Id}",
                        ActionText = $"Xem chi tiết {f.Name}",
                        QuickSuggestions = new List<string> { $"Chỉ đường đến {f.Name}", "Cách đặt sân", "Tìm kèo bóng đá" }
                    });
                }
                return Ok(new ChatResponse 
                { 
                    Reply = "⚽ Bạn có thể vào mục **'Tìm & Đặt sân'**, chọn bộ môn 'Bóng đá mini' để tra cứu tất cả các sân gần bạn kèm bảng giá chi tiết nhé!", 
                    ActionUrl = "/Booking/Index?sportTypeId=1", 
                    ActionText = "Tìm sân bóng đá" 
                });
            }

            // =========================================================================
            // 4. TRA CỨU SÂN PICKLEBALL
            // =========================================================================
            if (!wantsTeam && Has("pickleball", "pikle", "pích", "pickel", "pich", "pickle"))
            {
                var pFacilities = allFacilities.Where(f => f.SportType != null && RemoveDiacritics(f.SportType.Name).ToLower().Contains("pickleball")).ToList();
                if (pFacilities.Any())
                {
                    var f = pFacilities.First();
                    return Ok(new ChatResponse
                    {
                        Reply = $"🏓 **THÔNG TIN CỤM SÂN PICKLEBALL:**\n\n" +
                            $"Cụm sân **{f.Name}** đang hoạt động:\n" +
                            $"• **Địa chỉ:** {f.Address}\n" +
                            $"• **Quy mô:** {f.Courts.Count} sân Pickleball Pro có mái che chống nóng, mặt sân acrylic chuyên dụng độ nảy chuẩn quốc tế bảo vệ khớp gối.\n" +
                            $"• **Giờ mở cửa:** {DACN.Services.BookingRules.OpenHoursText(f)}.\n" +
                            $"• **Dịch vụ tiện ích:** Cho thuê vợt Carbon Pro chính hãng, bán bóng thi đấu 3 quả/hộp, có huấn luyện viên hướng dẫn cho người mới bắt đầu!",
                        ActionUrl = $"/Booking/FacilityDetail/{f.Id}",
                        ActionText = $"Đặt sân {f.Name} ngay",
                        QuickSuggestions = new List<string> { $"Chỉ đường đến {f.Name}", "Cách đặt sân", "Ghép đội Pickleball" }
                    });
                }
                return Ok(new ChatResponse 
                { 
                    Reply = "🏓 Bộ môn Pickleball đang cực kỳ thịnh hành! Bạn có thể xem lịch trống và đặt chỗ tức thì tại mục Tìm sân.", 
                    ActionUrl = "/Booking/Index", 
                    ActionText = "Khám phá sân Pickleball" 
                });
            }

            // =========================================================================
            // 5. CẦU LÔNG & TENNIS
            // =========================================================================
            if (!wantsTeam && Has("cầu lông", "badminton", "tennis", "quần vợt", "cau long", "quan vot"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "🏸🎾 **SÂN CẦU LÔNG & QUẦN VỢT TENNIS:**\n\n" +
                        "• **Cầu lông:** Sân thảm chuyên dụng chống trượt cao cấp, hệ thống đèn chiếu sáng tiêu chuẩn không gây lóa mắt khi đập cầu.\n" +
                        "• **Tennis:** Mặt sân cứng tiêu chuẩn thi đấu, có dịch vụ nhặt bóng và cho thuê vợt theo giờ.\n\n" +
                        "Bạn có thể chọn bộ môn trong thanh tìm kiếm để tra cứu sân gần nhất và xem bảng giá khung giờ!",
                    ActionUrl = "/Booking/Index",
                    ActionText = "Xem danh sách cụm sân",
                    QuickSuggestions = new List<string> { "Cách đặt sân", "Chỉ đường đến sân", "Tìm kèo giao lưu" }
                });
            }

            // =========================================================================
            // 6. CƠ CHẾ CHỐNG TRÙNG LỊCH (SERIALIZABLE ISOLATION SAFE)
            // =========================================================================
            if (Has("trùng lịch", "chống trùng", "concurrency", "bị trùng", "an toàn", "trung lich", "chong trung"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "🛡️ **CƠ CHẾ CHỐNG TRÙNG LỊCH TUYỆT ĐỐI TẠI DATSAN247:**\n\n" +
                        "DatSan247 giải quyết triệt để lỗi đặt trùng giờ (Race Condition) bằng giải pháp kỹ thuật nâng cao:\n\n" +
                        "1. **Cấp độ cô lập giao dịch Serializable (SQL Server):**\n" +
                        "   Khi bạn nhấn nút đặt sân, hệ thống thực thi giao dịch với khóa phân đoạn cao nhất. Không giao dịch nào khác được phép xen ngang vào cùng khung giờ đó.\n" +
                        "2. **Khóa tạm thời 5 phút (Holding Lock):**\n" +
                        "   Khung giờ sẽ chuyển ngay sang trạng thái *'Đang giữ chỗ'* màu vàng cam. Những người chơi khác truy cập cùng thời điểm sẽ không thể bấm đặt đè lên.\n" +
                        "3. **Cơ chế RowVersion (Concurrency Token):**\n" +
                        "   Mỗi bản ghi được kiểm tra phiên bản dữ liệu tự động, ngăn chặn hoàn toàn việc 2 người cùng thanh toán 1 sân.\n\n" +
                        "👉 Nhờ đó bạn hoàn toàn yên tâm khi đặt sân tại DatSan247!",
                    ActionUrl = "/Home/Privacy",
                    ActionText = "Xem cam kết bảo mật & chính sách",
                    QuickSuggestions = new List<string> { "Cách thanh toán qua VietQR", "Cách đặt sân", "Chỉ đường đến sân" }
                });
            }

            // =========================================================================
            // 9. CHÍNH SÁCH HỦY ĐƠN & DỜI LỊCH
            // =========================================================================
            // "hủy" chỉ khớp khi là một từ riêng (tránh nhầm với "huyện"); "mưa" chỉ khớp khi có dấu (tránh nhầm với "mua")
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanQ, @"\bhuy\b") || q.Contains("mưa")
                || Has("đổi lịch", "dời lịch", "đổi giờ", "hoàn tiền", "hoàn lại", "doi lich", "doi gio", "hoan tien", "hoan lai"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "🔄 **CHÍNH SÁCH HỦY & DỜI LỊCH ĐẶT SÂN:**\n\n" +
                        $"• **Tự hủy bất cứ lúc nào trước giờ thi đấu** tại mục *'Lịch đặt của tôi'*. Hủy trước ít nhất {DACN.Services.BookingRules.FreeCancelHours} tiếng được hoàn **100%**; hủy trong vòng {DACN.Services.BookingRules.FreeCancelHours} tiếng trước giờ đá được hoàn **{DACN.Services.BookingRules.LateCancelRefundPercent}%**. Nếu chủ sân hủy đơn, bạn luôn được hoàn 100%.\n" +
                        "• **Đơn giữ chỗ chưa thanh toán:** có thể tự hủy bất cứ lúc nào trước giờ thi đấu.\n" +
                        "• **Thời tiết mưa bão bất khả kháng:** Đối với sân ngoài trời, nếu trời mưa to không thể thi đấu, bạn hãy liên hệ chủ sân. Khi chủ sân hủy đơn, bạn được hoàn **100%** và có thể đặt lại ngày khác.\n" +
                        "• **Muốn đổi giờ:** hiện chưa hỗ trợ dời lịch trực tiếp; bạn hủy đơn cũ (theo chính sách hoàn tiền ở trên) rồi đặt khung giờ mới.\n" +
                        "• **Hỗ trợ tổng đài:** Mọi sự cố phát sinh bạn có thể gọi hotline **1900 6868** để nhân viên hỗ trợ xử lý tức thì.",
                    ActionUrl = "/Booking/MyBookings",
                    ActionText = "Quản lý vé của bạn",
                    QuickSuggestions = new List<string> { "Cách đặt sân", "Chỉ đường đến sân", "Thanh toán VietQR" }
                });
            }

            // =========================================================================
            // 7. THANH TOÁN VIETQR (KHÔNG TIỀN MẶT ĐỂ ĐẢM BẢO GIỮ CHỖ THẬT)
            // =========================================================================
            if (Has("thanh toán", "vietqr", "chuyển khoản", "trả tiền", "chuyển tiền", "momo", "tiền mặt", "phương thức", "thanh toan", "chuyen khoan", "tra tien", "chuyen tien", "tien mat"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "💳 **CHÍNH SÁCH THANH TOÁN TẠI DATSAN247:**\n\n" +
                        "1. **Chuyển khoản trực tuyến qua VietQR (Napas 24/7):**\n" +
                        "   • Để đảm bảo giữ chỗ 100% và tránh tình trạng bùng sân, hệ thống áp dụng hình thức **thanh toán trực tuyến qua mã VietQR**.\n" +
                        "   • Khi đặt sân, hệ thống hiển thị mã VietQR chứa đúng số tiền và mã hóa đơn `#BK...`.\n" +
                        "   • Bạn chỉ cần mở bất kỳ app ngân hàng (*VietinBank, VCB, MB, Techcombank, BIDV, VPBank, MoMo...*) quét mã để chuyển tiền.\n\n" +
                        "2. **Gửi biên lai & nhận vé:**\n" +
                        $"   • Sau khi chuyển tiền (trong {DACN.Services.BookingRules.HoldMinutes} phút giữ chỗ), bạn đính kèm ảnh biên lai. Khung giờ được giữ cho bạn trong lúc chủ sân kiểm tra.\n" +
                        "   • Khi chủ sân xác nhận đã nhận tiền, vé điện tử có mã QR sẽ có hiệu lực và bạn nhận được thông báo.\n\n" +
                        "⚠️ **Lưu ý:** Đặt online cần chuyển khoản để giữ chỗ. Nếu bạn đặt trực tiếp tại quầy, chủ sân có thể thu tiền mặt.",
                    ActionUrl = "/Booking/MyBookings",
                    ActionText = "Xem đơn đặt của tôi",
                    QuickSuggestions = new List<string> { "Xem vé điện tử QR", "Cách đặt sân", "Chỉ đường đến sân" }
                });
            }

            // =========================================================================
            // 8. GHÉP ĐỘI & TÌM KÈO THỂ THAO (TEAMWORK)
            // =========================================================================
            if (Has("ghép đội", "tìm kèo", "kèo", "tìm bạn", "đối thủ", "team", "giao lưu", "ghep doi", "tim keo", "keo", "tim ban", "doi thu", "giao luu"))
            {
                var hotPost = await DACN.Services.MatchPostQueries.NotStarted(_context.MatchPosts)
                    .Where(p => p.Status == DACN.Models.Enums.MatchPostStatus.Open)
                    .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
                var hotPostInfo = hotPost != null 
                    ? $"\n\n🔥 **Kèo nóng đang tuyển slot:**\n• Trận: *\"{hotPost.Title}\"*\n• Địa điểm: *{hotPost.VenueName}*\n• Ngày: *{hotPost.MatchDate:dd/MM/yyyy}* ({hotPost.StartTime:hh\\:mm} - {hotPost.EndTime:hh\\:mm})\n• " + (hotPost.PostType == DACN.Models.Enums.MatchPostType.FindOpponent
                        ? $"Đội hình: *{DACN.Services.MatchTeams.Format(hotPost.CurrentSlots, Math.Max(1, hotPost.TotalSlots - hotPost.CurrentSlots))}* (đội *{hotPost.TeamName ?? "chủ kèo"}* đang chờ đối)"
                        : $"Slot: *{hotPost.CurrentSlots}/{hotPost.TotalSlots} người* (Cần thêm {Math.Max(0, hotPost.TotalSlots - hotPost.CurrentSlots)} người)")
                    : "";

                return Ok(new ChatResponse
                {
                    Reply = "👥 **TÍNH NĂNG GHÉP ĐỘI & TÌM KÈO THỂ THAO:**\n\n" +
                        "Bạn bị thiếu người đá bóng hoặc đội của bạn muốn tìm đối thủ giao lưu cọ xát?\n\n" +
                        "• **Tìm kèo & Xin gia nhập:** Xem danh sách các bài đăng đang thiếu slot gần bạn, nhấn *'Xem & Gia nhập'* để tham gia.\n" +
                        "• **Đăng bài tuyển người:** Nếu bạn đã đặt sân nhưng thiếu người, bạn chỉ cần bấm *'+ Đăng kèo mới'* để tuyển thành viên chia tiền sân cùng.\n" +
                        "• **Có đội sẵn, tìm đối (đội gặp đội):** Bấm *'Có đội sẵn? Tìm đối'* để đăng kèo kèm tên đội và số người mỗi đội. Các đội khác bấm *'Nhận kèo'*, bạn chọn 1 đội để chốt. Hoặc vào tab *'Đội gặp đội'* để nhận kèo của đội khác.\n" +
                        "• **1-Click tạo kèo:** Đơn đặt sân đã thanh toán có thể chuyển thành bài đăng tìm kèo chỉ với 1 thao tác!" + hotPostInfo,
                    ActionUrl = "/Teamwork/Index",
                    ActionText = "Vào Sảnh Ghép Đội ngay",
                    QuickSuggestions = new List<string> { "Đăng bài tìm kèo mới", "Cách đặt sân", "Chỉ đường đến sân" }
                });
            }

            // =========================================================================
            // 10. KÊNH CHỦ SÂN (FACILITY OWNER)
            // =========================================================================
            if (Has("chủ sân", "đăng ký sân", "hợp tác", "quản lý sân", "chủ cơ sở", "chu san", "dang ky san", "hop tac", "quan ly san"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "🏢 **DÀNH CHO CHỦ CƠ SỞ THỂ THAO & SÂN BÃI:**\n\n" +
                        "DatSan247 cung cấp hệ thống quản trị chuyên nghiệp cho chủ sân:\n" +
                        "• **Đăng ký cụm sân mới:** Tự động tạo sân con và thiết lập sẵn bảng giá khung giờ hoạt động.\n" +
                        "• **Bảng Lịch Đặt (Schedule Board):** Dòng thời gian trực quan giúp nhân viên kiểm soát toàn bộ các sân theo thời gian thực.\n" +
                        "• **Quản lý dịch vụ tiện ích:** Thêm mới và cấu hình bảng giá thuê áo bib, bóng, vợt, nước uống.\n" +
                        "• **Duyệt đơn & Thu tiền:** Xác nhận khách check-in và thu tiền mặt trực tiếp tại quầy.",
                    ActionUrl = "/Owner/Facility/Index",
                    ActionText = "Vào Kênh Chủ Sân",
                    QuickSuggestions = new List<string> { "Cách đặt sân", "Chỉ đường đến sân", "Chống trùng lịch" }
                });
            }

            // =========================================================================
            // 10b. LIÊN HỆ / HOTLINE
            // =========================================================================
            if (Has("liên hệ", "hotline", "số điện thoại", "sđt", "tổng đài", "email", "gọi điện", "lien he", "so dien thoai", "sdt", "tong dai", "goi dien"))
            {
                return Ok(new ChatResponse
                {
                    Reply = "📞 **LIÊN HỆ DATSAN247:**\n\n" +
                        "• **Hotline:** 1900 6868 (06:00 – 23:00 mỗi ngày)\n" +
                        "• **Email:** hotro@datsan247.vn\n" +
                        "• **Liên hệ chủ sân:** số điện thoại của từng cụm sân có trên trang chi tiết sân (mục Liên hệ).",
                    ActionUrl = "/Booking/MyBookings",
                    ActionText = "Xem đơn đặt của tôi",
                    QuickSuggestions = new List<string> { "Chính sách hủy & hoàn tiền", "Cách đặt sân", "Thanh toán VietQR" }
                });
            }

            // =========================================================================
            // 11. MẶC ĐỊNH / CHÀO HỎI THÔNG MINH
            // =========================================================================
            var defaultFacilitiesText = string.Join(", ", allFacilities.Select(f => f.Name));

            return Ok(new ChatResponse
            {
                Reply = "🤖 Xin chào bạn! Tôi là **Trợ Lý Thể Thao AI** của DatSan247.\n\n" +
                    "Tôi có thể hỗ trợ và giải đáp chi tiết cho bạn về:\n" +
                    "1️⃣ **Cách dùng hệ thống:** Hướng dẫn từ bước tìm sân, chọn khung giờ, thanh toán đến khi nhận vé.\n" +
                    "2️⃣ **Chỉ đường Google Maps:** Hướng dẫn đường đi và bật GPS dẫn đường đến bất kỳ cụm sân nào (*" + defaultFacilitiesText + "*).\n" +
                    "3️⃣ **Tra cứu lịch trống & Giá vé:** Kiểm tra giờ hoạt động, bảng giá và dịch vụ đi kèm.\n" +
                    "4️⃣ **Chống trùng lịch & VietQR:** Cơ chế giữ chỗ an toàn và xuất vé điện tử check-in.\n" +
                    "5️⃣ **Ghép đội & Tìm kèo:** Kết nối bạn bè cùng sở thích thể thao.\n\n" +
                    "👉 Bạn hãy nhập câu hỏi hoặc chọn một trong các gợi ý bên dưới nhé!",
                ActionUrl = "/Booking/Index",
                ActionText = "Xem tất cả cụm sân",
                QuickSuggestions = new List<string> { "Cách dùng hệ thống", "Chỉ đường đến sân", "Sân bóng đá gần đây", "Sân Pickleball", "Chống trùng lịch" }
            });
        }
    }
}
