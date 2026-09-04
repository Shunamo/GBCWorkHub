using System;
using System.Collections.Generic;
using System.Globalization;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 점유·체크인·업무기록 시각은 항상 한국 표준시(UTC+9).
    /// Windows 로컬이나 해외 사이트 PC 시계에 의존하지 않는다.
    /// </summary>
    public static class KoreaTime
    {
        public const string WindowsTimeZoneId = "Korea Standard Time";

        private static readonly Dictionary<string, TimeZoneInfo> SiteZones =
            new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);

        static KoreaTime()
        {
            Zone = FindZone(WindowsTimeZoneId, TimeSpan.FromHours(9));
            RegisterSiteTimeZone("CMC", "Arabian Standard Time");
            RegisterSiteTimeZone("MNGHA", "Arab Standard Time");
            RegisterSiteTimeZone("RC", "Arab Standard Time");
            RegisterSiteTimeZone("AURORA", "US Mountain Standard Time");
        }

        public static TimeZoneInfo Zone { get; private set; }

        public static DateTime Now
        {
            get
            {
                DateTime kst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
                return DateTime.SpecifyKind(kst, DateTimeKind.Unspecified);
            }
        }

        public static DateTime Today
        {
            get { return Now.Date; }
        }

        public static void RegisterSiteTimeZone(string siteCode, string windowsTimeZoneId)
        {
            if (string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(windowsTimeZoneId))
                return;
            TimeZoneInfo zone = FindZone(windowsTimeZoneId.Trim(), TimeSpan.Zero);
            if (zone == null)
                return;
            SiteZones[siteCode.Trim().ToUpperInvariant()] = zone;
        }

        public static DateTime ToKorea(DateTime value)
        {
            DateTime utc;
            if (value.Kind == DateTimeKind.Utc)
            {
                utc = value;
            }
            else if (value.Kind == DateTimeKind.Local)
            {
                utc = value.ToUniversalTime();
            }
            else
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            }

            DateTime kst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
            return DateTime.SpecifyKind(kst, DateTimeKind.Unspecified);
        }

        public static DateTime? ToKorea(DateTime? value)
        {
            return value.HasValue ? (DateTime?)ToKorea(value.Value) : null;
        }

        public static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return value;
            if (value.Kind == DateTimeKind.Local)
                return value.ToUniversalTime();
            DateTime unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
        }

        public static DateTime ToUtcWallClock(DateTime koreaOrAny)
        {
            DateTime utc = ToUtc(koreaOrAny);
            return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Oracle TIMESTAMP(TZ 없음) 점유 시각. Utc/Local은 한국으로, Unspecified는 이미 한국 벽시계로 본다.
        /// 사이트 로컬(두바이 등)로 해석하지 않는다 — 그건 TFS 체크인만.
        /// </summary>
        public static DateTime FromOccupancyDb(DateTime value)
        {
            return ToKorea(value);
        }

        public static DateTime? FromOccupancyDb(DateTime? value)
        {
            return value.HasValue ? (DateTime?)FromOccupancyDb(value.Value) : null;
        }

        public static DateTime FromSiteLocal(string siteCode, DateTime siteLocal)
        {
            TimeZoneInfo siteZone = ResolveSiteZone(siteCode);
            if (siteZone == null || siteZone.BaseUtcOffset == Zone.BaseUtcOffset)
                return ToKorea(siteLocal);

            DateTime unspecified = DateTime.SpecifyKind(siteLocal, DateTimeKind.Unspecified);
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, siteZone);
            DateTime kst = TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
            return DateTime.SpecifyKind(kst, DateTimeKind.Unspecified);
        }

        public static DateTime? ParseToKorea(string raw, string siteCode)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            DateTime dt;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)
                && !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                return null;
            }

            if (dt.Kind == DateTimeKind.Utc || LooksLikeUtc(raw))
            {
                if (dt.Kind == DateTimeKind.Unspecified)
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return ToKorea(dt);
            }

            if (dt.Kind == DateTimeKind.Local)
                return ToKorea(dt);

            return FromSiteLocal(siteCode, dt);
        }

        public static string Format(DateTime? value, string format)
        {
            if (!value.HasValue)
                return "-";
            if (string.IsNullOrEmpty(format))
                format = "yyyy-MM-dd HH:mm:ss";
            return ToKorea(value.Value).ToString(format);
        }

        public static string ToRoundTripUtc(DateTime value)
        {
            return ToUtc(value).ToString("o", CultureInfo.InvariantCulture);
        }

        private static bool LooksLikeUtc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            string s = raw.Trim();
            if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return true;
            int t = s.IndexOf('T');
            if (t < 0)
                return false;
            return s.IndexOf('+', t) >= 0 || s.LastIndexOf('-') > t;
        }

        private static TimeZoneInfo ResolveSiteZone(string siteCode)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
                return null;
            TimeZoneInfo zone;
            if (SiteZones.TryGetValue(siteCode.Trim().ToUpperInvariant(), out zone))
                return zone;
            return null;
        }

        private static TimeZoneInfo FindZone(string windowsId, TimeSpan fallbackOffset)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch
            {
                if (fallbackOffset == TimeSpan.Zero && windowsId != WindowsTimeZoneId)
                    return null;
                TimeSpan offset = fallbackOffset == TimeSpan.Zero ? TimeSpan.FromHours(9) : fallbackOffset;
                try
                {
                    return TimeZoneInfo.CreateCustomTimeZone(
                        windowsId + "-KST",
                        offset,
                        windowsId,
                        windowsId);
                }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone(
                        "KST",
                        TimeSpan.FromHours(9),
                        "Korea Standard Time",
                        "Korea Standard Time");
                }
            }
        }
    }
}
