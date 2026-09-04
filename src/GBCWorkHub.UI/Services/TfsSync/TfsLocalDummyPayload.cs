using System;
using System.Collections.Generic;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>실제 점유·종료·가져오기 단계에서만 쓰는 로컬 더미 체크인. Oracle에는 넣지 않는다.</summary>
    internal static class TfsLocalDummyPayload
    {
        public const int NewChangesetId = 900001;
        public const int ExistingInboxChangesetId = 900002;
        public const int AlreadyInWorkLogChangesetId = 900003;
        public const int NewChangesetId2 = 900004;
        public const int NewChangesetId3 = 900005;

        public static bool IsEnabled
        {
            get { return false; }
        }

        public static TfsRecentChangesetsPayload Build(TfsSyncRequest request)
        {
            string authorId = ((Environment.UserDomainName ?? string.Empty).Trim()
                + "\\"
                + (Environment.UserName ?? string.Empty).Trim()).Trim('\\');
            string authorName = OccupancyNameStore.TryGet();
            if (string.IsNullOrWhiteSpace(authorName))
                authorName = authorId;
            string requestId = request != null ? request.RequestId : Guid.NewGuid().ToString("N");
            DateTime checkedIn = KoreaTime.Now.AddMinutes(-40);

            var changesets = new List<TfsChangesetItem>
            {
                Item(NewChangesetId, authorId, authorName, checkedIn.AddMinutes(20),
                    "세션 만료 후 재로그인 처리", "LoginService.cs"),
                Item(ExistingInboxChangesetId, authorId, authorName, checkedIn.AddMinutes(10),
                    "예약 조회 성능 개선", "ReservationQuery.sql"),
                Item(AlreadyInWorkLogChangesetId, authorId, authorName, checkedIn,
                    "처방 저장 오류 수정", "OrderSave.cs"),
                Item(NewChangesetId2, authorId, authorName, checkedIn.AddMinutes(25),
                    "검사결과 조회 오류 수정", "LabResultView.cs"),
                Item(NewChangesetId3, authorId, authorName, checkedIn.AddMinutes(30),
                    "수납 금액 반올림 처리", "PaymentCalc.cs")
            };

            return new TfsRecentChangesetsPayload
            {
                Type = TfsClipboardPayloadService.ExpectedType,
                Success = true,
                SchemaVersion = 1,
                QueryMode = "SESSION",
                ComputerName = request != null ? request.RemoteComputerName : null,
                RemoteComputerName = request != null ? request.RemoteComputerName : null,
                RequestId = requestId,
                DeliveryId = Guid.NewGuid().ToString("N"),
                AuthorizedUserId = authorId,
                ReturnedItemCount = changesets.Count,
                Changesets = changesets,
                SessionStartAt = request != null
                    ? request.SessionStartedAt.ToString("o")
                    : null,
                SessionEndAt = request != null
                    ? request.SessionEndedAt.ToString("o")
                    : null,
                SessionToken = request != null ? request.SessionToken : null
            };
        }

        private static TfsChangesetItem Item(
            int id, string authorId, string authorName, DateTime at, string comment, string fileName)
        {
            return new TfsChangesetItem
            {
                ChangesetId = id,
                AuthorId = authorId,
                AuthorName = authorName,
                CheckedInAt = KoreaTime.ToRoundTripUtc(at),
                Comment = comment,
                ChangedFileCount = 1,
                ChangedFiles = new List<TfsChangedFileItem>
                {
                    new TfsChangedFileItem
                    {
                        ChangeType = "edit",
                        FileName = fileName,
                        Path = "$/GBC/" + fileName
                    }
                }
            };
        }
    }
}
