using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// PC별 게시 RDP(.rdp)를 파일명으로 찾아 실행.
    /// Downloads 고정이 아니라 사용자 폴더 전체에서 파일명 검색.
    /// 예: cpub-HOBCARE07-QuickSessionCollection-CmsRdsh.rdp
    /// </summary>
    public static class PublishedRdpLauncher
    {
        private static readonly string[] SkipDirectoryNames =
        {
            "AppData", "Application Data", "Local Settings",
            "node_modules", ".git", ".vs", "Packages",
            "INetCache", "Temp", "tmp", "Cache", "Caches",
            "$Recycle.Bin", "System Volume Information",
            "Windows", "Program Files", "Program Files (x86)"
        };

        public static LaunchResult TryFindForPc(string siteCode, string pcName)
        {
            string site = (siteCode ?? string.Empty).Trim().ToUpperInvariant();
            string pc = (pcName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(pc))
                return LaunchResult.Fail("PC 정보가 없습니다.");

            string prefix = site + "." + pc + ".";

            // 1) 전체 경로 지정
            string exactPath = (ConfigurationManager.AppSettings[prefix + "RdpFile"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(exactPath))
            {
                if (!File.Exists(exactPath))
                    return LaunchResult.Fail(
                        "설정된 RDP 파일이 없습니다.\n" + exactPath + "\n\n"
                        + BuildMissingFileMessage(pc, exactPath));
                return LaunchResult.Ok(exactPath);
            }

            // 2) 파일명 검색 — DB/설정 파일명 우선, 없으면 PC명으로 추정
            var patterns = BuildSearchPatterns(prefix, pc);
            var matches = new List<string>();
            string tried = string.Empty;

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                tried = string.IsNullOrEmpty(tried) ? pattern : tried + ", " + pattern;
                matches = FindRdpFilesByName(pattern);
                if (matches.Count > 0)
                    break;
            }

            if (matches.Count == 0)
            {
                return LaunchResult.Fail(BuildMissingFileMessage(pc, tried));
            }

            string best = matches
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .First();

            return LaunchResult.Ok(best);
        }

        public static LaunchResult TryLaunchForPc(string siteCode, string pcName)
        {
            var found = TryFindForPc(siteCode, pcName);
            if (!found.Succeeded)
                return found;
            return StartRdp(found.Path);
        }

        /// <summary>
        /// 우선순위: RdpFileName → RdpFilePattern → RdpHint → PC명 기반(cpub-{pc}-*.rdp, *{pc}*.rdp)
        /// PC명은 나중에 DB에서 오므로, 별도 파일명 설정 없이도 검색 가능.
        /// </summary>
        private static List<string> BuildSearchPatterns(string prefix, string pcName)
        {
            var list = new List<string>();

            string fileName = (ConfigurationManager.AppSettings[prefix + "RdpFileName"] ?? string.Empty).Trim();
            string pattern = (ConfigurationManager.AppSettings[prefix + "RdpFilePattern"] ?? string.Empty).Trim();
            string hint = (ConfigurationManager.AppSettings[prefix + "RdpHint"] ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(fileName))
                list.Add(NormalizeRdpPattern(fileName));
            if (!string.IsNullOrWhiteSpace(pattern))
                list.Add(NormalizeRdpPattern(pattern));
            if (!string.IsNullOrWhiteSpace(hint))
                list.Add("*" + hint + "*.rdp");

            // DB PC명만으로도 검색 (게시 RDP 관례: cpub-{PC명}-...)
            list.Add("cpub-" + pcName + "-*.rdp");
            list.Add("*" + pcName + "*.rdp");

            // 파일명만: 하이픈 없는 변형도 검색 (예: HO-BCARE-07 → cpub-HOBCARE07-*.rdp)
            // PC명 매칭/점유키 정규화와는 무관
            string compact = CompactForFileSearch(pcName);
            if (!string.IsNullOrWhiteSpace(compact)
                && !string.Equals(compact, pcName, StringComparison.OrdinalIgnoreCase))
            {
                list.Add("cpub-" + compact + "-*.rdp");
                list.Add("*" + compact + "*.rdp");
            }

            return list
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildMissingFileMessage(string pcName, string tried)
        {
            string compact = CompactForFileSearch(pcName);
            string example = !string.IsNullOrWhiteSpace(compact)
                ? "cpub-" + compact + "-QuickSessionCollection-CmsRdsh.rdp"
                : "cpub-" + (pcName ?? "PC") + "-*.rdp";

            return (pcName ?? "PC") + " 용 게시 RDP 파일이 이 PC에 없습니다.\n\n"
                + "WorkHub가 .rdp를 만들지 않습니다.\n"
                + "Forti VPN 연결 후 RC RD Web(원격 데스크톱 웹)에서 해당 PC를 누르면\n"
                + "브라우저가 게시 .rdp를 다운로드합니다. 그 파일을 그대로 쓰면 됩니다.\n\n"
                + "파일 이름 예:\n  " + example + "\n"
                + "두는 곳: 다운로드, 바탕화면, 문서\n"
                + (string.IsNullOrWhiteSpace(tried) ? "" : "검색 패턴: " + tried + "\n")
                + "받은 뒤 다시 접속하세요.";
        }

        private static string CompactForFileSearch(string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName))
                return string.Empty;
            var chars = pcName.Trim().ToCharArray();
            var sb = new System.Text.StringBuilder(chars.Length);
            foreach (char c in chars)
            {
                if (c != '-' && c != '_' && !char.IsWhiteSpace(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string NormalizeRdpPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return pattern;

            string p = pattern.Trim();
            if (!p.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase)
                && p.IndexOf('*') < 0
                && p.IndexOf('?') < 0)
            {
                p = p + ".rdp";
            }
            return p;
        }

        private static List<string> FindRdpFilesByName(string pattern)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool exactName = pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0;

            foreach (var root in GetSearchRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                // 흔한 위치 먼저 (빠름)
                foreach (var quick in GetQuickDirs(root))
                    CollectMatches(quick, pattern, exactName, recursive: false, found);

                // 없으면 사용자 폴더 전체에서 파일명 검색
                if (found.Count == 0)
                    CollectMatches(root, pattern, exactName, recursive: true, found);
            }

            return found.ToList();
        }

        private static void CollectMatches(
            string directory,
            string pattern,
            bool exactName,
            bool recursive,
            HashSet<string> found)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            if (!recursive)
            {
                TryAddFiles(directory, pattern, exactName, found);
                return;
            }

            var stack = new Stack<string>();
            stack.Push(directory);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                TryAddFiles(dir, pattern, exactName, found);

                // 이미 찾았으면 조기 종료 가능하지만, 최신 파일 고르려면 조금 더 돌림
                // 너무 느려지지 않게 매치가 많아지면 중단
                if (found.Count >= 20)
                    return;

                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var child in children)
                {
                    string name = Path.GetFileName(child) ?? string.Empty;
                    if (ShouldSkipDirectory(name))
                        continue;
                    stack.Push(child);
                }
            }
        }

        private static void TryAddFiles(string directory, string pattern, bool exactName, HashSet<string> found)
        {
            try
            {
                if (exactName)
                {
                    string path = Path.Combine(directory, pattern);
                    if (File.Exists(path))
                        found.Add(path);
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(Path.GetExtension(file), ".rdp", StringComparison.OrdinalIgnoreCase))
                        found.Add(file);
                }
            }
            catch
            {
                // 권한 등 skip
            }
        }

        private static bool ShouldSkipDirectory(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            foreach (var skip in SkipDirectoryNames)
            {
                if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> GetSearchRoots()
        {
            var roots = new List<string>();

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
                roots.Add(profile);

            try
            {
                string publicDownloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                    "..", "Downloads");
                string full = Path.GetFullPath(publicDownloads);
                if (Directory.Exists(full))
                    roots.Add(full);
            }
            catch
            {
                // skip
            }

            return roots;
        }

        private static IEnumerable<string> GetQuickDirs(string profileRoot)
        {
            yield return Path.Combine(profileRoot, "Downloads");
            yield return Path.Combine(profileRoot, "Desktop");
            yield return Path.Combine(profileRoot, "Documents");
            yield return Path.Combine(profileRoot, "OneDrive");
            yield return Path.Combine(profileRoot, "OneDrive - Documents");

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                yield return docs;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
                yield return desktop;
        }

        private static LaunchResult StartRdp(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return LaunchResult.Ok(path);
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail("RDP 실행 실패: " + ex.Message + "\n" + path);
            }
        }

        public sealed class LaunchResult
        {
            public bool Succeeded { get; private set; }
            public string Message { get; private set; }
            public string Path { get; private set; }

            public static LaunchResult Ok(string path)
            {
                return new LaunchResult
                {
                    Succeeded = true,
                    Path = path,
                    Message = "RDP를 실행했습니다."
                };
            }

            public static LaunchResult Fail(string message)
            {
                return new LaunchResult { Succeeded = false, Message = message ?? "실패" };
            }
        }
    }
}
