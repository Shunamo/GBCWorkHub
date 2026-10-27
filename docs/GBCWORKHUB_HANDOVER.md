# GBCWorkHub 인수인계 문서

> 관련 문서: [TFS_SCHEDULER_OPERATIONS.md](TFS_SCHEDULER_OPERATIONS.md)(TFS 수집·Windows 작업 스케줄러 상세) · [SITE_ROLLBACK_RUNBOOK.md](SITE_ROLLBACK_RUNBOOK.md)(사이트별 롤백·장애 복구) · [Architecture_Implementation_Report.md](Architecture_Implementation_Report.md)(코드 레벨 심층 조사, 1200여 줄) · [Clipboard_Communication_Reference.md](Clipboard_Communication_Reference.md)(클립보드 프로토콜 상세) · [DB_Schema_Reference.md](DB_Schema_Reference.md)(DB 스키마) · [AURORA_GUIDE.md](AURORA_GUIDE.md)(최종 사용자 가이드)
>
> 작성 원칙: 코드를 직접 읽고 확인된 내용만 기술합니다. 확인되지 않은 항목은 **"확인 필요"**로 표시했습니다. 비밀번호·API 토큰·DB 연결 문자열의 실제 값은 기록하지 않았습니다. 이 문서 작성 과정에서 **운영 코드/DB/스케줄러/배포 환경은 어떤 것도 변경하지 않았습니다**(읽기 전용 조사).

최종 확인일: 2026-10-27

---

## 0. 최종 운영 점검 체크리스트 (후임자용 요약)

인수받은 직후, 아래를 순서대로 한 번씩 직접 해보는 것을 권장합니다.

- [ ] `git clone` → `nuget restore` → `msbuild`로 로컬 빌드가 되는지 (§5-1)
- [ ] `App.config.example`을 복사해 `App.config`를 만들고, 테스트 Oracle 계정으로 앱이 뜨는지 (§5-1)
- [ ] 아무 사이트(AURORA 권장)의 실제 원격 PC에 RDP 접속 → 점유 상태가 WorkHub 화면에 반영되는지 (§3)
- [ ] 접속 해제 후 "체크인 내역 가져오기" 팝업이 뜨고, 체크인 보관함에 changeset이 표시되는지 (§TFS_SCHEDULER_OPERATIONS.md)
- [ ] 각 사이트(AURORA/CMC/RC) 원격 PC 1대씩, `schtasks /query /tn "<작업명>" /fo LIST /v`로 TFS 수집 작업이 `Ready` 상태인지 (§TFS_SCHEDULER_OPERATIONS.md §7)
- [ ] GitHub Actions `release.yml`이 등록된 Secret(`APP_CONFIG_XML` 등)에 접근 권한이 있는지 확인(리포지토리 설정)
- [ ] `git tag --sort=-creatordate`로 최신 릴리즈 버전과 실제 배포된 exe 버전이 일치하는지
- [ ] `backup_snapshot.ps1`(소스 백업)과 `GBCWorkHub_AutoBackup` 스케줄 작업이 본인 PC에도 필요한지 판단해 재설정 (§6)
- [ ] 이 세 문서(본 문서 + TFS_SCHEDULER_OPERATIONS + SITE_ROLLBACK_RUNBOOK)에 적힌 "확인 필요" 항목들을 실제 운영 담당자/현장 PC로 하나씩 검증
- [ ] `GBCWorkHub.CsvImportTool.exe --replace`는 **절대 사전 백업·승인 없이 실행하지 않기** — 전 사이트 업무기록이 삭제됩니다 ([SITE_ROLLBACK_RUNBOOK.md §2-0](SITE_ROLLBACK_RUNBOOK.md#2-0--가장-위험한-단일-명령--gbcworkhubcsvimporttoolexe---replace-코드로-확인됨))

---

## 1. 시스템 개요

### 1-1. 개발 목적과 사용자

GBCWorkHub는 **원격 PC 점유/세션 관리, 업무기록(작업 이력) 관리, TFS(Team Foundation Server) 체크인 내역 연동**을 하나로 묶은 Windows 데스크톱 앱(.NET Framework 4.8 WPF)입니다. 해외사업운영팀(`해외사업운영팀`, sql 폴더의 엑셀 파일명 기준)이 여러 해외 거점(AURORA/CMC/RC/MNGHA)의 **공유 원격 PC**에 접속해 개발 업무를 수행하고, 그 과정에서 발생한 TFS 체크인 내역을 자동으로 가져와 업무기록으로 남기는 용도로 만들어졌습니다. [확인된 구현 — `README.md`, `docs/AURORA_GUIDE.md`, sql 폴더의 사이트별 작업내역 엑셀 파일명]

### 1-2. 전체 시스템 구조 (요약)

```
WPF Client(GBCWorkHub.UI) ── Oracle DB(XSUP 스키마) : 원격 PC 점유상태 + 업무기록 공유
        │
        ├─ mstsc / .rdp 파일 / 브라우저(PMP) ── 원격 공유 PC(AURORA/CMC/RC/MNGHA)
        │                                           │
        │                                     SessionAgent(원격 단발 실행)
        │                                           │
        │                                   Windows 작업 스케줄러(이벤트 트리거)
        │                                           │
        │                                       TFS 서버(사이트별 별도)
        │
        └─ RDP 클립보드 ── 원격 SessionAgent와의 유일한 통신 채널
```

상세 아키텍처 다이어그램과 호출 흐름은 [Architecture_Implementation_Report.md](Architecture_Implementation_Report.md) §1~§4를 참고하세요(본 문서보다 코드 레벨로 훨씬 상세합니다).

### 1-3. 기술 스택

| 영역 | 내용 |
|---|---|
| 런타임 | .NET Framework 4.8 (전 프로젝트 공통) |
| UI | WPF, 수동 구현 MVVM(`RelayCommand`, DI 컨테이너 없음 — 생성자 체이닝으로 Repository 결합) |
| DB 접근 | Oracle.ManagedDataAccess 19.21.0/21.15.0, ADO.NET 직접 사용(ORM 없음) |
| 패키징 | Costura.Fody — 참조 DLL을 exe 안에 병합해 **단일 실행파일**로 배포 |
| JSON | Newtonsoft.Json 13.0.3 (System.Text.Json 미사용) |
| Excel/CSV | ClosedXML 0.104.2 + 자체 구현 CSV 파서(RFC4180 스타일) |
| 외부 연동 | Oracle DB, TFS(REST/Client OM), RDP(mstsc)/PMP(웹)/FortiClient VPN, GitHub Releases(자동 업데이트) |

### 1-4. 솔루션 구조

`GBCWorkHub.sln`에 포함된 프로젝트(`.sln` 미포함 독립 도구는 별도 표기):

| 프로젝트 | 경로 | 역할 |
|---|---|---|
| GBCWorkHub.DTO | `src/GBCWorkHub.DTO` | 순수 데이터 모델 |
| GBCWorkHub.DAC | `src/GBCWorkHub.DAC` | Oracle 데이터 접근(Repository), 파일 로거 |
| GBCWorkHub.BIZ | `src/GBCWorkHub.BIZ` | 업무 로직(점유, TFS 파싱, 업무기록, Excel 임포터) |
| GBCWorkHub.UI | `src/GBCWorkHub.UI` | WPF 클라이언트 본체(실행파일) |
| GBCWorkHub.BIZ.Tests / UI.Tests | `src/GBCWorkHub.*.Tests` | 수제 테스트 러너(프레임워크 없는 Exe) |
| GBCWorkHub.CsvImportTool | `src/GBCWorkHub.CsvImportTool` | 콘솔 배치 — Excel/CSV→DB 이관 |
| GBCWorkHub.SessionAgent | `tools/GBCWorkHub.SessionAgent` | **원격 PC**에서 실행되는 별도 프로그램(RDP 접속/해제 캡처, TFS 조회) |
| GBCWorkHub.Updater | `tools/GBCWorkHub.Updater` | 자동 업데이트 적용기 |
| GBCWorkHub.LegacyRedirect | `tools/GBCWorkHub.LegacyRedirect` | 구버전 설치 경로 호환용 리다이렉트 |

`.sln` 미포함 독립 사본: `tools/agent-updates/GBCWorkHub.SessionAgent`(개선판), `tools/ImportWorkLogExcel`(콘솔 도구) — [TFS_SCHEDULER_OPERATIONS.md §4](TFS_SCHEDULER_OPERATIONS.md#4-저장소-안의-sessionagent-코드가-여러-세대로-공존함--반드시-숙지)에 왜 여러 세대가 공존하는지 정리되어 있습니다.

### 1-5. 주요 디렉터리

```
src/GBCWorkHub.UI/
 ├─ Views/            — MainTabBar, MyPageView, RemoteWorkspaceView, WorkLog/, Popup/
 ├─ ViewModels/        — MainViewModel(루트), RemoteWorkspaceViewModel(최대 규모), WorkLog/
 ├─ Services/          — ClipboardMonitorService, RemoteSessionController, TfsSync/, Update/
 ├─ Controls/          — 커스텀 WPF 컨트롤
 ├─ Models/            — 요청/결과 DTO
 └─ Assets/            — XAML 테마, 폰트(Pretendard)
tools/
 ├─ GBCWorkHub.SessionAgent/      — 원격 PC용 프로그램(표준판)
 ├─ agent-updates/                — SessionAgent 개선판(별도 분기)
 ├─ site-setup/                   — 사이트별 설치/스케줄러 등록 스크립트(CMC/RC는 base64 내장 소스가 유일한 원본)
 └─ GBCWorkHub.Updater/           — 자동 업데이트 적용기
sql/                              — DDL·DML 스크립트(번호순 적용), README_DB_SETUP.md, Aurora_SessionAgent.ps1(AURORA 정식 소스)
docs/                             — 본 문서군 + 기존 심층 조사 문서
scripts/                          — 릴리즈 패키징/버전 주입/업데이트 E2E 테스트 스크립트
```

### 1-6. 환경설정 파일

| 파일 | 역할 |
|---|---|
| `src/GBCWorkHub.UI/App.config.example` | 커밋되는 템플릿. Oracle 연결 문자열 자리표시자, **사이트별 PC 목록(IP/PC명)**, `RemotePc.DbPollSeconds`, `Tfs.*` 플래그, `SiteTimeZone.*`, `Update.*`(자동 업데이트) 키 포함 |
| `src/GBCWorkHub.UI/App.config` | 로컬 전용(커밋 금지). 위 템플릿을 복사해 실제 Oracle 계정/TNS 입력 |
| `%LocalAppData%\GBCWorkHub\GBCWorkHub.exe.config` | 배포본 실행 시 exe 내장 설정이 최초 1회 추출되는 실제 런타임 설정 파일 |
| `sql/README_DB_SETUP.md` | DB 계정 권한(GRANT 목록), 테이블별 DDL 파일 매핑 |

### 1-7. 개발 환경과 운영 환경의 차이

| 항목 | 개발 | 운영(배포) |
|---|---|---|
| 실행 형태 | `msbuild` Debug 빌드, DLL 별도 파일 | Release 빌드, Costura.Fody로 **DLL 전부 exe에 내장** |
| 설정 소스 | `src/GBCWorkHub.UI/App.config`(로컬 작성) | exe 내장 → `%LocalAppData%`로 추출 |
| DB 연결 | 테스트 계정 또는 `YOUR_`로 DB 없이 UI만 구동 | 운영 Oracle 계정(GitHub Secret `APP_CONFIG_XML`로 CI에서 주입) |
| 자동 업데이트 | 비활성 또는 로컬/모의 서버 대상(`scripts/test-update-*.ps1`) | GitHub Releases 공개 엔드포인트(PAT 불필요) |
| SessionAgent | 저장소 안의 소스를 직접 빌드/실행 | 각 사이트 원격 PC에 `tools/site-setup/Setup_*` 스크립트로 1회 설치 |

---

## 2. 주요 기능

실제 코드에 존재하는 기능만 정리했습니다. 각 기능의 상세 흐름은 괄호 안 문서를 참고하세요.

### 2-1. 원격 PC 점유 / 접속 관리

- **설명**: 사이트별 공유 원격 PC의 점유 상태(비어있음/사용중)를 Oracle DB(`XSUP.MSDWHTKD`)로 여러 사용자가 공유. 조건부 UPDATE(Compare-And-Swap)로 동시 접속 경합을 처리.
- **이용 절차**: 사이트 선택(AURORA/CMC/RC) → 빈 PC 카드 클릭 → 접속 방식(mstsc/.rdp/브라우저 PMP)은 사이트마다 다름 → 작업 후 원격 창 닫으면 자동 해제.
- **화면**: `RemoteWorkspaceView.xaml`
- **프론트엔드**: `src/GBCWorkHub.UI/ViewModels/RemoteWorkspaceViewModel.cs`, `Services/RemoteSessionController.cs`, `Services/RdpSessionTrackingService.cs`, `Services/CmcPmpSessionMonitor.cs`, `Services/FortiClientLauncher.cs`
- **백엔드**: `src/GBCWorkHub.BIZ/RemotePcBiz.cs`, `RemotePcShareBiz.cs`, `src/GBCWorkHub.DAC/OracleRemotePcRepository.cs`, `RemotePcDac.cs`(App.config 기반 PC 목록 — DB 아님)
- **저장소**: Oracle `XSUP.MSDWHTKD`(현재 상태) + `XSUP.MSDWHTKH`(접속 이력)
- **외부 의존성**: mstsc.exe(Windows 내장), FortiClient(RC), 사내 PMP 웹 게이트웨이(CMC)
- **정상 동작 확인**: PC 카드 클릭 시 점유 상태가 즉시 "접속중"으로 바뀌고, 원격 창이 실제로 열리는지
- **오류 시 확인**: DB 폴링 상태 표시(상단), `C:\GBCWorkHub\Logs\WorkHub-*.log`, 해당 사이트의 App.config PC 목록 등록 여부

### 2-2. 업무기록(WorkLog) 관리

- **설명**: 수행한 작업 내역(티켓/화면/프로젝트/소스파일 등)을 기록·검색·수정. TFS 체크인에서 가져오거나 직접 입력 가능.
- **이용 절차**: 업무기록 탭 → 목록 조회/검색(소속·작성자·기간 필터) → "새로 작성" 또는 "TFS 가져오기"로 등록 → 수정/삭제
- **화면**: `src/GBCWorkHub.UI/Views/WorkLog/WorkLogListView.xaml`(Classic/Glass 2테마), `WorkLogEditDialog.xaml`
- **프론트엔드**: `ViewModels/WorkLog/WorkLogListViewModel.cs`, `WorkLogEditDialogViewModel.cs`
- **백엔드**: `src/GBCWorkHub.BIZ/WorkLog/WorkLogBiz.cs`, `src/GBCWorkHub.DAC/OracleWorkLogRepository.cs`
- **저장소**: Oracle `XSUP.MSDWHTKD_WRK`(헤더) + `_PRJ`/`_SRC`(상세) + `_CS`(TFS changeset 레지스트리)
- **정상 동작 확인**: 저장 후 목록에 즉시 반영, 소속/작성자로 필터링 가능
- **오류 시 확인**: DB 연결 상태, `MSDWHTKD_WRK*` 권한(`sql/README_DB_SETUP.md`)

### 2-3. TFS 체크인 자동 수집 → 업무기록 가져오기

- **설명**: 원격 PC에서 RDP 접속/해제 시 SessionAgent가 TFS 최근 체크인을 조회해 클립보드로 전달하면, "체크인 보관함" 화면에서 선택해 업무기록으로 저장.
- **상세**: 전체 파이프라인, 사이트별 차이, 인증 방식, Windows 작업 스케줄러 등록 방법은 **[TFS_SCHEDULER_OPERATIONS.md](TFS_SCHEDULER_OPERATIONS.md)** 전체를 참고 — 이 기능이 이 인수인계의 핵심이므로 별도 문서로 분리했습니다.
- **화면**: `Views/WorkLog/TfsCheckinInboxView.xaml`, `TfsImportDialog.xaml`

### 2-4. Excel/CSV 업무기록 일괄 가져오기

- **설명**: 정해진 양식(`Ticket No, Ticket Contents, Menu(Screen Name), PC, Type, Category, Project Name, Source Path & File Name, Person in charge, Start Date, ...`)의 엑셀/CSV 파일을 파싱해 업무기록으로 일괄 저장. 셀 병합 범위를 작업 경계로 인식(`WorkLogExcelXlsxImporter.cs` 상단 주석). 내용 전체(헤더+프로젝트+소스)가 같은 건은 fingerprint로 중복 스킵(`WorkLogContentDedup`).
- **이용 절차**: UI의 가져오기 다이얼로그 또는 독립 콘솔 도구(`src/GBCWorkHub.CsvImportTool`, `tools/ImportWorkLogExcel`) 실행
- **백엔드**: `src/GBCWorkHub.BIZ/WorkLog/WorkLogExcelXlsxImporter.cs`, `WorkLogExcelCsvImporter.cs`, `WorkLogBiz.GetContentDedupKeysAsync`
- **⚠️ 위험**: 콘솔 도구의 `--replace` 옵션은 이름과 달리 **사이트 구분 없이 전 사이트(AURORA/CMC/RC/MNGHA)의 업무기록 테이블 전체를 삭제**합니다. 사용 전 반드시 [SITE_ROLLBACK_RUNBOOK.md §2-0](SITE_ROLLBACK_RUNBOOK.md#2-0--가장-위험한-단일-명령--gbcworkhubcsvimporttoolexe---replace-코드로-확인됨)을 읽고 사전 백업·승인 절차를 따르세요.
- **관련 데이터**: `sql/` 폴더의 `해외사업운영팀_프로그램_작업내역_*.xlsx`가 원본 템플릿 예시이자, `sql/33~36` 일괄 이관 스크립트의 원천 데이터였던 것으로 추정 [확인 필요 — 엑셀 파일과 SQL 이관 스크립트의 직접적 생성 관계는 본 조사에서 1:1 대조하지 않음]
- **정상 동작 확인**: 가져오기 후 중복 없이 건수가 예상과 일치하는지
- **오류 시 확인**: 양식의 헤더 이름이 `ExpectedHeaders`와 정확히 일치하는지(`WorkLogExcelCsvImporter.cs`)

### 2-5. 자동 업데이트

- **설명**: GitHub Releases의 `version.json`을 폴링해 신버전 발견 시 배너 표시 → zip 다운로드/SHA256 검증 → `GBCWorkHubUpdater.exe`가 설치 폴더 교체 → 재시작. 재시작 직후 1회성 릴리즈 노트 팝업(v1.3.3부터, `PendingReleaseNoticeStore`) + "이전 버전들 변경사항" 토글(`ReleaseHistoryService`, GitHub Releases API 직접 호출).
- **상세 배포 파이프라인, 버전 태그 체계, 롤백 한계**: [SITE_ROLLBACK_RUNBOOK.md §1](SITE_ROLLBACK_RUNBOOK.md#1-코드배포-롤백) 참고
- **관련 코드**: `src/GBCWorkHub.UI/Services/Update/UpdateService.cs`, `ReleaseHistoryService.cs`, `PendingReleaseNoticeStore.cs`, `tools/GBCWorkHub.Updater/Program.cs`

---

## 3. 사이트별 운영 구조 요약

상세 비교표와 장애 대응은 [SITE_ROLLBACK_RUNBOOK.md §3](SITE_ROLLBACK_RUNBOOK.md#3-사이트별-특이사항-요약), TFS 수집 비교는 [TFS_SCHEDULER_OPERATIONS.md §3](TFS_SCHEDULER_OPERATIONS.md#3-사이트별-tfs-수집-대상-비교)을 참고하세요. 여기서는 식별자와 역할만 요약합니다.

| 사이트 코드 | 역할 | 접속 방식 | 비고 |
|---|---|---|---|
| `AURORA` | 미국 거점(시간대 US Mountain) 공유 개발 PC | mstsc 직접 | 가장 많은 PC(15대, App.config.example 기준) 등록, TFS는 Client OM 사용 |
| `CMC` | 중동(두바이, Arabian Standard Time) 공유 PC | 브라우저 PMP 웹 게이트웨이 | TFS 전용 계정(`DXBCMC\yang.ss`) 사용, FortiClient 불필요 |
| `RC` | 사우디 등(Arab Standard Time) 공유 PC | FortiClient VPN → 게시 RDP(.rdp 파일) | 가장 많은 등록 PC(25대), WorkHub가 .rdp 파일을 직접 생성하지 않고 기존 다운로드 파일을 탐색 |
| `MNGHA` | 추가 해외 거점(Arab Standard Time) | AURORA와 유사한 방식으로 추정 | App.config.example에 PC 목록이 없어 **기본 비활성**. 실시간 TFS 수집 경로도 저장소에 없음(§TFS_SCHEDULER_OPERATIONS.md §3) |

SITE_CD는 `src/GBCWorkHub.DAC/RemotePcDac.cs`에 `{"AURORA","CMC","RC","MNGHA"}`로 하드코딩되어 있으며, **DB가 아니라 App.config이 SITE/PC 목록의 소스**입니다. 신규 PC 추가는 App.config 편집 + `sql/06,08,13`류의 MERGE 시드 스크립트 실행이 필요합니다.

---

## 4. 운영 및 배포 인수인계

### 4-1. 개발 환경 — 최초 실행

```bat
git clone https://github.com/Shunamo/GBCWorkHub.git
cd GBCWorkHub
nuget restore GBCWorkHub.sln
msbuild GBCWorkHub.sln /p:Configuration=Debug /p:Platform="Any CPU"
copy src\GBCWorkHub.UI\App.config.example src\GBCWorkHub.UI\App.config
rem App.config의 GbcWorkHubDb에 Oracle 계정/TNS 입력 (YOUR_ 값 그대로면 DB 없이 UI만 뜸)
```
실행 파일: `src\GBCWorkHub.UI\bin\Debug\GBCWorkHub.exe`

필요 프로그램: Windows, [.NET Framework 4.8 Developer Pack], Visual Studio 2019+ 또는 Build Tools(MSBuild), [NuGet CLI](`nuget.exe`).

### 4-2. 테스트

`GBCWorkHub.BIZ.Tests`, `GBCWorkHub.UI.Tests`는 테스트 프레임워크(NUnit/xUnit 등) 없이 직접 빌드/실행되는 콘솔 Exe입니다. `msbuild`로 빌드 후 각 프로젝트의 산출물 exe를 직접 실행해 결과를 콘솔에서 확인합니다. `.github/workflows/ci.yml`이 main/master push·PR에 대해 Debug 빌드 + 이 테스트들을 자동 실행합니다.

### 4-3. 배포(운영) 흐름

```bash
git tag -a v1.3.23 -m "릴리즈 노트로 쓰일 메시지"
git push origin v1.3.23
```
→ GitHub Actions(`.github/workflows/release.yml`)가 버전 주입 → `App.config` 생성(Secret `APP_CONFIG_XML`) → MSBuild Release 빌드(UI/Updater/LegacyRedirect) → 버전 검증 → `scripts/pack-release.ps1`로 패키징 → GitHub Release에 업로드:
- `GBCWorkHub-vX.Y.Z.zip`(자동 업데이트용)
- `GBCWorkHub-Setup-vX.Y.Z.zip`(신규 설치용)
- `GBCWorkHub-SiteSetup-vX.Y.Z.zip`(`tools/site-setup` 묶음)
- `version.json`

클라이언트는 `releases/latest/download/version.json`을 폴링해 자동 업데이트합니다. 상세·롤백 한계는 [SITE_ROLLBACK_RUNBOOK.md §1](SITE_ROLLBACK_RUNBOOK.md#1-코드배포-롤백) 참고.

### 4-4. 서비스 실행/중지/재시작

이 앱은 Windows 서비스가 아니라 **사용자가 직접 실행하는 데스크톱 exe**입니다. "재시작"은 프로세스 종료 후 재실행이며, 업데이트 적용 시 Updater가 자동으로 수행합니다. 원격 PC의 SessionAgent는 상주 프로세스가 아니라 **이벤트 발생 시 단발 실행**되므로 별도의 중지/재시작 개념이 없습니다(스케줄 작업 자체를 Disable/Enable하는 것이 사실상의 on/off).

### 4-5. 운영 로그 위치

| 위치 | 내용 |
|---|---|
| `C:\GBCWorkHub\Logs\WorkHub-yyyyMMdd.log` | 로컬 WorkHub 진단/DB 로그(자동 삭제 없음 — 무한 누적 주의) |
| `C:\GBCWorkHub\Logs\Crash-*.log` | 미처리 예외(크래시) 로그 |
| `C:\GBCWorkHub\Logs\SessionAgent.log`(원격 PC) | SessionAgent 실행 로그 |
| `C:\GBCWorkHub\SessionAgentError.log`(원격 PC) | SessionAgent 예외 로그 |

로그 경로 `C:\GBCWorkHub`는 하드코딩된 절대경로입니다. 일반 사용자 권한 환경에서 쓰기 실패 가능성이 있으나, 실패 시 조용히 무시되도록 되어 있습니다(`C:\` 드라이브 쓰기 권한을 사전에 보장하는 것이 안전).

### 4-6. 환경설정 변경 방법

- 사이트/PC 목록, DB 연결, 자동 업데이트 설정: `App.config`(운영은 GitHub Secret `APP_CONFIG_XML` 갱신 후 재배포)
- TFS 서버 주소/경로: 사이트별 SessionAgent 소스 코드 상수(App.config이 아님) — [TFS_SCHEDULER_OPERATIONS.md §4](TFS_SCHEDULER_OPERATIONS.md#4-저장소-안의-sessionagent-코드가-여러-세대로-공존함--반드시-숙지) 참고

### 4-7. 배포 후 검증 절차 / 이전 버전 복구 / 장애 점검 순서

→ [SITE_ROLLBACK_RUNBOOK.md](SITE_ROLLBACK_RUNBOOK.md) 전체(특히 §1, §4) 참고.

---

## 5. 최종 검증 메모 (작성자 자체 점검)

- 현재 코드에 존재하는 기능만 기술했으며, 미구현/부분구현 항목은 원 출처 문서(`Architecture_Implementation_Report.md`)의 표기를 그대로 인용해 과장하지 않았습니다.
- 4개 사이트(AURORA/CMC/RC/MNGHA) 전부를 다루었고, MNGHA처럼 정보가 부족한 사이트는 "확인 필요"로 명시했습니다.
- TFS 수집 구조는 실제 SessionAgent 소스(여러 세대)를 직접 읽고 디코딩해 검증했으며, 추측 없이 파일:함수 단위로 근거를 남겼습니다.
- Windows 작업 스케줄러는 **이 PC에서 실제로 `schtasks`를 실행해 조회한 결과**와 **코드상의 등록 로직**을 구분해 표기했습니다.
- 롤백 절차 중 "코드로 확인됨"과 "수동 절차 제안(실행 전례 미확인)"을 구분했습니다 — 특히 자동 업데이트에는 진짜 버전 롤백 기능이 없다는 점을 명확히 했습니다.
- 운영 DB/스케줄러/배포 환경에 대한 수정, 실제 롤백 실행, 스케줄러 재실행은 **일절 수행하지 않았습니다**. `schtasks /query`, SQL/소스 `Read`, `git log`/`git tag` 등 읽기 전용 명령만 사용했습니다.
- 비밀번호/토큰/DB 연결 문자열의 실제 값은 어디에도 기록하지 않았습니다(서버 호스트명/IP, 계정명은 운영에 필수적인 식별 정보로 판단해 포함했습니다).

---

변경 이력:
- 2026-10-27: 최초 작성 (전체 코드베이스 조사 + 로컬 작업 스케줄러 실측 기반, 읽기 전용)
- 2026-10-27: 교차검증 보강 — `CsvImportTool --replace`의 전사이트 삭제 위험 경고 추가(별도 DB 롤백 전담 조사와 교차대조)
- 2026-10-27: 확인된 구버전 죽은 사본 3개(`tools/Aurora_SessionAgent.ps1`, `CMC_SessionAgent_Program.cs`, `RC_SessionAgent_Program.cs`) 삭제 반영 — 디렉터리 구조 표 갱신
