# GBCWorkHub

원격 PC 점유/세션, 업무기록, TFS 연동을 다루는 .NET Framework 4.8 WPF 앱입니다.

## 요구 사항

- Windows
- [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- Visual Studio 2019 이상 또는 Build Tools (`MSBuild`)
- [NuGet CLI](https://www.nuget.org/downloads) (`nuget.exe`를 PATH에 두거나 Visual Studio에서 복원)

## 빌드

```bat
git clone https://github.com/Shunamo/GBCWorkHub.git
cd GBCWorkHub
nuget restore GBCWorkHub.sln
msbuild GBCWorkHub.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Visual Studio에서 `GBCWorkHub.sln`을 연 뒤 **Restore NuGet Packages** → **빌드**해도 됩니다.

실행 파일: `src\GBCWorkHub.UI\bin\Debug\GBCWorkHub.UI.exe`

## 배포 (DLL 없이)

Release 빌드하면 `Newtonsoft.Json`, `Oracle.ManagedDataAccess`, `GBCWorkHub.BIZ/DAC/DTO`, ClosedXML 계열 DLL이 **exe 안에 포함**됩니다 (Costura.Fody).

배포할 것:

- `GBCWorkHub.UI.exe`
- `GBCWorkHubUpdater.exe` (자동 업데이트 적용기 — 설치 폴더에 함께 둠)
- `GBCWorkHub.UI.exe.config` (`App.config`가 빌드된 것 — DB/사이트 설정) 또는 BundledConfig
- `Fonts\` 폴더 (Pretendard otf) — embed 시 LocalAppData로 추출

zip에 DLL을 넣을 필요는 없습니다. PDB·XML 문서는 넣지 않아도 됩니다.

### 자동 업데이트 (요약)

운영 Release Storage는 **GitHub Releases**(공개 static HTTPS)입니다. Client에 PAT를 넣지 않습니다.

1. `git tag v1.2.0 && git push origin v1.2.0`
2. Actions가 Release 빌드 → `GBCWorkHub-v1.2.0.zip` + `version.json`(packageUrl=해당 Release asset) → GitHub Release 업로드
3. 클라이언트는 `https://github.com/{owner}/{repo}/releases/latest/download/version.json` 조회
4. 새 버전이면 배너 → zip 다운로드(SHA256) → TEMP Updater → 재시작

`App.config` 기본값: `Update.SourceType=GitHubRelease`, `Update.ReleaseOwner` / `Update.ReleaseRepo`  
(소스 repo와 Release repo 분리 시 Owner/Repo 또는 `Update.ReleaseBaseUrl` 변경. Actions는 `RELEASE_OWNER` / `RELEASE_REPO` / `RELEASE_BASE_URL` vars)

로컬 E2E:
- LocalFolder: `powershell -File scripts\test-update-e2e.ps1`
- GitHub Releases 시뮬: `powershell -File scripts\test-update-github-e2e.ps1`

## 실행 전 설정

```bat
copy src\GBCWorkHub.UI\App.config.example src\GBCWorkHub.UI\App.config
```

`App.config`의 `GbcWorkHubDb`에 Oracle 계정·TNS를 넣습니다. `YOUR_`가 있으면 DB 없이 UI만 뜹니다.  
`App.config`는 커밋하지 마세요.

스키마/DDL은 `sql/` 과 `sql/README_DB_SETUP.md`를 참고하세요.

Session Agent는 솔루션의 `tools/GBCWorkHub.SessionAgent` 프로젝트입니다.

오로라 사용자 가이드(첫 실행 점유명): [docs/AURORA_GUIDE.md](docs/AURORA_GUIDE.md)
