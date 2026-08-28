-- ============================================================
-- GBC Work Hub — MSDWHTKH_FILE: watcher-era → two-point + TFVC 분류 확장
-- 09_MSDWHTKH_FILE_DDL.sql 이 이미 실행되었다는 전제. DEV_SESSION 신규 테이블 없음 —
-- 기존 MSDWHTKH(세션)/MSDWHTKH_FILE(파일)만 그대로 재사용, 컬럼만 확장.
-- DBA 실행용. 앱에서 자동 실행하지 않음.
-- ============================================================
-- FileSystemWatcher 기반 설계(FIRST_CHANGE_DT/LAST_CHANGE_DT/START_HASH/END_HASH)는
-- polling daemon 없이는 못 쓰므로 폐기 — connect/disconnect 2-point snapshot + TFVC
-- Changeset/Pending 대조로 대체. 컬럼은 남기고 NOT NULL만 해제한다(과거 실행 흔적 보존).
-- ============================================================

ALTER TABLE XSUP.MSDWHTKH_FILE MODIFY (FIRST_CHANGE_DT NULL);
ALTER TABLE XSUP.MSDWHTKH_FILE MODIFY (LAST_CHANGE_DT NULL);

ALTER TABLE XSUP.MSDWHTKH_FILE ADD (
    CHANGE_KIND             VARCHAR2(30),
    TFVC_STATUS             VARCHAR2(20),
    CHANGESET_ID            NUMBER,
    DIFF_AVAILABLE          CHAR(1) DEFAULT 'N',
    DIFF_SUMMARY            VARCHAR2(1000)
);

ALTER TABLE XSUP.MSDWHTKH_FILE ADD CONSTRAINT CK_MSDWHTKH_FILE_KIND CHECK (
    CHANGE_KIND IN ('CheckedIn', 'PendingChanged', 'SessionMetadataChanged', 'Added', 'Deleted')
    OR CHANGE_KIND IS NULL
);

ALTER TABLE XSUP.MSDWHTKH_FILE ADD CONSTRAINT CK_MSDWHTKH_FILE_TFVC CHECK (
    TFVC_STATUS IN ('CHECKED_IN', 'PENDING', 'NONE') OR TFVC_STATUS IS NULL
);

ALTER TABLE XSUP.MSDWHTKH_FILE ADD CONSTRAINT CK_MSDWHTKH_FILE_DIFF CHECK (
    DIFF_AVAILABLE IN ('Y', 'N')
);

CREATE INDEX IX_MSDWHTKH_FILE_KIND ON XSUP.MSDWHTKH_FILE (CHANGE_KIND);
CREATE INDEX IX_MSDWHTKH_FILE_CS ON XSUP.MSDWHTKH_FILE (CHANGESET_ID);

-- 권한 (앱 계정명으로 바꿔서 실행)
-- GRANT SELECT, INSERT ON XSUP.MSDWHTKH_FILE TO <APP_USER>; (이미 부여되어 있으면 생략)

-- 확인:
-- SELECT SESSION_TOKEN, FILE_PATH, CHANGE_KIND, TFVC_STATUS, CHANGESET_ID, CONFIDENCE, REASON
--   FROM XSUP.MSDWHTKH_FILE
--  WHERE SESSION_TOKEN = :token
--  ORDER BY FILE_PATH;
