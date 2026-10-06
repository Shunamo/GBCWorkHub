using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services
{
    /// <summary>"엑셀 시트" 바로가기 핀 — 개인별 로컬 설정(%LocalAppData%\GBCWorkHub)이라
    /// DB에 저장 안 하고 공유도 안 된다. 핀한 사람 화면에서만 위로 올라와 보인다.</summary>
    public static class ExcelShortcutPinStore
    {
        private static readonly object Sync = new object();

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "PinnedExcelShortcuts.json");
            }
        }

        public static HashSet<long> LoadPinnedIds()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return new HashSet<long>();
                    string json = File.ReadAllText(StorePath);
                    var ids = JsonConvert.DeserializeObject<List<long>>(json);
                    return ids != null ? new HashSet<long>(ids) : new HashSet<long>();
                }
                catch
                {
                    return new HashSet<long>();
                }
            }
        }

        public static bool TogglePin(long shortcutId)
        {
            lock (Sync)
            {
                var ids = LoadPinnedIdsUnlocked();
                bool nowPinned;
                if (ids.Contains(shortcutId))
                {
                    ids.Remove(shortcutId);
                    nowPinned = false;
                }
                else
                {
                    ids.Add(shortcutId);
                    nowPinned = true;
                }
                SaveUnlocked(ids);
                return nowPinned;
            }
        }

        private static HashSet<long> LoadPinnedIdsUnlocked()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new HashSet<long>();
                string json = File.ReadAllText(StorePath);
                var ids = JsonConvert.DeserializeObject<List<long>>(json);
                return ids != null ? new HashSet<long>(ids) : new HashSet<long>();
            }
            catch
            {
                return new HashSet<long>();
            }
        }

        private static void SaveUnlocked(HashSet<long> ids)
        {
            try
            {
                string json = JsonConvert.SerializeObject(new List<long>(ids));
                File.WriteAllText(StorePath, json);
            }
            catch
            {
                // 로컬 설정 저장 실패로 기능 전체를 막지 않는다.
            }
        }
    }
}
