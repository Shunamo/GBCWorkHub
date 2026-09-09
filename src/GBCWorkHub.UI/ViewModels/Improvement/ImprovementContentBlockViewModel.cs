using System;
using GBCWorkHub.DTO.Improvement;

namespace GBCWorkHub.UI.ViewModels.Improvement
{
    /// <summary>
    /// 요청 설명을 블로그 글처럼 텍스트/이미지를 원하는 순서로 섞어 쓸 수 있게 하는 한 단락.
    /// 작성 중에는 텍스트 블록(직접 입력) 또는 이미지 블록(로컬 파일, 아직 미업로드)이고,
    /// 상세 보기에서는 저장된 첨부(ImprovementAttachmentDto)를 그대로 감싼 이미지 블록이다.
    /// 이미지 블록은 작성 시점에 즉시 고유 키(ContentKey)를 부여받아, 본문 텍스트에는 순번이 아니라
    /// 이 키를 참조하는 마커를 남긴다 — 다른 이미지를 삭제해도 이 블록이 가리키는 대상이 바뀌지 않는다.
    /// </summary>
    public sealed class ImprovementContentBlockViewModel : ViewModelBase
    {
        private string _text;

        public bool IsText { get; private set; }
        public bool IsImage { get { return !IsText; } }

        public string Text
        {
            get { return _text; }
            set { SetProperty(ref _text, value); }
        }

        public byte[] ImageData { get; private set; }
        public string FileName { get; private set; }
        public string ContentKey { get; private set; }

        public ImprovementContentBlockViewModel(string text)
        {
            IsText = true;
            _text = text ?? string.Empty;
        }

        public ImprovementContentBlockViewModel(byte[] imageData, string fileName)
        {
            IsText = false;
            ImageData = imageData;
            FileName = fileName;
            ContentKey = NewContentKey();
        }

        public ImprovementContentBlockViewModel(ImprovementAttachmentDto attachment)
        {
            IsText = false;
            ImageData = attachment.FileData;
            FileName = attachment.FileName;
            ContentKey = attachment.ContentKey;
        }

        /// <summary>
        /// 짧은(10자) 영숫자 키. 요청 하나 안에서만 유일하면 되므로 GUID 전체를 쓰지 않고
        /// 잘라 쓴다 — DESCRIPTION이 4000자 제한이라 마커를 짧게 유지하는 편이 유리하다.
        /// </summary>
        private static string NewContentKey()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 10);
        }
    }
}
