using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// Pretendard otf를 exe에 넣고, 실행 시 Fonts 폴더로 풀어 FontFamily로 쓴다.
    /// pack URI로 넣으면 weight가 안 묶여 SemiBold가 깨진다.
    /// </summary>
    internal static class BundledFonts
    {
        private static readonly string[] Files =
        {
            "Pretendard-Regular.otf",
            "Pretendard-Medium.otf",
            "Pretendard-SemiBold.otf",
            "Pretendard-Bold.otf"
        };

        // DynamicResource가 Source 문자열만 복사해도 BaseUri가 남도록 인스턴스를 붙잡아 둔다.
        private static FontFamily _family;

        public static void EnsureExtracted()
        {
            string fontDir = BundledConfig.FontsDirectory;
            foreach (string file in Files)
            {
                BundledConfig.ExtractResource(
                    "GBCWorkHub.UI.Fonts." + file,
                    Path.Combine(fontDir, file));
            }
        }

        public static void ApplyTo(Application app)
        {
            if (app == null || app.Resources == null)
                return;

            try
            {
                string fontDir = BundledConfig.FontsDirectory;
                string regular = Path.Combine(fontDir, "Pretendard-Regular.otf");
                if (!File.Exists(regular))
                    return;

                string dir = Path.GetFullPath(fontDir);
                if (dir[dir.Length - 1] != Path.DirectorySeparatorChar)
                    dir += Path.DirectorySeparatorChar;

                // 폴더 폰트는 반드시 (baseUri, "./#Family") 생성자.
                // Source에 file URI를 이어 붙이면 #이 프래그먼트로 잘려 한글이 빈 글리프로 나온다.
                _family = new FontFamily(new Uri(dir), "./#Pretendard, Malgun Gothic, Segoe UI");
                SetFontFamily(app.Resources, _family);
            }
            catch
            {
            }
        }

        private static void SetFontFamily(ResourceDictionary resources, FontFamily family)
        {
            resources["AppFontFamily"] = family;
            if (resources.MergedDictionaries == null)
                return;
            foreach (var dict in resources.MergedDictionaries)
            {
                if (dict != null)
                    SetFontFamily(dict, family);
            }
        }
    }
}
