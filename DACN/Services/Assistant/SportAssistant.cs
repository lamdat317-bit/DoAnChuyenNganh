using System.Globalization;
using DACN.Data;
using DACN.Models;
using DACN.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace DACN.Services.Assistant
{
    /// <summary>Ngữ cảnh hội thoại: trình duyệt giữ lại và gửi kèm câu hỏi sau ("sân này", "mai thì sao?").</summary>
    public class ChatContext
    {
        public int? FacilityId { get; set; }
        public string? LastIntent { get; set; }
        public string? Date { get; set; }
    }

    public class ChatRequest
    {
        public string? Message { get; set; }
        public ChatContext? Context { get; set; }
    }

    public class ChatResponse
    {
        public string Reply { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public string? ActionText { get; set; }
        public List<string>? QuickSuggestions { get; set; }
        public ChatContext? Context { get; set; }
    }

    /// <summary>
    /// Trợ lý đặt sân chạy offline (không gọi AI bên ngoài):
    /// 1) chuẩn hóa câu hỏi (bỏ dấu, teencode), 2) chấm điểm ý định theo cụm từ, 3) tách thực thể (sân, môn, tỉnh, quận, ngày, giờ, giá),
    /// 4) trả lời bằng DỮ LIỆU THẬT trong DB: giờ trống, giá theo khung giờ, đơn và số dư ví của người hỏi.
    /// </summary>
    public class SportAssistant
    {
        private readonly ApplicationDbContext _db;
        private readonly IBankService _bank;
        private readonly ILogger<SportAssistant> _logger;
        private readonly TimeProvider _clock;
        private static readonly CultureInfo Vi = new("vi-VN");

        public SportAssistant(ApplicationDbContext db, IBankService bank, ILogger<SportAssistant> logger, TimeProvider clock)
        {
            _db = db;
            _bank = bank;
            _logger = logger;
            _clock = clock;
        }

        private DateTime LocalNow => _clock.GetLocalNow().DateTime;
        private DateOnly Today => DateOnly.FromDateTime(LocalNow);

        // ============================ Dữ liệu & thực thể ============================

        private sealed class FacilityInfo
        {
            public required Facility F { get; init; }
            public required string FoldedName { get; init; }
            public required HashSet<string> Distinctive { get; init; }
            public decimal MinPrice { get; init; }
            public decimal MaxPrice { get; init; }
            public double Rating { get; init; }
            public int ReviewCount { get; init; }
            public string Where => string.Join(", ", new[] { F.DistrictName, F.City?.Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private sealed class Entities
        {
            public FacilityInfo? Facility { get; set; }
            public SportType? Sport { get; set; }
            public CityProvince? City { get; set; }
            public string? District { get; set; }
            public DateOnly? Date { get; set; }
            public (TimeSpan From, TimeSpan To, bool Exact)? Time { get; set; }
            public long? PriceLimit { get; set; }
            public bool Cheap { get; set; }
            public bool TopRated { get; set; }
            public bool HasFilters => Sport != null || City != null || District != null || PriceLimit != null;
        }

        // Từ chung trong tên sân, không dùng để nhận ra sân cụ thể
        private static readonly HashSet<string> GenericTokens = new()
        {
            "san", "bong", "da", "mini", "cau", "long", "tennis", "pickleball", "club", "clb", "the", "thao", "sports", "sport",
            "arena", "nha", "thi", "dau", "cum", "center", "centre", "va", "quan", "vot", "co", "nhan", "tao", "trung", "tam", "park", "court"
        };

        private static readonly (string Key, string[] Aliases)[] SportAliases =
        {
            ("bong da", new[] { "bong da", "da bong", "da banh", "san banh", "football", "futsal", "san co", "san 5", "san 7", "san bong" }),
            ("cau long", new[] { "cau long" }),
            ("tennis", new[] { "tennis", "quan vot" }),
            ("pickleball", new[] { "pickleball" })
        };

        private async Task<List<FacilityInfo>> LoadFacilitiesAsync()
        {
            var facilities = await _db.Facilities.AsNoTracking()
                .Where(f => f.ApprovalStatus == FacilityApprovalStatus.Approved)
                .Include(f => f.Courts).ThenInclude(c => c.TimeSlots)
                .Include(f => f.SportType)
                .Include(f => f.City)
                .Include(f => f.Services)
                .AsSplitQuery()
                .ToListAsync();
            var ratings = await _db.Reviews.AsNoTracking()
                .GroupBy(r => r.FacilityId)
                .Select(g => new { g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.Key);

            return facilities.Select(f =>
            {
                var prices = f.Courts.Where(c => c.IsActive).SelectMany(c => c.TimeSlots).Select(t => t.Price).ToList();
                if (prices.Count == 0) prices.Add(BookingRules.DefaultPricePerHour);
                var name = AssistantNlu.Fold(f.Name);
                var placeTokens = (AssistantNlu.Fold(f.DistrictName ?? "") + " " + AssistantNlu.Fold(f.City?.Name ?? ""))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                ratings.TryGetValue(f.Id, out var r);
                return new FacilityInfo
                {
                    F = f,
                    FoldedName = name,
                    Distinctive = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 2 && !GenericTokens.Contains(t) && !placeTokens.Contains(t)).ToHashSet(),
                    MinPrice = prices.Min(),
                    MaxPrice = prices.Max(),
                    Rating = r?.Avg ?? 0,
                    ReviewCount = r?.Count ?? 0
                };
            }).ToList();
        }

        private async Task<Entities> ExtractAsync(ParsedText t, List<FacilityInfo> all, ChatContext? ctx)
        {
            var e = new Entities();
            var tokens = t.Folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var placeText = t.Folded;

            // Sân cụ thể: khớp nguyên tên, hoặc khớp phần lớn các từ đặc trưng trong tên
            double best = 0;
            foreach (var fi in all)
            {
                double score = 0;
                if (t.Has(fi.FoldedName)) score = 10;
                else if (fi.Distinctive.Count > 0)
                {
                    int hit = fi.Distinctive.Count(tokens.Contains);
                    double ratio = hit / (double)fi.Distinctive.Count;
                    if (ratio >= 0.5 && (hit >= 2 || (fi.Distinctive.Count == 1 && fi.Distinctive.First().Length >= 4))) score = ratio + hit * 0.01;
                }
                if (score > best) { best = score; e.Facility = fi; }
            }
            if (e.Facility != null) placeText = placeText.Replace(" " + e.Facility.FoldedName + " ", " ");

            // Môn
            var sports = await _db.SportTypes.AsNoTracking().ToListAsync();
            foreach (var (key, aliases) in SportAliases)
            {
                if (!aliases.Any(t.Has)) continue;
                e.Sport = sports.FirstOrDefault(s => AssistantNlu.Fold(s.Name).Contains(key));
                if (e.Sport != null) break;
            }

            // Tỉnh / thành
            var cities = await _db.CityProvinces.AsNoTracking().ToListAsync();
            foreach (var c in cities)
            {
                var n = AssistantNlu.Fold(c.Name).Replace("tp ", "").Replace("thanh pho ", "").Trim();
                if (n.Length > 0 && t.Has(n)) { e.City = c; placeText = placeText.Replace(" " + n + " ", " "); break; }
            }

            // Quận / huyện (lấy theo các cụm sân đang có)
            foreach (var d in all.Select(f => f.F.DistrictName).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderByDescending(d => d!.Length))
            {
                var n = AssistantNlu.Fold(d!).Replace("quan ", "quan ").Trim();
                var bare = n.StartsWith("quan ") || n.StartsWith("huyen ") ? n[(n.IndexOf(' ') + 1)..] : n;
                if (t.Has(n) || (bare.Length >= 4 && !bare.All(char.IsDigit) && t.Has(bare)))
                {
                    e.District = d;
                    placeText = placeText.Replace(" " + n + " ", " ").Replace(" " + bare + " ", " ");
                    break;
                }
            }

            e.Date = AssistantNlu.ParseDate(t, placeText, Today);
            e.Time = AssistantNlu.ParseTime(t, placeText);
            e.PriceLimit = AssistantNlu.ParsePriceLimit(t.Folded);
            e.Cheap = t.HasAny("re", "re nhat", "gia re", "binh dan", "tiet kiem", "sinh vien");
            e.TopRated = t.HasAny("tot nhat", "danh gia cao", "xin nhat", "dep nhat", "chat luong", "uy tin");

            // Không nêu sân nào nhưng đang nói về 1 sân ở câu trước ("sân này", "còn trống không?") → dùng lại sân đó
            if (e.Facility == null && ctx?.FacilityId is int fid)
            {
                var prev = all.FirstOrDefault(f => f.F.Id == fid);
                bool refersBack = t.HasAny("san nay", "o do", "cho do", "san do", "cho nay", "o day", "no");
                bool conflicts = (e.Sport != null && prev?.F.SportTypeId != e.Sport.Id) || (e.City != null && prev?.F.CityId != e.City.Id) || e.District != null;
                if (prev != null && (refersBack || !conflicts && !e.HasFilters)) e.Facility = prev;
            }
            // Bộ lọc chỉ còn đúng 1 cụm sân → coi như hỏi về sân đó
            if (e.Facility == null && (e.District != null || (e.Sport != null && e.City != null)))
            {
                var only = Filter(all, e).ToList();
                if (only.Count == 1) e.Facility = only[0];
            }
            return e;
        }

        private static IEnumerable<FacilityInfo> Filter(IEnumerable<FacilityInfo> all, Entities e) => all
            .Where(f => e.Sport == null || f.F.SportTypeId == e.Sport.Id)
            .Where(f => e.City == null || f.F.CityId == e.City.Id)
            .Where(f => e.District == null || f.F.DistrictName == e.District)
            .Where(f => e.PriceLimit == null || f.MinPrice <= e.PriceLimit);

        // ============================ Điều phối ============================

        public async Task<ChatResponse> AskAsync(string? message, ChatContext? ctx, string? userId)
        {
            if (string.IsNullOrWhiteSpace(message)) return await GreetingAsync(userId);
            if (message.Length > 500) message = message[..500];

            var t = new ParsedText(message);
            var all = await LoadFacilitiesAsync();
            var e = await ExtractAsync(t, all, ctx);
            var scores = AssistantNlu.ScoreIntents(t);
            var intent = AssistantNlu.Best(scores);

            // Ngữ cảnh: "mai thì sao?", "7h tối?" sau khi vừa hỏi lịch trống / giá → hỏi tiếp cùng ý
            Enum.TryParse<ChatIntent>(ctx?.LastIntent, out var last);
            bool hasWhen = e.Date != null || e.Time != null;
            if (intent == ChatIntent.Unknown)
            {
                if (hasWhen && last is ChatIntent.Availability or ChatIntent.Price) intent = last;
                else if (hasWhen) intent = ChatIntent.Availability;
                else if (e.Facility != null && last is ChatIntent.Price or ChatIntent.Directions or ChatIntent.OpeningHours or ChatIntent.Amenities or ChatIntent.Reviews or ChatIntent.Availability
                         && ctx?.FacilityId != e.Facility.F.Id) intent = last;
                else if (e.Facility != null) return Overview(e.Facility);
                else if (e.HasFilters || e.Cheap || e.TopRated) intent = ChatIntent.FindFacility;
            }
            // "đặt sân X tối mai", "tìm sân trống 19h" → xem lịch trống
            if (intent is ChatIntent.Guide or ChatIntent.FindFacility && (hasWhen || (intent == ChatIntent.Guide && e.Facility != null)))
                intent = ChatIntent.Availability;
            if (intent == ChatIntent.FindFacility && e.Facility != null && !e.HasFilters) return Overview(e.Facility);

            // Nhớ ngày của câu trước khi người dùng chỉ đổi giờ ("thế 8h thì sao?")
            if (e.Date == null && e.Time != null && DateOnly.TryParse(ctx?.Date, CultureInfo.InvariantCulture, out var prevDate) && prevDate >= Today)
                e.Date = prevDate;

            var response = intent switch
            {
                ChatIntent.Availability => await AvailabilityAsync(e, all),
                ChatIntent.Price => Price(e, all),
                ChatIntent.FindFacility => Find(e, all, t),
                ChatIntent.Directions => Directions(e, all),
                ChatIntent.OpeningHours => OpeningHours(e, all),
                ChatIntent.Amenities => Amenities(e, all),
                ChatIntent.Reviews => await ReviewsAsync(e, all),
                ChatIntent.MyBookings => await MyBookingsAsync(userId),
                ChatIntent.Wallet => await WalletAsync(userId, t),
                ChatIntent.CancelPolicy => await CancelPolicyAsync(userId, t),
                ChatIntent.HoldPolicy => await HoldPolicyAsync(userId),
                ChatIntent.Payment => await PaymentAsync(userId, t),
                ChatIntent.Teamwork => await TeamworkAsync(e),
                ChatIntent.Guide => Guide(),
                ChatIntent.Owner => Owner(),
                ChatIntent.Contact => Contact(),
                ChatIntent.Account => Account(t, userId),
                ChatIntent.Safety => Safety(),
                ChatIntent.Greeting => await GreetingAsync(userId),
                ChatIntent.Thanks => Simple("😊 Rất vui được giúp bạn! Cần gì cứ nhắn mình nhé, chúc bạn chơi thể thao vui vẻ!", "Sân trống tối nay", "Đơn của tôi"),
                ChatIntent.Bye => Simple("👋 Tạm biệt bạn! Hẹn gặp lại trên sân nhé ⚽🏸", "Sân trống tối nay"),
                ChatIntent.Help => Help(),
                _ => Fallback(message, all)
            };

            // Câu trả lời tự đặt ngữ cảnh (vd danh sách nhiều sân → bỏ sân cũ); câu trả lời chung thì giữ sân đang nói tới
            response.Context ??= new ChatContext { FacilityId = e.Facility?.F.Id ?? ctx?.FacilityId };
            response.Context.LastIntent ??= intent.ToString();
            response.Context.Date ??= e.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return response;
        }

        // ============================ Lịch trống ============================

        private sealed record FreeBlock(TimeSpan Start, TimeSpan End, int Free, int Total, decimal Price, int CourtId, bool Peak);

        private async Task<List<FreeBlock>> BlocksAsync(Facility f, DateOnly date)
        {
            var courts = f.Courts.Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
            var ids = courts.Select(c => c.Id).ToList();
            var threshold = BookingRules.HoldThreshold(DateTime.UtcNow);
            var booked = await _db.Bookings.AsNoTracking()
                .Where(BookingRules.IsActive(threshold))
                .Where(b => ids.Contains(b.CourtId) && b.BookingDate == date)
                .Select(b => new { b.CourtId, b.StartTime, b.EndTime })
                .ToListAsync();
            var nowTime = LocalNow.TimeOfDay;

            var result = new List<FreeBlock>();
            foreach (var (start, end) in BookingRules.HourBlocks(f))
            {
                if (date < Today || (date == Today && end <= nowTime)) continue;
                var free = courts.Where(c => !booked.Any(b => b.CourtId == c.Id && b.StartTime < end && b.EndTime > start)).ToList();
                var peak = courts.SelectMany(c => c.TimeSlots).Any(s => s.IsPeakHour && s.StartTime < end && s.EndTime > start);
                decimal price = free.Count > 0 ? free.Min(c => BookingRules.CalculatePrice(c.TimeSlots, start, end)) : 0;
                result.Add(new FreeBlock(start, end, free.Count, courts.Count, price, free.FirstOrDefault()?.Id ?? 0, peak));
            }
            return result;
        }

        private static bool InWindow(FreeBlock b, (TimeSpan From, TimeSpan To, bool Exact)? w) =>
            w == null || (b.Start < w.Value.To && b.End > w.Value.From);

        private async Task<ChatResponse> AvailabilityAsync(Entities e, List<FacilityInfo> all)
        {
            var date = e.Date ?? Today;
            var dayText = DayText(date);
            var windowText = e.Time is { } w ? (w.Exact ? $" lúc {BookingRules.Hm(w.From)}" : $" {BookingRules.Hm(w.From)}–{BookingRules.Hm(w.To)}") : "";

            if (e.Facility == null)
            {
                var candidates = Filter(all, e).ToList();
                if (!e.HasFilters || candidates.Count == 0)
                {
                    var pick = (candidates.Count > 0 ? candidates : all).OrderByDescending(f => f.Rating).Take(4).ToList();
                    return new ChatResponse
                    {
                        Reply = $"🗓️ Bạn muốn xem lịch trống {dayText}{windowText} ở **sân nào**?" +
                                (e.HasFilters && candidates.Count == 0 ? "\n\nKhông có cụm sân nào khớp đúng bộ lọc bạn nêu, đây là vài sân gợi ý:" : "\n\nVí dụ một vài cụm sân:") + "\n" +
                                string.Join("\n", pick.Select(f => $"• **{f.F.Name}** · {f.F.SportType?.Name} · {f.Where}")) +
                                "\n\n👉 Nhắn tên sân, hoặc môn + khu vực (vd: *\"cầu lông Tân Bình tối mai\"*).",
                        ActionUrl = "/Booking/Index", ActionText = "Xem tất cả cụm sân",
                        QuickSuggestions = pick.Take(3).Select(f => $"{f.F.Name} {dayText}").ToList(),
                        Context = new ChatContext { LastIntent = nameof(ChatIntent.Availability) }
                    };
                }

                // Nhiều sân: tóm tắt mỗi sân còn bao nhiêu khung
                var lines = new List<(FacilityInfo F, List<FreeBlock> Free)>();
                foreach (var f in candidates.Take(6))
                    lines.Add((f, (await BlocksAsync(f.F, date)).Where(b => b.Free > 0 && InWindow(b, e.Time)).ToList()));
                lines = lines.OrderByDescending(l => l.Free.Count).ThenBy(l => l.F.MinPrice).ToList();
                var filterText = string.Join(" · ", new[] { e.Sport?.Name, e.District, e.City?.Name }.Where(s => s != null));

                return new ChatResponse
                {
                    Reply = $"🗓️ **Lịch trống {dayText}{windowText}** ({filterText}):\n\n" +
                            string.Join("\n", lines.Select(l => l.Free.Count == 0
                                ? $"• **{l.F.F.Name}**: ❌ hết khung trống"
                                : $"• **{l.F.F.Name}**: còn {l.Free.Count} khung · sớm nhất {BookingRules.Hm(l.Free[0].Start)} · từ {Money.Vnd(l.Free.Min(b => b.Price))}")) +
                            "\n\n👉 Nhắn tên một sân để xem chi tiết từng khung giờ.",
                    ActionUrl = BuildSearchUrl(e), ActionText = "Xem danh sách sân",
                    QuickSuggestions = lines.Where(l => l.Free.Count > 0).Take(3).Select(l => $"{l.F.F.Name} {dayText}").ToList(),
                    Context = new ChatContext { LastIntent = nameof(ChatIntent.Availability) }
                };
            }

            var fi = e.Facility;
            var blocks = await BlocksAsync(fi.F, date);
            var inWindow = blocks.Where(b => InWindow(b, e.Time)).ToList();
            var free = inWindow.Where(b => b.Free > 0).ToList();
            var head = $"🗓️ **{fi.F.Name}** · {dayText}{windowText}\n";
            string body;
            FreeBlock? target = free.FirstOrDefault();

            if (blocks.Count == 0)
                body = date < Today ? "Ngày này đã qua rồi bạn ơi." : $"Hôm nay sân đã hết giờ hoạt động ({BookingRules.OpenHoursText(fi.F)}). Bạn xem ngày mai nhé!";
            else if (e.Time != null && inWindow.Count == 0)
                body = $"⏰ Khung giờ bạn hỏi nằm ngoài giờ mở cửa ({BookingRules.OpenHoursText(fi.F)}) hoặc đã qua.";
            else if (e.Time is { Exact: true } && free.Count > 0 && free.Count == inWindow.Count)
                body = "✅ **Còn trống!**\n" + string.Join("\n", free.Select(BlockLine));
            else if (free.Count == 0)
            {
                var near = blocks.Where(b => b.Free > 0)
                    .OrderBy(b => e.Time == null ? 0 : Math.Abs((b.Start - e.Time.Value.From).TotalMinutes)).ThenBy(b => b.Start).Take(4).OrderBy(b => b.Start).ToList();
                target = near.FirstOrDefault();
                body = "❌ **Đã kín** trong khung này." + (near.Count > 0
                    ? "\n\nKhung gần nhất còn trống:\n" + string.Join("\n", near.Select(BlockLine))
                    : "\nCả ngày đã kín sân, bạn thử ngày khác nhé.");
            }
            else
            {
                var shown = free.Take(8).ToList();
                body = (e.Time is { Exact: true } ? "⚠️ Một phần khung bạn hỏi đã có người đặt. Các khung còn trống:\n" : "Các khung còn trống:\n") +
                       string.Join("\n", shown.Select(BlockLine)) +
                       (free.Count > shown.Count ? $"\n… và {free.Count - shown.Count} khung khác." : "");
            }

            var dateParam = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new ChatResponse
            {
                Reply = head + "\n" + body + $"\n\n💡 Đặt xong bạn có {BookingRules.HoldMinutes} phút để thanh toán bằng ví (xác nhận OTP).",
                ActionUrl = target != null ? $"/Booking/Matrix?courtId={target.CourtId}&date={dateParam}" : $"/Booking/FacilityDetail/{fi.F.Id}",
                ActionText = target != null ? $"Chọn giờ & đặt sân {DayText(date)}" : "Xem chi tiết sân",
                QuickSuggestions = new List<string>
                {
                    date == Today ? "Ngày mai thì sao?" : "Ngày mốt thì sao?",
                    $"Giá sân {fi.F.Name}",
                    $"Chỉ đường đến {fi.F.Name}"
                },
                Context = new ChatContext { FacilityId = fi.F.Id, LastIntent = nameof(ChatIntent.Availability), Date = dateParam }
            };
        }

        private static string BlockLine(FreeBlock b) =>
            $"• {BookingRules.Hm(b.Start)}–{BookingRules.Hm(b.End)} · còn {b.Free}/{b.Total} sân · {Money.Vnd(b.Price)}{(b.Peak ? " 🔥 giờ vàng" : "")}";

        // ============================ Giá ============================

        private ChatResponse Price(Entities e, List<FacilityInfo> all)
        {
            if (e.Facility == null)
            {
                var list = Filter(all, e).OrderBy(f => f.MinPrice).Take(5).ToList();
                if (list.Count == 0) list = all.OrderBy(f => f.MinPrice).Take(5).ToList();
                var filterText = string.Join(" · ", new[] { e.Sport?.Name, e.District, e.City?.Name }.Where(s => s != null));
                return new ChatResponse
                {
                    Reply = $"💰 **Giá thuê sân theo giờ**{(filterText.Length > 0 ? $" ({filterText})" : "")}, rẻ nhất trước:\n\n" +
                            string.Join("\n", list.Select(f => $"• **{f.F.Name}** · {f.F.SportType?.Name}: {PriceRange(f)}/giờ")) +
                            "\n\n👉 Nhắn tên sân để xem bảng giá chi tiết từng khung giờ.",
                    ActionUrl = BuildSearchUrl(e, "price_asc"), ActionText = "Xem sân giá tốt",
                    QuickSuggestions = list.Take(3).Select(f => $"Giá sân {f.F.Name}").ToList()
                };
            }

            var fi = e.Facility;
            var courts = fi.F.Courts.Where(c => c.IsActive).ToList();
            var court = courts.OrderByDescending(c => c.TimeSlots.Count).FirstOrDefault();
            string table;
            if (court == null || court.TimeSlots.Count == 0)
                table = $"• Cả ngày: {Money.Vnd(BookingRules.DefaultPricePerHour)}/giờ";
            else
                table = string.Join("\n", court.TimeSlots.OrderBy(s => s.StartTime)
                    .Select(s => $"• {BookingRules.Hm(s.StartTime)}–{BookingRules.Hm(s.EndTime)}: {Money.Vnd(s.Price)}/giờ{(s.IsPeakHour ? " 🔥 giờ vàng" : "")}"));
            bool differs = courts.Select(c => string.Join("|", c.TimeSlots.OrderBy(s => s.StartTime).Select(s => $"{s.StartTime}-{s.EndTime}-{s.Price}"))).Distinct().Count() > 1;

            string example = "";
            if (e.Time is { } w && court != null)
            {
                var price = BookingRules.CalculatePrice(court.TimeSlots, w.From, w.To);
                example = $"\n\n🧮 Khung {BookingRules.Hm(w.From)}–{BookingRules.Hm(w.To)}: **{Money.Vnd(price)}**";
            }
            var services = fi.F.Services.Where(s => s.IsActive).Take(3).ToList();

            return new ChatResponse
            {
                Reply = $"💰 **Bảng giá {fi.F.Name}**{(differs ? $" (sân {court!.Name}; các sân khác có thể chênh lệch)" : "")}:\n" + table + example +
                        (services.Count > 0 ? "\n\n🧃 Dịch vụ thêm: " + string.Join(", ", services.Select(s => $"{s.Name} {Money.Vnd(s.Price)}/{s.Unit}")) : "") +
                        "\n\nGiá tính đúng theo số phút bạn chọn, hiện rõ trước khi thanh toán.",
                ActionUrl = $"/Booking/FacilityDetail/{fi.F.Id}", ActionText = $"Xem {fi.F.Name}",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Ngày mai còn trống không?", $"Chỉ đường đến {fi.F.Name}" },
                Context = new ChatContext { FacilityId = fi.F.Id, LastIntent = nameof(ChatIntent.Price) }
            };
        }

        private static string PriceRange(FacilityInfo f) =>
            f.MinPrice == f.MaxPrice ? Money.Vnd(f.MinPrice) : $"{Money.Vnd(f.MinPrice)} – {Money.Vnd(f.MaxPrice)}";

        // ============================ Tìm sân ============================

        private ChatResponse Find(Entities e, List<FacilityInfo> all, ParsedText? t = null)
        {
            var list = Filter(all, e).ToList();

            // "Quận / tỉnh nào có sân?" → tóm tắt độ phủ theo khu vực
            if (t != null && t.HasAny("quan nao", "huyen nao", "tinh nao", "khu vuc nao", "thanh pho nao", "o tinh nao", "nhung dau") && list.Count > 0)
            {
                var byCity = list.GroupBy(f => f.F.City?.Name ?? "Khác").OrderByDescending(g => g.Count()).ToList();
                return new ChatResponse
                {
                    Reply = $"🗺️ **Khu vực có sân{(e.Sport != null ? $" {e.Sport.Name}" : "")}** ({list.Count} cụm sân):\n\n" +
                            string.Join("\n", byCity.Select(g => $"• **{g.Key}** ({g.Count()}): " +
                                string.Join(", ", g.Select(f => f.F.DistrictName).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct()))) +
                            "\n\n👉 Nhắn tên quận hoặc tỉnh để xem danh sách sân ở đó.",
                    ActionUrl = BuildSearchUrl(e), ActionText = "Xem tất cả cụm sân",
                    QuickSuggestions = byCity.Take(3).Select(g => $"{e.Sport?.Name ?? "Sân"} ở {g.Key}").ToList(),
                    Context = new ChatContext { LastIntent = nameof(ChatIntent.FindFacility) }
                };
            }
            string? relaxNote = null;
            if (list.Count == 0 && e.HasFilters)
            {
                // Nới bộ lọc: bỏ quận, rồi bỏ giá, rồi bỏ tỉnh
                var relaxed = new Entities { Sport = e.Sport, City = e.City };
                list = Filter(all, relaxed).ToList();
                if (list.Count == 0) list = Filter(all, new Entities { Sport = e.Sport }).ToList();
                if (list.Count == 0) list = all;
                relaxNote = "Không có sân khớp đúng yêu cầu, mình gợi ý các sân gần giống nhất:";
            }

            var sorted = (e.Cheap || e.PriceLimit != null)
                ? list.OrderBy(f => f.MinPrice).ThenByDescending(f => f.Rating)
                : list.OrderByDescending(f => f.Rating).ThenByDescending(f => f.ReviewCount).ThenBy(f => f.MinPrice);
            var top = sorted.Take(5).ToList();
            var filterText = string.Join(" · ", new[] { e.Sport?.Name, e.District, e.City?.Name, e.PriceLimit != null ? $"dưới {Money.Vnd(e.PriceLimit.Value)}/giờ" : null }.Where(s => s != null));

            return new ChatResponse
            {
                Reply = $"🔎 **{(filterText.Length > 0 ? filterText : "Cụm sân nổi bật")}** · tìm thấy {list.Count} cụm sân\n" +
                        (relaxNote != null ? relaxNote + "\n" : "") + "\n" +
                        string.Join("\n", top.Select(f => $"• **{f.F.Name}** · {f.F.SportType?.Name}\n   📍 {f.Where} · {Stars(f)} · {PriceRange(f)}/giờ")) +
                        (list.Count > top.Count ? $"\n… và {list.Count - top.Count} sân khác." : "") +
                        "\n\n👉 Hỏi tiếp kiểu *\"[tên sân] tối mai còn trống không?\"* để mình kiểm tra lịch.",
                ActionUrl = BuildSearchUrl(e, e.Cheap || e.PriceLimit != null ? "price_asc" : "rating"), ActionText = "Xem kết quả đầy đủ",
                QuickSuggestions = top.Take(3).Select(f => $"{f.F.Name} tối nay").ToList(),
                Context = new ChatContext { FacilityId = top.Count == 1 ? top[0].F.Id : null, LastIntent = nameof(ChatIntent.FindFacility) }
            };
        }

        private static string Stars(FacilityInfo f) => f.ReviewCount == 0 ? "chưa có đánh giá" : $"⭐ {f.Rating:0.0} ({f.ReviewCount})";

        private static string BuildSearchUrl(Entities e, string? sort = null)
        {
            var q = new List<string>();
            if (e.Sport != null) q.Add($"sportTypeId={e.Sport.Id}");
            if (e.City != null) q.Add($"cityId={e.City.Id}");
            if (e.District != null) q.Add($"keyword={Uri.EscapeDataString(e.District)}");
            if (e.PriceLimit != null) q.Add($"maxPrice={e.PriceLimit}");
            if (sort != null) q.Add($"sortBy={sort}");
            return "/Booking/Index" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        }

        private ChatResponse Overview(FacilityInfo fi)
        {
            var f = fi.F;
            return new ChatResponse
            {
                Reply = $"🏟️ **{f.Name}**\n" +
                        $"• Môn: {f.SportType?.Name} · {f.Courts.Count(c => c.IsActive)} sân\n" +
                        $"• 📍 {f.Address}{(fi.Where.Length > 0 ? $" ({fi.Where})" : "")}\n" +
                        $"• ⏰ {BookingRules.OpenHoursText(f)}\n" +
                        $"• 💰 {PriceRange(fi)}/giờ\n" +
                        $"• {Stars(fi)}" +
                        (f.Services.Any(s => s.IsActive) ? $"\n• 🧃 {f.Services.Count(s => s.IsActive)} dịch vụ đi kèm" : "") +
                        "\n\nBạn muốn xem lịch trống, bảng giá hay chỉ đường?",
                ActionUrl = $"/Booking/FacilityDetail/{f.Id}", ActionText = "Xem trang sân",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Bảng giá", "Chỉ đường", "Đánh giá" },
                Context = new ChatContext { FacilityId = f.Id, LastIntent = "Overview" }
            };
        }

        // ============================ Thông tin sân ============================

        private ChatResponse AskWhichFacility(string question, Entities e, List<FacilityInfo> all, string prefix, ChatIntent intent)
        {
            var list = (e.HasFilters ? Filter(all, e) : all).OrderByDescending(f => f.Rating).Take(5).ToList();
            if (list.Count == 0) list = all.OrderByDescending(f => f.Rating).Take(5).ToList();
            return new ChatResponse
            {
                Reply = $"{question}\n\n" + string.Join("\n", list.Select(f => $"• **{f.F.Name}** · {f.Where}")) + "\n\n👉 Nhắn tên sân (hoặc bấm gợi ý bên dưới).",
                ActionUrl = BuildSearchUrl(e), ActionText = "Xem tất cả cụm sân",
                QuickSuggestions = list.Take(3).Select(f => $"{prefix} {f.F.Name}").ToList(),
                Context = new ChatContext { LastIntent = intent.ToString() }
            };
        }

        private ChatResponse Directions(Entities e, List<FacilityInfo> all)
        {
            if (e.Facility == null) return AskWhichFacility("🧭 Bạn muốn chỉ đường đến **cụm sân nào**?", e, all, "Chỉ đường đến", ChatIntent.Directions);
            var f = e.Facility.F;
            return new ChatResponse
            {
                Reply = $"📍 **{f.Name}**\n• Địa chỉ: {FacilityMap.FullAddress(f)}\n• Giờ mở cửa: {BookingRules.OpenHoursText(f)}" +
                        (f.Latitude.HasValue && f.Longitude.HasValue && FacilityMap.HasCoordinates(f) ? $"\n• GPS: {f.Latitude:F5}, {f.Longitude:F5}" : "") +
                        "\n\nBấm nút bên dưới, Google Maps sẽ lấy vị trí hiện tại của bạn và dẫn đường tới sân.",
                ActionUrl = FacilityMap.DirectionsUrl(f), ActionText = "Mở Google Maps chỉ đường",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Bảng giá", "Giờ mở cửa" },
                Context = new ChatContext { FacilityId = f.Id, LastIntent = nameof(ChatIntent.Directions) }
            };
        }

        private ChatResponse OpeningHours(Entities e, List<FacilityInfo> all)
        {
            if (e.Facility == null) return AskWhichFacility("⏰ Mỗi cụm sân có giờ mở cửa riêng. Bạn hỏi **sân nào**?", e, all, "Giờ mở cửa", ChatIntent.OpeningHours);
            var f = e.Facility.F;
            return new ChatResponse
            {
                Reply = $"⏰ **{f.Name}** mở cửa **{BookingRules.OpenHoursText(f)}** mỗi ngày." +
                        (f.IsOvernight ? "\nSân mở qua nửa đêm: khung sau 00:00 được tính vào ngày hôm sau." : ""),
                ActionUrl = $"/Booking/FacilityDetail/{f.Id}", ActionText = "Xem trang sân",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Bảng giá", "Chỉ đường" },
                Context = new ChatContext { FacilityId = f.Id, LastIntent = nameof(ChatIntent.OpeningHours) }
            };
        }

        private ChatResponse Amenities(Entities e, List<FacilityInfo> all)
        {
            if (e.Facility == null) return AskWhichFacility("🧃 Mỗi sân có dịch vụ đi kèm khác nhau (thuê vợt, bóng, áo bib, nước…). Bạn hỏi **sân nào**?", e, all, "Dịch vụ sân", ChatIntent.Amenities);
            var f = e.Facility.F;
            var services = f.Services.Where(s => s.IsActive).OrderBy(s => s.Name).ToList();
            return new ChatResponse
            {
                Reply = services.Count == 0
                    ? $"**{f.Name}** chưa đăng dịch vụ đi kèm trên hệ thống. Bạn có thể hỏi trực tiếp chủ sân khi đến."
                    : $"🧃 **Dịch vụ tại {f.Name}** (chọn thêm lúc đặt sân):\n" + string.Join("\n", services.Select(s => $"• {s.Name}: {Money.Vnd(s.Price)}/{s.Unit}")),
                ActionUrl = $"/Booking/FacilityDetail/{f.Id}", ActionText = "Xem trang sân",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Bảng giá", "Chỉ đường" },
                Context = new ChatContext { FacilityId = f.Id, LastIntent = nameof(ChatIntent.Amenities) }
            };
        }

        private async Task<ChatResponse> ReviewsAsync(Entities e, List<FacilityInfo> all)
        {
            if (e.Facility == null)
            {
                var top = Filter(all, e).Where(f => f.ReviewCount > 0).OrderByDescending(f => f.Rating).ThenByDescending(f => f.ReviewCount).Take(5).ToList();
                return new ChatResponse
                {
                    Reply = top.Count == 0 ? "Chưa có đánh giá nào khớp yêu cầu của bạn."
                        : "⭐ **Sân được đánh giá cao nhất:**\n\n" + string.Join("\n", top.Select(f => $"• **{f.F.Name}** · {Stars(f)} · {f.Where}")),
                    ActionUrl = BuildSearchUrl(e, "rating"), ActionText = "Xem sân đánh giá cao",
                    QuickSuggestions = top.Take(3).Select(f => $"Đánh giá {f.F.Name}").ToList(),
                    Context = new ChatContext { LastIntent = nameof(ChatIntent.Reviews) }
                };
            }
            var fi = e.Facility;
            var recent = await _db.Reviews.AsNoTracking().Include(r => r.Customer)
                .Where(r => r.FacilityId == fi.F.Id && r.Comment != null && r.Comment != "")
                .OrderByDescending(r => r.CreatedAt).Take(3).ToListAsync();
            return new ChatResponse
            {
                Reply = $"⭐ **{fi.F.Name}**: {Stars(fi)}" + (recent.Count == 0 ? "" : "\n\nNhận xét gần đây:\n" + string.Join("\n",
                    recent.Select(r => $"• {new string('★', r.Rating)}{new string('☆', 5 - r.Rating)} \"{Shorten(r.Comment!, 110)}\" — {r.Customer?.FullName ?? "Khách"}"))),
                ActionUrl = $"/Booking/FacilityDetail/{fi.F.Id}", ActionText = "Xem tất cả đánh giá",
                QuickSuggestions = new List<string> { "Tối nay còn trống không?", "Bảng giá", "Chỉ đường" },
                Context = new ChatContext { FacilityId = fi.F.Id, LastIntent = nameof(ChatIntent.Reviews) }
            };
        }

        // ============================ Cá nhân: đơn & ví ============================

        private static ChatResponse LoginRequired(string what, string returnUrl) => new()
        {
            Reply = $"🔐 Bạn cần **đăng nhập** để mình xem {what} của bạn.",
            ActionUrl = "/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), ActionText = "Đăng nhập",
            QuickSuggestions = new List<string> { "Cách đặt sân", "Sân trống tối nay" }
        };

        private async Task<List<Booking>> UpcomingBookingsAsync(string userId)
        {
            var today = Today;
            var list = await _db.Bookings.AsNoTracking()
                .Include(b => b.Court).ThenInclude(c => c!.Facility)
                .Where(b => b.CustomerId == userId && !b.IsCounterBooking && b.Status != BookingStatus.Cancelled
                            && b.Status != BookingStatus.Completed && b.BookingDate >= today)
                .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
                .Take(20).ToListAsync();
            var now = LocalNow;
            var utc = DateTime.UtcNow;
            return list.Where(b => BookingRules.EndsAt(b) > now && BookingRules.IsActiveNow(b, utc)).ToList();
        }

        private async Task<ChatResponse> MyBookingsAsync(string? userId)
        {
            if (userId == null) return LoginRequired("lịch đặt sân", "/Booking/MyBookings");
            var list = await UpcomingBookingsAsync(userId);
            if (list.Count == 0)
                return new ChatResponse
                {
                    Reply = "📭 Bạn chưa có lịch đặt sân nào sắp tới. Đặt một trận nhé?",
                    ActionUrl = "/Booking/Index", ActionText = "Tìm sân ngay",
                    QuickSuggestions = new List<string> { "Sân trống tối nay", "Sân bóng đá giá rẻ", "Tìm kèo" }
                };

            var unpaid = list.FirstOrDefault(b => b.PaymentStatus == PaymentStatus.Unpaid && b.Status == BookingStatus.Pending);
            return new ChatResponse
            {
                Reply = $"📅 **Lịch sắp tới của bạn** ({list.Count} đơn):\n\n" +
                        string.Join("\n", list.Take(5).Select(b =>
                            $"• **#BK-{b.Id}** · {b.Court?.Facility?.Name} ({b.Court?.Name})\n   {DayText(b.BookingDate)} {BookingRules.Hm(b.StartTime)}–{BookingRules.Hm(b.EndTime)} · {Money.Vnd(b.TotalAmount)} · {BookingRules.StatusBadge(b).Text}")) +
                        (unpaid != null ? $"\n\n⚠️ Đơn #BK-{unpaid.Id} chưa thanh toán, còn khoảng **{Math.Max(1, BookingRules.HoldRemainingSeconds(unpaid, DateTime.UtcNow) / 60)} phút** giữ chỗ." : ""),
                ActionUrl = unpaid != null ? $"/Booking/Checkout?bookingId={unpaid.Id}" : "/Booking/MyBookings",
                ActionText = unpaid != null ? $"Thanh toán đơn #BK-{unpaid.Id}" : "Xem lịch đặt của tôi",
                QuickSuggestions = new List<string> { "Hủy thì được hoàn bao nhiêu?", "Số dư ví", "Chỉ đường đến sân" },
                Context = new ChatContext { FacilityId = list[0].Court?.FacilityId, LastIntent = nameof(ChatIntent.MyBookings) }
            };
        }

        private async Task<ChatResponse> WalletAsync(string? userId, ParsedText t)
        {
            if (userId == null)
                return new ChatResponse
                {
                    Reply = "💳 Mỗi tài khoản DatSan247 có sẵn **1 ví ngân hàng ảo** (số tài khoản 9704…). Bạn nạp tiền vào ví rồi thanh toán đơn đặt sân bằng mã OTP; tiền hoàn khi hủy cũng về thẳng ví.\n\n🔐 Đăng nhập để xem số dư của bạn.",
                    ActionUrl = "/Account/Login?ReturnUrl=%2FWallet", ActionText = "Đăng nhập",
                    QuickSuggestions = new List<string> { "Cách thanh toán", "Chính sách hoàn tiền" }
                };

            var acc = await _bank.EnsureAccountAsync(userId);
            var recent = await _db.BankTransactions.AsNoTracking()
                .Where(x => x.FromAccountId == acc.Id || x.ToAccountId == acc.Id)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(3).ToListAsync();
            bool wantsDeposit = t.HasAny("nap tien", "nap them", "nap", "het tien", "khong du tien", "thieu tien");

            return new ChatResponse
            {
                Reply = $"💳 **Ví DatSan247 của bạn**\n• Số tài khoản: {acc.AccountNo}\n• Số dư: **{Money.Vnd(acc.Balance)}**" +
                        (recent.Count == 0 ? "\n\nChưa có giao dịch nào." : "\n\nGiao dịch gần đây:\n" + string.Join("\n", recent.Select(x =>
                            $"• {x.CreatedAt.ToLocalTime():dd/MM HH:mm} · {Money.TypeBadge(x.Type).Label} · {(x.ToAccountId == acc.Id ? "+" : "−")}{Money.Vnd(x.Amount)}"))) +
                        (wantsDeposit ? $"\n\n➕ Nạp tiền (giả lập) tại trang Internet Banking, mỗi lần từ {Money.Vnd(PaymentPolicy.MinDeposit)} đến {Money.Vnd(PaymentPolicy.MaxDeposit)}." : ""),
                ActionUrl = "/Wallet", ActionText = wantsDeposit ? "Nạp tiền vào ví" : "Mở Internet Banking",
                QuickSuggestions = new List<string> { "Đơn của tôi", "Cách thanh toán", "Chính sách hoàn tiền" },
                Context = new ChatContext { LastIntent = nameof(ChatIntent.Wallet) }
            };
        }

        private async Task<ChatResponse> CancelPolicyAsync(string? userId, ParsedText t)
        {
            var reply = "🔄 **Chính sách hủy & hoàn tiền** (khách tự hủy đơn đã thanh toán):\n" +
                        $"• Trước giờ chơi **≥ {PaymentPolicy.FullRefundHours} tiếng**: hoàn **100%**\n" +
                        $"• Từ **{PaymentPolicy.PartialRefundHours} đến dưới {PaymentPolicy.FullRefundHours} tiếng**: hoàn **{PaymentPolicy.PartialRefundPercent}%**\n" +
                        $"• **Dưới {PaymentPolicy.PartialRefundHours} tiếng**: không hoàn tiền\n" +
                        "• Đã tới giờ chơi: không tự hủy được, liên hệ chủ sân\n" +
                        "• **Chủ sân hủy** (mưa bão, bảo trì…): luôn hoàn **100%**\n" +
                        "• Đơn chưa thanh toán: hủy thoải mái, không mất phí\n\n" +
                        "Tiền hoàn được cộng **ngay vào ví** DatSan247. Muốn đổi giờ: hủy đơn cũ rồi đặt khung mới.";

            if (userId != null)
            {
                var next = (await UpcomingBookingsAsync(userId)).FirstOrDefault(b => b.PaymentStatus == PaymentStatus.Paid);
                if (next != null)
                {
                    var hours = (BookingRules.StartsAt(next) - LocalNow).TotalHours;
                    var pct = PaymentPolicy.RefundPercent(hours);
                    var amount = PaymentPolicy.RefundAmount(Money.ToVnd(next.TotalAmount), pct);
                    reply += $"\n\n📌 Đơn gần nhất **#BK-{next.Id}** ({DayText(next.BookingDate)} {BookingRules.Hm(next.StartTime)}, còn khoảng {FormatHours(hours)}): " +
                             (hours <= 0 ? "đã tới giờ chơi, không tự hủy được." : $"nếu hủy bây giờ được hoàn **{pct}% = {Money.Vnd(amount)}**.");
                }
            }
            return new ChatResponse
            {
                Reply = reply,
                ActionUrl = userId != null ? "/Booking/MyBookings" : "/Home/Privacy",
                ActionText = userId != null ? "Quản lý / hủy đơn" : "Xem chính sách",
                QuickSuggestions = new List<string> { "Đơn của tôi", "Số dư ví", "Liên hệ hỗ trợ" },
                Context = new ChatContext { LastIntent = nameof(ChatIntent.CancelPolicy) }
            };
        }

        private async Task<ChatResponse> HoldPolicyAsync(string? userId)
        {
            var reply = $"⏳ Khi bạn bấm đặt, khung giờ được **giữ riêng cho bạn {BookingRules.HoldMinutes} phút**. Thanh toán bằng ví trong thời gian này để chốt sân; " +
                        "quá hạn đơn **tự hủy** và khung giờ mở lại cho người khác (không mất tiền vì bạn chưa trả).";
            string? url = null, text = null;
            if (userId != null)
            {
                var pending = (await UpcomingBookingsAsync(userId)).FirstOrDefault(b => b.Status == BookingStatus.Pending && b.PaymentStatus == PaymentStatus.Unpaid);
                if (pending != null)
                {
                    reply += $"\n\n⚠️ Đơn **#BK-{pending.Id}** của bạn còn khoảng **{Math.Max(1, BookingRules.HoldRemainingSeconds(pending, DateTime.UtcNow) / 60)} phút** giữ chỗ.";
                    url = $"/Booking/Checkout?bookingId={pending.Id}";
                    text = "Thanh toán ngay";
                }
            }
            return new ChatResponse
            {
                Reply = reply, ActionUrl = url ?? "/Booking/Index", ActionText = text ?? "Tìm sân",
                QuickSuggestions = new List<string> { "Cách thanh toán", "Chính sách hủy", "Đơn của tôi" }
            };
        }

        private async Task<ChatResponse> PaymentAsync(string? userId, ParsedText t)
        {
            var reply = "💳 **Thanh toán bằng ví DatSan247** (ngân hàng ảo):\n" +
                        "1. Nạp tiền vào ví ở trang **Internet Banking** (giả lập, không trừ tiền thật).\n" +
                        "2. Đặt sân → trang thanh toán hiện số dư và số tiền cần trả.\n" +
                        $"3. Bấm **Gửi mã OTP** → mã 6 số hiện trên màn hình, hiệu lực **{PaymentPolicy.OtpMinutes} phút** (sai quá {PaymentPolicy.OtpMaxAttempts} lần phải lấy mã mới).\n" +
                        "4. Nhập mã → tiền chuyển thẳng vào ví chủ sân, đơn chuyển **Đã thanh toán** và có vé QR.\n\n" +
                        $"⏳ Phải thanh toán trong {BookingRules.HoldMinutes} phút giữ chỗ. Ví thiếu tiền thì trang sẽ báo cần nạp thêm bao nhiêu.";
            if (t.HasAny("vietqr", "chuyen khoan", "momo", "tien mat"))
                reply += "\n\nℹ️ Đặt online hiện chỉ thanh toán qua ví. Tiền mặt chỉ áp dụng khi chủ sân đặt hộ tại quầy.";
            if (t.HasAny("khong nhan duoc otp", "khong thay otp", "otp sai", "otp het han", "loi otp"))
                reply += "\n\n🛠️ Không thấy / hết hạn OTP: bấm **Gửi lại mã** ngay trên trang thanh toán để lấy mã mới.";
            if (userId != null)
            {
                var acc = await _bank.EnsureAccountAsync(userId);
                reply += $"\n\nSố dư ví hiện tại của bạn: **{Money.Vnd(acc.Balance)}**.";
            }
            return new ChatResponse
            {
                Reply = reply, ActionUrl = "/Wallet", ActionText = "Mở Internet Banking",
                QuickSuggestions = new List<string> { "Số dư ví", "Chính sách hoàn tiền", "Đơn của tôi" },
                Context = new ChatContext { LastIntent = nameof(ChatIntent.Payment) }
            };
        }

        // ============================ Ghép đội & khác ============================

        private async Task<ChatResponse> TeamworkAsync(Entities e)
        {
            var q = _db.MatchPosts.AsNoTracking().NotStarted().Where(p => p.Status == MatchPostStatus.Open);
            if (e.Sport != null) q = q.Where(p => p.SportTypeId == e.Sport.Id);
            if (e.Date != null) q = q.Where(p => p.MatchDate == e.Date);
            var posts = await q.OrderBy(p => p.MatchDate).ThenBy(p => p.StartTime).Take(3).ToListAsync();

            var reply = "👥 **Ghép đội & tìm kèo**\n" +
                        "• **Thiếu người:** đăng kèo tuyển thêm thành viên, chia tiền sân.\n" +
                        "• **Có đội sẵn, tìm đối:** đăng kèo đội gặp đội, các đội khác bấm *Nhận kèo*, bạn chọn 1 đội để chốt.\n" +
                        "• **Từ đơn đã thanh toán:** bấm *Tạo kèo* ngay trong Lịch đặt của tôi.";
            reply += posts.Count == 0
                ? $"\n\nHiện chưa có kèo nào đang mở{(e.Sport != null ? $" cho {e.Sport.Name}" : "")}{(e.Date != null ? $" {DayText(e.Date.Value)}" : "")}. Bạn tạo kèo đầu tiên nhé!"
                : "\n\n🔥 **Kèo đang tuyển:**\n" + string.Join("\n", posts.Select(p =>
                    $"• *{p.Title}* · {p.VenueName} · {DayText(p.MatchDate)} {BookingRules.Hm(p.StartTime)}–{BookingRules.Hm(p.EndTime)} · " +
                    (p.PostType == MatchPostType.FindOpponent ? "tìm đội đối" : $"cần thêm {Math.Max(0, p.TotalSlots - p.CurrentSlots)} người")));
            return new ChatResponse
            {
                Reply = reply, ActionUrl = "/Teamwork/Index", ActionText = "Vào sảnh ghép đội",
                QuickSuggestions = new List<string> { "Kèo bóng đá cuối tuần", "Kèo cầu lông tối nay", "Đơn của tôi" },
                Context = new ChatContext { LastIntent = nameof(ChatIntent.Teamwork) }
            };
        }

        private static ChatResponse Guide() => new()
        {
            Reply = "📖 **Đặt sân trên DatSan247 trong 5 bước:**\n" +
                    "1. **Tìm sân:** mục *Tìm & Đặt sân*, lọc theo môn, tỉnh/thành, giá; xem ảnh, bản đồ, đánh giá.\n" +
                    "2. **Chọn giờ:** ma trận lịch theo ngày (ô trống chọn được; 🔥 là giờ vàng giá cao hơn; ô xám đã có người đặt).\n" +
                    "3. **Thêm dịch vụ** (thuê vợt, bóng, nước…) nếu cần, tổng tiền tự tính.\n" +
                    $"4. **Thanh toán bằng ví** trong {BookingRules.HoldMinutes} phút giữ chỗ, xác nhận bằng mã OTP.\n" +
                    "5. **Nhận vé QR**, đưa mã cho chủ sân check-in khi tới sân.\n\n" +
                    "💡 Cứ hỏi mình kiểu *\"cầu lông Tân Bình tối mai còn trống không?\"*, mình tra lịch giúp bạn.",
            ActionUrl = "/Booking/Index", ActionText = "Bắt đầu tìm sân",
            QuickSuggestions = new List<string> { "Sân trống tối nay", "Cách thanh toán", "Chính sách hủy" }
        };

        private static ChatResponse Owner() => new()
        {
            Reply = "🏢 **Dành cho chủ sân:**\n" +
                    "• Đăng cụm sân (ảnh, địa chỉ tự lên Google Maps, giờ mở cửa, giá thường & giờ vàng), Admin duyệt rồi hiển thị.\n" +
                    "• Bảng lịch đặt theo thời gian thực, đặt hộ tại quầy, check-in bằng mã QR.\n" +
                    "• Tiền khách thanh toán về thẳng **ví chủ sân**; hủy đơn thì hệ thống tự hoàn cho khách.\n" +
                    "• Thống kê doanh thu, đánh giá, thông báo qua Telegram / email.\n\n" +
                    "Để trở thành chủ sân, liên hệ Admin qua hotline **1900 6868** hoặc email **hotro@datsan247.vn**.",
            ActionUrl = "/Owner/Facility/Index", ActionText = "Vào kênh chủ sân",
            QuickSuggestions = new List<string> { "Liên hệ hỗ trợ", "Cách đặt sân" }
        };

        private static ChatResponse Contact() => new()
        {
            Reply = "📞 **Liên hệ DatSan247:**\n• Hotline: **1900 6868** (06:00 – 23:00)\n• Email: **hotro@datsan247.vn**\n• Số của từng chủ sân có trên trang chi tiết sân.",
            ActionUrl = "/Booking/MyBookings", ActionText = "Xem đơn của tôi",
            QuickSuggestions = new List<string> { "Chính sách hủy", "Đơn của tôi" }
        };

        private static ChatResponse Account(ParsedText t, string? userId)
        {
            if (t.HasAny("quen mat khau", "lay lai mat khau"))
                return new ChatResponse
                {
                    Reply = "🔑 Quên mật khẩu: vào trang Đăng nhập → *Quên mật khẩu*, nhập email; link đặt lại (hiệu lực 2 giờ) được gửi vào hộp thư của bạn.",
                    ActionUrl = "/Account/ForgotPassword", ActionText = "Lấy lại mật khẩu"
                };
            if (t.HasAny("bi khoa"))
                return Simple("🔒 Đăng nhập sai 5 lần liên tiếp sẽ bị tạm khóa **5 phút** để chống dò mật khẩu. Đợi 5 phút hoặc đặt lại mật khẩu là mở khóa ngay.", "Quên mật khẩu");
            return new ChatResponse
            {
                Reply = userId != null
                    ? "👤 Bạn đang đăng nhập. Sửa thông tin, đổi mật khẩu ở trang **Hồ sơ cá nhân**."
                    : "👤 Đăng ký miễn phí bằng email, tạo xong là có ngay ví DatSan247 để thanh toán.",
                ActionUrl = userId != null ? "/Account/Profile" : "/Account/Register",
                ActionText = userId != null ? "Hồ sơ cá nhân" : "Đăng ký tài khoản",
                QuickSuggestions = new List<string> { "Quên mật khẩu", "Cách đặt sân" }
            };
        }

        private static ChatResponse Safety() => new()
        {
            Reply = "🛡️ **Không bao giờ trùng lịch hay mất tiền oan:**\n" +
                    "• Đặt sân chạy trong transaction có khóa, 2 người bấm cùng lúc thì chỉ 1 người giữ được khung giờ.\n" +
                    $"• Khung giờ được giữ {BookingRules.HoldMinutes} phút cho người đặt, quá hạn tự mở lại.\n" +
                    "• Thanh toán khóa dòng ví & đơn, mỗi đơn chỉ thanh toán / hoàn tiền **đúng 1 lần**, số dư không thể âm.\n" +
                    "• Xác nhận thanh toán bằng OTP.",
            ActionUrl = "/Home/Privacy", ActionText = "Xem chính sách",
            QuickSuggestions = new List<string> { "Cách thanh toán", "Chính sách hủy" }
        };

        private async Task<ChatResponse> GreetingAsync(string? userId)
        {
            string name = "";
            if (userId != null)
                name = await _db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "";
            var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            var hour = LocalNow.Hour;
            var hello = hour < 11 ? "Chào buổi sáng" : hour < 14 ? "Chào buổi trưa" : hour < 18 ? "Chào buổi chiều" : "Chào buổi tối";
            return new ChatResponse
            {
                Reply = $"👋 {hello}{(first != null ? $" **{first}**" : "")}! Mình là **trợ lý DatSan247**.\n\n" +
                        "Mình có thể:\n• Tìm sân theo môn, khu vực, giá\n• Kiểm tra **giờ trống** và **giá** theo ngày giờ bạn muốn\n• Xem **đơn của bạn**, **số dư ví**, tính tiền hoàn nếu hủy\n• Chỉ đường, giờ mở cửa, đánh giá, ghép đội\n\n" +
                        "Thử hỏi: *\"cầu lông Tân Bình tối mai còn trống không?\"*",
                QuickSuggestions = new List<string> { "Sân trống tối nay", "Sân bóng đá giá rẻ ở Hà Nội", "Đơn của tôi", "Số dư ví" }
            };
        }

        private static ChatResponse Help() => new()
        {
            Reply = "🤖 Mình là **trợ lý đặt sân DatSan247**, hiểu cả tiếng Việt không dấu và viết tắt. Ví dụ bạn có thể hỏi:\n" +
                    "• *\"sân pickleball quận 7\"*, *\"sân bóng đá dưới 300k ở hn\"*\n" +
                    "• *\"Chảo Lửa tối mai còn trống không\"*, *\"19h thứ 7 còn sân k\"*\n" +
                    "• *\"giá sân Star giờ vàng\"*, *\"chỉ đường đến sân này\"*\n" +
                    "• *\"đơn của tôi\"*, *\"ví còn bao nhiêu\"*, *\"hủy giờ thì hoàn bao nhiêu\"*\n" +
                    "• *\"tìm kèo bóng đá cuối tuần\"*",
            QuickSuggestions = new List<string> { "Sân trống tối nay", "Đơn của tôi", "Cách thanh toán" }
        };

        private ChatResponse Fallback(string message, List<FacilityInfo> all)
        {
            // Ghi lại câu chưa hiểu để bổ sung từ khóa về sau
            _logger.LogInformation("[Trợ lý] Chưa hiểu câu hỏi: {Message}", message);
            return new ChatResponse
            {
                Reply = "🤔 Xin lỗi, mình chưa hiểu ý bạn. Bạn thử hỏi cụ thể hơn, ví dụ:\n" +
                        "• *\"sân cầu lông ở Tân Bình\"*\n• *\"" + (all.FirstOrDefault()?.F.Name ?? "Tên sân") + " tối nay còn trống không\"*\n" +
                        "• *\"đơn của tôi\"* · *\"số dư ví\"* · *\"chính sách hủy\"*\n\nHoặc gọi hotline **1900 6868** để gặp nhân viên.",
                QuickSuggestions = new List<string> { "Bạn làm được gì?", "Sân trống tối nay", "Cách đặt sân", "Liên hệ hỗ trợ" }
            };
        }

        private static ChatResponse Simple(string reply, params string[] suggestions) =>
            new() { Reply = reply, QuickSuggestions = suggestions.ToList() };

        // ============================ Định dạng ============================

        private string DayText(DateOnly d)
        {
            if (d == Today) return "hôm nay";
            if (d == Today.AddDays(1)) return "ngày mai";
            if (d == Today.AddDays(2)) return "ngày mốt";
            return d.ToString("dddd dd/MM", Vi).ToLower(Vi);
        }

        private static string FormatHours(double h) =>
            h <= 0 ? "0 phút" : h < 1 ? $"{(int)(h * 60)} phút" : h < 48 ? $"{(int)h} tiếng" : $"{(int)(h / 24)} ngày";

        private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }
}
