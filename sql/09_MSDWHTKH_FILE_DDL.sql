-- ============================================================
-- GBC Work Hub — Dev Session 파일 변경 이력 (Phase 1, 결정론적)
-- 부모: XSUP.MSDWHTKH (SESSION_TOKEN = DEV_SESSION의 SESSION_ID 상관키)
-- 앱은 LOG_ID(surrogate PK)를 메모리에 들고 있지 않고 SESSION_TOKEN만 들고 있음
-- (RemoteSessionController._activeSessionToken) — 그래서 FK도 SESSION_TOKEN 기준.
-- 마스터(MSDWHTKD)/이력(MSDWHTKH)은 변경하지 않음. 자식 테이블만 추가.
-- DBA 실행용. 앱에서 자동 실행하지 않음.
-- ============================================================
-- 세션 종료 시 1회, SessionChangedFiles 전체를 배치 INSERT.
-- 파일 내용은 저장하지 않음 — 경로/타임스탬프/해시(64자리)만 저장.
-- ============================================================

CREATE SEQUENCE XSUP.SEQ_MSDWHTKH_FILE
    START WITH 1
    INCREMENT BY 1
    NOCACHE
    NOCYCLE;

CREATE TABLE XSUP.MSDWHTKH_FILE (
    FILE_ROW_ID             NUMBER            NOT NULL,
    SESSION_TOKEN           VARCHAR2(50)      NOT NULL,
    FILE_PATH               VARCHAR2(1000)    NOT NULL,
    FIRST_CHANGE_DT         TIMESTAMP         NOT NULL,
    LAST_CHANGE_DT          TIMESTAMP         NOT NULL,
    CONTENT_CHANGED         CHAR(1)           NOT NULL,
    START_HASH              VARCHAR2(64),
    END_HASH                VARCHAR2(64),
    CONFIDENCE              VARCHAR2(10),
    REASON                  VARCHAR2(500),
    CREATED_AT              TIMESTAMP         DEFAULT SYSTIMESTAMP NOT NULL,
    UPDT_DTM                TIMESTAMP         DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT PK_MSDWHTKH_FILE PRIMARY KEY (FILE_ROW_ID),
    CONSTRAINT FK_MSDWHTKH_FILE_TOKEN FOREIGN KEY (SESSION_TOKEN)
        REFERENCES XSUP.MSDWHTKH (SESSION_TOKEN) ON DELETE CASCADE,
    CONSTRAINT UQ_MSDWHTKH_FILE UNIQUE (SESSION_TOKEN, FILE_PATH),
    CONSTRAINT CK_MSDWHTKH_FILE_CHANGED CHECK (CONTENT_CHANGED IN ('Y', 'N')),
    CONSTRAINT CK_MSDWHTKH_FILE_CONF CHECK (CONFIDENCE IN ('HIGH', 'MEDIUM', 'LOW') OR CONFIDENCE IS NULL)
);

CREATE INDEX IX_MSDWHTKH_FILE_TOKEN ON XSUP.MSDWHTKH_FILE (SESSION_TOKEN);

-- 권한 (앱 계정명으로 바꿔서 실행)
-- GRANT SELECT, INSERT ON XSUP.MSDWHTKH_FILE TO <APP_USER>;
-- GRANT SELECT ON XSUP.SEQ_MSDWHTKH_FILE TO <APP_USER>;

-- 확인 (LOG_ID로 보고 싶으면 MSDWHTKH와 SESSION_TOKEN으로 조인):
-- SELECT h.LOG_ID, f.*
--   FROM XSUP.MSDWHTKH_FILE f
--   JOIN XSUP.MSDWHTKH h ON h.SESSION_TOKEN = f.SESSION_TOKEN
--  WHERE f.SESSION_TOKEN = :token
--  ORDER BY f.FIRST_CHANGE_DT;
