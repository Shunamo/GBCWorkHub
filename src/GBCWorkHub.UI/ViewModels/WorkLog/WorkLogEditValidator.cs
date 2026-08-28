using System;
using System.Collections.Generic;
using System.Linq;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 업무기록 편집 저장용 cross-field / tree consistency 규칙.
    /// UI(Step/Select/FieldError)는 모른다 — Issue만 반환한다.
    /// </summary>
    public static class WorkLogEditValidator
    {
        /// <summary>
        /// 저장 가능 여부. 기존 Save()와 동일하게 첫 오류에서 중단.
        /// </summary>
        public static WorkLogValidationIssue ValidateForSave(
            string siteCode,
            IEnumerable<WorkLogMenuSectionNode> menus)
        {
            if (string.IsNullOrWhiteSpace(siteCode)
                || string.Equals(siteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
            {
                return new WorkLogValidationIssue(
                    WorkLogValidationKind.MissingSite,
                    "Site",
                    "선택해 주세요.");
            }

            if (menus == null)
                return null;

            foreach (var menu in menus)
            {
                if (menu == null)
                    continue;

                if (string.IsNullOrWhiteSpace(menu.PersonInCharge))
                {
                    return new WorkLogValidationIssue(
                        WorkLogValidationKind.MissingMenuPerson,
                        "PersonInCharge",
                        "입력해 주세요.",
                        menu);
                }

                if (!menu.StartDate.HasValue)
                {
                    return new WorkLogValidationIssue(
                        WorkLogValidationKind.MissingMenuStartDate,
                        "Period",
                        "시작일을 입력해 주세요.",
                        menu);
                }

                bool anyProject = menu.Types != null
                    && menu.Types
                        .Where(t => t != null && t.Categories != null)
                        .SelectMany(t => t.Categories)
                        .Where(c => c != null && c.Projects != null)
                        .SelectMany(c => c.Projects)
                        .Any(p => p != null);

                if (!anyProject)
                {
                    return new WorkLogValidationIssue(
                        WorkLogValidationKind.MissingProject,
                        "Type",
                        "Type/Category/Project를 추가해 주세요.");
                }

                if (menu.Types == null)
                    continue;

                foreach (var t in menu.Types)
                {
                    if (t == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(t.Type) || t.Type == WorkLogFieldMasters.Unselected)
                    {
                        return new WorkLogValidationIssue(
                            WorkLogValidationKind.MissingType,
                            "Type",
                            "선택해 주세요.",
                            t);
                    }

                    if (t.Categories == null)
                        continue;

                    foreach (var c in t.Categories)
                    {
                        if (c == null || c.Projects == null)
                            continue;

                        foreach (var p in c.Projects)
                        {
                            if (p == null)
                                continue;
                            if (p.Sources == null || p.Sources.Count == 0)
                            {
                                return new WorkLogValidationIssue(
                                    WorkLogValidationKind.MissingSource,
                                    "Source",
                                    "추가해 주세요.",
                                    p);
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 선택 중인 Menu의 담당자/시작일 (IDataErrorInfo용).
        /// </summary>
        public static string ValidateMenuRequired(WorkLogMenuSectionNode menu)
        {
            if (menu == null)
                return null;
            if (string.IsNullOrWhiteSpace(menu.PersonInCharge))
                return "담당자를 입력해 주세요.";
            if (!menu.StartDate.HasValue)
                return "시작일을 입력해 주세요.";
            return null;
        }
    }

    public enum WorkLogValidationKind
    {
        MissingSite,
        MissingMenuPerson,
        MissingMenuStartDate,
        MissingProject,
        MissingType,
        MissingSource
    }

    /// <summary>첫 오류 1건. Validator는 state를 보관하지 않는다.</summary>
    public sealed class WorkLogValidationIssue
    {
        public WorkLogValidationIssue(
            WorkLogValidationKind kind,
            string fieldKey,
            string message,
            WorkLogTreeNodeBase targetNode = null)
        {
            Kind = kind;
            FieldKey = fieldKey ?? string.Empty;
            Message = message ?? string.Empty;
            TargetNode = targetNode;
        }

        public WorkLogValidationKind Kind { get; private set; }
        public string FieldKey { get; private set; }
        public string Message { get; private set; }

        /// <summary>있으면 VM이 Tree.Select에 사용. MissingSite/MissingProject는 null일 수 있음.</summary>
        public WorkLogTreeNodeBase TargetNode { get; private set; }
    }
}
