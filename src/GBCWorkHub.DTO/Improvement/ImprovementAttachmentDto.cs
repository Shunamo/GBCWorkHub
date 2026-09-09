using System;

namespace GBCWorkHub.DTO.Improvement
{
    /// <summary>XSUP.MSDWHTKD_REQ_ATTACH 1행. FILE_DATA(BLOB)는 목록 조회 시 비워두고 상세/다운로드 시에만 채운다.</summary>
    public sealed class ImprovementAttachmentDto
    {
        public long AttachId { get; set; }
        public long ReqId { get; set; }
        public string FileName { get; set; }
        public string FileExt { get; set; }
        public long FileSize { get; set; }
        public byte[] FileData { get; set; }
        public DateTime? CreatedAt { get; set; }
        /// <summary>작성 시점에 클라이언트가 생성하는 짧은 고유 키. 본문의 {{img:&lt;키&gt;}} 마커가 이 값을 참조한다.</summary>
        public string ContentKey { get; set; }
    }
}
