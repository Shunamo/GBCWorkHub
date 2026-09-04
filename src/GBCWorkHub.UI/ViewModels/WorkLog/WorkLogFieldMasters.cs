using System.Collections.ObjectModel;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 업무기록 UI 고정 코드값 Master. XAML 하드코딩 대신 여기서만 공급.
    /// </summary>
    public static class WorkLogFieldMasters
    {
        public const string Unselected = "미선택";
        public const string EmptyCategory = "";

        public const string TreePlaceholderMenu = "Menu";
        public const string TreePlaceholderType = "Type";
        public const string TreePlaceholderCategory = "Category";
        public const string TreePlaceholderProject = "Project";
        public const string TreePlaceholderSource = "Source code";

        public static ObservableCollection<string> CreateTypes()
        {
            return new ObservableCollection<string>
            {
                Unselected,
                "Client",
                "Server",
                "EQS",
                "DB Object",
                "RebFiles",
                "Table",
                "Code"
            };
        }

        public static ObservableCollection<string> CreateDeploymentStatuses()
        {
            return new ObservableCollection<string>
            {
                Unselected,
                WorkLogDeployStatusLabels.LocalPc,
                WorkLogDeployStatusLabels.Rollback,
                WorkLogDeployStatusLabels.Staging,
                WorkLogDeployStatusLabels.Production
            };
        }

        /// <summary>작성용 사이트 (전체 제외).</summary>
        public static ObservableCollection<string> CreateSiteCodes()
        {
            return new ObservableCollection<string>
            {
                WorkLogSiteCodes.Aurora,
                WorkLogSiteCodes.Rc,
                WorkLogSiteCodes.Cmc,
                WorkLogSiteCodes.Mngha
            };
        }

        public const string TicketInternal = "내부";
        public const string TicketUnregistered = "미등록";

        public static ObservableCollection<string> CategoriesForType(string type)
        {
            // 빈 Category("")도 정상 선택으로 허용
            var list = new ObservableCollection<string> { EmptyCategory };
            if (string.IsNullOrWhiteSpace(type) || type == Unselected)
                return list;

            switch (type)
            {
                case "EQS":
                    list.Add("EQS");
                    break;
                case "DB Object":
                    list.Add("Package");
                    list.Add("Procedure");
                    list.Add("Function");
                    list.Add("View");
                    list.Add("Table");
                    list.Add("Trigger");
                    break;
                case "Client":
                    list.Add("UI");
                    list.Add("DTO");
                    list.Add("Global Resource");
                    list.Add("Code");
                    list.Add("RebFiles");
                    break;
                case "Server":
                    list.Add("BIZ");
                    list.Add("DAC");
                    list.Add("DTO");
                    list.Add("Global Resource");
                    list.Add("Code");
                    list.Add("RebFiles");
                    break;
                case "RebFiles":
                case "Table":
                case "Code":
                    // 빈 값 허용 — Unselected만
                    break;
            }
            return list;
        }

        public static bool AllowsEmptyCategory(string type)
        {
            if (string.IsNullOrWhiteSpace(type) || type == Unselected)
                return true;
            return type == "RebFiles" || type == "Table" || type == "Code"
                || type == "EQS" || type == "DB Object" || type == "Client" || type == "Server";
        }

        public static bool IsCategoryValidForType(string type, string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == Unselected)
                return AllowsEmptyCategory(type);
            var allowed = CategoriesForType(type);
            foreach (var c in allowed)
            {
                if (string.Equals(c, category, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool RequiresCategoryHint(string type, string category)
        {
            // Client/Server만 Category 권장. Table/Code/EQS/DB Object 등은 비움 정상.
            if (!string.Equals(type, "Client", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "Server", System.StringComparison.OrdinalIgnoreCase))
                return false;
            return string.IsNullOrWhiteSpace(category) || category == Unselected;
        }

        /// <summary>
        /// Project Name 미기입 허용.
        /// DB Object/EQS/Table/Code/RebFiles 전체, Client·Server의 Global Resource·Code·RebFiles 등.
        /// </summary>
        public static bool AllowsEmptyProjectName(string type, string category)
        {
            if (string.IsNullOrWhiteSpace(type) || type == Unselected)
                return true;

            if (string.Equals(type, "EQS", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "DB Object", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Table", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Code", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "RebFiles", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(type, "Client", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Server", System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(category, "Global Resource", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(category, "Code", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(category, "RebFiles", System.StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool RequiresProjectNameHint(string type, string category, string projectName)
        {
            if (AllowsEmptyProjectName(type, category))
                return false;
            return string.IsNullOrWhiteSpace(projectName);
        }
    }
}
