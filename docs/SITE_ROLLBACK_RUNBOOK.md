# GBCWorkHub — 사이트별 롤백 & 장애 복구 런북

> 상위 문서: [GBCWORKHUB_HANDOVER.md](GBCWORKHUB_HANDOVER.md) · 관련 문서: [TFS_SCHEDULER_OPERATIONS.md](TFS_SCHEDULER_OPERATIONS.md)
> 이 문서는 실제 코드(`.github/workflows/*`, `scripts/*.ps1`, `tools/GBCWorkHub.Updater`, `sql/*.sql`, git 태그 이력)를 직접 조사하여 작성했습니다. **"자동 롤백 기능이 있다"고 단정할 수 있는 코드는 발견되지 않았습니다** — 이 문서에 적힌 절차 대부분은 "현재 코드 구조상 가능한 수동 절차"이며, 실제 운영 중 이 절차를 수행한 전례가 있는지는 확인되지 않았습니다. 신뢰도 표기: **[코드로 확인됨]** / **[수동 절차 제안 — 실행 전례 미확인]** / **[확인 필요]**

최종 확인일: 2026-10-27

---

## 1. 코드/배포 롤백

### 1-1. 버전 관리 체계 [코드로 확인됨]

- 태그 형식: `vMAJOR.MINOR.PATCH` (`.github/workflows/release.yml`가 `^\d+\.\d+\.\d+$` 정규식으로 강제)
- 태그 push(`git push origin vX.Y.Z`) 또는 `workflow_dispatch` → GitHub Actions가 빌드 → GitHub Releases에 업로드
- 이번 조사 시점의 실제 태그(최신순 일부): `v1.3.22, v1.3.21, v1.3.20, v1.3.19, v1.3.18, v1.3.17, v1.3.16, v1.3.15, v1.3.14, v1.3.13, v1.3.12, v1.3.11, v1.3.10, v1.3.9` + `guides`(버전 태그 아님, 용도 **확인 필요**)
- **v1.3.9 이전 버전에 대한 git 태그는 저장소에 없습니다.** 과거에 태그 없이 배포됐는지, 삭제됐는지는 **확인 필요**.
- 릴리즈 노트의 유일한 소스는 **annotated 태그 메시지**입니다. `git tag -a vX.Y.Z -m "..."`로 태그를 만들 때 메시지를 비우면(lightweight 태그) 릴리즈 노트도 비고, 클라이언트 팝업 자체가 뜨지 않습니다.

### 1-2. 이전 정상 버전 확인 방법

```bash
git tag --sort=-creatordate              # 최신순 태그 목록
git log -1 --format=%ai v1.3.21          # 특정 태그의 생성 일시
git log v1.3.20..v1.3.21 --oneline       # 두 버전 사이 변경 커밋
```
GitHub Releases 페이지에서도 각 버전의 릴리즈 노트(annotated 태그 메시지)를 그대로 볼 수 있습니다.

### 1-3. 자동 업데이트/롤백 메커니즘의 실제 동작 [코드로 확인됨]

`tools/GBCWorkHub.Updater/Program.cs` 흐름: 메인 프로세스 종료 대기 → 패키지 SHA256 재검증 → **설치 폴더의 기존 파일을 `%TEMP%\GBCWorkHubUpdater\<세션>-work\backup\`로 백업** → 새 파일 교체 → 교체 작업 자체가 **중간에 실패한 경우에만** 그 백업에서 즉시 복원.

**중요 — 이것은 "버전 롤백 기능"이 아닙니다.**
- 이 백업은 "업데이트 적용 트랜잭션이 깨졌을 때의 원자성 보장"용입니다. 업데이트가 일단 "성공"으로 끝나면, 그 백업을 다시 꺼내 쓰는 코드는 어디에도 없습니다.
- 백업은 `%TEMP%`의 세션별 GUID 폴더에 남으며, 설치 폴더나 영구 저장소에 보존되지 않습니다. Windows가 임시 폴더를 정리하면 사라집니다.
- **"N번째 이전 버전으로 되돌리기" UI나 CLI 옵션은 존재하지 않습니다.**
- 클라이언트의 버전 확인 로직(`UpdateService.CheckForUpdateAsync` → `AppVersion.IsNewer`)은 **더 높은 버전만** 받아들입니다. 즉 **자동 업데이트 경로로는 다운그레이드가 불가능**합니다.

### 1-4. 문제 발생 시 수동 롤백 절차 [수동 절차 제안 — 실행 전례 미확인]

1. `git tag --sort=-creatordate`로 문제 버전 이전의 정상 태그 확인
2. 해당 태그를 기준으로 `.github/workflows/release.yml`을 `workflow_dispatch`로 수동 재실행하거나, 로컬에서 `git checkout vX.Y.Z` 후 `scripts/set-assembly-version.ps1` → MSBuild Release → `scripts/pack-release.ps1`로 동일 패키지를 재생성
3. **문제가 된 GitHub Release를 최신에서 내리고(draft 처리 또는 삭제), 정상 버전을 새로운 패치 번호(예: vX.Y.(Z+1))로 재태깅해 재배포** — Updater가 버전 비교 없이 "주어진 패키지"를 그대로 덮어쓰므로, 기술적으로는 구버전 zip을 수동 배포하는 것도 가능하나, 자동 업데이트 경로(클라이언트 자발적 확인)로는 다운그레이드가 막혀 있으므로 **IT 담당자가 각 PC에 직접 재설치하는 방식**이 되어야 할 가능성이 높습니다.
4. 문제 발생 직후라면, 해당 PC의 `%TEMP%\GBCWorkHubUpdater\<세션>-work\backup\`이 아직 남아있을 수 있어 직전 버전 파일을 수동으로 복사해 쓸 수 있지만, 세션 GUID를 알아야 하고 OS가 이미 정리했을 수도 있어 **공식 복구 경로로 신뢰할 수 없습니다.**

### 1-5. 설정 파일 복원

- `App.config`(배포 후 `GBCWorkHub.exe.config`)는 Release 빌드 시 GitHub Secret `APP_CONFIG_XML`에서 주입됩니다. DB 연결 문자열·사이트 PC 목록이 바뀌는 변경이라면 이 Secret을 갱신한 뒤 재배포해야 합니다(이 저장소에서 Secret 실제 값은 확인하지 않았습니다 — 접근 필요 시 리포지토리 설정에서 확인).
- 로컬 PC에 추출된 설정은 `%LocalAppData%\GBCWorkHub\GBCWorkHub.exe.config`이며, 문제 시 이 파일을 삭제하면 다음 실행 때 exe 내장본으로 재추출됩니다(`BundledConfig.EnsureExtracted`).

### 1-6. 배포 후 정상 동작 확인 / 사이트 간 영향

- 이 앱은 단일 exe로 전 사이트가 공유합니다. **코드/배포 롤백은 전 사이트에 동시에 영향**을 줍니다(사이트별로 다른 버전을 운영할 수 있는 구조가 아님).
- 검증: 실행 후 상단 DB 연결 상태, 사이트 드롭다운에 AURORA/CMC/RC(/MNGHA) 전부 정상 표시되는지, 임의 사이트에서 PC 카드 목록이 뜨는지 확인.

---

## 2. TFS 수집 데이터 롤백

### 2-0. ⚠️ 가장 위험한 단일 명령 — `GBCWorkHub.CsvImportTool.exe --replace` [코드로 확인됨]

이 문서에서 가장 먼저 숙지해야 할 위험입니다. **이름과 실제 동작이 다릅니다.**

- `--replace` 옵션은 "지금 지정한 Excel/CSV 파일(보통 특정 사이트 1건)로 교체한다"는 뜻처럼 보이지만, 실제 구현(`src/GBCWorkHub.DAC/OracleWorkLogRepository.cs`의 `DeleteAllCore`)은 **`WHERE` 절 없이 `MSDWHTKD_WRK_SRC`/`_PRJ`/`_CS`/`MSDWHTKD_WRK` 4개 테이블 전체를 삭제**합니다.
- **`SITE_CD` 필터가 전혀 없습니다** — 즉 AURORA 담당자가 AURORA 엑셀만 다시 올리려고 `--replace`를 실행해도, **CMC/RC/MNGHA를 포함한 전 사이트의 업무기록이 통째로 삭제**된 뒤 지금 지정한 파일 내용만 다시 적재됩니다.
- 삭제 전 확인 프롬프트, 사이트 필터, 자동 백업이 코드 어디에도 없습니다. 단건 삭제(`DeleteByIdCore`)와 마찬가지로 하드 DELETE이며 트랜잭션 보호 범위도 제한적입니다.

**실행 전 필수 절차 [수동 절차 제안 — 스크립트 자체에는 없는 추가 안전장치]**:
```sql
-- 반드시 사전 백업 (전 사이트 대상임에 유의)
CREATE TABLE XSUP.MSDWHTKD_WRK_BAK_20261027 AS SELECT * FROM XSUP.MSDWHTKD_WRK;
CREATE TABLE XSUP.MSDWHTKD_WRK_PRJ_BAK_20261027 AS SELECT * FROM XSUP.MSDWHTKD_WRK_PRJ;
CREATE TABLE XSUP.MSDWHTKD_WRK_SRC_BAK_20261027 AS SELECT * FROM XSUP.MSDWHTKD_WRK_SRC;
CREATE TABLE XSUP.MSDWHTKD_WRK_CS_BAK_20261027 AS SELECT * FROM XSUP.MSDWHTKD_WRK_CS;
```
- **`--replace` 사용 전 반드시 DBA/운영 담당자 사전 승인을 받고, 위 백업을 완료한 뒅에만 실행하세요.** 다른 사이트 데이터까지 날아간 것을 뒤늦게 발견하면 이 백업 없이는 복구 수단이 없습니다.
- 참고: `OracleTfsWorkLogRepository.GetRegisteredChangesetIdsCore`도 `SITE_CD`로 거르지 않고 전사 범위로 등록된 changeset ID를 반환합니다 — 멀티사이트 환경에서 "이미 등록된 changeset" 판정이 사이트 경계를 넘어 교차 적용될 가능성이 있습니다(호출부의 추가 필터 여부는 **확인 필요**).

### 2-1. 관련 스크립트와 각각의 성격

| 스크립트 | 성격 | 대상 |
|---|---|---|
| `sql/33~36_MSDWHTKD_WRK_*_HISTORY_IMPORT.sql` | 사이트별 과거 엑셀 이력의 **1회성 일괄 INSERT** | AURORA/CMC/RC/MNGHA 각각 |
| `sql/37_MSDWHTKD_WRK_HISTORY_IMPORT_CLEANUP_OLD_BATCH.sql` | **파괴적 DELETE** — 구버전(33~36 최초 실행분) 정리용 | 전 사이트 공통(`TEAM_NM='해외사업운영팀'` 한정) |
| `sql/11_MSDWHTKH_PURGE_MONTHLY.sql` | 접속 이력(`MSDWHTKH`) 1개월 초과분 DELETE | 전 사이트 공통 |

### 2-2. 중복 방지 설계

- 33~36번은 `CLIENT_KEY = 'XLSXIMPORT:사이트:티켓번호'` 합성 키로 `SELECT COUNT(*)` 선확인 후에만 INSERT — **DB 제약이 아니라 스크립트 로직**으로 재실행해도 안전(idempotent)하도록 설계되어 있습니다. `MSDWHTKD_WRK` 자체에는 `CLIENT_KEY` UNIQUE 제약이 없습니다.
- 실시간 TFS 수집 경로(§TFS_SCHEDULER_OPERATIONS.md §6)는 `MSDWHTKD_WRK_CS.UNIQUE(SITE_CD, CHANGESET_ID)`만 DB 레벨 제약이며, 동일 changeset 재저장 시 **기존 레지스트리 행을 삭제 후 재삽입**(최종 저장 우선)합니다.

### 2-3. 37번 Cleanup 스크립트 — 상세(현재 열려 있는 파일)

**이 스크립트는 단순 조회가 아니라 명백한 DELETE입니다.**

- 필터 조건: `CLIENT_KEY IS NULL` **그리고** `TEAM_NM = '해외사업운영팀'` — 둘 다 만족하는 행만 삭제 대상
  - `TEAM_NM`이 다른 팀 행, `CLIENT_KEY`가 채워진 행(새 버전 33~36 재실행분, 또는 앱에서 사용자가 직접 저장한 행 — 앱은 저장 시 `ClientKey = item.Id`로 항상 채움)은 영향받지 않음
  - 실질적으로 삭제되는 것은 **"CLIENT_KEY 태그 체계 도입 이전, 33~36번 구버전으로 들어간 해외사업운영팀 이력 전체" 한 배치**
- 처리 순서: `MSDWHTKD_WRK_SRC` 자식 삭제(이미 `ON DELETE CASCADE`라 사실상 중복이나 무해) → `MSDWHTKD_WRK_PRJ` 자식 삭제 → `MSDWHTKD_WRK` 부모 삭제 → `COMMIT`
- **백업/드라이런 로직 없음**: 스크립트 안에 백업 테이블 생성, RENAME 등 안전장치가 전혀 없습니다. 유일한 안전장치는 삭제 전/후 `SELECT COUNT(*)` 확인과, `COMMIT`이 삭제 직후 바로 실행된다는 점(세션을 끊기 전까지만 `ROLLBACK` 가능)입니다.

**실행 전 권고 절차 [수동 절차 제안 — 스크립트 자체에는 없는 추가 안전장치]**:
```sql
-- 1) 영향 건수 확인 (스크립트에 이미 포함)
SELECT COUNT(*) FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀';

-- 2) 삭제 전 반드시 별도 백업 테이블로 떠 둘 것 (스크립트에는 없음 — 직접 추가)
CREATE TABLE XSUP.MSDWHTKD_WRK_BAK_20261027 AS
SELECT * FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀';
-- PRJ/SRC도 동일하게 LOG_ID IN (...) 조건으로 백업

-- 3) 37번 스크립트 실행 (COMMIT 전에 재확인)

-- 4) 삭제 후 확인 (스크립트에 이미 포함, 0건이어야 함)
```
**사전 승인 필요**: 운영 DB의 DELETE이므로, 담당자 승인 없이 실행하지 마세요.

### 2-4. 잘못 수집된 데이터 식별 방법

- 사이트 구분: `MSDWHTKD_WRK.SITE_CD` (AURORA/CMC/RC/MNGHA)
- 특정 수집 실행 단위 식별: 실시간 수집분은 `CHANGESET_ID`(해당 changeset 단위로 식별), 과거 일괄 이관분은 `CLIENT_KEY`(`XLSXIMPORT:사이트:티켓번호`) 또는 37번 스크립트 기준의 `CLIENT_KEY IS NULL AND TEAM_NM='해외사업운영팀'`
- 날짜 기준 식별: `MSDWHTKD_WRK`의 등록/작성일시 컬럼으로 특정 기간 필터(정확한 컬럼명은 `docs/DB_Schema_Reference.md` 및 `sql/04_MSDWHTKD_WRK_DDL.sql` 참고)

### 2-5. 재수집 시 중복 발생 가능성

- 실시간 TFS 재수집(체크인 보관함의 "재조회")은 §TFS_SCHEDULER_OPERATIONS.md §6의 last-write-wins 특성상, 같은 changeset을 다시 가져와 **다른 업무기록으로 저장하면 레지스트리 소유권만 이동**하고 이전 업무기록 자체가 자동으로 지워지지는 않습니다 — 즉 "두 업무기록에 같은 changeset 설명이 남아있는" 형태의 실질적 중복이 발생할 수 있습니다. 재수집 후에는 반드시 기존 업무기록 목록에서 중복 여부를 육안 확인하세요.
- 과거 일괄 이관(33~36번)은 `CLIENT_KEY` 중복 체크로 재실행 안전하도록 설계되어 있으나, 이 체크 로직 자체의 버그 가능성까지 배제할 수는 없습니다(코드 리뷰로만 확인, 대량 재실행 테스트는 하지 않음 — **확인 필요**).

### 2-6. 데이터 복구 후 검증

- 삭제/정정 작업 후에는 해당 `SITE_CD`의 업무기록 목록 화면에서 건수와 최신 항목을 WorkHub UI로 직접 확인
- `MSDWHTKD_WRK_CS` 레지스트리와 실제 `MSDWHTKD_WRK.CHANGESET_ID`가 1:1로 맞는지 샘플 대조 권고

---

## 3. 사이트별 특이사항 요약

| 항목 | AURORA | CMC | RC | MNGHA |
|---|---|---|---|---|
| 점유 키 | IP 주소 | PC명 | PC명 | **확인 필요**(README에 명시 안 됨) |
| 접속 방식 | mstsc 직접 | 브라우저 PMP 웹(ManageEngine) | FortiClient VPN → 게시 RDP(.rdp 파일) | mstsc 직접(AURORA와 동일 분류로 추정) |
| TFS 서버 | 전용(§TFS_SCHEDULER_OPERATIONS §3) | 전용 | 전용 | 실시간 수집 없음 |
| 시간대(App.config `SiteTimeZone.*`) | US Mountain Standard Time | Arabian Standard Time | Arab Standard Time | Arab Standard Time |
| App.config.example 등록 PC 수 | 15대 | 9대 | 25대 | 0대(비활성) |
| 코드/배포 롤백 영향 범위 | 전 사이트 공통(단일 exe) | 전 사이트 공통 | 전 사이트 공통 | 전 사이트 공통 |
| TFS 데이터 롤백 영향 범위 | `SITE_CD='AURORA'`로 한정 가능 | `SITE_CD='CMC'`로 한정 가능 | `SITE_CD='RC'`로 한정 가능 | `SITE_CD='MNGHA'`로 한정 가능 |

**한 사이트 장애가 다른 사이트에 주는 영향**:
- TFS 서버/SessionAgent는 사이트별로 완전히 독립 — 한 사이트 TFS 장애가 다른 사이트 수집을 막지 않음.
- Oracle DB(`XSUP.MSDWHTKD*`)와 WorkHub exe 자체는 전 사이트 공용 — **DB 장애나 앱 배포 문제는 전 사이트 동시 장애**로 번짐.
- 원격 PC 점유 상태(`MSDWHTKD`)는 PC 단위로 독립되어 있어, 한 PC의 점유 꼬임이 다른 PC 점유에 직접 영향을 주지는 않습니다(동시성 제어가 PC별 조건부 UPDATE이기 때문).

---

## 4. 장애 유형별 복구 — 증상 → 원인 후보 → 확인 방법 → 조치 → 복구 확인 → 재발 방지

### 4-1. 특정 사이트의 데이터만 수집되지 않는 경우
→ [TFS_SCHEDULER_OPERATIONS.md §9-1](TFS_SCHEDULER_OPERATIONS.md#9-1-특정-사이트만-tfs-데이터가-수집되지-않음) 참고.

### 4-2. 모든 사이트의 TFS 수집이 중단된 경우
- **증상**: AURORA/CMC/RC 전부에서 체크인 보관함이 비어 있음
- **원인 후보**: (a) 각 사이트 TFS 서버가 동시에 죽는 것은 가능성 낮음 → (b) 로컬 WorkHub 버전 문제로 클립보드 수신/파싱이 깨졌을 가능성, (c) Oracle DB 장애로 "수집은 됐는데 저장만 안 되는" 것이 "수집 자체가 안 되는" 것으로 오인되는 경우
- **확인 방법**: WorkHub 상단 DB 연결 상태 확인 → 정상이면 최근 배포(버전) 변경 여부 확인(`git log --oneline -10`) → 클립보드 수신 로그(`DiagnosticLogger`) 확인
- **조치**: DB 문제면 DBA 에스컬레이션. 최근 배포 문제로 의심되면 §1-4 롤백 절차
- **복구 확인**: 아무 사이트에서나 재접속 후 체크인 보관함 정상 표시
- **재발 방지**: 배포 전 최소 1개 사이트 PC로 스모크 테스트

### 4-3. Windows 작업 스케줄러가 실행되지 않는 경우
- **증상**: `Last Run Time`이 오래전, 또는 `Status`가 `Disabled`
- **원인 후보**: 수동/정책으로 비활성화됨, 로그인 계정이 바뀌어 트리거 주체(SID)가 무효화됨, 이벤트 로그 자체가 비활성화됨
- **확인 방법**: `schtasks /query /tn "<작업명>" /fo LIST /v`의 `Scheduled Task State` 확인, `Get-WinEvent -ListLog "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational"` 로 이벤트 로그 활성 여부 확인
- **조치**: `Enable-ScheduledTask`로 재활성화, 계정이 바뀌었다면 해당 사이트 설치 스크립트 재실행(재등록)
- **복구 확인**: 재접속 후 즉시 트리거되어 `SessionAgent.log`에 새 라인 기록되는지 확인
- **재발 방지**: PC 재설치/계정 변경 작업 체크리스트에 "SessionAgent 재등록" 항목 추가(§TFS_SCHEDULER_OPERATIONS.md §8)

### 4-4. 스케줄러는 성공으로 표시되지만 데이터가 갱신되지 않는 경우
→ [TFS_SCHEDULER_OPERATIONS.md §9-3](TFS_SCHEDULER_OPERATIONS.md#9-3-스케줄러는-성공으로-표시되는데-데이터가-갱신되지-않음) 참고.

### 4-5. TFS 인증 또는 연결이 실패하는 경우
→ [TFS_SCHEDULER_OPERATIONS.md §9-4](TFS_SCHEDULER_OPERATIONS.md#9-4-tfs-인증연결-실패) 참고.

### 4-6. 특정 기간의 데이터가 누락된 경우
→ [TFS_SCHEDULER_OPERATIONS.md §9-5](TFS_SCHEDULER_OPERATIONS.md#9-5-특정-기간-데이터-누락--중복-수집) 및 본 문서 §2-4 참고.

### 4-7. 데이터가 중복 수집된 경우
- **증상**: 같은 changeset 내용이 업무기록 목록에 2건 이상 보임
- **원인 후보**: §2-5의 last-write-wins 구조상 재조회를 반복 실행한 경우
- **확인 방법**: `SELECT * FROM XSUP.MSDWHTKD_WRK WHERE CHANGESET_ID = :id` 로 중복 행 조회(정확한 컬럼은 `docs/DB_Schema_Reference.md` 확인)
- **조치**: 중복 행 중 하나를 수동 삭제(앱 UI의 삭제 기능 사용 가능 — `WorkLogBiz.DeleteAsync` 존재) — **DB 직접 DELETE는 사전 승인 및 백업 후 진행**
- **복구 확인**: 목록에 1건만 남는지 확인
- **재발 방지**: 재조회 전 기존 목록에 이미 등록된 changeset인지 먼저 확인하는 습관화(UI의 "이미 가져온 changeset 제외" 필터는 DRAFT 상태 건은 걸러내지 못함에 유의 — §TFS_SCHEDULER_OPERATIONS.md §6)

### 4-8. 수집된 데이터와 화면 표시 결과가 다른 경우
- **원인 후보**: WorkHub 클라이언트 캐시(세션 내 로컬 캐시 `_localImportedChangesetIds` 등)와 DB 실제 상태의 불일치, 여러 사용자가 동시에 접속 중인 환경에서의 갱신 타이밍 차이
- **확인 방법**: 앱 재시작(캐시 초기화) 후 동일 현상 재현 여부 확인
- **조치**: 재현되면 DB 쿼리로 실제 값을 직접 확인해 UI 바인딩/쿼리 로직 버그로 좁혀 조사(코드 변경이 필요하면 사전 보고 후 진행 — 최상단 원칙 참고)
- **복구 확인**: 재시작 후 DB 값과 화면 값 일치
- **재발 방지**: 이 유형의 재현 패턴을 기록해두고 반복되면 캐시 무효화 로직 점검을 별도 과제로 제안

### 4-9. 배포 이후 기존 기능이 동작하지 않는 경우
- **증상**: 업데이트 후 특정 화면/기능이 깨짐
- **원인 후보**: 신규 배포 버전의 버그
- **확인 방법**: 문제 재현 후 `C:\GBCWorkHub\Logs\Crash-*.log`(크래시 로그) 및 `WorkHub-yyyyMMdd.log` 확인, 버전 번호(설정 화면/실행파일 속성) 확인
- **조치**: §1-4 코드/배포 롤백 절차. 자동 다운그레이드가 막혀 있으므로 **IT 담당자가 각 PC에 구버전 패키지를 수동 재설치**해야 할 가능성이 높음을 인지
- **복구 확인**: 구버전 재설치 후 문제 기능 정상 동작 확인
- **재발 방지**: 릴리즈 전 체크리스트에 "주요 화면 스모크 테스트" 추가, 다음 패치 버전에서 근본 수정 후 재배포

### 4-10. 데이터베이스 오류가 발생한 경우
- **증상**: WorkHub 상단에 DB 연결 실패 표시, 저장/조회 시 예외
- **원인 후보**: Oracle 서버 장애, 계정 권한/비밀번호 만료, 네트워크(TNS) 문제, 테이블/시퀀스 누락(수동 DDL 작업 중 실수 포함)
- **확인 방법**: `sql/README_DB_SETUP.md`의 GRANT 목록과 실제 권한 비교, TNS 접속 테스트(`tnsping` 또는 SQL*Plus 접속), Oracle 서버 측 로그/알림 확인(이 저장소 범위 밖)
- **조치**: DBA 에스컬레이션. 애플리케이션 코드 변경이 필요한 문제가 아니라면 WorkHub 쪽에서 할 수 있는 조치는 제한적(재연결 폴링이 자동으로 계속 시도함)
- **복구 확인**: DB 연결 상태 정상 표시, `SELECT 1 FROM DUAL` 성공
- **재발 방지**: DB 계정 비밀번호 만료 주기를 운영 캘린더에 등록해 사전 갱신

---

## 5. 확인 필요 항목 모음 (이 문서 전용)

- `guides`라는 비-버전 git 태그의 용도
- v1.3.9 이전 버전의 git 태그 부재 사유
- `deploy/GBCWorkHub.UI.zip`, `deploy/GBCWorkHub.UI_Deploy.zip`이 실제로 어느 시점/버전에 대응하는지(내용 미확인)
- 운영 환경에서 "배포 실패 후 수동 롤백"을 실제로 수행한 전례나 공식 런북이 이 문서 이전에 별도로 존재했는지
- MNGHA의 점유 키 방식(IP vs PC명)
- 37번 Cleanup 스크립트의 실행 이력 — 이미 실행되어 재실행이 불필요한 상태인지, 아직 미실행 상태인지

---

변경 이력:
- 2026-10-27: 최초 작성 (배포/버전관리/SQL 스크립트 코드 조사 기반, 읽기 전용 — DB/git/스케줄러에 어떠한 변경도 가하지 않음)
- 2026-10-27: 별도 DB 롤백 전담 조사와 교차검증 — §2-0 `CsvImportTool --replace`(전사이트 WRK 테이블 무조건 삭제) 위험 경고 신규 추가. 이것이 이 저장소에서 가장 위험한 단일 명령으로 확인됨
