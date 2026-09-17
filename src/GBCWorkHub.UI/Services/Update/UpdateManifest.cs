using System;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.Update
{
    public sealed class UpdateManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("packageFile")]
        public string PackageFile { get; set; }

        [JsonProperty("packageUrl")]
        public string PackageUrl { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        /// <summary>업데이트 적용 후 재실행할 exe 파일명. 비어 있으면 UpdateService.MainExeFileName(빌드에 박힌 기본값)을 쓴다.
        /// 이 필드를 매니페스트에서 읽게 해두면, 앞으로 exe 이름이 바뀌어도 기존 설치본이 자동으로 새 이름을 따라간다.</summary>
        [JsonProperty("mainExeFileName")]
        public string MainExeFileName { get; set; }

        [JsonIgnore]
        public System.Version ParsedVersion
        {
            get
            {
                System.Version v;
                return System.Version.TryParse(Normalize(Version), out v) ? v : null;
            }
        }

        public static string Normalize(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            string s = version.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);
            var parts = s.Split('.');
            if (parts.Length == 3)
                s = s + ".0";
            return s;
        }
    }
}
