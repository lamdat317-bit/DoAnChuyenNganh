using System;
using System.Collections.Generic;
using System.Linq;
using DACN.Models;

namespace DACN.Services
{
    /// <summary>
    /// Giữ bảng giá khung giờ của sân con luôn khớp với giờ hoạt động của cụm sân:
    /// khi tạo sân / đổi giờ mở cửa, khung giá nằm ngoài giờ hoạt động bị cắt bỏ, phần giờ mới chưa có giá được bổ sung.
    /// </summary>
    public static class TimeSlotPlanner
    {
        public const decimal DefaultPrice = 180000m;

        /// <summary>Khung giá mặc định phủ toàn bộ giờ hoạt động (mỗi đoạn hoạt động một khung).</summary>
        public static List<TimeSlot> DefaultSlots(int courtId, Facility facility, decimal price = DefaultPrice)
            => BookingRules.OpenSegments(facility)
                .Select(s => new TimeSlot { CourtId = courtId, StartTime = s.Start, EndTime = s.End, Price = price, IsPeakHour = false })
                .ToList();

        /// <summary>
        /// Đặt giá riêng cho khoảng [start, end) trên danh sách khung giá chưa lưu (vd khung giờ cao điểm lúc tạo sân).
        /// Khoảng giờ được cắt theo giờ hoạt động; các khung cũ bị chồng lấn được cắt / tách đôi / bỏ
        /// để mỗi thời điểm chỉ thuộc 1 khung giá. Trả về false nếu khoảng giờ nằm ngoài giờ hoạt động.
        /// </summary>
        public static bool ApplyPriceRange(List<TimeSlot> slots, int courtId, Facility facility,
            TimeSpan start, TimeSpan end, decimal price, bool isPeakHour)
        {
            bool applied = false;
            foreach (var (segStart, segEnd) in BookingRules.OpenSegments(facility))
            {
                var s = start > segStart ? start : segStart;
                var e = end < segEnd ? end : segEnd;
                if (e <= s) continue;
                applied = true;

                foreach (var old in slots.Where(x => x.StartTime < e && x.EndTime > s).ToList())
                {
                    bool keepsLeft = old.StartTime < s, keepsRight = old.EndTime > e;
                    if (keepsLeft && keepsRight)
                    {
                        slots.Add(new TimeSlot { CourtId = courtId, StartTime = e, EndTime = old.EndTime, Price = old.Price, IsPeakHour = old.IsPeakHour });
                        old.EndTime = s;
                    }
                    else if (keepsLeft) old.EndTime = s;
                    else if (keepsRight) old.StartTime = e;
                    else slots.Remove(old);
                }
                slots.Add(new TimeSlot { CourtId = courtId, StartTime = s, EndTime = e, Price = price, IsPeakHour = isPeakHour });
            }
            return applied;
        }

        /// <summary>
        /// Điều chỉnh các khung giá của 1 sân theo giờ hoạt động mới.
        /// Trả về (số khung bị xóa/cắt, số khung được thêm). Thay đổi được ghi thẳng vào <paramref name="slots"/>
        /// (khung cần xóa nằm trong <paramref name="toRemove"/>, khung mới trong <paramref name="toAdd"/>).
        /// </summary>
        public static (int Trimmed, int Added) Normalize(int courtId, ICollection<TimeSlot> slots, Facility facility,
            List<TimeSlot> toRemove, List<TimeSlot> toAdd)
        {
            var segments = BookingRules.OpenSegments(facility);
            int trimmed = 0, added = 0;
            var fallbackPrice = slots.Any() ? slots.OrderBy(s => s.Price).First().Price : DefaultPrice;

            // 1. Cắt các khung giá về phần giao với giờ hoạt động (1 khung có thể giao 2 đoạn -> tách đôi)
            foreach (var slot in slots.ToList())
            {
                var parts = segments
                    .Select(seg => (Start: slot.StartTime > seg.Start ? slot.StartTime : seg.Start,
                                    End: slot.EndTime < seg.End ? slot.EndTime : seg.End))
                    .Where(p => p.End > p.Start)
                    .ToList();

                if (parts.Count == 0)
                {
                    toRemove.Add(slot);
                    trimmed++;
                    continue;
                }

                if (parts[0].Start != slot.StartTime || parts[0].End != slot.EndTime) trimmed++;
                slot.StartTime = parts[0].Start;
                slot.EndTime = parts[0].End;
                foreach (var extra in parts.Skip(1))
                {
                    toAdd.Add(new TimeSlot { CourtId = courtId, StartTime = extra.Start, EndTime = extra.End, Price = slot.Price, IsPeakHour = slot.IsPeakHour });
                }
            }

            // 2. Bổ sung khung giá cho phần giờ hoạt động chưa được phủ
            var covered = slots.Where(s => !toRemove.Contains(s)).Concat(toAdd)
                .Select(s => (s.StartTime, s.EndTime)).OrderBy(s => s.StartTime).ToList();
            foreach (var (segStart, segEnd) in segments)
            {
                var cursor = segStart;
                foreach (var c in covered.Where(c => c.EndTime > segStart && c.StartTime < segEnd).OrderBy(c => c.StartTime))
                {
                    if (c.StartTime > cursor)
                    {
                        toAdd.Add(new TimeSlot { CourtId = courtId, StartTime = cursor, EndTime = c.StartTime, Price = fallbackPrice });
                        added++;
                    }
                    if (c.EndTime > cursor) cursor = c.EndTime;
                }
                if (cursor < segEnd)
                {
                    toAdd.Add(new TimeSlot { CourtId = courtId, StartTime = cursor, EndTime = segEnd, Price = fallbackPrice });
                    added++;
                }
            }
            return (trimmed, added);
        }
    }
}
