using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.CsvImportTool
{
    /// <summary>
    /// UI 없이 CSV/XLSX → DB 검증용.
    /// 기본: 파싱만 (dry-run). --save 일 때만 INSERT.
    /// xlsx는 기본 Aurora 시트 (--sheet 로 변경 가능).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string filePath = null;
                string sheetName = WorkLogExcelXlsxImporter.DefaultSheetName;
                int max = 10;
                bool save = false;
                bool all = false;
                bool replace = false;
                var ticketFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (string.Equals(a, "--save", StringComparison.OrdinalIgnoreCase))
                        save = true;
                    else if (string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase))
                        all = true;
                    else if (string.Equals(a, "--replace", StringComparison.OrdinalIgnoreCase))
                        replace = true;
                    else if (string.Equals(a, "--sheet", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        sheetName = args[++i];
                    else if (string.Equals(a, "--ticket", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        foreach (var t in args[++i].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string ticketKey = t.Trim();
                            if (ticketKey.Length > 0)
                                ticketFilter.Add(ticketKey);
                        }
                    }
                    else if (string.Equals(a, "--max", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        int.TryParse(args[++i], out max);
                    else if (!a.StartsWith("-", StringComparison.Ordinal))
                        filePath = a;
                }

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    string sqlDir = Path.GetFullPath(Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\sql"));
                    string xlsx = Directory.Exists(sqlDir)
                        ? Directory.GetFiles(sqlDir, "*aurora*.xlsx").FirstOrDefault()
                        : null;
                    if (string.IsNullOrEmpty(xlsx) && Directory.Exists(sqlDir))
                        xlsx = Directory.GetFiles(sqlDir, "*.xlsx").FirstOrDefault();
                    string utf8 = Directory.Exists(sqlDir)
                        ? Directory.GetFiles(sqlDir, "*_utf8.csv").FirstOrDefault()
                        : null;
                    filePath = !string.IsNullOrEmpty(xlsx)
                        ? xlsx
                        : (!string.IsNullOrEmpty(utf8)
                            ? utf8
                            : Path.Combine(sqlDir, "sample_worklog_import.csv"));
                }

                if (!File.Exists(filePath))
                {
                    Console.WriteLine("파일 없음: " + filePath);
                    Console.WriteLine("사용법: GBCWorkHub.CsvImportTool.exe [csv|xlsx경로] [--sheet Aurora] [--all|--max N] [--save] [--replace]");
                    return 2;
                }

                bool isXlsx = filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase);

                Console.WriteLine("FILE : " + filePath);
                if (isXlsx)
                    Console.WriteLine("SHEET: " + sheetName);
                Console.WriteLine("MAX  : " + (all || max <= 0 ? "ALL" : max.ToString()));
                Console.WriteLine("MODE : " + (save ? "SAVE (DB INSERT)" : "DRY-RUN (파싱만)")
                    + (replace ? " + REPLACE(기존 전체 삭제)" : ""));
                Console.WriteLine();

                IList<WorkLogRecordDto> allRecords = WorkLogExcelImporter.ParseFile(
                    filePath, isXlsx ? sheetName : null);
                IEnumerable<WorkLogRecordDto> query = allRecords;
                if (ticketFilter.Count > 0)
                {
                    query = query.Where(r =>
                    {
                        if (r == null || string.IsNullOrWhiteSpace(r.TicketNo))
                            return false;
                        foreach (var part in r.TicketNo.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (ticketFilter.Contains(part.Trim()))
                                return true;
                        }
                        return ticketFilter.Contains(r.TicketNo);
                    });
                }
                var records = (all || max <= 0 || ticketFilter.Count > 0)
                    ? query.ToList()
                    : query.Take(Math.Max(1, max)).ToList();
                Console.WriteLine("파싱 결과: 전체 " + allRecords.Count + "건 → 처리 " + records.Count + "건"
                    + (ticketFilter.Count > 0 ? " (ticket filter)" : ""));
                Console.WriteLine(new string('-', 72));

                bool verbose = records.Count <= 30;
                int idx = 0;
                foreach (var r in records)
                {
                    idx++;
                    if (!verbose && idx > 3 && idx < records.Count - 2)
                    {
                        if (idx == 4)
                            Console.WriteLine("  ... (중간 " + (records.Count - 5) + "건 생략) ...");
                        continue;
                    }

                    Console.WriteLine(string.Format(
                        "[{0}] Ticket={1} Menu={2} PC={3} Person={4}",
                        idx,
                        r.TicketNo ?? "-",
                        Trunc(r.MenuName, 40),
                        r.PcName ?? "-",
                        r.PersonInCharge ?? "-"));
                    Console.WriteLine(string.Format(
                        "     Start={0} End={1} Deploy={2}/{3} Projects={4} Sources={5}",
                        Fmt(r.StartDate),
                        Fmt(r.EndDate),
                        r.DeploymentStatus ?? "-",
                        Fmt(r.DeploymentDate),
                        r.Projects != null ? r.Projects.Count : 0,
                        r.Sources != null ? r.Sources.Count : 0));
                    if (verbose)
                        Console.WriteLine("     Contents=" + Trunc(r.TicketContents, 60));
                    Console.WriteLine();
                }

                if (!save)
                {
                    Console.WriteLine("dry-run 끝. DB에 전체 넣으려면:");
                    Console.WriteLine("  GBCWorkHub.CsvImportTool.exe \"" + filePath + "\" --sheet Aurora --all --replace --save");
                    return 0;
                }

                var biz = new WorkLogBiz();
                if (!biz.IsConfigured)
                {
                    Console.WriteLine("실패: App.config GbcWorkHubDb 미설정");
                    return 3;
                }

                if (replace)
                {
                    Console.WriteLine("기존 업무기록 전체 삭제 중...");
                    int deleted = biz.DeleteAllAsync().GetAwaiter().GetResult();
                    if (deleted < 0)
                    {
                        Console.WriteLine("삭제 실패: " + (biz.LastConnectionError ?? "?"));
                        return 4;
                    }
                    Console.WriteLine("삭제 완료 (영향 행 합계≈" + deleted + ")");
                    Console.WriteLine();
                }

                int ok = 0;
                int fail = 0;
                int n = 0;
                foreach (var r in records)
                {
                    n++;
                    bool saved = biz.SaveAsync(r).GetAwaiter().GetResult();
                    if (saved)
                    {
                        ok++;
                        if (ok <= 5 || ok % 50 == 0 || n == records.Count)
                            Console.WriteLine("OK  [" + n + "/" + records.Count + "] LOG_ID=" + r.LogId
                                + " Ticket=" + (r.TicketNo ?? "-"));
                    }
                    else
                    {
                        fail++;
                        Console.WriteLine("FAIL [" + n + "/" + records.Count + "] Ticket="
                            + (r.TicketNo ?? "-") + " err=" + (biz.LastConnectionError ?? "?"));
                    }
                }

                Console.WriteLine();
                Console.WriteLine("저장 완료: ok=" + ok + " fail=" + fail + " / " + records.Count);
                Console.WriteLine("Oracle: SELECT COUNT(*) FROM XSUP.MSDWHTKD_WRK;");
                return fail > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine(ex);
                return 9;
            }
        }

        private static string Fmt(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "-";
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "-";
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length <= max)
                return t;
            return t.Substring(0, max) + "...";
        }
    }
}
