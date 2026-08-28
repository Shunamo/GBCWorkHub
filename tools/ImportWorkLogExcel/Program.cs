using System;
using System.IO;
using System.Threading.Tasks;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO.WorkLog;

namespace ImportWorkLogExcel
{
    /// <summary>
    /// 사용: ImportWorkLogExcel.exe &lt;xlsx/csv경로&gt; &lt;SITE_CD&gt;
    /// 예: ImportWorkLogExcel.exe "..\..\sql\해외사업운영팀 프로그램 작업내역_RC.xlsx" RC
    /// 예: ImportWorkLogExcel.exe "..\..\sql\해외사업운영팀 프로그램 작업내역_CMC.xlsx" CMC
    /// 선택: 세 번째 인자로 시트명 (기본: 자동 / CMC Dubai (2) 등)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return MainAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex);
                return 2;
            }
        }

        private static async Task<int> MainAsync(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.WriteLine("Usage: ImportWorkLogExcel.exe <file.xlsx|csv> <SITE_CD> [sheetName]");
                return 1;
            }

            string path = Path.GetFullPath(args[0]);
            string site = (args[1] ?? string.Empty).Trim().ToUpperInvariant();
            string sheet = args.Length >= 3 ? (args[2] ?? string.Empty).Trim() : null;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("File not found: " + path);
                return 1;
            }
            if (string.IsNullOrWhiteSpace(site) || site == WorkLogSiteCodes.All)
            {
                Console.Error.WriteLine("SITE_CD required (e.g. RC, AURORA, CMC)");
                return 1;
            }

            Console.WriteLine("File=" + path);
            Console.WriteLine("Site=" + site);
            if (!string.IsNullOrWhiteSpace(sheet))
                Console.WriteLine("Sheet=" + sheet);

            var biz = new WorkLogBiz();
            if (!biz.IsConfigured)
            {
                Console.Error.WriteLine("DB not configured: " + (biz.LastConnectionError ?? "GbcWorkHubDb"));
                return 1;
            }

            var records = WorkLogExcelImporter.ParseFile(path, sheet);
            if (records == null || records.Count == 0)
            {
                Console.Error.WriteLine("No records parsed.");
                return 1;
            }

            Console.WriteLine("Parsed=" + records.Count);

            int ok = 0;
            int fail = 0;
            foreach (var dto in records)
            {
                if (dto == null)
                    continue;
                dto.SiteCode = site;
                if (string.IsNullOrWhiteSpace(dto.WriteStatus))
                    dto.WriteStatus = "COMPLETED";

                bool saved = await biz.SaveAsync(dto).ConfigureAwait(false);
                if (saved)
                {
                    ok++;
                    Console.WriteLine("OK LogId=" + dto.LogId
                        + " Ticket=" + (dto.TicketNo ?? "-")
                        + " Site=" + dto.SiteCode);
                }
                else
                {
                    fail++;
                    Console.Error.WriteLine("FAIL Ticket=" + (dto.TicketNo ?? "-")
                        + " err=" + (biz.LastConnectionError ?? "unknown"));
                }
            }

            Console.WriteLine("Done ok=" + ok + " fail=" + fail);
            return fail == 0 ? 0 : 1;
        }
    }
}
