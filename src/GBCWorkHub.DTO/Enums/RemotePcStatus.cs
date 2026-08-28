namespace GBCWorkHub.DTO.Enums
{
    /// <summary>
    /// 원격 PC 상태
    /// </summary>
    public enum RemotePcStatus
    {
        /// <summary>로그인된 사용자 세션 없음</summary>
        Available = 0,

        /// <summary>현재 활성 세션 존재</summary>
        InUse = 1,

        /// <summary>세션은 남아 있으나 접속이 끊긴 상태</summary>
        Disconnected = 2,

        /// <summary>PC에 접근할 수 없는 상태</summary>
        Offline = 3,

        /// <summary>권한/네트워크 문제로 상태 조회 실패</summary>
        Unknown = 4
    }
}
