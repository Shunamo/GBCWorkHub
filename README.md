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
- `GBCWorkHub.UI.exe.config` (`App.config`가 빌드된 것 — DB/사이트 설정)
- `Fonts\` 폴더 (Pretendard otf)

zip에 DLL을 넣을 필요는 없습니다. PDB는 넣지 않아도 됩니다.

## 실행 전 설정

```bat
copy src\GBCWorkHub.UI\App.config.example src\GBCWorkHub.UI\App.config
```

`App.config`의 `GbcWorkHubDb`에 Oracle 계정·TNS를 넣습니다. `YOUR_`가 있으면 DB 없이 UI만 뜹니다.  
`App.config`는 커밋하지 마세요.

스키마/DDL은 `sql/` 과 `sql/README_DB_SETUP.md`를 참고하세요.

Session Agent는 솔루션의 `tools/GBCWorkHub.SessionAgent` 프로젝트입니다.
