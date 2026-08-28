using System;

namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>
    /// TFS 변경 파일을 업무기록 후보로 추천할지 / 제외할지 분류.
    /// 우선순위: 제외(자동생성·임시·메타) → 추천(.cs/.xaml/.dll/.sql/.eqs/Resource) → 비후보.
    /// </summary>
    public static class TfsWorkLogCandidateClassifier
    {
        public enum Decision
        {
            /// <summary>업무기록 Source 후보로 포함.</summary>
            Recommended = 0,

            /// <summary>자동생성·임시·메타 — 업무기록에서 제외.</summary>
            Excluded = 1,

            /// <summary>추천 확장자가 아님 — 기본 제외.</summary>
            NotCandidate = 2
        }

        public sealed class Result
        {
            public Decision Decision { get; set; }
            public string RuleCode { get; set; }
            public string Reason { get; set; }
        }

        public static Result Classify(string fileName, string originalPath)
        {
            string name = string.IsNullOrWhiteSpace(fileName)
                ? ExtractFileName(originalPath)
                : fileName.Trim();
            string path = originalPath ?? string.Empty;
            string nameLower = name.ToLowerInvariant();
            string pathLower = path.Replace('\\', '/').ToLowerInvariant();

            Result excluded;
            if (TryExclude(nameLower, pathLower, out excluded))
                return excluded;

            if (IsRecommended(nameLower, pathLower))
            {
                return new Result
                {
                    Decision = Decision.Recommended,
                    RuleCode = "CAND_RECOMMENDED",
                    Reason = "업무기록 후보 확장자/Resource"
                };
            }

            return new Result
            {
                Decision = Decision.NotCandidate,
                RuleCode = "CAND_NOT_CANDIDATE",
                Reason = "업무기록 후보 확장자가 아님"
            };
        }

        public static bool IsRecommendedCandidate(string fileName, string originalPath)
        {
            return Classify(fileName, originalPath).Decision == Decision.Recommended;
        }

        private static bool TryExclude(string nameLower, string pathLower, out Result result)
        {
            // 경로 세그먼트: 빌드/캐시/패키지/IDE 메타
            if (HasPathSegment(pathLower, "obj")
                || HasPathSegment(pathLower, "bin")
                || HasPathSegment(pathLower, ".vs")
                || HasPathSegment(pathLower, "packages")
                || HasPathSegment(pathLower, "node_modules")
                || HasPathSegment(pathLower, "testdata")
                || pathLower.Contains("/__macosx/")
                || pathLower.Contains("/.git/"))
            {
                result = Excluded("CAND_EXCL_BUILD_META", "빌드/캐시/메타 경로 제외");
                return true;
            }

            // 웹서비스 참조 메타 (.disco / .wsdl / .xsd)
            if (nameLower.EndsWith(".disco", StringComparison.Ordinal)
                || nameLower.EndsWith(".wsdl", StringComparison.Ordinal)
                || nameLower.EndsWith(".xsd", StringComparison.Ordinal))
            {
                result = Excluded("CAND_EXCL_SVC_META", "웹서비스 참조 메타 파일 제외");
                return true;
            }

            // 임시·백업·OS 잡파일
            if (nameLower.EndsWith(".tmp", StringComparison.Ordinal)
                || nameLower.EndsWith(".temp", StringComparison.Ordinal)
                || nameLower.EndsWith(".bak", StringComparison.Ordinal)
                || nameLower.EndsWith(".cache", StringComparison.Ordinal)
                || nameLower.EndsWith(".log", StringComparison.Ordinal)
                || nameLower.StartsWith("~$", StringComparison.Ordinal)
                || nameLower == "thumbs.db"
                || nameLower == "desktop.ini"
                || nameLower == ".ds_store")
            {
                result = Excluded("CAND_EXCL_TEMP", "임시/백업 파일 제외");
                return true;
            }

            // 빌드 산출·디버그 심볼 (배포 DLL은 추천 대상이므로 .dll은 여기서 제외하지 않음)
            if (nameLower.EndsWith(".pdb", StringComparison.Ordinal)
                || nameLower.EndsWith(".ilk", StringComparison.Ordinal)
                || nameLower.EndsWith(".exp", StringComparison.Ordinal)
                || nameLower.EndsWith(".idb", StringComparison.Ordinal)
                || nameLower.EndsWith(".ncb", StringComparison.Ordinal)
                || nameLower.EndsWith(".suo", StringComparison.Ordinal)
                || nameLower.EndsWith(".user", StringComparison.Ordinal)
                || nameLower.EndsWith(".vspscc", StringComparison.Ordinal)
                || nameLower.EndsWith(".vssscc", StringComparison.Ordinal))
            {
                result = Excluded("CAND_EXCL_BUILD_ARTIFACT", "빌드 산출물/메타 파일 제외");
                return true;
            }

            // 프로젝트/솔루션/패키지 메타
            if (nameLower.EndsWith(".csproj", StringComparison.Ordinal)
                || nameLower.EndsWith(".vbproj", StringComparison.Ordinal)
                || nameLower.EndsWith(".fsproj", StringComparison.Ordinal)
                || nameLower.EndsWith(".sln", StringComparison.Ordinal)
                || nameLower.EndsWith(".props", StringComparison.Ordinal)
                || nameLower.EndsWith(".targets", StringComparison.Ordinal)
                || nameLower == "packages.config"
                || nameLower == "nuget.config"
                || nameLower == ".gitignore"
                || nameLower == ".gitattributes"
                || nameLower == "assemblyinfo.cs"
                || nameLower.EndsWith(".assemblyattributes.cs", StringComparison.Ordinal))
            {
                result = Excluded("CAND_EXCL_PROJECT_META", "프로젝트/설정 메타 파일 제외");
                return true;
            }

            // 자동 생성 소스
            if (nameLower.EndsWith(".g.cs", StringComparison.Ordinal)
                || nameLower.EndsWith(".g.i.cs", StringComparison.Ordinal)
                || nameLower.EndsWith(".designer.cs", StringComparison.Ordinal)
                || nameLower.EndsWith(".generated.cs", StringComparison.Ordinal)
                || nameLower.StartsWith("temporarygeneratedfile_", StringComparison.Ordinal)
                || nameLower.Contains(".designer.")
                || nameLower.EndsWith(".xaml.g.cs", StringComparison.Ordinal))
            {
                result = Excluded("CAND_EXCL_AUTOGEN", "자동 생성 파일 제외");
                return true;
            }

            result = null;
            return false;
        }

        private static bool IsRecommended(string nameLower, string pathLower)
        {
            // Resource (Global Resource / .resources.*)
            if (IsResourceFile(nameLower, pathLower))
                return true;

            if (nameLower.EndsWith(".eqs", StringComparison.Ordinal))
                return true;
            if (nameLower.EndsWith(".sql", StringComparison.Ordinal))
                return true;
            if (nameLower.EndsWith(".dll", StringComparison.Ordinal))
                return true;
            if (nameLower.EndsWith(".xaml", StringComparison.Ordinal))
                return true;
            // .xaml.cs 포함해 모든 .cs (단, 위에서 자동생성 .g.cs 등은 이미 제외)
            if (nameLower.EndsWith(".cs", StringComparison.Ordinal))
                return true;

            return false;
        }

        private static bool IsResourceFile(string nameLower, string pathLower)
        {
            if (nameLower.Contains(".resources.xml")
                || nameLower.Contains(".resources.dll")
                || nameLower.Contains("dictionary.resources")
                || nameLower.EndsWith(".resx", StringComparison.Ordinal)
                || nameLower.EndsWith(".resources", StringComparison.Ordinal))
                return true;

            // GlobalResource 폴더의 리소스 산출물
            if (pathLower.Contains("globalresource") || pathLower.Contains("global resource"))
            {
                if (nameLower.EndsWith(".xml", StringComparison.Ordinal)
                    || nameLower.EndsWith(".dll", StringComparison.Ordinal)
                    || nameLower.Contains("resource"))
                    return true;
            }

            return false;
        }

        private static Result Excluded(string rule, string reason)
        {
            return new Result
            {
                Decision = Decision.Excluded,
                RuleCode = rule,
                Reason = reason
            };
        }

        private static bool HasPathSegment(string pathLower, string segment)
        {
            if (string.IsNullOrEmpty(pathLower) || string.IsNullOrEmpty(segment))
                return false;

            string s = segment.ToLowerInvariant();
            foreach (var part in pathLower.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ExtractFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string n = path.Replace('\\', '/').TrimEnd('/');
            int idx = n.LastIndexOf('/');
            return idx >= 0 && idx < n.Length - 1 ? n.Substring(idx + 1) : n;
        }
    }
}
