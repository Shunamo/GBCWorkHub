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
```

접속 이력: `sql/03_MSDWHTKH_DDL.sql` (구 `MSDWHTKD_LOG` → `MSDWHTKH`)  
이미 LOG를 만든 경우: `sql/03b_MSDWHTKD_LOG_TO_MSDWHTKH_RENAME.sql`  
업무기록: `sql/04_MSDWHTKD_WRK_DDL.sql` (헤더 + Project + Source)  
업무기록 사이트: `sql/04c_ALTER_WRK_SITE_CD.sql` (`MSDWHTKD_WRK.SITE_CD`)  
등록 Changeset: `sql/04d_MSDWHTKD_WRK_CS_DDL.sql` (`MSDWHTKD_WRK_CS`)  
원격 PC 사이트: `sql/07_ALTER_MSDWHTKD_SITE_CD.sql` (`MSDWHTKD.SITE_CD`)

마스터는 **XSUP.MSDWHTKD** 하나. 접속 이력은 **XSUP.MSDWHTKH**. 업무기록은 `MSDWHTKD_WRK*`.  
`MSDWHTFS` / `MSDWHTWK` / `MSDWHTKD_LOG` 는 사용하지 않음.

## 테스트 원격 PC

| REMOTE_ACCS_IP_ADDR | REMOTE_PC_NM |
|---------------------|--------------|
| 172.16.49.61 | KEB-7VY98V3 |


Oracle **User Id / Password / Data Source**만 connection string에 넣습니다.
