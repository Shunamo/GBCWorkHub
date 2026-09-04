# GBCWorkHub — RDP Clipboard as Communication Channel: Reference

> 대상: 로컬 WPF 클라이언트("WorkHub")와 RDP 세션 내부의 원격 PC에서 단발 실행되는 프로그램("SessionAgent") 간 통신 설계.
> 본 문서는 실제 코드(`src/GBCWorkHub.UI/Services/{ClipboardMonitorService.cs, TfsSync/*}`, `src/GBCWorkHub.BIZ/*`, `tools/GBCWorkHub.SessionAgent/*`, `tools/Aurora_SessionAgent.ps1`)를 직접 읽고 검증해 작성했다. 추측 없음. 신뢰도 표기: **[확인된 구현]** **[부분 구현]** **[미구현]** **[추정 불가]**.

---

## 1. WHY — 왜 클립보드를 통신망으로 썼는가

### 1-1. 근본 제약

로컬 WorkHub 프로세스와 원격 PC는 **서로 다른 머신**이며, 접점은 RDP 세션 하나뿐이다. 원격 PC 내부에서 실행되는 SessionAgent는 **상주 프로세스가 아니라 RDP 로그온/로그오프 시점에만 단발 실행되는 프로그램**이고, 로컬↔원격 사이에는 소켓/HTTP/Named Pipe 등 어떤 직접 네트워크 채널도 존재하지 않는다(원격 SessionAgent → TFS 서버로의 HTTP는 별도로 존재하지만, 이는 로컬 WorkHub와 무관한 아웃바운드 호출이다).

이 제약이 코드 주석에 명시적으로 드러난다:

```csharp
// tools/GBCWorkHub.SessionAgent/ClipboardHelper.cs:8
/// RDP 클립보드 = 제어 채널 + 사용자 복붙 공유.
```

```csharp
// tools/GBCWorkHub.SessionAgent/Program.cs:71-77
// No FileSystemWatcher, no resident process ...
// The SYNC_REQUEST clipboard message ... is reused here as the only channel
// that carries SESSION_TOKEN to this machine
```

즉 RDP가 표준으로 제공하는 **클립보드 리다이렉션**(로컬 클립보드와 원격 세션 클립보드를 `rdpclip.exe`가 자동 동기화하는 기능)을 유일하게 이용 가능한 양방향 데이터 이동 수단으로 판단하고, 이를 프로토콜 전송 매체로 **의도적으로 전용(re-purpose)**한 것이다. [확인된 구현]

### 1-2. 왜 다른 방법이 아니었는가 (코드가 뒷받침하는 근거)

- **파일 공유/네트워크 드라이브**: 원격 PC가 사내망에서 격리되어 있어 로컬에서 직접 접근 불가한 환경으로 추정(SessionAgent가 원격에서 실행되어야만 하는 이유 자체가 이것). 코드에 별도 파일 공유 경로 접근 로직 없음.
- **원격 PC에 상주 서버 프로그램을 두는 방식**: `Program.cs:71-77` 주석이 명시적으로 이를 배제 — "No FileSystemWatcher, no resident process." 상주 프로세스를 두면 배포/방화벽/보안 승인 부담이 커지므로, RDP 로그온 이벤트에 바인딩된 **단발 실행 스크립트/exe**로 설계했다.
- **TFS 서버 자체를 로컬이 직접 조회**: 클라이언트(GBCWorkHub.UI) 어디에도 `Microsoft.TeamFoundation.*` 참조가 없다(0건) — 로컬 PC는애초에 TFS 서버에 네트워크 경로가 없거나 인증 컨텍스트가 다른 것으로 추정되며, TFS 조회는 원격 PC(SessionAgent)만 수행한다.

결론: **RDP 클립보드 리다이렉션은 "우연히 쓰인 편의 기능"이 아니라, 다른 통신 수단이 전혀 없는 환경에서 선택된 유일한 실행 가능 채널**이다. [확인된 구현, 코드 주석 근거]

---

## 2. WHAT — 프로토콜 전체 설계

리포지토리 전체에서 `"GBCWORKHUB[A-Z_]*::"` 패턴을 전수 검색한 결과, **정확히 6종의 prefix**가 하나의 클립보드 슬롯을 멀티플렉싱한다.

| Prefix | 방향 | 데이터 형식 | 용도 |
|---|---|---|---|
| `GBCWORKHUB::` | 원격→로컬 | JSON: `{type:"GBC_RDP_STATUS", schemaVersion, computerName, windowsUser, clientName, collectedAt, latestEventId, latestRecordId, sessionRaw, triggerType, isVerifiedDisconnect, events}` | RDP 접속/해제 상태 통지 |
| `GBCWORKHUB_TFS::` | 원격→로컬 | JSON: `{type:"GBC_TFS_RECENT_CHANGESETS", success, requestId, deliveryId, deliveryMode, authorizedUserId, computerName, queryMode, sessionStartAt/EndAt, sessionToken, changesets:[...]}` | TFS 체크인 목록 응답 |
| `GBCWORKHUB_ACK::{deliveryId}` | 로컬→원격 | 평문(JSON 아님, prefix+ID) | 수신 확인(ACK) |
| `GBCWORKHUB_TFS_SYNC_REQUEST::` | 로컬→원격 | JSON(`TfsSyncRequestClipboardDto`): `type, requestId, targetComputerName, remoteIp, requestedAtUtc, sessionStartedAtUtc, sessionEndedAtUtc, sessionToken` | TFS 재조회 요청(sticky, 완료 전까지 유지) |
| `GBCWORKHUB_SESSION_TOKEN::` | 로컬→원격 | JSON: `{requestId, sessionToken, targetComputerName, remoteIp}` | 개발세션(파일변경) 2점 추적용 토큰 공지 |
| `GBCWORKHUB_SESSION_RESULT::` | 원격→로컬 | JSON(`SessionChangeReportDto`) | 2점(connect/disconnect) 파일 변경 추적 결과 |

추가로 prefix 없는 평문 포맷 하나: RC 사이트 레거시 한 줄 텍스트 `"yyyy-MM-dd HH:mm:ss | DOMAIN\user | PCNAME | connect|disconnect|logoff|reconnect"`(`RdpStatusBiz.cs:22-24` 정규식으로 감지, `GBC_RDP_STATUS`로 변환).

**파싱 우선순위**(`ClipboardMonitorService.ProcessClipboardText`, 긴 것부터 검사): `SESSION_RESULT → SESSION_TOKEN(로컬은 무시) → TFS → ACK(로컬은 무시) → SYNC_REQUEST(로컬은 무시) → GBCWORKHUB:: → RC 한 줄 정규식`.

**`GBCWORKHUB_TFS_SYNC_REQUEST::`와 `GBCWORKHUB_SESSION_TOKEN::`가 분리된 이유**(코드 주석): 이미 필드에 배포된 `Aurora_SessionAgent.ps1`이 SYNC_REQUEST prefix 유무로 "이건 TFS 재조회용 재접속"을 판단하기 때문에, 매 접속마다 이를 올리면 일반 접속도 TFS 재조회로 오인된다. 그래서 세션 토큰 공지는 **별도 prefix로 신설**했다. [확인된 구현]

---

## 3. HOW — 구현 상세

### 3-1. 변경 감지 — 이벤트 기반(폴링 아님)

```csharp
// ClipboardMonitorService.cs
private const int WM_CLIPBOARDUPDATE = 0x031D;
AddClipboardFormatListener(_hwnd);              // Win32 P/Invoke
HwndSource.AddHook(WndProc);                    // WPF 윈도우 메시지 후킹
```
클립보드가 바뀔 때마다 Windows가 `WM_CLIPBOARDUPDATE` 메시지를 보내고, 그때만 `Clipboard.GetText()`를 호출한다. **타이머 폴링이 아니다.**

**예외**: CMC(웹 기반 PMP RDP) 시나리오는 이벤트가 안정적으로 오지 않아 `TfsSyncCoordinator.WaitClipboardOnlyFetchAsync`가 **500ms 간격, 최대 120초**의 명시적 폴링 루프를 별도로 돈다(4틱=2000ms마다 SYNC_REQUEST 재기록 포함).

### 3-2. 읽기/쓰기 재시도(robustness)

| 위치 | 재시도 | 지연 | 대상 |
|---|---|---|---|
| `ClipboardMonitorService.TryReadClipboard` | 3회 | 50ms | `Clipboard.GetText()` 예외(다른 프로세스의 일시적 잠금) |
| 원격 `ClipboardHelper.TryCopyToClipboard`(SetText/Restore/Clear) | 각 5회 | 200ms | 원격 클립보드 API 예외 |
| `Aurora_SessionAgent.ps1 Set-ClipboardTextSafe` | 5회 | 200ms | PowerShell `Set-Clipboard` |
| 로컬 `WpfClipboardAccessor` | 재시도 없음(1회) | — | 재시도 책임은 `ClipboardMonitorService` 쪽에만 존재 |

### 3-3. 백업/복원 — Lease 상태 기계 (`ClipboardProtocolLeaseManager` / `ClipboardProtocolLease`)

이 부분이 프로토콜의 핵심 안전장치다.

**Lease 필드**: `Kind`(Ack=일시 / SyncRequest=완료 전까지 자동복원 없음), `WrittenText`+`WrittenHash`(SHA-256, 로그에는 해시만 남김), `UserBackup`(쓰기 직전 사용자 클립보드), `IsDisplaced`(한 번이라도 CAS 실패 시 영구 true), `Generation`(단조 증가 진단용 카운터).

**핵심 원칙 — Exact CAS(Compare-And-Swap)**: prefix가 `GBCWORKHUB`로 시작한다는 것만으로 복원/삭제하지 않는다. **자신이 실제로 쓴 정확한 문자열(`WrittenText`)이 지금도 클립보드에 그대로 있을 때만** 복원/갱신을 수행한다.
- 재전송 시도(`TryRefreshSyncRequest`)마다 exact-CAS를 재확인 → 실패하면 원격의 미수집 응답(`GBCWORKHUB_TFS::`/`GBCWORKHUB_SESSION_RESULT::`)이 와 있는지 먼저 확인 → 그것도 아니면 `IsDisplaced=true`로 표시하고 **이후 영구히 재전송 스킵**(다른 세션/사용자 내용을 덮지 않기 위함).
- Lease 종료 시(`TryEndLease`)에도 (a) 미수집 inbound payload면 손대지 않고, (b) prefix 없는 일반 텍스트면 손대지 않고, 그 외에만 `UserBackup` 복원 또는 클립보드 Clear.
- **동시에 활성 lease는 1개뿐**(`_activeLease` 단일 슬롯) — 두 프로토콜 문구가 동시에 클립보드를 요구하는 경쟁 상황 자체를 이 불변식으로 차단한다.

**`rdpclip.exe` 클립보드-비움 우회**: RDP 세션이 끊기면 `rdpclip.exe`가 클립보드를 강제로 비우는 부작용이 있다. 코드 주석이 이를 "고칠 수 없는 외부 동작"으로 명시하고 우회한다:
```
// TfsClipboardAckService.cs
// rdpclip이 클립보드를 비우는 그 이벤트를 복원으로 바꾼다.
// rdpclip.exe는 못 고치므로, 비움 통지에서 사용자 글을 다시 넣는다.
```
연결 해제 후 **15초짜리 armed 구간**을 설정해 그 안에서만 빈 클립보드 이벤트를 "복원 신호"로 해석한다(구간 밖의 빈 클립보드는 정상적인 사용자 Clear로 간주해 무시).

### 3-4. 비밀번호(자격증명) 보호

mstsc/PMP 로그인 자동 붙여넣기 직전, `PrepareClipboardForCredentials`가 아웃바운드 프로토콜 문구(SESSION_TOKEN 등)만 걷어내고 사용자가 복사해둔 원래 값(비밀번호 등)을 복원한다 — **프로토콜이 로그인 실패를 유발하지 않도록 하는 명시적 안전장치**. 미수집 TFS/SESSION_RESULT 응답은 이 과정에서도 보존된다. 단위테스트(FakeClipboardAccessor 기반) 3종으로 "토큰이 비번을 덮은 경우 복원", "이미 비번이면 유지", "토큰 위에 비번을 다시 복사해도 덮지 않음" 시나리오가 검증되어 있다.

### 3-5. 상관관계(correlation) 검증

원격 응답을 받아들이기 전 3중 검증을 수행한다(`TfsPayloadIngestService.ValidateCorrelation`, `TfsSyncCoordinator.TryValidateIncoming`에 각각 독립 구현):
1. **requestId** — 활성 요청의 requestId와 정확히 일치해야 함
2. **computerName loose-match** — 도메인/FQDN 제거 후 비교, 하이픈/언더스코어 차이(`KEB-3VNZVP2` vs `KEB3VNZVP2`)도 동일로 취급
3. **deliveryMode** — 활성 요청 중에는 `PENDING_RECONNECT` 또는 `DISCONNECT`(RC)만 허용

세 조건 중 하나라도 실패하면 응답을 거부(엉뚱한 세션/PC의 클립보드 값을 잘못 채택하지 않도록).

### 3-6. 타이밍 상수

| 상수 | 값 | 목적 |
|---|---|---|
| ACK/상태 hold | 1500ms | 표시 후 사용자 클립보드 복원까지 |
| 세션토큰 유지 | 5000ms | 접속 완료 후 SESSION_TOKEN 유지 |
| TFS payload 대기 타임아웃 | 60000ms | |
| RDP 재접속 확인 타임아웃 | 60000ms | |
| ACK 기록 후 mstsc 종료 대기 | 1000ms | |
| SYNC_REQUEST 기록 후 mstsc 실행 대기 | 2000ms | 원격 rdpclip 준비 시간 확보 |
| CMC 클립보드 폴링 간격 | 500ms(재기록 2000ms마다) | 이벤트 미수신 환경 보완 |
| CMC 전체 대기 | 120000ms | 재접속 시간 포함 |
| rdpclip 비움 armed 윈도 | 15초 | 재접속 종료 후 복원 유효 구간 |
| 원격 표준 hold | 2500ms | 일반 상태 문구 |
| 원격 개선판(agent-updates) hold | 1500ms | 동일 목적, 단축됨 |
| 원격 TFS payload hold | 4000ms | 더 큰 데이터라 더 길게 |
| agent-updates 중복 발행 TTL | 30분 | 동일 내용 재전송 포기 기준 |
| agent-updates 최대 재시도 | 20회 | 재기록 상한 |

### 3-7. 원격(SessionAgent) 측 트리거 방식 — 사이트별로 다름

| 배포 형태 | 트리거 | 특이사항 |
|---|---|---|
| `tools/GBCWorkHub.SessionAgent`(모듈화 .csproj) | `connect`/`disconnect` 인자로 실행, disconnect에서 즉시 TFS 수집 | `DevSession/*`로 2점(connect/disconnect) 파일 스냅샷 비교 + TFVC 대조 지원 |
| `tools/Aurora_SessionAgent.ps1` | Windows Task Scheduler의 **RDP 로그온 이벤트(ID 21)로만** 트리거 | disconnect 트리거 자체가 없음(`Invoke-Disconnect`는 로그만 남김) — **접속 시점에만** 이전 세션의 pending TFS를 확인해 전송하는 구조로, 표준판과 이벤트 전략이 근본적으로 다름 |
| `tools/agent-updates/GBCWorkHub.SessionAgent`(개선판) | 표준판과 동일 트리거 | 클립보드 exact-CAS + 중복발행 방지(`ClipboardDeliveryState`, TTL 30분/최대 20회) 추가, 대신 **DevSession 2점 추적 기능이 통째로 빠짐** — 표준판의 상위호환이 아니라 서로 다른 방향으로 분기한 브랜치 |

### 3-8. `TfsLocalDummyPayload.cs` — 로컬 시뮬레이션 경로 (현재 비활성)

원격 SessionAgent를 완전히 우회하고 더미 changeset 5건(ID 900001~900005)을 로컬에서 즉시 생성해 실제 저장 파이프라인(`TfsPayloadIngestService.Ingest` → 체크인 보관함 JSON)에 흘려보내는 개발/테스트용 경로가 존재한다. 다만:
- 진입 조건(`TfsSyncRequest.UseLocalDummyPayload` 또는 `TfsLocalDummyPayload.IsEnabled`)이 **코드상 상시 `false`로 고정**되어 있고, 이를 `true`로 바꾸는 코드 경로가 리포지토리 어디에도 없다.
- `App.config`에 `Tfs.UseLocalDummyPayload` 키가 존재하지만 **이 값을 읽어 실제 요청 객체에 배선하는 코드가 없어**, config를 바꿔도 동작하지 않는 죽은 설정이다.
- 결론: **현재 빌드에서는 도달 불가능한 dev-only 경로**이며 프로덕션 오발동 위험은 사실상 없다. [확인된 구현 — dev/test 전용]

---

## 4. END-TO-END TRACE

### (a) RDP 접속/해제 상태 신호 (Aurora 예시)
```
Task Scheduler(RDP 로그온 Event 21) → Aurora_SessionAgent.ps1 connect 실행
 → SYNC_REQUEST 짧게 peek(2초) → 없으면 New-RdpStatusObject(EventId=21) 생성
 → Send-ClipboardPayload → "GBCWORKHUB::"+JSON 클립보드 기록
[rdpclip 리다이렉션으로 로컬 클립보드 동기화]
 → ClipboardMonitorService.WndProc(WM_CLIPBOARDUPDATE) → ProcessClipboardText
 → RdpStatusBiz.TryHandleClipboardText → DetermineStatus(EventId=21 → "사용 중")
 → UI/DB 상태 갱신
[세션 종료 시 rdpclip이 클립보드를 비움 → 15초 armed 구간에서 사용자 클립보드 자동 복원]
```

### (b) TFS 체크인 가져오기 (일반 mstsc 재접속 경로)
```
사용자 "가져오기" 클릭
 → requestId 발급, "GBCWORKHUB_TFS_SYNC_REQUEST::"+JSON 클립보드 기록(사용자 클립보드 백업)
 → 2초 대기(rdpclip 준비) → mstsc 재접속(TfsSyncReconnect 목적)
[원격] 재로그인 트리거 → Try-ReadSyncRequest로 요청 확인 → Test-TargetedAtThisPc로 대상 검증
 → 먼저 "GBCWORKHUB::"(CONNECT) 상태를 2.5초 hold(로컬이 먼저 접속 확인하도록)
 → TFS OM/REST 조회 → "GBCWORKHUB_TFS::"+JSON 기록, ACK 도착까지 최대 6초 대기
[로컬] 클립보드 수신 → 3중 검증(requestId/computerName/deliveryMode) 통과
 → TfsPayloadIngestService.Ingest → 체크인 보관함 저장 성공 시 "GBCWORKHUB_ACK::"+deliveryId 기록
[원격] ACK prefix 감지 → 사용자 클립보드 복원 후 종료
[로컬] mstsc 강제 종료(CloseSyncRdpSafe)
```

### (c) 2점(connect/disconnect) 개발세션 파일 추적
```
모든 RDP 접속 직전: "GBCWORKHUB_SESSION_TOKEN::"+JSON 클립보드 기록
[원격] SyncRequest.TryParse로 최대 10회(300ms 간격) 폴링해 토큰 수신
 → connect 시점 파일 스냅샷(경로+타임스탬프+해시) 캡처, TFVC pending 조회
[원격] disconnect 시점: 재스냅샷 → diff 계산 → TFVC changeset 대조로 최종 분류
 → "GBCWORKHUB_SESSION_RESULT::"+JSON 클립보드 기록
[로컬] ClipboardMonitorService → SessionChangeResultReceived → DevSessionFileBiz가 결과 저장
```
(※ Aurora_SessionAgent.ps1에는 `SESSION_TOKEN` 처리 코드가 없어, 이 흐름은 사이트에 따라 동작하지 않을 수 있음 — §5-7 참고)

---

## 5. 알려진 한계 / 리스크 (코드/주석 근거가 있는 것만)

1. **암호화 없음** — 모든 `GBCWORKHUB*` payload는 평문 JSON으로 클립보드에 올라간다. 서명/암호화 로직은 코드 어디에도 없다. RDP 클립보드 리다이렉션 채널 자체의 기밀성에 전적으로 의존한다.
2. **단일 활성 lease** — 동시에 두 프로토콜 문구(예: ACK와 SYNC_REQUEST)를 유지할 수 없다. 나중 것이 이전 것의 지연 복원 콜백을 무효화하는 방식으로 설계되어 있다.
3. **`rdpclip.exe` 타이밍 의존** — 15초 armed 윈도는 마이크로소프트 프로세스의 비결정적 타이밍에 대한 워크어라운드이며, 코드 주석 자체가 이를 "고칠 수 없는 외부 동작"이라 인정한다.
4. **검증 로직 중복 구현** — 상관관계 검증(requestId/computerName/deliveryMode)이 두 클래스(`TfsPayloadIngestService`, `TfsSyncCoordinator`)에 독립적으로 각각 구현되어 있어, 한쪽만 수정하면 기준이 어긋날 위험이 있다.
5. **CMC 경로 장시간 폴링** — 최대 120초간 500ms 간격으로 클립보드를 반복 확인하며, 이 구간에 사용자가 다른 것을 복사하면 `Displaced` 상태가 되어 사용자가 다시 시도하기 전까지 복구되지 않는다.
6. **죽은 설정 존재** — `Tfs.UseLocalDummyPayload` App.config 키는 실제로 아무 코드와도 연결되어 있지 않아, 값을 바꿔도 동작이 변하지 않는다.
7. **사이트 간 기능 비대칭** — Aurora(PowerShell)는 `SESSION_TOKEN`/`SESSION_RESULT` 처리 코드 자체가 없어, 2점 개발세션 추적 기능이 Aurora 사이트에서는 동작하지 않을 가능성이 있다(로컬은 사이트 구분 없이 동일하게 토큰을 발행함).
8. **표준 SessionAgent와 개선판(agent-updates) 간 트레이드오프** — 개선판은 클립보드 안정성(exact-CAS, 중복방지)은 더 낫지만 2점 개발세션 추적 기능이 아예 빠져 있어, 둘을 그대로 교체 배포하면 기능 손실이 발생한다.

---

## 6. 요약 (한 문단)

GBCWorkHub는 로컬 클라이언트와 RDP 세션 내부에서 단발 실행되는 원격 프로그램(SessionAgent) 사이에 소켓/HTTP 같은 직접 통신 경로가 전혀 없는 환경에서, RDP가 표준 제공하는 클립보드 리다이렉션(`rdpclip.exe`)을 유일한 양방향 채널로 의도적으로 전용했다. 6종의 prefix로 하나의 클립보드 슬롯을 멀티플렉싱하고, exact-CAS 기반 lease 상태 기계로 사용자의 원본 클립보드 내용을 보호하며, requestId/computerName/deliveryMode 3중 검증으로 잘못된 세션의 응답을 걸러낸다. 암호화가 없고 `rdpclip.exe`의 비결정적 타이밍에 의존한다는 점이 이 설계의 근본적인 트레이드오프다.
