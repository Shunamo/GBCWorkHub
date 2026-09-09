using System;
using GBCWorkHub.DTO.Improvement;

namespace GBCWorkHub.UI.ViewModels.Improvement
{
    /// <summary>목록 행 표시 전용 래퍼. WorkLogListItemViewModel과 동일한 관례(DTO 래핑, 표시용 계산 프로퍼티).</summary>
    public sealed class ImprovementListItemViewModel : ViewModelBase
    {
        public ImprovementRequestDto Dto { get; private set; }

        public ImprovementListItemViewModel(ImprovementRequestDto dto)
        {
            Dto = dto ?? new ImprovementRequestDto();
        }

        public long ReqId { get { return Dto.ReqId; } }
        public string TicketNoText { get { return "#" + Dto.ReqId; } }
        public long UserId { get { return Dto.UserId; } }
        public string RequestType { get { return Dto.RequestType; } }
        public string TypeLabel { get { return ImprovementTypeLabels.ToLabel(Dto.RequestType); } }
        public string Title { get { return Dto.Title; } }
        public string AuthorName { get { return Dto.AuthorName; } }
        public bool HasDeletedAuthorSuffix { get { return Dto.IsAuthorDeleted; } }
        public string TeamName { get { return Dto.TeamName; } }
        public string AppVersion { get { return Dto.AppVersion; } }
        public string SiteCode { get { return Dto.SiteCode; } }
        public string Status { get { return Dto.Status; } }
        public string StatusLabel { get { return ImprovementStatusLabels.ToLabel(Dto.Status); } }

        public string CreatedAtText
        {
            get { return Dto.CreatedAt.HasValue ? Dto.CreatedAt.Value.ToString("yyyy-MM-dd") : "-"; }
        }

        public int ReproducedCount { get { return Dto.ReproducedCount; } }
        public int NotReproducedCount { get { return Dto.NotReproducedCount; } }
        public int CommentCount { get { return Dto.CommentCount; } }

        public bool IsResolved
        {
            get { return string.Equals(Dto.Status, ImprovementStatuses.Resolved, StringComparison.OrdinalIgnoreCase); }
        }

        public string ResolvedNoteText
        {
            get
            {
                return IsResolved && !string.IsNullOrWhiteSpace(Dto.ResolvedVersion)
                    ? "v" + Dto.ResolvedVersion + "에서 해결"
                    : null;
            }
        }

        public bool HasResolvedNote { get { return ResolvedNoteText != null; } }
    }
}
