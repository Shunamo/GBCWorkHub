namespace GBCWorkHub.UI.Models
{
    /// <summary>
    /// mstsc 실행 목적 구분.
    /// 런치는 동일(mstsc /v:)하고, 점유/팝업/종료 처리만 분기한다.
    /// </summary>
    public enum RdpLaunchPurpose
    {
        UserSession = 0,
        /// <summary>TFS 가져오기용 재접속 (Oracle 점유/TFS 재팝업 없음)</summary>
        TfsSyncReconnect = 1
    }
}
