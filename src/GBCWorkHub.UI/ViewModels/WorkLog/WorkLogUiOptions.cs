using System;
using System.Configuration;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 업무기록 목록 Glassmorphism UI 토글.
    /// App.config: WorkLog.UseGlassmorphism = true|false
    /// 런타임에서 UseGlassmorphism 속성을 직접 바꿔도 됨(재시작 전 테스트용).
    /// false면 기존(클래식) 목록 UI로 즉시 롤백.
    /// </summary>
    public static class WorkLogUiOptions
    {
        public const string ConfigKey = "WorkLog.UseGlassmorphism";

        private static bool? _override;

        /// <summary>
        /// true: 절제된 Glass UI / false: 기존 클래식 UI.
        /// Override가 있으면 App.config보다 우선.
        /// </summary>
        public static bool UseGlassmorphism
        {
            get
            {
                if (_override.HasValue)
                    return _override.Value;
                return ReadConfigBool(ConfigKey, defaultValue: true);
            }
            set { _override = value; }
        }

        /// <summary>런타임 오버라이드를 지우고 App.config 값을 다시 따름.</summary>
        public static void ClearOverride()
        {
            _override = null;
        }

        private static bool ReadConfigBool(string key, bool defaultValue)
        {
            try
            {
                string raw = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(raw))
                    return defaultValue;

                raw = raw.Trim();
                if (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "y", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "n", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                // AppSettings 읽기 실패 시 기본값
            }

            return defaultValue;
        }
    }
}
