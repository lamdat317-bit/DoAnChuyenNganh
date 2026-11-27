using System;
using DACN.Models;
using DACN.Models.Enums;

namespace DACN.Services
{
    /// <summary>
    /// Quy ước cho kèo Tìm đối (đội gặp đội):
    /// CurrentSlots = số người đội chủ kèo, TotalSlots = tổng người hai đội (vd 7 đấu 7 → 7 / 14).
    /// Mỗi yêu cầu tham gia là một đội; chủ kèo chốt đúng 1 đội thì kèo chuyển sang Đã đủ (Full).
    /// </summary>
    public static class MatchTeams
    {
        /// <summary>Số người mỗi đội tối đa của kèo Tìm đối (tổng hai đội không quá 50).</summary>
        public const int MaxPlayersPerTeam = 25;

        public static bool IsOpponentSearch(this MatchPost p) => p.PostType == MatchPostType.FindOpponent;

        /// <summary>Số người đội khách mà chủ kèo mong muốn.</summary>
        public static int OpponentSize(this MatchPost p) => Math.Max(1, p.TotalSlots - p.CurrentSlots);

        /// <summary>Chuỗi đội hình kiểu "7 đấu 7".</summary>
        public static string Format(int homeSize, int awaySize) => $"{homeSize} đấu {awaySize}";

        public static string HostTeamName(string? teamName, string? captainName) =>
            !string.IsNullOrWhiteSpace(teamName) ? teamName : $"Đội của {captainName ?? "đội trưởng"}";

        public static string HostTeamName(this MatchPost p) => HostTeamName(p.TeamName, p.Creator?.FullName);

        public static string TeamDisplayName(this MatchJoinRequest r) =>
            !string.IsNullOrWhiteSpace(r.TeamName) ? r.TeamName : $"Đội của {r.User?.FullName ?? "người chơi"}";
    }
}
