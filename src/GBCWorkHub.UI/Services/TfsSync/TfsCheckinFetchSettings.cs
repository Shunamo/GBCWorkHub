using System;
using System.IO;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// 원격 종료 후 「체크인 내역 가져오기」 자동 팝업 on/off.
    /// 수동 TFS 가져오기·보관함은 이 설정과 무관.
    /// </summary>
    public static class TfsCheckinFetchSettings
    {
        private sealed class FileDto
        {
            public bool Enabled { get; set; }
        }

        private static readonly object Sync = new object();
        private static bool? _cached;

        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub",
                    "tfs-checkin-fetch.json");
            }
        }

        /// <summary>기본값 true — 기존 동작 유지.</summary>
        public static bool IsEnabled
        {
            get
            {
                lock (Sync)
                {
                    if (_cached.HasValue)
                        return _cached.Value;
                    _cached = LoadUnlocked();
                    return _cached.Value;
                }
            }
            set
            {
                lock (Sync)
                {
                    _cached = value;
                    SaveUnlocked(value);
                }
                DiagnosticLogger.Info("TFS_FETCH_SETTING", "enabled=" + value);
            }
        }

        private static bool LoadUnlocked()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return true;
                var dto = JsonConvert.DeserializeObject<FileDto>(File.ReadAllText(FilePath));
                if (dto == null)
                    return true;
                return dto.Enabled;
            }
            catch
            {
                return true;
            }
        }

        private static void SaveUnlocked(bool enabled)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(
                    FilePath,
                    JsonConvert.SerializeObject(new FileDto { Enabled = enabled }, Formatting.Indented));
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_FETCH_SETTING", "save failed: " + ex.Message);
            }
        }
    }
}
