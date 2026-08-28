using System;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>ACK(일시 표시) / SYNC_REQUEST(동기화 요청, sticky) 구분.</summary>
    public enum ClipboardLeaseKind
    {
        Ack = 0,
        SyncRequest = 1
    }

    /// <summary>
    /// WorkHub가 클립보드에 프로토콜 문구를 쓴 "한 번의 소유권"을 나타낸다.
    /// 복원/삭제는 항상 (현재 클립보드 == WrittenText) exact CAS를 만족할 때만 수행한다 —
    /// prefix만 보고 판단하지 않는다.
    /// </summary>
    public sealed class ClipboardProtocolLease
    {
        /// <summary>단조 증가. 새 lease가 이전 lease를 자연스럽게 대체(supersede)했는지 판별용(로그/진단 목적).</summary>
        public int Generation { get; set; }

        public ClipboardLeaseKind Kind { get; set; }

        /// <summary>SyncRequest면 RequestId, Ack면 DeliveryId. 로그 상관관계용.</summary>
        public string RequestId { get; set; }

        /// <summary>이 lease가 실제로 클립보드에 쓴 정확한 문자열.</summary>
        public string WrittenText { get; set; }

        /// <summary>WrittenText의 SHA-256 (로그에 원문 대신 기록).</summary>
        public string WrittenHash { get; set; }

        /// <summary>쓰기 직전 사용자 클립보드(프로토콜 문구가 아니었을 때만). 없으면 null.</summary>
        public string UserBackup { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Ack: hold 종료 예정 시각. SyncRequest: sticky이므로 보통 미사용(MaxValue).</summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>
        /// 한 번이라도 exact CAS에 실패(=현재 클립보드가 더 이상 WrittenText가 아님)하면 true.
        /// 이후 재전송(TryRefreshSyncRequest)은 계속 스킵된다 — 사용자/타 세션 내용을 다시 덮지 않기 위함.
        /// </summary>
        public bool IsDisplaced { get; set; }
    }

    public enum ClipboardWriteOutcome
    {
        /// <summary>새로 SetText 했다.</summary>
        Written,

        /// <summary>이미 원하는 값이 클립보드에 있어 SetText를 생략했다(성공으로 취급).</summary>
        AlreadyCurrent,

        /// <summary>원격에서 이미 올라온 응답(TFS Payload 등)을 덮지 않기 위해 스킵했다(성공으로 취급 — 곧 수신 예정).</summary>
        SkippedForeignInboundPresent,

        /// <summary>사용자가 새 내용을 복사했거나 다른 세션 메시지가 있어 절대 덮지 않았다.</summary>
        Displaced,

        /// <summary>클립보드 API 예외 등으로 실패했다.</summary>
        Failed
    }

    public sealed class ClipboardWriteResult
    {
        public ClipboardWriteOutcome Outcome { get; set; }
        public ClipboardProtocolLease Lease { get; set; }

        public bool IsSuccessLike
        {
            get
            {
                return Outcome == ClipboardWriteOutcome.Written
                    || Outcome == ClipboardWriteOutcome.AlreadyCurrent
                    || Outcome == ClipboardWriteOutcome.SkippedForeignInboundPresent;
            }
        }
    }
}
