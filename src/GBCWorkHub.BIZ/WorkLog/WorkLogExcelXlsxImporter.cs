using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>
    /// 원본 .xlsx의 <b>셀 병합 범위</b>를 작업 경계로 사용한다.
    /// 병합된 값은 첫 칸에만 두고 아래 칸에 복제하지 않으며,
    /// 같은 병합 블록의 Type/Source 행만 한 업무기록으로 연결한다.
    /// </summary>
    public static class WorkLogExcelXlsxImporter
    {
        public const string DefaultSheetName = "Aurora";

        public static IList<WorkLogRecordDto> ParseFile(string filePath, string sheetName = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("엑셀 파일이 없습니다.", filePath);

            SheetTable table = ReadSheetAsTable(filePath, sheetName);
            return WorkLogExcelCsvImporter.ParseTable(table.Rows, "XLSX", table.WorkBlockStartIndexes);
        }

        public static SheetTable ReadSheetAsTable(string filePath, string sheetName)
        {
            using (var workbook = new XLWorkbook(filePath))
            {
                IXLWorksheet worksheet = ResolveWorksheet(workbook, sheetName);

                int lastRow = worksheet.LastRowUsed() != null
                    ? worksheet.LastRowUsed().RowNumber()
                    : 0;
                int lastCol = worksheet.LastColumnUsed() != null
                    ? worksheet.LastColumnUsed().ColumnNumber()
                    : 0;
                if (lastCol < 14)
                    lastCol = 14;

                // 병합 연속 칸(첫 셀 제외) → 값은 비우고, 블록 연결만 한다
                var mergeContinuation = new HashSet<long>();
                var mergeFirst = new Dictionary<long, long>(); // cell -> first cell pack
                foreach (var mergedRange in worksheet.MergedRanges)
                {
                    var first = mergedRange.RangeAddress.FirstAddress;
                    long firstPack = Pack(first.RowNumber, first.ColumnNumber);
                    foreach (var cell in mergedRange.Cells())
                    {
                        long pack = Pack(cell.Address.RowNumber, cell.Address.ColumnNumber);
                        mergeFirst[pack] = firstPack;
                        if (pack != firstPack)
                            mergeContinuation.Add(pack);
                    }
                }

                // 1) 표 읽기 (병합 연속 칸은 빈 문자열 — 값 채우기 없음)
                var rows = new List<IList<string>>();
                var excelRowOfTableIndex = new List<int>();
                for (int row = 1; row <= lastRow; row++)
                {
                    var line = new List<string>(lastCol);
                    bool any = false;
                    for (int col = 1; col <= lastCol; col++)
                    {
                        string value;
                        if (mergeContinuation.Contains(Pack(row, col)))
                            value = string.Empty;
                        else
                            value = SafeCellText(worksheet.Cell(row, col));

                        if (!string.IsNullOrWhiteSpace(value))
                            any = true;
                        line.Add(value);
                    }
                    if (any || row == 1)
                    {
                        rows.Add(line);
                        excelRowOfTableIndex.Add(row);
                    }
                }

                if (rows.Count == 0)
                    return new SheetTable(rows, new HashSet<int>());

                // 2) 헤더로 Ticket/Contents 열 위치 파악
                int headerIndex = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    string joined = string.Join("|", rows[i]);
                    if (joined.IndexOf("Ticket No", StringComparison.OrdinalIgnoreCase) >= 0
                        && joined.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        headerIndex = i;
                        break;
                    }
                }

                int ticketCol = FindHeaderColumn(rows[headerIndex], "ticket", "no") + 1; // 1-based
                int contentsCol = FindHeaderColumn(rows[headerIndex], "ticket", "content") + 1;
                if (ticketCol <= 0) ticketCol = 1;
                if (contentsCol <= 0) contentsCol = 2;

                // 3) 엑셀 병합 기준으로 작업 블록 시작 행(테이블 인덱스) 표시
                var blockStarts = new HashSet<int>();
                for (int ti = headerIndex + 1; ti < rows.Count; ti++)
                {
                    int excelRow = excelRowOfTableIndex[ti];
                    if (IsWorkBlockStart(worksheet, mergeFirst, mergeContinuation, excelRow, ticketCol, contentsCol))
                        blockStarts.Add(ti);
                }

                return new SheetTable(rows, blockStarts);
            }
        }

        /// <summary>
        /// Contents(또는 Ticket) 병합의 첫 행 / 비병합 실값 행 = 새 작업.
        /// Contents 병합 연속 행에 Ticket만 있으면 새 작업이 아님(값 복제·분리 안 함).
        /// </summary>
        private static bool IsWorkBlockStart(
            IXLWorksheet worksheet,
            Dictionary<long, long> mergeFirst,
            HashSet<long> mergeContinuation,
            int excelRow,
            int ticketCol,
            int contentsCol)
        {
            long contentsPack = Pack(excelRow, contentsCol);
            long ticketPack = Pack(excelRow, ticketCol);

            bool contentsIsContinuation = mergeContinuation.Contains(contentsPack);
            if (contentsIsContinuation)
                return false; // 같은 Contents 병합 블록 안

            bool contentsIsMergeFirst = mergeFirst.ContainsKey(contentsPack)
                && mergeFirst[contentsPack] == contentsPack;
            string contentsText = SafeCellText(worksheet.Cell(excelRow, contentsCol));
            if (contentsIsMergeFirst || !string.IsNullOrWhiteSpace(contentsText))
                return true; // Contents 병합 시작 또는 단독 기입

            // Contents가 비어 있고 병합 연속도 아님 → Ticket 병합/기입이 있으면 새 블록
            bool ticketIsContinuation = mergeContinuation.Contains(ticketPack);
            if (ticketIsContinuation)
                return false;

            bool ticketIsMergeFirst = mergeFirst.ContainsKey(ticketPack)
                && mergeFirst[ticketPack] == ticketPack;
            string ticketText = SafeCellText(worksheet.Cell(excelRow, ticketCol));
            return ticketIsMergeFirst || !string.IsNullOrWhiteSpace(ticketText);
        }

        private static int FindHeaderColumn(IList<string> header, params string[] needles)
        {
            for (int i = 0; i < header.Count; i++)
            {
                string h = (header[i] ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                bool ok = true;
                foreach (var n in needles)
                {
                    if (h.IndexOf(n, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return i;
            }
            return -1;
        }

        private static string SafeCellText(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty())
                return string.Empty;

            try
            {
                if (cell.DataType == XLDataType.DateTime)
                    return cell.GetDateTime().ToString("yyyy-MM-dd");
                return cell.GetFormattedString().Trim();
            }
            catch
            {
                try { return (cell.GetString() ?? string.Empty).Trim(); }
                catch { return string.Empty; }
            }
        }

        private static long Pack(int row, int col)
        {
            return ((long)row << 16) | (uint)col;
        }

        public sealed class SheetTable
        {
            public SheetTable(IList<IList<string>> rows, ISet<int> workBlockStartIndexes)
            {
                Rows = rows ?? new List<IList<string>>();
                WorkBlockStartIndexes = workBlockStartIndexes ?? new HashSet<int>();
            }

            public IList<IList<string>> Rows { get; private set; }
            /// <summary>Rows 인덱스. 엑셀 병합/기입 기준으로 새 작업이 시작되는 행.</summary>
            public ISet<int> WorkBlockStartIndexes { get; private set; }
        }

        /// <summary>
        /// 시트명 지정 시 그 시트. 없으면 Aurora → RCHSP → RC → CMC → 첫 시트 순.
        /// </summary>
        private static IXLWorksheet ResolveWorksheet(XLWorkbook workbook, string sheetName)
        {
            if (workbook == null || workbook.Worksheets.Count == 0)
                throw new InvalidOperationException("엑셀에 시트가 없습니다.");

            var names = workbook.Worksheets.Select(ws => ws.Name).ToList();
            IXLWorksheet worksheet;

            if (!string.IsNullOrWhiteSpace(sheetName)
                && workbook.Worksheets.TryGetWorksheet(sheetName.Trim(), out worksheet))
                return worksheet;

            string[] preferred =
            {
                sheetName,
                DefaultSheetName,
                "RCHSP",
                "RC",
                "Aurora",
                "CMC",
                "CMC Dubai",
                "CMC Dubai (2)"
            };
            foreach (string name in preferred)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (workbook.Worksheets.TryGetWorksheet(name.Trim(), out worksheet))
                    return worksheet;
            }

            // 대소문자 무시 매칭
            foreach (string name in preferred)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                worksheet = workbook.Worksheets.FirstOrDefault(ws =>
                    string.Equals(ws.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (worksheet != null)
                    return worksheet;
            }

            // 이름에 CMC 포함 시트 우선
            worksheet = workbook.Worksheets.FirstOrDefault(ws =>
                ws.Name != null
                && ws.Name.IndexOf("CMC", StringComparison.OrdinalIgnoreCase) >= 0);
            if (worksheet != null)
                return worksheet;

            if (workbook.Worksheets.Count == 1)
                return workbook.Worksheet(1);

            throw new InvalidOperationException(
                "시트를 찾지 못했습니다: '" + (sheetName ?? DefaultSheetName) + "'. 사용 가능: "
                + string.Join(", ", names));
        }
    }

    /// <summary>확장자에 따라 CSV / XLSX 파서를 고른다.</summary>
    public static class WorkLogExcelImporter
    {
        public static IList<WorkLogRecordDto> ParseFile(string filePath, string xlsxSheetName = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("파일 경로가 없습니다.", "filePath");

            string ext = Path.GetExtension(filePath) ?? string.Empty;
            if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
                return WorkLogExcelXlsxImporter.ParseFile(filePath, xlsxSheetName);

            return WorkLogExcelCsvImporter.ParseFile(filePath);
        }
    }
}
