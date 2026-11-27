using System;
using System.Linq;
using DACN.Models;

namespace DACN.Services
{
    public static class MatchPostQueries
    {
        /// <summary>
        /// Chỉ giữ các kèo chưa tới giờ bắt đầu (ngày sau hôm nay, hoặc hôm nay nhưng giờ bắt đầu còn ở phía trước).
        /// Kèo đã qua giờ sẽ tự biến mất khỏi danh sách mà không cần xóa; dữ liệu vẫn còn trong "Kèo của tôi".
        /// </summary>
        public static IQueryable<MatchPost> NotStarted(this IQueryable<MatchPost> query)
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            var nowTime = now.TimeOfDay;
            return query.Where(p => p.MatchDate > today || (p.MatchDate == today && p.StartTime > nowTime));
        }
    }
}
