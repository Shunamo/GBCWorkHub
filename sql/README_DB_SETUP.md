# Oracle DB 설정 (XSUP.MSDWHTKD)

Work Hub는 **XSUP.MSDWHTKD** 테이블 하나로 원격 PC 상태를 공유합니다.  
테이블 구조는 변경하지 않습니다.

## Golden 접속 정보 넣는 위치

**파일:** `src/GBCWorkHub.UI/App.config` (배포 후 `GBCWorkHub.UI.exe.config`)

```xml
<connectionStrings>
  <add name="GbcWorkHubDb"
       providerName="Oracle.ManagedDataAccess.Client"
       connectionString="User Id=...;Password=...;Data Source=...;" />
</connectionStrings>
```

| 항목 | 설명 |
|------|------|
| **User Id** | Golden Oracle DB 계정 (스키마 XSUP — DBA 확인) |
| **Password** | DB 계정 비밀번호 (Golden UI PW와 다를 수 있음 — DBA 확인) |
| **Data Source** | Golden `tnsnames.ora` TNS 별칭 또는 `호스트:1521/서비스명` |

## 필요한 DB 권한

```sql
GRANT SELECT, UPDATE ON XSUP.MSDWHTKD TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE ON XSUP.MSDWHTKH TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKH TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE, DELETE ON XSUP.MSDWHTKD_WRK TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE, DELETE ON XSUP.MSDWHTKD_WRK_PRJ TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE, DELETE ON XSUP.MSDWHTKD_WRK_SRC TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKD_WRK TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKD_WRK_PRJ TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKD_WRK_SRC TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE ON XSUP.MSDWHTKD_USR TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKD_USR TO <APP_USER>;
GRANT SELECT, INSERT, UPDATE ON XSUP.MSDWHTKD_PCMAP TO <APP_USER>;
GRANT SELECT ON XSUP.SEQ_MSDWHTKD_PCMAP TO <APP_USER>;
```

접속 이력: `sql/03_MSDWHTKH_DDL.sql` (구 `MSDWHTKD_LOG` → `MSDWHTKH`)  
이미 LOG를 만든 경우: `sql/03b_MSDWHTKD_LOG_TO_MSDWHTKH_RENAME.sql`  
업무기록: `sql/04_MSDWHTKD_WRK_DDL.sql` (헤더 + Project + Source)  
업무기록 사이트: `sql/04c_ALTER_WRK_SITE_CD.sql` (`MSDWHTKD_WRK.SITE_CD`)  
등록 Changeset: `sql/04d_MSDWHTKD_WRK_CS_DDL.sql` (`MSDWHTKD_WRK_CS`)  
원격 PC 사이트: `sql/07_ALTER_MSDWHTKD_SITE_CD.sql` (`MSDWHTKD.SITE_CD`)  
사용자·PC 별명: `sql/14_MSDWHTKD_USR_PCMAP.sql` (`MSDWHTKD_USR`, `MSDWHTKD_PCMAP`)  
PC 소속: `sql/16_ALTER_PCMAP_TEAM_NM.sql` (`MSDWHTKD_PCMAP.TEAM_NM`)  
PC 도메인·접속메모(엑셀): `sql/19_ALTER_PCMAP_PC_DOMAIN.sql` (`PC_DOMAIN`, `PC_NOTE`)  
vpn.xlsx 전체 PC: `sql/18_VPN_XLSX_SEED.sql` (PCMAP + 점유 행, 비밀번호 없음)

점유 마스터는 **XSUP.MSDWHTKD**. 접속 이력은 **XSUP.MSDWHTKH**. 업무기록은 `MSDWHTKD_WRK*`.  
사용자 디렉터리는 `MSDWHTKD_USR`, 원격 PC 이름↔IP는 `MSDWHTKD_PCMAP`.  
`MSDWHTFS` / `MSDWHTWK` / `GBC_REMOTE_PC` / `GBC_RDP_USAGE_LOG` 는 사용하지 않음. 남아 있으면 `sql/17_DROP_UNUSED.sql`.

## 원격 PC 시드

`sql/vpn.xlsx` 기준 전체 목록은 `sql/18_VPN_XLSX_SEED.sql`.  
소속은 vpn.xlsx PC칸(B열) 색: 보라=진료지원, 파랑=진료간호, 초록=원무, 주황=배포서버, 노랑=ETC.  
점유 키: AURORA=IP, RC/CMC=PC명. RDP 비밀번호는 스크립트에 넣지 않음.

소속 컬럼만: `sql/16_ALTER_PCMAP_TEAM_NM.sql`  
PC 도메인·접속메모(엑셀 ID/PW/Comment): `sql/19_ALTER_PCMAP_PC_DOMAIN.sql`  
진료간호 점유 3대만: `sql/15_AURORA_MSDWHTKD_NURSING.sql`


Oracle **User Id / Password / Data Source**만 connection string에 넣습니다.
