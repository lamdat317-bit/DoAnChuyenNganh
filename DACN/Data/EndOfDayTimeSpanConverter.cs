using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DACN.Data
{
    /// <summary>Cho phép lưu mốc 24:00 vào cột SQL kiểu time (xem ApplicationDbContext.ConfigureConventions).</summary>
    public class EndOfDayTimeSpanConverter : ValueConverter<TimeSpan, TimeSpan>
    {
        public static readonly TimeSpan EndOfDay = TimeSpan.FromDays(1);
        private static readonly TimeSpan SqlMaxTime = EndOfDay - TimeSpan.FromTicks(1);

        public EndOfDayTimeSpanConverter()
            : base(v => v >= EndOfDay ? SqlMaxTime : v,
                   v => v >= SqlMaxTime ? EndOfDay : v)
        {
        }
    }
}
