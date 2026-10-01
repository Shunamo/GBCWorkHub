namespace GBCWorkHub.DTO
{
    /// <summary>"엑셀시트" 탭 바로가기 한 줄 — XSUP.MSDWHTKD_EXCEL_SHORTCUT 한 행.</summary>
    public sealed class ExcelShortcutDto
    {
        public long ShortcutId { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string IconKey { get; set; }
        public int SortOrder { get; set; }
    }
}
