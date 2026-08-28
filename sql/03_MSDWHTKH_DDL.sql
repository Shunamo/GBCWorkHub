-- ============================================================
-- GBC Work Hub — 원격 PC 접속 이력
-- 테이블명: XSUP.MSDWHTKH  (구 MSDWHTKD_LOG → 개명)
-- 마스터(MSDWHTKD)는 변경하지 않음. 이력만 추가.
-- DBA 실행용. 앱에서 자동 실행하지 않음.
-- ============================================================

-- ------------------------------------------------------------
-- A) 이미 MSDWHTKD_LOG 가 있을 경우 — 개명만
-- ------------------------------------------------------------
-- ALTER TABLE XSUP.MSDWHTKD_LOG RENAME TO MSDWHTKH;
-- RENAME SEQ_MSDWHTKD_LOG TO SEQ_MSDWHTKH;
-- (제약/인덱스는 이름이 남아 있어도 동작에는 문제 없음. 새로 만들면 아래 이름 사용)

-- ------------------------------------------------------------
-- B) 신규 생성
-- ------------------------------------------------------------

-- 1) Sequence
CREATE SEQUENCE XSUP.SEQ_MSDWHTKH
    START WITH 1
    INCREMENT BY 1
    NOCACHE
    NOCYCLE;

-- 2) 접속 이력 테이블
--    키: SESSION_TOKEN (앱에서 reserve 시 발급하는 Guid)
--    참조: REMOTE_ACCS_IP_ADDR = MSDWHTKD.REMOTE_ACCS_IP_ADDR
CREATE TABLE XSUP.MSDWHTKH (
    LOG_ID                  NUMBER            NOT NULL,
    SESSION_TOKEN           VARCHAR2(50)      NOT NULL,
    REMOTE_ACCS_IP_ADDR     VARCHAR2(50)      NOT NULL,
    REMOTE_PC_NM            VARCHAR2(100),
    ACCS_USER_ID            VARCHAR2(200)     NOT NULL,
    ACCS_PC_NM              VARCHAR2(100),
    ACCS_IP_ADDR            VARCHAR2(50),
    SESSION_STATUS          VARCHAR2(30)      NOT NULL,
    REQUESTED_AT            TIMESTAMP         NOT NULL,
    CONFIRMED_AT            TIMESTAMP,
    ENDED_AT                TIMESTAMP,
    END_SOURCE              VARCHAR2(50),
    RESULT_MESSAGE          VARCHAR2(1000),
    CREATED_AT              TIMESTAMP         DEFAULT SYSTIMESTAMP NOT NULL,
    UPDT_DTM                TIMESTAMP         DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT PK_MSDWHTKH PRIMARY KEY (LOG_ID),
    CONSTRAINT UK_MSDWHTKH_TOKEN UNIQUE (SESSION_TOKEN),
    CONSTRAINT CK_MSDWHTKH_STS CHECK (
        SESSION_STATUS IN ('CONNECTING', 'IN_USE', 'ENDED', 'CANCELLED', 'FAILED', 'CHECK_REQUIRED')
    )
);

CREATE INDEX IX_MSDWHTKH_IP ON XSUP.MSDWHTKH (REMOTE_ACCS_IP_ADDR);
CREATE INDEX IX_MSDWHTKH_REQ ON XSUP.MSDWHTKH (REQUESTED_AT);

-- 3) 권한 (앱 계정명으로 바꿔서 실행)
-- xsup 본인 계정으로 만들었으면 GRANT 생략 가능.
-- GRANT SELECT, INSERT, UPDATE ON XSUP.MSDWHTKH TO <APP_USER>;
-- GRANT SELECT ON XSUP.SEQ_MSDWHTKH TO <APP_USER>;

-- 확인:
-- SELECT * FROM XSUP.MSDWHTKH ORDER BY LOG_ID DESC;

-- ============================================================
-- 앱에서 넣는 타이밍 (참고)
-- ============================================================
-- reserve 성공 시:
--   INSERT ... SESSION_STATUS='CONNECTING' or 'IN_USE', REQUESTED_AT=SYSTIMESTAMP
--
-- RDP 연결 확인(confirm) 시:
--   UPDATE ... SESSION_STATUS='IN_USE', CONFIRMED_AT=SYSTIMESTAMP
--   WHERE SESSION_TOKEN = :token
--
-- mstsc 종료 / release 시:
--   UPDATE ... SESSION_STATUS='ENDED', ENDED_AT=SYSTIMESTAMP, END_SOURCE='MSTSC_EXIT'
--   WHERE SESSION_TOKEN = :token
-- ============================================================
