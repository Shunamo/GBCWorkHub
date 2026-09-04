using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    /// <summary>점유 뺏기 알림 문구. 이력 END_SOURCE=TAKEOVER 의 RESULT_MESSAGE 에 넣는다.</summary>
    public static class OccupancyTakeoverMessage
    {
        public const string EndSource = "TAKEOVER";

        public static string BuildNotice(string affiliation, string name, string siteCode, string pcName)
        {
            return BuildNotice(affiliation, name, siteCode, pcName, KoreaTime.Now);
        }

        public static string BuildNotice(string affiliation, string name, string siteCode, string pcName, DateTime at)
        {
            string when = at.ToString("MM/dd HH:mm");
            string person = string.IsNullOrWhiteSpace(name) ? "다른 사용자" : name.Trim();
            string who = string.IsNullOrWhiteSpace(affiliation)
                ? person + "님"
                : affiliation.Trim() + "팀의 " + person + "님";
            return when + "에 " + who + "께서 " + FormatTarget(siteCode, pcName) + "을 점유하셨습니다.";
        }

        public static string FormatTarget(string siteCode, string pcName)
        {
            string pc = string.IsNullOrWhiteSpace(pcName) ? "원격 PC" : pcName.Trim();
            if (string.IsNullOrWhiteSpace(siteCode))
                return pc;
            return siteCode.Trim() + " " + pc;
        }
    }
}
