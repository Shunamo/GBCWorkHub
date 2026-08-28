using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GBCWorkHub.BIZ.WorkLog;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    public enum WorkLogTreeNodeKind
    {
        Menu,
        Type,
        Category,
        Project
    }

    /// <summary>
    /// 실제 WorkLogListItem + Sources로부터 만든 UI용 Tree Projection.
    /// Mock이 아니며 저장 시 원본 모델에 반영한다.
    /// </summary>
    public sealed class WorkLogTicketTreeViewModel : ViewModelBase
    {
        private WorkLogTreeNodeBase _selectedNode;

        public WorkLogListItemViewModel SourceItem { get; private set; }
        public ObservableCollection<WorkLogMenuSectionNode> MenuSections { get; private set; }

        public WorkLogTicketTreeViewModel()
        {
            MenuSections = new ObservableCollection<WorkLogMenuSectionNode>();
        }

        public WorkLogTreeNodeBase SelectedNode
        {
            get { return _selectedNode; }
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    RaisePropertyChanged("HasSelection");
                    RaisePropertyChanged("IsMenuSelected");
                    RaisePropertyChanged("IsTypeSelected");
                    RaisePropertyChanged("IsCategorySelected");
                    RaisePropertyChanged("IsProjectSelected");
                    RaisePropertyChanged("SelectedMenu");
                    RaisePropertyChanged("SelectedTypeNode");
                    RaisePropertyChanged("SelectedCategoryNode");
                    RaisePropertyChanged("SelectedProject");
                }
            }
        }

        public bool HasSelection { get { return SelectedNode != null; } }
        public bool IsMenuSelected { get { return SelectedNode is WorkLogMenuSectionNode; } }
        public bool IsTypeSelected { get { return SelectedNode is WorkLogTypeNode; } }
        public bool IsCategorySelected { get { return SelectedNode is WorkLogCategoryNode; } }
        public bool IsProjectSelected { get { return SelectedNode is WorkLogProjectNode; } }

        public WorkLogMenuSectionNode SelectedMenu { get { return SelectedNode as WorkLogMenuSectionNode; } }
        public WorkLogTypeNode SelectedTypeNode { get { return SelectedNode as WorkLogTypeNode; } }
        public WorkLogCategoryNode SelectedCategoryNode { get { return SelectedNode as WorkLogCategoryNode; } }
        public WorkLogProjectNode SelectedProject { get { return SelectedNode as WorkLogProjectNode; } }

        public static WorkLogTicketTreeViewModel FromListItem(WorkLogListItemViewModel item, bool expandTypesOnly)
        {
            var tree = new WorkLogTicketTreeViewModel { SourceItem = item };
            if (item == null)
                return tree;

            // expandTypesOnly=true: Menu만 펼침(상세 미리보기)
            // expandTypesOnly=false: 편집 목업처럼 Menu~Project까지 펼침
            bool expandDeep = !expandTypesOnly;

            var menu = new WorkLogMenuSectionNode
            {
                MenuName = string.IsNullOrWhiteSpace(item.MenuName) ? string.Empty : item.MenuName,
                Pc = item.Pc ?? string.Empty,
                PersonInCharge = item.PersonInCharge ?? string.Empty,
                StartDate = item.StartDate,
                EndDate = item.EndDate,
                IsExpanded = true
            };

            var sources = item.Sources ?? new ObservableCollection<WorkLogSourceEditItem>();
            var byType = sources.GroupBy(s => (s.Type ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var tg in byType.OrderBy(g => g.Key))
            {
                var typeNode = new WorkLogTypeNode
                {
                    Type = tg.Key,
                    IsExpanded = expandDeep
                };

                var byCat = tg.GroupBy(s => (s.Category ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
                foreach (var cg in byCat.OrderBy(g => g.Key))
                {
                    var catNode = new WorkLogCategoryNode
                    {
                        Type = tg.Key,
                        Category = cg.Key,
                        IsExpanded = expandDeep
                    };

                    var byProj = cg.GroupBy(s => (s.ProjectName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
                    foreach (var pg in byProj.OrderBy(g => g.Key))
                    {
                        var groupMeta = item.Groups != null
                            ? item.Groups.FirstOrDefault(g =>
                                string.Equals(g.Type ?? "", tg.Key, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(g.Category ?? "", cg.Key, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(g.ProjectName ?? "", pg.Key, StringComparison.OrdinalIgnoreCase))
                            : null;

                        var project = new WorkLogProjectNode
                        {
                            ProjectName = pg.Key,
                            Type = tg.Key,
                            Category = cg.Key,
                            DeploymentStatus = groupMeta != null && !string.IsNullOrWhiteSpace(groupMeta.DeploymentStatus)
                                ? groupMeta.DeploymentStatus
                                : (item.DeploymentStatus ?? string.Empty),
                            DeploymentDate = groupMeta != null && groupMeta.DeploymentDate.HasValue
                                ? groupMeta.DeploymentDate
                                : item.DeploymentDate,
                            Comment = groupMeta != null && !string.IsNullOrWhiteSpace(groupMeta.Comment)
                                ? groupMeta.Comment
                                : (item.Comment ?? string.Empty),
                            IsExpanded = expandDeep
                        };

                        foreach (var s in pg)
                        {
                            foreach (var expanded in ExpandSourceEntries(s))
                                project.Sources.Add(expanded);
                        }

                        catNode.Projects.Add(project);
                    }

                    typeNode.Categories.Add(catNode);
                }

                menu.Types.Add(typeNode);
            }

            tree.MenuSections.Add(menu);
            return tree;
        }

        /// <summary>Tree 편집 결과를 원본 ListItem에 반영.</summary>
        public void ApplyTo(WorkLogListItemViewModel target)
        {
            if (target == null)
                return;

            var menu = MenuSections.FirstOrDefault();
            if (menu != null)
            {
                target.MenuName = menu.MenuName ?? string.Empty;
                target.Pc = menu.Pc ?? string.Empty;
                target.PersonInCharge = WorkLogDraftMapper.NormalizePersonStorage(menu.PersonInCharge);
                target.StartDate = menu.StartDate;
                target.EndDate = menu.EndDate;
            }

            target.Sources.Clear();
            string firstDeploy = null;
            DateTime? firstDeployDate = null;
            string firstComment = null;

            foreach (var m in MenuSections)
            {
                foreach (var t in m.Types)
                {
                    foreach (var c in t.Categories)
                    {
                        foreach (var p in c.Projects)
                        {
                            if (firstDeploy == null && !string.IsNullOrWhiteSpace(p.DeploymentStatus))
                            {
                                firstDeploy = p.DeploymentStatus;
                                firstDeployDate = p.DeploymentDate;
                            }
                            if (firstComment == null && !string.IsNullOrWhiteSpace(p.Comment))
                                firstComment = p.Comment;

                            foreach (var s in p.Sources)
                            {
                                s.Type = NormalizeMaster(t.Type);
                                s.Category = NormalizeMaster(c.Category);
                                s.ProjectName = p.ProjectName ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(s.ChangeDetailText))
                                    s.ChangeDetailText = s.FileName ?? string.Empty;
                                // DisplayText 역할 — 사용자 편집값. TFS FileName/OriginalPath는 유지.
                                // 줄바꿈이 남아 있으면 Source를 각각 분리
                                foreach (var expanded in ExpandSourceEntries(s))
                                    target.Sources.Add(expanded);
                            }
                        }
                    }
                }
            }

            // 기존 평면 모델 호환: Ticket 레벨 배포/코멘트는 첫 Project 값으로 유지
            if (firstDeploy != null)
                target.DeploymentStatus = firstDeploy;
            if (firstDeployDate.HasValue)
                target.DeploymentDate = firstDeployDate;
            if (firstComment != null)
                target.Comment = firstComment;

            WorkLogDraftMapper.RebuildGroupsFromSources(target);

            // Project별 배포/코멘트를 Group에 보존
            foreach (var m in MenuSections)
            {
                foreach (var t in m.Types)
                {
                    foreach (var c in t.Categories)
                    {
                        foreach (var p in c.Projects)
                        {
                            var g = target.Groups.FirstOrDefault(x =>
                                string.Equals(x.Type ?? "", t.Type ?? "", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Category ?? "", c.Category ?? "", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.ProjectName ?? "", p.ProjectName ?? "", StringComparison.OrdinalIgnoreCase));
                            if (g != null)
                            {
                                g.DeploymentStatus = p.DeploymentStatus;
                                g.DeploymentDate = p.DeploymentDate;
                                g.Comment = p.Comment;
                            }
                        }
                    }
                }
            }

            WorkLogListItemViewModel.RebuildTypeBadges(target);
        }

        private static string NormalizeMaster(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == WorkLogFieldMasters.Unselected)
                return string.Empty;
            return value.Trim();
        }

        public static WorkLogSourceEditItem CloneSource(WorkLogSourceEditItem s)
        {
            if (s == null)
                return new WorkLogSourceEditItem();
            return new WorkLogSourceEditItem
            {
                FileName = s.FileName,
                OriginalPath = s.OriginalPath,
                ChangeType = s.ChangeType,
                SourceOrigin = s.SourceOrigin,
                AppliedRuleCode = s.AppliedRuleCode,
                IsAutoClassified = s.IsAutoClassified,
                ChangeDetailText = s.ChangeDetailText,
                Type = s.Type,
                Category = s.Category,
                ProjectName = s.ProjectName,
                NeedsReview = s.NeedsReview,
                ReviewReason = s.ReviewReason,
                IsChecked = s.IsChecked,
                IsActive = s.IsActive,
                RecordedAt = s.RecordedAt
            };
        }

        /// <summary>
        /// ChangeDetail/OriginalPath에 줄바꿈이 있으면 줄마다 Source 행으로 펼친다.
        /// (SQL 스크립트는 SplitSourceEntries가 1건으로 유지)
        /// </summary>
        public static IEnumerable<WorkLogSourceEditItem> ExpandSourceEntries(WorkLogSourceEditItem source)
        {
            if (source == null)
                yield break;

            string raw = !string.IsNullOrWhiteSpace(source.ChangeDetailText)
                ? source.ChangeDetailText
                : (!string.IsNullOrWhiteSpace(source.OriginalPath)
                    ? source.OriginalPath
                    : (source.FileName ?? string.Empty));

            var parts = WorkLogExcelCsvImporter.SplitSourceEntries(raw);
            if (parts == null || parts.Count == 0)
            {
                var one = CloneSource(source);
                if (string.IsNullOrWhiteSpace(one.ChangeDetailText))
                {
                    one.ChangeDetailText = !string.IsNullOrWhiteSpace(one.FileName)
                        ? one.FileName
                        : (one.OriginalPath ?? string.Empty);
                }
                yield return one;
                yield break;
            }

            if (parts.Count == 1)
            {
                var one = CloneSource(source);
                string line = parts[0];
                string fileName = WorkLogExcelCsvImporter.ExtractSourceFileName(line);
                if (string.IsNullOrWhiteSpace(one.FileName))
                    one.FileName = fileName;
                if (string.IsNullOrWhiteSpace(one.OriginalPath))
                    one.OriginalPath = line;
                if (string.IsNullOrWhiteSpace(one.ChangeDetailText)
                    || one.ChangeDetailText.IndexOf('\n') >= 0
                    || one.ChangeDetailText.IndexOf('\r') >= 0)
                    one.ChangeDetailText = fileName;
                yield return one;
                yield break;
            }

            foreach (var line in parts)
            {
                string fileName = WorkLogExcelCsvImporter.ExtractSourceFileName(line);
                var clone = CloneSource(source);
                clone.OriginalPath = line;
                clone.FileName = fileName;
                clone.ChangeDetailText = fileName;
                yield return clone;
            }
        }

        /// <summary>
        /// Project의 Type/Category 변경 시 동일 Menu 아래에서 즉시 재배치.
        /// Project/Source 인스턴스는 유지한다.
        /// </summary>
        public bool RelocateProject(WorkLogProjectNode project, string newType, string newCategory)
        {
            if (project == null)
                return false;

            newType = NormalizeMaster(newType);
            newCategory = NormalizeMaster(newCategory);
            if (!WorkLogFieldMasters.IsCategoryValidForType(
                    string.IsNullOrEmpty(newType) ? WorkLogFieldMasters.Unselected : newType,
                    newCategory))
                newCategory = string.Empty;

            WorkLogMenuSectionNode ownerMenu = null;
            WorkLogTypeNode ownerType = null;
            WorkLogCategoryNode ownerCat = null;

            foreach (var m in MenuSections)
            {
                foreach (var t in m.Types)
                {
                    foreach (var c in t.Categories)
                    {
                        if (c.Projects.Contains(project))
                        {
                            ownerMenu = m;
                            ownerType = t;
                            ownerCat = c;
                            break;
                        }
                    }
                    if (ownerMenu != null)
                        break;
                }
                if (ownerMenu != null)
                    break;
            }

            if (ownerMenu == null || ownerCat == null)
                return false;

            bool sameBucket =
                string.Equals(NormalizeMaster(ownerType.Type), newType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeMaster(ownerCat.Category), newCategory, StringComparison.OrdinalIgnoreCase);

            project.Type = newType;
            project.Category = newCategory;
            foreach (var s in project.Sources)
            {
                s.Type = newType;
                s.Category = newCategory;
                s.ProjectName = project.ProjectName ?? string.Empty;
            }

            if (sameBucket)
                return true;

            ownerCat.Projects.Remove(project);
            CleanupEmpty(ownerMenu, ownerType, ownerCat);

            var targetType = FindOrCreateType(ownerMenu, newType);
            var targetCat = FindOrCreateCategory(targetType, newCategory);
            targetCat.Projects.Add(project);
            targetType.IsExpanded = true;
            targetCat.IsExpanded = true;
            ownerMenu.IsExpanded = true;
            return true;
        }

        /// <summary>Type 노드 이름 변경. 형제 있으면 merge. 하위 Category 유효성 검사.</summary>
        public bool ApplyTypeNodeRename(WorkLogTypeNode typeNode, string newType)
        {
            if (typeNode == null)
                return false;
            newType = NormalizeMaster(newType);

            WorkLogMenuSectionNode ownerMenu = null;
            foreach (var m in MenuSections)
            {
                if (m.Types.Contains(typeNode))
                {
                    ownerMenu = m;
                    break;
                }
            }
            if (ownerMenu == null)
                return false;

            // 유효하지 않은 Category 정리
            foreach (var c in typeNode.Categories)
            {
                c.Type = newType;
                if (!WorkLogFieldMasters.IsCategoryValidForType(
                    string.IsNullOrEmpty(newType) ? WorkLogFieldMasters.Unselected : newType,
                    c.Category))
                {
                    c.Category = string.Empty;
                    foreach (var p in c.Projects)
                    {
                        p.Type = newType;
                        p.Category = string.Empty;
                        foreach (var s in p.Sources)
                        {
                            s.Type = newType;
                            s.Category = string.Empty;
                        }
                    }
                }
                else
                {
                    foreach (var p in c.Projects)
                    {
                        p.Type = newType;
                        foreach (var s in p.Sources)
                            s.Type = newType;
                    }
                }
            }

            var sibling = ownerMenu.Types.FirstOrDefault(t =>
                t != typeNode
                && string.Equals(NormalizeMaster(t.Type), newType, StringComparison.OrdinalIgnoreCase));

            if (sibling != null)
            {
                foreach (var c in typeNode.Categories.ToList())
                {
                    typeNode.Categories.Remove(c);
                    var targetCat = FindOrCreateCategory(sibling, NormalizeMaster(c.Category));
                    foreach (var p in c.Projects.ToList())
                    {
                        c.Projects.Remove(p);
                        p.Type = newType;
                        p.Category = targetCat.Category;
                        targetCat.Projects.Add(p);
                    }
                }
                ownerMenu.Types.Remove(typeNode);
                sibling.IsExpanded = true;
                Select(sibling);
            }
            else
            {
                typeNode.Type = newType;
            }
            return true;
        }

        public bool ApplyCategoryNodeRename(WorkLogCategoryNode catNode, string newCategory)
        {
            if (catNode == null)
                return false;
            newCategory = NormalizeMaster(newCategory);

            WorkLogMenuSectionNode ownerMenu = null;
            WorkLogTypeNode ownerType = null;
            foreach (var m in MenuSections)
            {
                foreach (var t in m.Types)
                {
                    if (t.Categories.Contains(catNode))
                    {
                        ownerMenu = m;
                        ownerType = t;
                        break;
                    }
                }
                if (ownerType != null)
                    break;
            }
            if (ownerType == null)
                return false;

            if (!WorkLogFieldMasters.IsCategoryValidForType(
                string.IsNullOrEmpty(ownerType.Type) ? WorkLogFieldMasters.Unselected : ownerType.Type,
                newCategory))
                newCategory = string.Empty;

            var sibling = ownerType.Categories.FirstOrDefault(c =>
                c != catNode
                && string.Equals(NormalizeMaster(c.Category), newCategory, StringComparison.OrdinalIgnoreCase));

            if (sibling != null)
            {
                foreach (var p in catNode.Projects.ToList())
                {
                    catNode.Projects.Remove(p);
                    p.Category = newCategory;
                    foreach (var s in p.Sources)
                        s.Category = newCategory;
                    sibling.Projects.Add(p);
                }
                ownerType.Categories.Remove(catNode);
                sibling.IsExpanded = true;
                Select(sibling);
            }
            else
            {
                catNode.Category = newCategory;
                foreach (var p in catNode.Projects)
                {
                    p.Category = newCategory;
                    foreach (var s in p.Sources)
                        s.Category = newCategory;
                }
            }
            return true;
        }

        private static WorkLogTypeNode FindOrCreateType(WorkLogMenuSectionNode menu, string type)
        {
            var existing = menu.Types.FirstOrDefault(t =>
                string.Equals(NormalizeMaster(t.Type), type, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;
            var node = new WorkLogTypeNode { Type = type, IsExpanded = true };
            menu.Types.Add(node);
            return node;
        }

        private static WorkLogCategoryNode FindOrCreateCategory(WorkLogTypeNode typeNode, string category)
        {
            var existing = typeNode.Categories.FirstOrDefault(c =>
                string.Equals(NormalizeMaster(c.Category), category, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;
            var node = new WorkLogCategoryNode
            {
                Type = typeNode != null ? typeNode.Type : string.Empty,
                Category = category,
                IsExpanded = true
            };
            typeNode.Categories.Add(node);
            return node;
        }

        private static void CleanupEmpty(
            WorkLogMenuSectionNode menu,
            WorkLogTypeNode typeNode,
            WorkLogCategoryNode catNode)
        {
            if (catNode != null && catNode.Projects.Count == 0 && typeNode != null)
                typeNode.Categories.Remove(catNode);
            if (typeNode != null && typeNode.Categories.Count == 0 && menu != null)
                menu.Types.Remove(typeNode);
        }

        public void Select(WorkLogTreeNodeBase node)
        {
            ClearSelectionFlags();
            SelectedNode = node;
            if (node != null)
                node.IsSelected = true;
        }

        private void ClearSelectionFlags()
        {
            foreach (var m in MenuSections)
            {
                m.IsSelected = false;
                foreach (var t in m.Types)
                {
                    t.IsSelected = false;
                    foreach (var c in t.Categories)
                    {
                        c.IsSelected = false;
                        foreach (var p in c.Projects)
                            p.IsSelected = false;
                    }
                }
            }
        }
    }

    public abstract class WorkLogTreeNodeBase : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isSelected;
        private bool _showActions;
        private bool _isTreeVisible = true;

        public abstract WorkLogTreeNodeKind Kind { get; }
        public abstract string Title { get; }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public bool ShowActions
        {
            get { return _showActions; }
            set { SetProperty(ref _showActions, value); }
        }

        /// <summary>트리 검색 필터 결과. false면 행 숨김.</summary>
        public bool IsTreeVisible
        {
            get { return _isTreeVisible; }
            set { SetProperty(ref _isTreeVisible, value); }
        }
    }

    public sealed class WorkLogMenuSectionNode : WorkLogTreeNodeBase
    {
        private string _menuName;
        private string _pc;
        private string _personInCharge;
        private DateTime? _startDate;
        private DateTime? _endDate;
        private string _startDateInput;
        private string _endDateInput;

        public WorkLogMenuSectionNode()
        {
            Types = new ObservableCollection<WorkLogTypeNode>();
        }

        public override WorkLogTreeNodeKind Kind { get { return WorkLogTreeNodeKind.Menu; } }

        public ObservableCollection<WorkLogTypeNode> Types { get; private set; }

        public string MenuName
        {
            get { return _menuName; }
            set
            {
                if (SetProperty(ref _menuName, value))
                    RaisePropertyChanged("Title");
            }
        }

        public string Pc
        {
            get { return _pc; }
            set
            {
                if (SetProperty(ref _pc, value))
                    RaisePropertyChanged("MetaLine");
            }
        }

        public string PersonInCharge
        {
            get { return _personInCharge ?? string.Empty; }
            set
            {
                // 입력 중 정규화하지 않음(콤마 유지). 저장 시에만 NormalizePersonStorage.
                if (SetProperty(ref _personInCharge, value ?? string.Empty))
                    RaisePropertyChanged("MetaLine");
            }
        }

        public DateTime? StartDate
        {
            get { return _startDate; }
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    _startDateInput = WorkLogDraftMapper.FormatDateText(value);
                    RaisePropertyChanged("StartDateText");
                    RaisePropertyChanged("PeriodDisplay");
                }
            }
        }

        public DateTime? EndDate
        {
            get { return _endDate; }
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    _endDateInput = WorkLogDraftMapper.FormatDateText(value);
                    RaisePropertyChanged("EndDateText");
                    RaisePropertyChanged("PeriodDisplay");
                }
            }
        }

        public string StartDateText
        {
            get
            {
                return _startDateInput != null
                    ? _startDateInput
                    : WorkLogDraftMapper.FormatDateText(StartDate);
            }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                _startDateInput = masked;
                RaisePropertyChanged("StartDateText");
                if (string.IsNullOrEmpty(masked))
                {
                    _startDate = null;
                    RaisePropertyChanged("StartDate");
                    RaisePropertyChanged("PeriodDisplay");
                    return;
                }
                DateTime? parsed;
                string err;
                if (masked.Length == 10 && WorkLogDraftMapper.TryParseDateText(masked, out parsed, out err))
                {
                    _startDate = parsed;
                    RaisePropertyChanged("StartDate");
                    RaisePropertyChanged("PeriodDisplay");
                }
            }
        }

        public string EndDateText
        {
            get
            {
                return _endDateInput != null
                    ? _endDateInput
                    : WorkLogDraftMapper.FormatDateText(EndDate);
            }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                _endDateInput = masked;
                RaisePropertyChanged("EndDateText");
                if (string.IsNullOrEmpty(masked))
                {
                    _endDate = null;
                    RaisePropertyChanged("EndDate");
                    RaisePropertyChanged("PeriodDisplay");
                    return;
                }
                DateTime? parsed;
                string err;
                if (masked.Length == 10 && WorkLogDraftMapper.TryParseDateText(masked, out parsed, out err))
                {
                    _endDate = parsed;
                    RaisePropertyChanged("EndDate");
                    RaisePropertyChanged("PeriodDisplay");
                }
            }
        }

        public override string Title
        {
            get { return string.IsNullOrWhiteSpace(MenuName) ? "메뉴 미입력" : MenuName; }
        }

        public string MetaLine
        {
            get
            {
                return (string.IsNullOrWhiteSpace(Pc) ? "-" : Pc)
                    + " · "
                    + (string.IsNullOrWhiteSpace(PersonInCharge)
                        ? "-"
                        : WorkLogDraftMapper.FormatPersonDisplay(PersonInCharge));
            }
        }

        public string PeriodDisplay
        {
            get
            {
                string a = StartDate.HasValue ? StartDate.Value.ToString("yyyy-MM-dd") : "----";
                string b = EndDate.HasValue ? EndDate.Value.ToString("yyyy-MM-dd") : "";
                return string.IsNullOrEmpty(b) ? a : a + " ~ " + b;
            }
        }
    }

    public sealed class WorkLogTypeNode : WorkLogTreeNodeBase
    {
        private string _type;

        public WorkLogTypeNode()
        {
            Categories = new ObservableCollection<WorkLogCategoryNode>();
        }

        public override WorkLogTreeNodeKind Kind { get { return WorkLogTreeNodeKind.Type; } }
        public ObservableCollection<WorkLogCategoryNode> Categories { get; private set; }

        public string Type
        {
            get { return _type; }
            set
            {
                if (SetProperty(ref _type, value))
                    RaisePropertyChanged("Title");
            }
        }

        public override string Title
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Type)
                    || string.Equals(Type, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                    return WorkLogFieldMasters.Unselected;
                return Type;
            }
        }

    }

    public sealed class WorkLogCategoryNode : WorkLogTreeNodeBase
    {
        private string _type;
        private string _category;

        public WorkLogCategoryNode()
        {
            Projects = new ObservableCollection<WorkLogProjectNode>();
        }

        public override WorkLogTreeNodeKind Kind { get { return WorkLogTreeNodeKind.Category; } }
        public ObservableCollection<WorkLogProjectNode> Projects { get; private set; }

        public string Type
        {
            get { return _type; }
            set
            {
                if (SetProperty(ref _type, value))
                {
                    RaisePropertyChanged("Title");
                    RaisePropertyChanged("IsPlaceholderTitle");
                    RaisePropertyChanged("HasDisplayTitle");
                }
            }
        }

        public string Category
        {
            get { return _category; }
            set
            {
                if (SetProperty(ref _category, value))
                {
                    RaisePropertyChanged("Title");
                    RaisePropertyChanged("IsPlaceholderTitle");
                    RaisePropertyChanged("HasDisplayTitle");
                }
            }
        }

        public override string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Category)
                    && !string.Equals(Category, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                    return Category;
                // Table/EQS 등 빈 Category 정상 — Client/Server만 플레이스홀더
                return IsPlaceholderTitle ? WorkLogFieldMasters.Unselected : string.Empty;
            }
        }

        /// <summary>Client/Server에서 Category 비움일 때만 연한 플레이스홀더.</summary>
        public bool IsPlaceholderTitle
        {
            get { return WorkLogFieldMasters.RequiresCategoryHint(Type, Category); }
        }

        public bool HasDisplayTitle
        {
            get { return !string.IsNullOrWhiteSpace(Title); }
        }
    }

    public sealed class WorkLogProjectNode : WorkLogTreeNodeBase
    {
        private string _projectName;
        private string _type;
        private string _category;
        private string _deploymentStatus;
        private DateTime? _deploymentDate;
        private string _deploymentDateInput;
        private string _comment;

        public WorkLogProjectNode()
        {
            Sources = new ObservableCollection<WorkLogSourceEditItem>();
        }

        public override WorkLogTreeNodeKind Kind { get { return WorkLogTreeNodeKind.Project; } }
        public ObservableCollection<WorkLogSourceEditItem> Sources { get; private set; }

        public string ProjectName
        {
            get { return _projectName; }
            set
            {
                if (SetProperty(ref _projectName, value))
                {
                    RaisePropertyChanged("Title");
                    RaisePropertyChanged("IsPlaceholderTitle");
                    RaisePropertyChanged("HasDisplayTitle");
                }
            }
        }

        public string Type
        {
            get { return _type; }
            set
            {
                if (SetProperty(ref _type, value))
                {
                    RaisePropertyChanged("Title");
                    RaisePropertyChanged("IsPlaceholderTitle");
                    RaisePropertyChanged("HasDisplayTitle");
                }
            }
        }

        public string Category
        {
            get { return _category; }
            set
            {
                if (SetProperty(ref _category, value))
                {
                    RaisePropertyChanged("Title");
                    RaisePropertyChanged("IsPlaceholderTitle");
                    RaisePropertyChanged("HasDisplayTitle");
                }
            }
        }

        public string DeploymentStatus
        {
            get { return _deploymentStatus; }
            set
            {
                if (SetProperty(ref _deploymentStatus, value))
                {
                    RaisePropertyChanged("DeploymentSummary");
                    RaisePropertyChanged("HasDeploymentSummary");
                }
            }
        }

        public DateTime? DeploymentDate
        {
            get { return _deploymentDate; }
            set
            {
                if (SetProperty(ref _deploymentDate, value))
                {
                    _deploymentDateInput = WorkLogDraftMapper.FormatDateText(value);
                    RaisePropertyChanged("DeploymentDateText");
                    RaisePropertyChanged("DeploymentSummary");
                    RaisePropertyChanged("HasDeploymentSummary");
                }
            }
        }

        public string DeploymentDateText
        {
            get
            {
                return _deploymentDateInput != null
                    ? _deploymentDateInput
                    : WorkLogDraftMapper.FormatDateText(DeploymentDate);
            }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                _deploymentDateInput = masked;
                RaisePropertyChanged("DeploymentDateText");
                if (string.IsNullOrEmpty(masked))
                {
                    _deploymentDate = null;
                    RaisePropertyChanged("DeploymentDate");
                    RaisePropertyChanged("DeploymentSummary");
                    RaisePropertyChanged("HasDeploymentSummary");
                    return;
                }
                DateTime? parsed;
                string err;
                if (masked.Length == 10 && WorkLogDraftMapper.TryParseDateText(masked, out parsed, out err))
                {
                    _deploymentDate = parsed;
                    RaisePropertyChanged("DeploymentDate");
                    RaisePropertyChanged("DeploymentSummary");
                    RaisePropertyChanged("HasDeploymentSummary");
                }
            }
        }

        public string Comment
        {
            get { return _comment; }
            set
            {
                if (SetProperty(ref _comment, value))
                    RaisePropertyChanged("HasComment");
            }
        }

        public bool HasComment
        {
            get { return !string.IsNullOrWhiteSpace(Comment); }
        }

        public override string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ProjectName))
                    return ProjectName;
                // DB Object/EQS/Global Resource 등 빈 Project Name 정상 — 필요한 경우만 플레이스홀더
                return IsPlaceholderTitle ? WorkLogFieldMasters.Unselected : string.Empty;
            }
        }

        /// <summary>UI/BIZ 등 Project가 필요한 경우에만 연한 플레이스홀더.</summary>
        public bool IsPlaceholderTitle
        {
            get { return WorkLogFieldMasters.RequiresProjectNameHint(Type, Category, ProjectName); }
        }

        public bool HasDisplayTitle
        {
            get { return !string.IsNullOrWhiteSpace(Title); }
        }

        public string DeploymentSummary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DeploymentStatus) || DeploymentStatus == WorkLogFieldMasters.Unselected)
                    return string.Empty;
                string d = DeploymentDate.HasValue ? " · " + DeploymentDate.Value.ToString("yyyy-MM-dd") : string.Empty;
                return DeploymentStatus + d;
            }
        }

        public bool HasDeploymentSummary
        {
            get { return !string.IsNullOrWhiteSpace(DeploymentSummary); }
        }

        public string SourcePreviewLine
        {
            get
            {
                if (Sources == null || Sources.Count == 0)
                    return string.Empty;
                string first = Sources[0].ChangeDetailText ?? Sources[0].FileName ?? string.Empty;
                if (Sources.Count == 1)
                    return first;
                return first + " 외 " + (Sources.Count - 1) + "개";
            }
        }
    }
}
