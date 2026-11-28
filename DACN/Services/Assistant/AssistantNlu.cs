using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DACN.Services.Assistant
{
    /// <summary>Các ý định (intent) trợ lý hiểu được. Thứ tự = độ ưu tiên khi hai ý định bằng điểm.</summary>
    public enum ChatIntent
    {
        Unknown = 0,
        MyBookings, Wallet, Availability, Price, Directions, OpeningHours, Amenities, Reviews,
        CancelPolicy, HoldPolicy, Payment, Teamwork, FindFacility, Guide, Owner, Contact, Account, Safety,
        Greeting, Thanks, Bye, Help
    }

    /// <summary>Câu hỏi đã được chuẩn hóa: bỏ dấu, sửa từ viết tắt, tách token.</summary>
    public sealed class ParsedText
    {
        /// <summary>Chữ thường, CÒN dấu (phân biệt "tối"/"tôi", "mốt"/"một", "mưa"/"mua").</summary>
        public string Raw { get; }
        /// <summary>Chữ thường, KHÔNG dấu, đã thay từ viết tắt, bọc 2 đầu bằng khoảng trắng để so cụm từ theo ranh giới từ.</summary>
        public string Folded { get; }
        public HashSet<string> RawTokens { get; }

        public ParsedText(string message)
        {
            Raw = Regex.Replace(message.ToLowerInvariant(), @"\s+", " ").Trim();
            RawTokens = Raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim(',', '.', '?', '!', ';', '"', '\'', '(', ')')).ToHashSet();
            Folded = " " + AssistantNlu.ExpandSlang(AssistantNlu.Fold(message)) + " ";
        }

        /// <summary>Có cụm từ (dạng không dấu) đứng nguyên vẹn trong câu.</summary>
        public bool Has(string foldedPhrase) => Folded.Contains(" " + foldedPhrase + " ");
        public bool HasAny(params string[] foldedPhrases) => foldedPhrases.Any(Has);
        /// <summary>Có từ đúng nguyên dấu (vd "tối", "ví", "mưa").</summary>
        public bool HasRawWord(string word) => RawTokens.Contains(word);
    }

    public static class AssistantNlu
    {
        /// <summary>Chữ thường, bỏ dấu tiếng Việt, đ→d, giữ chữ số và các ký tự : / - để đọc giờ & ngày.</summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var formD = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c == 'đ') sb.Append('d');
                else if (char.IsLetterOrDigit(c) || c == ':' || c == '/') sb.Append(c);
                else if (c == '-' || c == '–') sb.Append(" - ");
                else if (c == '<') sb.Append(" duoi ");
                else sb.Append(' ');
            }
            return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }

        // Từ viết tắt / teencode hay gặp trong chat → dạng đầy đủ (đã bỏ dấu)
        private static readonly Dictionary<string, string> Slang = new()
        {
            ["ko"] = "khong", ["k"] = "khong", ["kh"] = "khong", ["hok"] = "khong", ["hong"] = "khong", ["khg"] = "khong", ["hem"] = "khong",
            ["dc"] = "duoc", ["dk"] = "duoc", ["đc"] = "duoc",
            ["bn"] = "bao nhieu", ["bnhieu"] = "bao nhieu", ["bnh"] = "bao nhieu",
            ["j"] = "gi", ["ji"] = "gi",
            ["mk"] = "minh", ["mik"] = "minh", ["mjk"] = "minh", ["mh"] = "minh",
            ["bd"] = "bong da", ["banh"] = "banh",
            ["pkb"] = "pickleball", ["pick"] = "pickleball", ["pickle"] = "pickleball", ["picklebal"] = "pickleball", ["pikleball"] = "pickleball",
            ["badminton"] = "cau long",
            ["hcm"] = "ho chi minh", ["tphcm"] = "ho chi minh", ["sg"] = "ho chi minh", ["saigon"] = "ho chi minh",
            ["hn"] = "ha noi", ["hanoi"] = "ha noi", ["dn"] = "da nang", ["danang"] = "da nang",
            ["sdt"] = "so dien thoai", ["dt"] = "dien thoai",
            ["vs"] = "voi", ["trog"] = "trong", ["dag"] = "dang", ["ng"] = "nguoi", ["nguoj"] = "nguoi",
            ["tks"] = "cam on", ["thanks"] = "cam on", ["thank"] = "cam on", ["thx"] = "cam on", ["camon"] = "cam on",
            ["ok"] = "ok", ["oke"] = "ok", ["okie"] = "ok",
            ["cn"] = "chu nhat", ["t7"] = "thu 7", ["t2"] = "thu 2", ["t3"] = "thu 3", ["t4"] = "thu 4", ["t5"] = "thu 5", ["t6"] = "thu 6",
            ["otp"] = "otp", ["qr"] = "qr",
            ["ib"] = "internet banking"
        };

        public static string ExpandSlang(string folded)
        {
            var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Slang.TryGetValue(t, out var full) ? full : t)
                // q7 → quan 7 ; 300k → 300000
                .Select(t => Regex.IsMatch(t, @"^q\d{1,2}$") ? "quan " + t[1..] : t)
                .Select(t => Regex.Replace(t, @"^(\d+)k$", m => (long.Parse(m.Groups[1].Value) * 1000).ToString()));
            return string.Join(' ', tokens);
        }

        // ============================ Ý định ============================

        /// <summary>Cụm từ (không dấu) gợi ý từng ý định. Điểm = số từ của cụm (cụm dài cụ thể hơn → nặng ký hơn).</summary>
        private static readonly Dictionary<ChatIntent, string[]> IntentPhrases = new()
        {
            [ChatIntent.Greeting] = new[] { "xin chao", "chao", "hello", "hi", "alo", "hey", "chao ban", "chao bot", "chao shop", "chao ad" },
            [ChatIntent.Thanks] = new[] { "cam on", "ok cam on", "hay qua", "tuyet voi", "ok", "duoc roi" },
            [ChatIntent.Bye] = new[] { "tam biet", "bye", "bai bai", "hen gap lai" },
            [ChatIntent.Help] = new[] { "ban la ai", "ban lam duoc gi", "giup duoc gi", "tro giup", "help", "chuc nang", "ho tro gi", "biet lam gi" },
            [ChatIntent.Guide] = new[] { "cach dat", "huong dan", "dat san nhu the nao", "lam sao de dat", "lam sao dat", "quy trinh", "cach dung", "su dung", "the nao de dat", "dat nhu nao", "dat the nao", "cac buoc", "muon dat san", "dat san" },
            [ChatIntent.FindFacility] = new[] { "tim san", "san nao", "goi y", "gioi thieu", "danh sach san", "o dau co", "gan day", "gan nhat", "san gan", "muon da", "muon choi", "choi o dau", "nen choi", "co san nao", "san tot", "san dep", "san re", "re nhat", "tot nhat", "nhung san", "cac san", "may san", "quan nao", "huyen nao", "tinh nao", "khu vuc nao", "thanh pho nao", "o tinh nao", "nhung dau" },
            [ChatIntent.Availability] = new[] { "con trong", "trong khong", "lich trong", "gio trong", "khung gio", "con san", "con cho", "het san", "kin lich", "con gio", "gio nao", "dat duoc khong", "con slot", "san trong", "co trong", "trong gio", "con trong khong", "luc nao trong", "trong luc nao", "co san khong" },
            [ChatIntent.Price] = new[] { "gia", "bao nhieu tien", "bao nhieu 1 gio", "bao nhieu mot gio", "bang gia", "chi phi", "ton bao nhieu", "het bao nhieu", "mac khong", "gia ca", "tien san", "gia san", "bao nhieu", "gia gio vang", "re khong" },
            [ChatIntent.Directions] = new[] { "chi duong", "duong di", "ban do", "map", "google map", "dinh vi", "o dau", "dia chi", "vi tri", "di den", "di toi", "nam o dau", "cho nao", "duong nao", "di nhu the nao", "di the nao" },
            [ChatIntent.OpeningHours] = new[] { "gio mo cua", "mo cua", "dong cua", "may gio mo", "may gio dong", "gio hoat dong", "hoat dong", "mo den may gio", "gio giac", "mo luc may gio", "mo may gio" },
            [ChatIntent.Amenities] = new[] { "dich vu", "tien ich", "thue vot", "thue bong", "nuoc uong", "bai xe", "gui xe", "giu xe", "ao bib", "do an", "phong tam", "wc", "thue giay", "di kem", "thue do" },
            [ChatIntent.Reviews] = new[] { "danh gia", "review", "nhan xet", "co tot khong", "chat luong", "bao nhieu sao", "uy tin", "nguoi ta noi", "feedback", "co on khong" },
            [ChatIntent.Payment] = new[] { "thanh toan", "tra tien", "otp", "ma xac nhan", "chuyen khoan", "vietqr", "momo", "tien mat", "phuong thuc", "tra bang", "thanh toan nhu the nao", "ma otp", "khong nhan duoc otp" },
            [ChatIntent.Wallet] = new[] { "so du", "nap tien", "nap them", "internet banking", "tai khoan ngan hang", "so tai khoan", "lich su giao dich", "het tien", "vi tien", "con bao nhieu tien", "vi cua toi", "vi cua minh", "rut tien", "khong du tien", "thieu tien" },
            [ChatIntent.MyBookings] = new[] { "don cua toi", "don cua minh", "lich dat", "ve cua toi", "ve cua minh", "toi da dat", "minh da dat", "don dat", "don sap toi", "lich cua toi", "lich cua minh", "ve dien tu", "don nao", "toi dat", "minh dat", "da dat chua", "don cua em", "lich da dat" },
            [ChatIntent.CancelPolicy] = new[] { "huy", "huy don", "huy san", "hoan tien", "hoan lai", "doi lich", "doi gio", "doi ngay", "doi san", "khong di duoc", "ban dot xuat", "bao huy", "hoan bao nhieu", "duoc hoan" },
            [ChatIntent.HoldPolicy] = new[] { "giu cho", "giu san", "het han", "qua han", "tu huy", "bao lau", "giu bao lau", "15 phut", "dem nguoc" },
            [ChatIntent.Teamwork] = new[] { "ghep doi", "tim keo", "keo", "tim doi", "doi thu", "giao luu", "thieu nguoi", "tuyen nguoi", "ghep", "tim ban", "cap keo", "nhan keo", "da giao huu", "giao huu" },
            [ChatIntent.Owner] = new[] { "chu san", "dang ky san", "hop tac", "quan ly san", "kinh doanh", "mo san", "dang san", "cho thue san", "lam chu san" },
            [ChatIntent.Contact] = new[] { "lien he", "hotline", "so dien thoai", "tong dai", "email", "goi dien", "khieu nai", "gap nhan vien", "nguoi that", "admin" },
            [ChatIntent.Account] = new[] { "dang ky", "dang nhap", "quen mat khau", "doi mat khau", "tai khoan", "mat khau", "bi khoa", "tao tai khoan", "lay lai mat khau" },
            [ChatIntent.Safety] = new[] { "trung lich", "chong trung", "bi trung", "dat trung", "cung dat", "an toan", "bi lua", "bao mat" }
        };

        /// <summary>Cụm từ làm GIẢM điểm một ý định (tránh hiểu nhầm, vd "tham gia" không phải hỏi giá).</summary>
        private static readonly Dictionary<ChatIntent, string[]> NegativePhrases = new()
        {
            [ChatIntent.Price] = new[] { "tham gia", "gia nhap", "gia dinh", "gia han", "danh gia" },
            [ChatIntent.Account] = new[] { "tai khoan ngan hang", "so tai khoan" },
            [ChatIntent.Guide] = new[] { "dat san nao", "dat duoc khong" },
            [ChatIntent.Directions] = new[] { "o dau co", "choi o dau" },
            [ChatIntent.Greeting] = new[] { "chao lua" } // tên sân "Chảo Lửa"
        };

        public static Dictionary<ChatIntent, double> ScoreIntents(ParsedText t)
        {
            var scores = new Dictionary<ChatIntent, double>();
            foreach (var (intent, phrases) in IntentPhrases)
            {
                double s = phrases.Where(t.Has).Sum(p => p.Count(c => c == ' ') + 1.0);
                if (NegativePhrases.TryGetValue(intent, out var negs))
                    s -= negs.Where(t.Has).Sum(p => p.Count(c => c == ' ') + 1.0);
                if (s > 0) scores[intent] = s;
            }

            // Các tín hiệu cần nhìn chữ có dấu
            if (t.HasRawWord("ví") || t.HasRawWord("ví?")) Add(scores, ChatIntent.Wallet, 2);
            if (t.HasRawWord("mưa") || t.HasRawWord("bão")) Add(scores, ChatIntent.CancelPolicy, 2);
            if (t.HasRawWord("mấy") && t.HasRawWord("giờ") && (t.Has("mo") || t.Has("dong"))) Add(scores, ChatIntent.OpeningHours, 2);

            // Lời chào / cảm ơn chỉ tính khi câu ngắn và không hỏi gì khác
            int words = t.Folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            foreach (var smallTalk in new[] { ChatIntent.Greeting, ChatIntent.Thanks, ChatIntent.Bye })
                if (scores.ContainsKey(smallTalk) && (words > 6 || scores.Keys.Any(k => k < ChatIntent.Greeting)))
                    scores.Remove(smallTalk);
            return scores;
        }

        private static void Add(Dictionary<ChatIntent, double> d, ChatIntent i, double v) => d[i] = d.GetValueOrDefault(i) + v;

        public static ChatIntent Best(Dictionary<ChatIntent, double> scores) =>
            scores.Count == 0 ? ChatIntent.Unknown
                : scores.OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key).First().Key;

        // ============================ Ngày & giờ ============================

        /// <summary>Đọc ngày: hôm nay / mai / mốt / thứ 2..CN / cuối tuần / dd/mm[/yyyy]. Null = không nhắc tới ngày.</summary>
        public static DateOnly? ParseDate(ParsedText t, string foldedWithoutPlaces, DateOnly today)
        {
            var f = " " + foldedWithoutPlaces + " ";
            bool Has(string p) => f.Contains(" " + p + " ");

            var m = Regex.Match(f, @"\b(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?\b");
            if (m.Success)
            {
                int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
                int y = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : today.Year;
                if (y < 100) y += 2000;
                if (mo is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, mo))
                {
                    var date = new DateOnly(y, mo, d);
                    if (!m.Groups[3].Success && date < today.AddDays(-1)) date = date.AddYears(1);
                    return date;
                }
            }

            if (Has("hom nay") || Has("toi nay") || Has("chieu nay") || Has("sang nay") || Has("trua nay") || Has("dem nay") || Has("bay gio") || Has("luc nay")) return today;
            if (t.HasRawWord("mốt") || Has("ngay mot") || Has("ngay kia") || Has("hom kia")) return today.AddDays(2);
            // "mai" không dấu (người gõ không dấu) — "mái" (mái che) có dấu nên không bị nhầm; tên quận "Hoàng Mai" đã được bỏ trước
            if (Has("ngay mai") || Has("sang mai") || Has("chieu mai") || Has("toi mai") || t.HasRawWord("mai") || t.HasRawWord("mai?")) return today.AddDays(1);

            var weekday = ParseWeekday(f);
            if (weekday.HasValue)
            {
                int diff = ((int)weekday.Value - (int)today.DayOfWeek + 7) % 7;
                if (Has("tuan sau") || Has("tuan toi")) diff += diff == 0 ? 7 : 7;
                return today.AddDays(diff);
            }
            if (Has("cuoi tuan"))
            {
                int diff = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
                return today.AddDays(today.DayOfWeek == DayOfWeek.Sunday ? 0 : diff);
            }
            return null;
        }

        private static DayOfWeek? ParseWeekday(string f)
        {
            if (f.Contains(" chu nhat ")) return DayOfWeek.Sunday;
            var names = new Dictionary<string, DayOfWeek>
            {
                ["2"] = DayOfWeek.Monday, ["hai"] = DayOfWeek.Monday, ["3"] = DayOfWeek.Tuesday, ["ba"] = DayOfWeek.Tuesday,
                ["4"] = DayOfWeek.Wednesday, ["tu"] = DayOfWeek.Wednesday, ["5"] = DayOfWeek.Thursday, ["nam"] = DayOfWeek.Thursday,
                ["6"] = DayOfWeek.Friday, ["sau"] = DayOfWeek.Friday, ["7"] = DayOfWeek.Saturday, ["bay"] = DayOfWeek.Saturday
            };
            var m = Regex.Match(f, @" thu (2|3|4|5|6|7|hai|ba|tu|nam|sau|bay) ");
            return m.Success ? names[m.Groups[1].Value] : null;
        }

        /// <summary>
        /// Đọc khung giờ: "19h", "7h tối", "19:30", "17h-19h", "từ 5h đến 7h chiều", hoặc buổi: sáng / trưa / chiều / tối / đêm.
        /// Trả về (giờ bắt đầu, giờ kết thúc, có giờ cụ thể không). Null = không nhắc tới giờ.
        /// </summary>
        public static (TimeSpan From, TimeSpan To, bool Exact)? ParseTime(ParsedText t, string foldedWithoutPlaces)
        {
            var f = " " + foldedWithoutPlaces + " ";
            string? period = DetectPeriod(t, f);

            var times = new List<TimeSpan>();
            foreach (Match m in Regex.Matches(f, @"(?<![\d/])(\d{1,2})\s*(gio|h|g|:)\s*(\d{2})?(?:\s*(sang|trua|chieu|toi|dem))?(?![\d/])"))
            {
                int h = int.Parse(m.Groups[1].Value);
                int min = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                var p = m.Groups[4].Success ? m.Groups[4].Value : period;
                if (h > 24 || min > 59) continue;
                // "N giờ" có thể là thời lượng ("chơi 2 giờ", "bao nhiêu 1 giờ"): chỉ nhận là giờ bắt đầu khi có ngữ cảnh rõ
                if (m.Groups[2].Value == "gio" && !m.Groups[4].Success && h < 6)
                {
                    var before = f[..m.Index].TrimEnd();
                    if (!Regex.IsMatch(before, @"(luc|tu|den|khoang|tam|vao|-)$")) continue;
                }
                if (h < 12 && p is "chieu" or "toi" || (h < 12 && p == "dem" && h >= 6)) h += 12;
                else if (p == null && h >= 1 && h <= 5) h += 12; // "5h" khi đặt sân thường là 17h
                if (h == 24) h = 0;
                times.Add(new TimeSpan(h, min, 0));
            }

            if (times.Count >= 2 && times[1] > times[0]) return (times[0], times[1], true);
            if (times.Count >= 1) return (times[0], times[0].Add(TimeSpan.FromHours(1)), true);

            return period switch
            {
                "sang" => (new TimeSpan(5, 0, 0), new TimeSpan(11, 0, 0), false),
                "trua" => (new TimeSpan(11, 0, 0), new TimeSpan(14, 0, 0), false),
                "chieu" => (new TimeSpan(13, 0, 0), new TimeSpan(18, 0, 0), false),
                "toi" => (new TimeSpan(17, 0, 0), new TimeSpan(22, 0, 0), false),
                "dem" => (new TimeSpan(20, 0, 0), BookingRules.EndOfDay, false),
                _ => null
            };
        }

        private static string? DetectPeriod(ParsedText t, string f)
        {
            if (f.Contains(" sang nay ") || f.Contains(" sang mai ") || t.HasRawWord("sáng")) return "sang";
            if (f.Contains(" trua nay ") || t.HasRawWord("trưa")) return "trua";
            if (f.Contains(" chieu nay ") || f.Contains(" chieu mai ") || t.HasRawWord("chiều")) return "chieu";
            // "tối" có dấu khác "tôi" (ngôi thứ nhất); không dấu thì chỉ nhận "tối nay / tối mai"
            if (f.Contains(" toi nay ") || f.Contains(" toi mai ") || t.HasRawWord("tối")) return "toi";
            if (f.Contains(" dem nay ") || t.HasRawWord("đêm") || t.HasRawWord("khuya")) return "dem";
            return null;
        }

        /// <summary>Giới hạn giá: "dưới 300k", "&lt; 250.000", "tối đa 200 nghìn". Null = không có.</summary>
        public static long? ParsePriceLimit(string folded)
        {
            var m = Regex.Match(folded, @"(?:duoi|toi da|khong qua|re hon|it hon|max)\s*(\d[\d\.]*)\s*(nghin|ngan|tr|trieu|d|dong|vnd)?");
            if (!m.Success) return null;
            if (!long.TryParse(m.Groups[1].Value.Replace(".", ""), out var v)) return null;
            var unit = m.Groups[2].Value;
            if (unit is "nghin" or "ngan" || (unit == "" && v < 1000)) v *= 1000;
            if (unit is "tr" or "trieu") v *= 1_000_000;
            return v;
        }

        /// <summary>Đọc số tiền trong câu (vd "nạp 200k" → 200000).</summary>
        public static long? ParseAmount(string folded)
        {
            var m = Regex.Match(folded, @"\b(\d[\d\.]{3,})\b");
            return m.Success && long.TryParse(m.Groups[1].Value.Replace(".", ""), out var v) ? v : null;
        }
    }
}
