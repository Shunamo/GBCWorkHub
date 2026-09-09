namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminOccupancyStatRowViewModel
    {
        public string TeamName { get; private set; }
        public int AuroraCount { get; private set; }
        public int CmcCount { get; private set; }
        public int RcCount { get; private set; }
        public int MnghaCount { get; private set; }

        public int TotalCount
        {
            get { return AuroraCount + CmcCount + RcCount + MnghaCount; }
        }

        public AdminOccupancyStatRowViewModel(string teamName, int[] siteCounts)
        {
            TeamName = string.IsNullOrWhiteSpace(teamName) ? "미지정" : teamName.Trim();
            AuroraCount = CountAt(siteCounts, 0);
            CmcCount = CountAt(siteCounts, 1);
            RcCount = CountAt(siteCounts, 2);
            MnghaCount = CountAt(siteCounts, 3);
        }

        private static int CountAt(int[] arr, int index)
        {
            if (arr == null || index < 0 || index >= arr.Length)
                return 0;
            return arr[index];
        }
    }
}
