using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>
    /// 엑셀 업무기록 CSV/표 파서.
    /// 병합 연속 행은 Ticket Contents가 비어 있고 Type/Source만 있음 → 직전 작업에 연결.
    /// xlsx는 병합 첫 칸에만 값을 두고 나머지는 비운 채로 넘어온다 (값 복제 없음).
    /// Source Path &amp; File Name: 줄바꿈 = 각각 별도 Source (SQL 스크립트만 1건).
    /// </summary>
    public static class WorkLogExcelCsvImporter
    {
        private static readonly string[] ExpectedHeaders =
        {
            "Ticket No",
            "Ticket Contents",
            "Menu (Screen Name)",
            "PC",
            "Type",
            "Category",
            "Project Name",
            "Source Path & File Name",
            "Person in charge",
            "Start Date",
            "End Date",
            "Deployment Status",
            "Deployment Date",
            "Comment"
        };

        public static IList<WorkLogRecordDto> ParseFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("CSV 파일이 없습니다.", filePath);

            string text = ReadAllTextAutoEncoding(filePath);
            var rows = ParseCsv(text);
            return ParseTable(rows, "CSV");
        }

        /// <summary>
        /// 헤더 포함 2차원 표 → 업무기록 DTO.
        /// workBlockStartIndexes가 있으면(xlsx 병합 기준) 그 행만 새 작업으로 시작한다.
        /// </summary>
        public static IList<WorkLogRecordDto> ParseTable(
            IList<IList<string>> rows,
            string sourceOrigin = "CSV",
            ISet<int> workBlockStartIndexes = null)
        {
            if (rows == null || rows.Count == 0)
                return new List<WorkLogRecordDto>();

            int headerIndex = FindHeaderRow(rows);
            if (headerIndex < 0)
                throw new InvalidOperationException(
                    "헤더를 찾지 못했습니다. Ticket No, Type, Source Path & File Name 등이 있어야 합니다.");

            string origin = string.IsNullOrWhiteSpace(sourceOrigin) ? "CSV" : sourceOrigin.Trim();
            bool fromXlsx = origin.Equals("XLSX", StringComparison.OrdinalIgnoreCase);
            string keyPrefix = fromXlsx ? "xlsx-" : "csv-";
            bool useMergeBlocks = workBlockStartIndexes != null && workBlockStartIndexes.Count > 0;

            var col = MapColumns(rows[headerIndex]);
            var records = new List<WorkLogRecordDto>();
            WorkLogRecordDto current = null;
            string carryType = string.Empty;
            string carryCategory = string.Empty;

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                var cells = rows[i];
                if (IsEmptyRow(cells))
                    continue;

                string rawTicketNo = NormalizeTicket(Get(cells, col, "Ticket No"));
                string rawContents = Clean(Get(cells, col, "Ticket Contents"));
                string rawMenu = Clean(Get(cells, col, "Menu (Screen Name)"));
                string rawPc = Clean(Get(cells, col, "PC"));
                string type = Clean(Get(cells, col, "Type"));
                string category = Clean(Get(cells, col, "Category"));
                string project = Clean(Get(cells, col, "Project Name"));
                string sourceCell = Get(cells, col, "Source Path & File Name");
                string rawPerson = NormalizePersons(Get(cells, col, "Person in charge"));
                DateTime? rawStart = ParseDate(Get(cells, col, "Start Date"));
                DateTime? rawEnd = ParseDate(Get(cells, col, "End Date"));
                string rawDeploy = Clean(Get(cells, col, "Deployment Status"));
                DateTime? rawDeployDate = ParseDate(Get(cells, col, "Deployment Date"));
                string rawComment = Clean(Get(cells, col, "Comment"));
                bool hasDetail = !string.IsNullOrWhiteSpace(type)
                    || !string.IsNullOrWhiteSpace(category)
                    || !string.IsNullOrWhiteSpace(project)
                    || !string.IsNullOrWhiteSpace(Clean(sourceCell));

                if (string.IsNullOrWhiteSpace(rawTicketNo)
                    && string.IsNullOrWhiteSpace(rawContents)
                    && string.IsNullOrWhiteSpace(rawMenu)
                    && !hasDetail
                    && current == null)
                    continue;

                bool startNew;
                if (useMergeBlocks)
                {
                    // 엑셀 병합/기입 경계 그대로
                    startNew = current == null || workBlockStartIndexes.Contains(i);
                }
                else if (fromXlsx)
                {
                    startNew = current == null || !string.IsNullOrWhiteSpace(rawContents);
                }
                else
                {
                    startNew = current == null
                        || !string.IsNullOrWhiteSpace(rawContents)
                        || !string.IsNullOrWhiteSpace(rawTicketNo);
                    if (startNew
                        && current != null
                        && !string.IsNullOrWhiteSpace(rawTicketNo)
                        && string.IsNullOrWhiteSpace(rawContents)
                        && string.IsNullOrWhiteSpace(rawMenu)
                        && string.IsNullOrWhiteSpace(rawPc)
                        && string.IsNullOrWhiteSpace(rawPerson)
                        && hasDetail)
                        startNew = false;
                }

                if (startNew)
                {
                    current = new WorkLogRecordDto
                    {
                        ClientKey = keyPrefix + Guid.NewGuid().ToString("N").Substring(0, 12),
                        WriteStatus = "COMPLETED",
                        TicketNo = rawTicketNo,
                        TicketContents = rawContents,
                        MenuName = rawMenu,
                        PcName = rawPc,
                        PersonInCharge = rawPerson,
                        StartDate = rawStart,
                        EndDate = rawEnd,
                        DeploymentStatus = rawDeploy,
                        DeploymentDate = rawDeployDate,
                        WorkComment = rawComment,
                        NeedsTicketReview = string.IsNullOrWhiteSpace(rawTicketNo)
                    };
                    records.Add(current);
                    carryType = string.Empty;
                    carryCategory = string.Empty;
                }
                else if (current != null)
                {
                    // 같은 병합 블록의 연속 행 — 첫 행 헤더 유지, 실기입된 칸만 보충
                    // 예: 13930 블록 안 13933 → Ticket에 같이 표시
                    if (!string.IsNullOrWhiteSpace(rawTicketNo))
                        current.TicketNo = AppendMultiValue(current.TicketNo, rawTicketNo);

                    if (string.IsNullOrWhiteSpace(current.MenuName) && !string.IsNullOrWhiteSpace(rawMenu))
                        current.MenuName = rawMenu;
                    else if (!string.IsNullOrWhiteSpace(rawMenu))
                        current.MenuName = AppendMultiValue(current.MenuName, rawMenu);

                    if (string.IsNullOrWhiteSpace(current.PcName) && !string.IsNullOrWhiteSpace(rawPc))
                        current.PcName = rawPc;
                    else if (!string.IsNullOrWhiteSpace(rawPc))
                        current.PcName = AppendMultiValue(current.PcName, rawPc);

                    if (string.IsNullOrWhiteSpace(current.PersonInCharge) && !string.IsNullOrWhiteSpace(rawPerson))
                        current.PersonInCharge = rawPerson;
                    else if (!string.IsNullOrWhiteSpace(rawPerson))
                        current.PersonInCharge = AppendMultiValue(current.PersonInCharge, rawPerson);

                    if (!current.StartDate.HasValue && rawStart.HasValue)
                        current.StartDate = rawStart;
                    if (!current.EndDate.HasValue && rawEnd.HasValue)
                        current.EndDate = rawEnd;
                    if (string.IsNullOrWhiteSpace(current.DeploymentStatus) && !string.IsNullOrWhiteSpace(rawDeploy))
                        current.DeploymentStatus = rawDeploy;
                    if (!current.DeploymentDate.HasValue && rawDeployDate.HasValue)
                        current.DeploymentDate = rawDeployDate;
                    if (string.IsNullOrWhiteSpace(current.WorkComment) && !string.IsNullOrWhiteSpace(rawComment))
                        current.WorkComment = rawComment;
                }

                if (!hasDetail || current == null)
                    continue;

                string resolvedType;
                string resolvedCategory;
                ResolveTypeCategory(
                    type,
                    category,
                    project,
                    ref carryType,
                    ref carryCategory,
                    out resolvedType,
                    out resolvedCategory);

                var projectDto = FindOrAddProject(current, resolvedType, resolvedCategory, project);
                projectDto.DeploymentStatus = FirstNonEmpty(projectDto.DeploymentStatus, current.DeploymentStatus);
                projectDto.DeploymentDate = projectDto.DeploymentDate ?? current.DeploymentDate;
                projectDto.Comment = FirstNonEmpty(projectDto.Comment, current.WorkComment);
                projectDto.SourceOrigin = origin;

                foreach (var sourceLine in SplitSourceEntries(sourceCell))
                {
                    string fileName = ExtractSourceFileName(sourceLine);
                    current.Sources.Add(new WorkLogSourceDto
                    {
                        FileName = fileName,
                        OriginalPath = sourceLine,
                        ChangeType = "Edit",
                        ChangeDetail = fileName,
                        Type = resolvedType,
                        Category = resolvedCategory,
                        ProjectName = project,
                        SourceOrigin = origin,
                        IsAutoClassified = false,
                        NeedsReview = false,
                        SortOrder = current.Sources.Count
                    });
                }

                current.ChangedFileCount = current.Sources.Count;
            }

            return records;
        }

        /// <summary>
        /// 엑셀 병합 규칙:
        /// - Type 비움 → 직전 Type (Client 유지)
        /// - Category 비움 → Project명(.UI/.DTO/.BIZ/.DAC) 추론, 없으면 직전 Category
        /// - Type만 새로 기입(Server 등)되고 Category 비움 → 직전 Category를 쓰지 않고 Project로 추론
        /// </summary>
        private static void ResolveTypeCategory(
            string rawType,
            string rawCategory,
            string project,
            ref string carryType,
            ref string carryCategory,
            out string resolvedType,
            out string resolvedCategory)
        {
            bool typeProvided = !string.IsNullOrWhiteSpace(rawType);
            bool categoryProvided = !string.IsNullOrWhiteSpace(rawCategory);

            resolvedType = typeProvided ? rawType : carryType;
            string inferred = InferCategoryFromProject(project);

            if (categoryProvided)
            {
                resolvedCategory = rawCategory;
            }
            else if (!string.IsNullOrWhiteSpace(inferred))
            {
                resolvedCategory = inferred;
            }
            else if (typeProvided)
            {
                // Type이 바뀐 행인데 Category 없음 → 이전 UI 등을 끌어오지 않음
                resolvedCategory = string.Empty;
            }
            else
            {
                resolvedCategory = carryCategory;
            }

            if (!string.IsNullOrWhiteSpace(resolvedType))
                carryType = resolvedType;
            if (!string.IsNullOrWhiteSpace(resolvedCategory))
                carryCategory = resolvedCategory;
        }

        /// <summary>HIS.MC.NM.NO.OA.DTO → DTO, HIS....UI → UI 등.</summary>
        private static string InferCategoryFromProject(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return string.Empty;

            string p = project.Trim();
            string[] known =
            {
                "UI", "DTO", "BIZ", "DAC", "EQS", "Package", "Procedure", "Function", "View", "Trigger"
            };

            foreach (var k in known)
            {
                if (string.Equals(p, k, StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith("." + k, StringComparison.OrdinalIgnoreCase))
                    return k;
            }

            var parts = p.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                foreach (var k in known)
                {
                    if (string.Equals(parts[i], k, StringComparison.OrdinalIgnoreCase))
                        return k;
                }
            }

            return string.Empty;
        }

        private static string AppendMultiValue(string existing, string add)
        {
            if (string.IsNullOrWhiteSpace(add))
                return existing ?? string.Empty;
            if (string.IsNullOrWhiteSpace(existing))
                return add.Trim();

            string needle = add.Trim();
            foreach (var part in existing.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            return existing.TrimEnd() + "\n" + needle;
        }

        private static WorkLogProjectDto FindOrAddProject(
            WorkLogRecordDto record,
            string type,
            string category,
            string project)
        {
            foreach (var p in record.Projects)
            {
                if (p == null)
                    continue;
                if (string.Equals(p.Type ?? "", type ?? "", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Category ?? "", category ?? "", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.ProjectName ?? "", project ?? "", StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            var created = new WorkLogProjectDto
            {
                Type = type,
                Category = category,
                ProjectName = project,
                SortOrder = record.Projects.Count
            };
            record.Projects.Add(created);
            return created;
        }

        /// <summary>
        /// Source Path &amp; File Name 셀: 줄바꿈 = 각각 별도 Source.
        /// SQL 스크립트(CONN/INSERT/SELECT 등)만 셀 전체를 1건으로 유지.
        /// </summary>
        public static IList<string> SplitSourceEntries(string cell)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(cell))
                return result;

            string cleaned = Clean(cell);
            if (string.IsNullOrWhiteSpace(cleaned))
                return result;

            var parts = cleaned
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(Clean)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (parts.Count == 0)
                return result;

            if (parts.Count == 1)
            {
                result.Add(parts[0]);
                return result;
            }

            // SQL 본문(여러 줄 스크립트)만 하나로. 그 외 줄바꿈은 전부 개별 Source.
            if (LooksLikeSqlBlob(cleaned, parts))
            {
                result.Add(cleaned);
                return result;
            }

            result.AddRange(parts);
            return result;
        }

        private static bool LooksLikeSqlBlob(string cleaned, IList<string> parts)
        {
            if (parts == null || parts.Count == 0)
                return false;

            // 전부 파일/식별자면 SQL 아님
            if (parts.All(LooksLikeFileEntry))
                return false;

            string upper = (cleaned ?? string.Empty).ToUpperInvariant();
            if (upper.Contains("@DL_")
                || upper.Contains("CREATE OR REPLACE")
                || upper.Contains("INSERT INTO")
                || upper.Contains("UPDATE ")
                || upper.Contains("DELETE FROM")
                || upper.StartsWith("CONN ", StringComparison.Ordinal)
                || upper.StartsWith("INSERT ", StringComparison.Ordinal)
                || upper.StartsWith("SELECT ", StringComparison.Ordinal)
                || upper.StartsWith("UPDATE ", StringComparison.Ordinal)
                || upper.StartsWith("DELETE ", StringComparison.Ordinal)
                || upper.StartsWith("CREATE ", StringComparison.Ordinal)
                || upper.StartsWith("ALTER ", StringComparison.Ordinal)
                || upper.StartsWith("--", StringComparison.Ordinal))
                return true;

            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                string u = p.Trim().ToUpperInvariant();
                if (u.StartsWith("CONN ") || u.StartsWith("INSERT ") || u.StartsWith("SELECT ")
                    || u.StartsWith("UPDATE ") || u.StartsWith("DELETE ") || u.StartsWith("CREATE ")
                    || u.StartsWith("ALTER ") || u.StartsWith("WHERE ") || u.StartsWith("FROM ")
                    || u.StartsWith("BEGIN") || u.StartsWith("--") || u.Contains("@DL_"))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeFileEntry(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;
            string t = line.Trim();
            if (t.Length > 240)
                return false;
            string upper = t.ToUpperInvariant();
            if (upper.StartsWith("INSERT ") || upper.StartsWith("SELECT ") || upper.StartsWith("UPDATE ")
                || upper.StartsWith("DELETE ") || upper.StartsWith("CREATE ") || upper.StartsWith("ALTER ")
                || upper.StartsWith("WHERE ") || upper.StartsWith("FROM ") || upper.StartsWith("CONN ")
                || upper.StartsWith("--") || upper.Contains(";\n") || upper.Contains("@DL_"))
                return false;

            string[] exts = { ".SQL", ".XAML", ".CS", ".VB", ".XML", ".JSON", ".JS", ".TS", ".REB", ".TXT", ".PY", ".EQS", ".DLL" };
            foreach (var ext in exts)
            {
                if (upper.EndsWith(ext))
                    return true;
            }

            // EQS/화면 ID처럼 확장자 없는 짧은 식별자
            return t.Length <= 120 && t.IndexOf(' ') < 0 && t.IndexOf(';') < 0;
        }

        public static string ExtractSourceFileName(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return string.Empty;
            string t = pathOrName.Trim();
            // 혹시 남은 줄바꿈이면 첫 줄만
            int nl = t.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0)
                t = t.Substring(0, nl).Trim();
            int slash = Math.Max(t.LastIndexOf('/'), t.LastIndexOf('\\'));
            if (slash >= 0 && slash < t.Length - 1)
                return t.Substring(slash + 1).Trim();
            // "package xsup.pkg_xxx.sql" → 마지막 토큰
            if (t.IndexOf(' ') > 0 && t.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return tokens[tokens.Length - 1];
            }
            return t;
        }

        private static string NormalizeTicket(string raw)
        {
            // 숫자/문자/한글 티켓 모두 허용 (예: 11541, 내부, 티켓파악불가)
            string t = Clean(raw);
            if (string.IsNullOrEmpty(t))
                return string.Empty;
            while (t.StartsWith("TN-", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(3).Trim();
            // 헤더 잔여물·명백한 비티켓 제외
            if (string.Equals(t, "Ticket No", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "charge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "Date", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "Status", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return t;
        }

        private static string Clean(string value)
        {
            if (value == null)
                return string.Empty;
            string t = value.Trim();
            // 전각 공백·엑셀 깨진 날짜 표시
            t = t.Replace("　", " ").Trim();
            if (t == "-" || t == "########" || t == "#REF!" || t == "#N/A")
                return string.Empty;
            return t;
        }

        /// <summary>
        /// 담당자 여러 명(줄바꿈/쉼표/슬래시) → "정다은, 오기영" 한 줄로 저장.
        /// </summary>
        private static string NormalizePersons(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string normalized = raw.Replace("　", " ")
                .Replace('\r', '\n')
                .Replace('/', '\n')
                .Replace('|', '\n')
                .Replace(';', '\n')
                .Replace('，', '\n'); // 전각 콤마

            var names = new List<string>();
            foreach (var part in normalized.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (string.IsNullOrEmpty(name) || name == "-" || name == "########")
                    continue;
                bool exists = false;
                foreach (var n in names)
                {
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    names.Add(name);
            }

            if (names.Count == 0)
                return string.Empty;

            string joined = string.Join(", ", names);
            // DB VARCHAR2(200) 대비
            if (joined.Length > 200)
                joined = joined.Substring(0, 200);
            return joined;
        }

        private static DateTime? ParseDate(string raw)
        {
            string t = Clean(raw);
            if (string.IsNullOrEmpty(t) || t.IndexOf('#') >= 0)
                return null;

            DateTime dt;
            string[] formats =
            {
                "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
                "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy",
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"
            };
            if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt.Date;
            if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("ko-KR"), DateTimeStyles.None, out dt))
                return dt.Date;
            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt.Date;

            // Excel serial date
            double serial;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out serial)
                && serial > 20000 && serial < 80000)
            {
                try
                {
                    return DateTime.FromOADate(serial).Date;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) ? a : (b ?? string.Empty);
        }

        private static string Get(IList<string> cells, Dictionary<string, int> col, string key)
        {
            int idx;
            if (!col.TryGetValue(key, out idx) || idx < 0 || idx >= cells.Count)
                return string.Empty;
            return cells[idx] ?? string.Empty;
        }

        private static bool IsEmptyRow(IList<string> cells)
        {
            if (cells == null || cells.Count == 0)
                return true;
            return cells.All(c => string.IsNullOrWhiteSpace(Clean(c)));
        }

        private static int FindHeaderRow(IList<IList<string>> rows)
        {
            for (int i = 0; i < Math.Min(rows.Count, 5); i++)
            {
                var joined = string.Join("|", rows[i].Select(Clean));
                if (joined.IndexOf("Ticket No", StringComparison.OrdinalIgnoreCase) >= 0
                    && joined.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return rows.Count > 0 ? 0 : -1;
        }

        private static Dictionary<string, int> MapColumns(IList<string> header)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                string h = NormalizeHeader(header[i]);
                if (string.IsNullOrEmpty(h))
                    continue;

                if (ContainsAll(h, "ticket", "no") || h == "ticket no")
                    map["Ticket No"] = i;
                else if (ContainsAll(h, "ticket", "content"))
                    map["Ticket Contents"] = i;
                else if (h.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0
                         || h.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0)
                    map["Menu (Screen Name)"] = i;
                else if (h == "pc")
                    map["PC"] = i;
                else if (h == "type")
                    map["Type"] = i;
                else if (h == "category")
                    map["Category"] = i;
                else if (ContainsAll(h, "project"))
                    map["Project Name"] = i;
                else if (h.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0
                         || h.IndexOf("file name", StringComparison.OrdinalIgnoreCase) >= 0)
                    map["Source Path & File Name"] = i;
                else if (h.IndexOf("person", StringComparison.OrdinalIgnoreCase) >= 0
                         || h.IndexOf("charge", StringComparison.OrdinalIgnoreCase) >= 0)
                    map["Person in charge"] = i;
                else if (ContainsAll(h, "start", "date"))
                    map["Start Date"] = i;
                else if (ContainsAll(h, "end", "date"))
                    map["End Date"] = i;
                else if (ContainsAll(h, "deployment", "status") || ContainsAll(h, "deploy", "status"))
                    map["Deployment Status"] = i;
                else if (ContainsAll(h, "deployment", "date") || ContainsAll(h, "deploy", "date"))
                    map["Deployment Date"] = i;
                else if (h == "comment")
                    map["Comment"] = i;
            }

            // 위치 기반 fallback (엑셀 고정 순서)
            for (int i = 0; i < ExpectedHeaders.Length; i++)
            {
                if (!map.ContainsKey(ExpectedHeaders[i]) && i < header.Count)
                    map[ExpectedHeaders[i]] = i;
            }

            return map;
        }

        private static string NormalizeHeader(string header)
        {
            if (header == null)
                return string.Empty;
            string h = header.Replace("\"", string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("　", " ");
            while (h.IndexOf("  ", StringComparison.Ordinal) >= 0)
                h = h.Replace("  ", " ");
            return h.Trim();
        }

        private static bool ContainsAll(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        private static string ReadAllTextAutoEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            // UTF-8(무BOM)을 CP949로 읽으면 한글 바이트가 콤마로 깨져 컬럼이 밀림 → 유효 UTF-8 우선
            string utf8;
            if (TryDecodeUtf8Strict(bytes, out utf8))
                return utf8;

            try
            {
                return Encoding.GetEncoding(949).GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }

        private static bool TryDecodeUtf8Strict(byte[] bytes, out string text)
        {
            text = null;
            try
            {
                var utf8 = new UTF8Encoding(false, true); // throw on invalid
                text = utf8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>RFC4180 스타일 CSV (따옴표·줄바꿈 필드 지원).</summary>
        public static List<IList<string>> ParseCsv(string text)
        {
            var rows = new List<IList<string>>();
            if (string.IsNullOrEmpty(text))
                return rows;

            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }

                if (c == '\r')
                    continue;

                if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    continue;
                }

                cell.Append(c);
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}
