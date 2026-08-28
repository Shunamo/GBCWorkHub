using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>
    /// TFS 경로/파일명 기반 Type·Category·ProjectName 결정론적 분류.
    /// 우선순위: EQS → Client UI → Server BIZ → Server DAC → DTO → Global Resource → DB Object → RebFiles → Unclassified
    /// </summary>
    public static class TfsPathClassifier
    {
        private static readonly Regex HisProjectRegex = new Regex(
            @"HIS\.[A-Za-z0-9_.]+\.(UI|DTO|BIZ|DAC)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex HisDllRegex = new Regex(
            @"^(HIS\.[A-Za-z0-9_.]+\.(UI|DTO|BIZ|DAC))\.dll$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static WorkSourceItem Classify(
            int changesetId,
            string changeType,
            string itemType,
            string fileName,
            string originalPath)
        {
            string path = originalPath ?? string.Empty;
            string name = string.IsNullOrWhiteSpace(fileName) ? ExtractFileName(path) : fileName.Trim();
            string pathNorm = NormalizePath(path);
            string nameLower = (name ?? string.Empty).ToLowerInvariant();
            string pathLower = pathNorm.ToLowerInvariant();

            var result = ClassifyCore(pathNorm, pathLower, name, nameLower);
            string project = result.ProjectName;
            if (string.IsNullOrEmpty(project))
                project = TryExtractProjectName(pathNorm, name) ?? string.Empty;

            return new WorkSourceItem
            {
                ChangesetId = changesetId,
                ChangeType = changeType ?? string.Empty,
                ItemType = itemType ?? "File",
                FileName = name,
                OriginalPath = path,
                DisplayValue = !string.IsNullOrWhiteSpace(name) ? name : path,
                SourceOrigin = SourceOrigin.Tfs,
                Confidence = result.Confidence,
                NeedsReview = result.NeedsReview,
                ReviewReason = result.ReviewReason,
                Type = result.Type ?? string.Empty,
                Category = result.Category ?? string.Empty,
                ProjectName = project,
                AppliedRuleCode = result.RuleCode
            };
        }

        private static ClassificationResult ClassifyCore(
            string pathNorm,
            string pathLower,
            string name,
            string nameLower)
        {
            // 6-1 EQS
            if (HasSegment(pathLower, "eqs")
                || nameLower.EndsWith(".eqs", StringComparison.Ordinal)
                || pathLower.Contains("/eqs/")
                || pathLower.Contains("\\eqs\\"))
            {
                return new ClassificationResult
                {
                    Type = "EQS",
                    Category = "EQS",
                    ProjectName = TryExtractEqsProject(pathNorm),
                    Confidence = ParseConfidence.High,
                    RuleCode = "TFS_EQS_PATH"
                };
            }

            // 6-2 Client UI
            if (IsClientUi(pathLower, nameLower))
            {
                return new ClassificationResult
                {
                    Type = "Client",
                    Category = "UI",
                    Confidence = ParseConfidence.High,
                    RuleCode = "TFS_CLIENT_UI"
                };
            }

            // 6-3 Server BIZ
            if (IsServerBiz(pathLower, nameLower))
            {
                return new ClassificationResult
                {
                    Type = "Server",
                    Category = "BIZ",
                    Confidence = ParseConfidence.High,
                    RuleCode = "TFS_SERVER_BIZ"
                };
            }

            // 6-4 Server DAC
            if (IsServerDac(pathLower, nameLower))
            {
                return new ClassificationResult
                {
                    Type = "Server",
                    Category = "DAC",
                    Confidence = ParseConfidence.High,
                    RuleCode = "TFS_SERVER_DAC"
                };
            }

            // 6-5 DTO
            if (IsDto(pathLower, nameLower))
            {
                string dtoType;
                string dtoRule;
                bool needsReview;
                ResolveClientOrServerType(pathLower, out dtoType, out dtoRule, out needsReview, "DTO");
                return new ClassificationResult
                {
                    Type = dtoType,
                    Category = "DTO",
                    Confidence = needsReview ? ParseConfidence.Medium : ParseConfidence.High,
                    NeedsReview = needsReview,
                    ReviewReason = needsReview ? "DTO Type(Client/Server)를 확인해야 합니다." : null,
                    RuleCode = dtoRule
                };
            }

            // 6-6 Global Resource
            if (IsGlobalResource(pathLower, nameLower))
            {
                string grType;
                string grRule;
                bool needsReview;
                ResolveClientOrServerType(pathLower, out grType, out grRule, out needsReview, "GLOBAL_RESOURCE");
                return new ClassificationResult
                {
                    Type = grType,
                    Category = "Global Resource",
                    Confidence = needsReview ? ParseConfidence.Medium : ParseConfidence.High,
                    NeedsReview = needsReview,
                    ReviewReason = needsReview ? "Global Resource Type(Client/Server)를 확인해야 합니다." : null,
                    RuleCode = grRule
                };
            }

            // 6-7 DB Object
            if (IsDbObject(pathLower, nameLower))
            {
                return ClassifyDbObject(pathLower, nameLower, name);
            }

            // 6-8 RebFiles
            if (pathLower.Contains("rebfiles") || nameLower.Contains("rebfiles"))
            {
                string rebType;
                string rebRule;
                bool needsReview;
                ResolveClientOrServerType(pathLower, out rebType, out rebRule, out needsReview, "REBFILES");
                return new ClassificationResult
                {
                    Type = rebType,
                    Category = "RebFiles",
                    Confidence = needsReview ? ParseConfidence.Medium : ParseConfidence.High,
                    NeedsReview = needsReview,
                    ReviewReason = needsReview ? "RebFiles Type(Client/Server)를 확인해야 합니다." : null,
                    RuleCode = needsReview ? "TFS_REBFILES_TYPE_UNRESOLVED" : ("TFS_" + (rebType ?? "").ToUpperInvariant() + "_REBFILES")
                };
            }

            // 6-9 Unclassified — 원본 보존
            return new ClassificationResult
            {
                Type = string.Empty,
                Category = string.Empty,
                Confidence = ParseConfidence.Low,
                NeedsReview = true,
                ReviewReason = "Type과 Category를 확인해야 합니다.",
                RuleCode = "TFS_UNCLASSIFIED"
            };
        }

        private static bool IsClientUi(string pathLower, string nameLower)
        {
            if (HasSegment(pathLower, "ui"))
                return true;
            if (nameLower.EndsWith(".xaml", StringComparison.Ordinal))
                return true;
            if (nameLower.EndsWith(".xaml.cs", StringComparison.Ordinal))
                return true;
            if (nameLower.EndsWith(".behavior.cs", StringComparison.Ordinal))
                return true;
            if (EndsWithProjectOrDll(nameLower, ".ui") || EndsWithProjectOrDll(nameLower, ".ui.dll"))
                return true;
            if (pathLower.Contains("his.deploy/client") || pathLower.Contains("his.deploy\\client"))
            {
                if (nameLower.EndsWith(".ui.dll", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool IsServerBiz(string pathLower, string nameLower)
        {
            if (HasSegment(pathLower, "biz"))
                return true;
            if (EndsWithProjectOrDll(nameLower, ".biz") || EndsWithProjectOrDll(nameLower, ".biz.dll"))
                return true;
            if ((pathLower.Contains("his.deploy/server") || pathLower.Contains("his.deploy\\server"))
                && nameLower.EndsWith(".biz.dll", StringComparison.Ordinal))
                return true;
            return false;
        }

        private static bool IsServerDac(string pathLower, string nameLower)
        {
            if (HasSegment(pathLower, "dac"))
                return true;
            if (EndsWithProjectOrDll(nameLower, ".dac") || EndsWithProjectOrDll(nameLower, ".dac.dll"))
                return true;
            if ((pathLower.Contains("his.deploy/server") || pathLower.Contains("his.deploy\\server"))
                && nameLower.EndsWith(".dac.dll", StringComparison.Ordinal))
                return true;
            return false;
        }

        private static bool IsDto(string pathLower, string nameLower)
        {
            if (HasSegment(pathLower, "dto"))
                return true;
            if (EndsWithProjectOrDll(nameLower, ".dto") || EndsWithProjectOrDll(nameLower, ".dto.dll"))
                return true;
            if (nameLower.Contains(".dto."))
                return true;
            return false;
        }

        private static bool IsGlobalResource(string pathLower, string nameLower)
        {
            if (nameLower.Contains(".resources.xml")
                || nameLower.Contains(".resources.dll")
                || nameLower.Contains("dictionary.resources"))
                return true;
            if (pathLower.Contains("globalresource")
                || pathLower.Contains("global resource")
                || HasSegment(pathLower, "globalresource")
                || HasSegment(pathLower, "resources"))
                return true;
            return false;
        }

        private static bool IsDbObject(string pathLower, string nameLower)
        {
            if (nameLower.EndsWith(".sql", StringComparison.Ordinal))
                return true;
            if (pathLower.Contains("/package")
                || pathLower.Contains("\\package")
                || pathLower.Contains("packagebody")
                || pathLower.Contains("/procedure")
                || pathLower.Contains("\\procedure")
                || pathLower.Contains("/function")
                || pathLower.Contains("\\function")
                || pathLower.Contains("/view")
                || pathLower.Contains("\\view")
                || pathLower.Contains("/trigger")
                || pathLower.Contains("\\trigger")
                || pathLower.Contains("/table")
                || pathLower.Contains("\\table")
                || pathLower.Contains("db object")
                || pathLower.Contains("dbobject"))
                return true;
            return false;
        }

        private static ClassificationResult ClassifyDbObject(string pathLower, string nameLower, string name)
        {
            string baseName = StripSqlExtension(nameLower);

            if (pathLower.Contains("packagebody")
                || HasSegment(pathLower, "package")
                || HasSegment(pathLower, "packagebody")
                || nameLower.Contains("packagebody")
                || baseName.StartsWith("pkg_", StringComparison.Ordinal))
            {
                return Db("Package", "TFS_DB_PACKAGE", ParseConfidence.High, false, null);
            }

            if (HasSegment(pathLower, "procedure")
                || baseName.StartsWith("prc_", StringComparison.Ordinal)
                || baseName.StartsWith("proc_", StringComparison.Ordinal))
            {
                return Db("Procedure", "TFS_DB_PROCEDURE", ParseConfidence.High, false, null);
            }

            if (HasSegment(pathLower, "function")
                || baseName.StartsWith("fn_", StringComparison.Ordinal)
                || baseName.StartsWith("func_", StringComparison.Ordinal))
            {
                return Db("Function", "TFS_DB_FUNCTION", ParseConfidence.High, false, null);
            }

            if (HasSegment(pathLower, "view")
                || baseName.StartsWith("vw_", StringComparison.Ordinal)
                || baseName.StartsWith("view_", StringComparison.Ordinal)
                || nameLower.StartsWith("view ", StringComparison.Ordinal)
                || nameLower.StartsWith("view.", StringComparison.Ordinal))
            {
                return Db("View", "TFS_DB_VIEW", ParseConfidence.High, false, null);
            }

            if (HasSegment(pathLower, "trigger")
                || baseName.StartsWith("trg_", StringComparison.Ordinal))
            {
                return Db("Trigger", "TFS_DB_TRIGGER", ParseConfidence.High, false, null);
            }

            if (HasSegment(pathLower, "table")
                || baseName.StartsWith("tbl_", StringComparison.Ordinal)
                || (nameLower.Contains("table") && nameLower.EndsWith(".sql", StringComparison.Ordinal)))
            {
                return Db("Table", "TFS_DB_TABLE", ParseConfidence.High, false, null);
            }

            return Db(string.Empty, "TFS_DB_CATEGORY_UNRESOLVED", ParseConfidence.Medium, true,
                "DB Object Category를 확인해야 합니다.");
        }

        private static ClassificationResult Db(
            string category,
            string rule,
            string confidence,
            bool needsReview,
            string reason)
        {
            return new ClassificationResult
            {
                Type = "DB Object",
                Category = category,
                Confidence = confidence,
                NeedsReview = needsReview,
                ReviewReason = reason,
                RuleCode = rule
            };
        }

        private static void ResolveClientOrServerType(
            string pathLower,
            out string type,
            out string ruleCode,
            out bool needsReview,
            string categoryKey)
        {
            bool isClient = IsClientPath(pathLower);
            bool isServer = IsServerPath(pathLower);

            if (isClient && !isServer)
            {
                type = "Client";
                needsReview = false;
                ruleCode = "TFS_CLIENT_" + categoryKey;
                return;
            }
            if (isServer && !isClient)
            {
                type = "Server";
                needsReview = false;
                ruleCode = "TFS_SERVER_" + categoryKey;
                return;
            }

            type = string.Empty;
            needsReview = true;
            ruleCode = "TFS_" + categoryKey + "_TYPE_UNRESOLVED";
        }

        private static bool IsClientPath(string pathLower)
        {
            if (pathLower.Contains("his.deploy/client") || pathLower.Contains("his.deploy\\client"))
                return true;
            if (HasSegment(pathLower, "client"))
                return true;
            if (HasSegment(pathLower, "ui"))
                return true;
            return false;
        }

        private static bool IsServerPath(string pathLower)
        {
            if (pathLower.Contains("his.deploy/server") || pathLower.Contains("his.deploy\\server"))
                return true;
            if (HasSegment(pathLower, "server"))
                return true;
            if (HasSegment(pathLower, "biz") || HasSegment(pathLower, "dac"))
                return true;
            return false;
        }

        /// <summary>
        /// HIS.*.UI / DTO / BIZ / DAC 형태 또는 Deploy DLL 파일명에서 ProjectName 추출.
        /// .resources.dll은 ProjectName으로 쓰지 않음.
        /// </summary>
        public static string TryExtractProjectName(string path, string fileName)
        {
            string name = fileName ?? string.Empty;
            if (name.IndexOf(".resources.", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            if (!string.IsNullOrEmpty(name))
            {
                var dllMatch = HisDllRegex.Match(name);
                if (dllMatch.Success)
                    return dllMatch.Groups[1].Value;

                // Deploy DLL: 마지막 .dll만 제거한 HIS.* 형태
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && name.StartsWith("HIS.", StringComparison.OrdinalIgnoreCase)
                    && name.IndexOf(".resources", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string withoutDll = name.Substring(0, name.Length - 4);
                    if (HisProjectRegex.IsMatch(withoutDll))
                        return withoutDll;
                }
            }

            string pathNorm = NormalizePath(path ?? string.Empty);
            var matches = HisProjectRegex.Matches(pathNorm);
            if (matches.Count > 0)
                return matches[matches.Count - 1].Value;

            // 세그먼트 단위 검사 (폴더명 임의 조합 금지 — 이미 HIS.*.(UI|DTO|BIZ|DAC) 형태만)
            foreach (var segment in SplitSegments(pathNorm))
            {
                if (HisProjectRegex.IsMatch(segment))
                    return HisProjectRegex.Match(segment).Value;
            }

            return null;
        }

        private static string TryExtractEqsProject(string pathNorm)
        {
            // 명확한 HIS 프로젝트만, 없으면 빈 값
            return TryExtractProjectName(pathNorm, ExtractFileName(pathNorm)) ?? string.Empty;
        }

        private static bool HasSegment(string pathLower, string segment)
        {
            if (string.IsNullOrEmpty(pathLower) || string.IsNullOrEmpty(segment))
                return false;
            string s = segment.ToLowerInvariant();
            foreach (var part in SplitSegments(pathLower))
            {
                if (string.Equals(part, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool EndsWithProjectOrDll(string nameLower, string suffix)
        {
            if (string.IsNullOrEmpty(nameLower) || string.IsNullOrEmpty(suffix))
                return false;
            return nameLower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripSqlExtension(string nameLower)
        {
            if (nameLower.EndsWith(".sql", StringComparison.Ordinal))
                return nameLower.Substring(0, nameLower.Length - 4);
            return nameLower;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static IEnumerable<string> SplitSegments(string pathNorm)
        {
            return pathNorm.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ExtractFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string n = path.Replace('\\', '/').TrimEnd('/');
            int idx = n.LastIndexOf('/');
            return idx >= 0 && idx < n.Length - 1 ? n.Substring(idx + 1) : n;
        }

        private sealed class ClassificationResult
        {
            public string Type { get; set; }
            public string Category { get; set; }
            public string ProjectName { get; set; }
            public string Confidence { get; set; }
            public bool NeedsReview { get; set; }
            public string ReviewReason { get; set; }
            public string RuleCode { get; set; }
        }
    }
}
