# GBCWorkHub UML

전체 기능 UML 소스 (**PlantUML**). 클립보드만이 아니라 **앱 셸 / 원격 점유 / RC·CMC / TFS 동기화 / WorkLog / DevSession**을 포함합니다.

## 산출물

| 종류 | 위치 |
|------|------|
| UML 소스 | `docs/uml/*.puml` |
| PNG 미리보기 | `docs/uml/png/*.png` |
| FigJam (시퀀스·개요) | [figma.com/board/tPVMe2xdqDajrx7gvdG0KW](https://www.figma.com/board/tPVMe2xdqDajrx7gvdG0KW) |

FigJam에는 Figma가 지원하는 **시퀀스/플로우**만 올렸습니다. **클래스·커뮤니케이션 다이어그램은 PlantUML이 정식 UML**입니다 (Figma `generate_diagram`은 class diagram 미지원).

## 보는 방법

1. `docs/uml/png/` PNG 열기
2. Cursor/VS Code **PlantUML** 확장으로 `.puml` 미리보기
3. 재렌더 (JDK + `plantuml.jar` 필요):

```powershell
java -jar docs\uml\plantuml.jar -tpng -o docs\uml\png docs\uml\*.puml
```

## 다이어그램 목록

### Class (C) — 구조

| File | 내용 |
|------|------|
| `C01-layers.puml` | 솔루션 레이어 / 컴포넌트 개요 |
| `C02-shell.puml` | MainWindow / MainViewModel / ClipboardMonitor / Popup |
| `C03-remote-occupancy.puml` | 갤러리·점유·RDP·Forti·CMC 모니터 |
| `C04-clipboard-tfs.puml` | 클립보드 lease + TFS sync 클래스 |
| `C05-worklog.puml` | WorkLog 목록/편집/Excel/TFS import |
| `C06-session-agent.puml` | 원격 SessionAgent |

### Sequence (S) — 시간 순 상호작용

| File | 내용 |
|------|------|
| `S01-startup.puml` | 앱 기동 |
| `S02-aurora-rdp.puml` | Aurora/일반 RDP 점유 |
| `S03-rc-forti.puml` | RC Forti + 게시 RDP |
| `S04-cmc-pmp.puml` | CMC PMP 웹 RDP |
| `S05-tfs-sync.puml` | TFS 클립보드 핸드셰이크 |
| `S06-worklog-excel.puml` | Excel 가져오기 |
| `S07-devsession.puml` | DevSession 스냅샷 |

### Communication (M) — 협업 관점

| File | 내용 |
|------|------|
| `M01-occupancy-comm.puml` | 점유 협업 |
| `M02-tfs-comm.puml` | TFS sync 협업 |
| `M03-cmc-comm.puml` | CMC 점유 협업 |

## FigJam에 들어간 것

- System overview (flowchart)
- TFS clipboard handshake (sequence)
- RC Forti RDP (sequence)
- CMC PMP occupancy (sequence)
