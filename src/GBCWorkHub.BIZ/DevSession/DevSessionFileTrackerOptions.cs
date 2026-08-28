namespace GBCWorkHub.BIZ.DevSession
{
    public sealed class DevSessionFileTrackerOptions
    {
        public string RootPath { get; set; } = @"D:\HISSolutions";

        public string[] IncludeExtensions { get; set; } =
        {
            ".cs", ".xaml", ".sql", ".xml", ".config"
        };

        /// <summary>Any path containing one of these as a full path segment is excluded.</summary>
        public string[] ExcludeDirSegments { get; set; } =
        {
            "bin", "obj", ".vs", "packages", "TestResults"
        };

        /// <summary>Filename-suffix markers for generated files that should not count as developer edits.</summary>
        public string[] ExcludeFileSuffixes { get; set; } =
        {
            ".g.cs", ".g.i.cs", ".designer.cs", ".AssemblyInfo.cs", ".GeneratedInternalTypeHelper.g.cs"
        };
    }
}
