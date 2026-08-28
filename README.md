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

## 실행 전 설정

```bat
copy src\GBCWorkHub.UI\App.config.example src\GBCWorkHub.UI\App.config
```

`App.config`의 `GbcWorkHubDb`에 Oracle 계정·TNS를 넣습니다. `YOUR_`가 있으면 DB 없이 UI만 뜹니다.  
`App.config`는 커밋하지 마세요.

스키마/DDL은 `sql/` 과 `sql/README_DB_SETUP.md`를 참고하세요.

Session Agent는 솔루션의 `tools/GBCWorkHub.SessionAgent` 프로젝트입니다.
