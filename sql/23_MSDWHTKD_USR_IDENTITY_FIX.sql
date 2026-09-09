-- ============================================================
-- MSDWHTKD_USR 사용자 식별 정규화
-- 불변 원칙 확정:
--   USR_ID      = 사람의 불변 식별자 (PK, 절대 재사용/재번호 안 함)
--   LOGIN_ID    = 계정 유일 식별자 (UX_MSDWHTKD_USR_LOGIN, sql/20에서 이미 생성됨)
--   LOCAL_PC_NM = 최근 접속 PC 참고정보일 뿐 — 식별키 아님
-- 근본 원인: UK_MSDWHTKD_USR_PC(UNIQUE LOCAL_PC_NM) 제약 때문에, 이미 다른 계정이 쓰던 PC에서
--            신규 가입 시 INSERT가 ORA-00001로 실패 → 앱 코드(UpsertUserWithAuth)가 그 PC의 기존
--            행을 신규 계정 정보로 덮어쓰는 폴백을 탔음. 앱 코드는 이 커밋에서 이미 제거됨
--            (OracleDirectoryRepository.UpsertUserWithAuth). 이 스크립트는 그 원인 제약을 제거한다.
-- DBA 실행용. 앱은 CREATE/ALTER/DROP 하지 않음. 데이터는 삭제/재번호하지 않음.
-- ============================================================

-- ------------------------------------------------------------
-- 0) 실행 전 진단 (반드시 먼저 확인)
-- ------------------------------------------------------------

-- 0-1) UK_MSDWHTKD_USR_PC 제약이 실제로 존재하는지
SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE, STATUS
  FROM ALL_CONSTRAINTS
 WHERE OWNER = 'XSUP' AND TABLE_NAME = 'MSDWHTKD_USR' AND CONSTRAINT_NAME = 'UK_MSDWHTKD_USR_PC';

-- 0-2) LOGIN_ID UNIQUE 인덱스 존재 확인 (sql/20에서 이미 생성되어 있어야 함)
SELECT INDEX_NAME, UNIQUENESS
  FROM ALL_INDEXES
 WHERE OWNER = 'XSUP' AND TABLE_NAME = 'MSDWHTKD_USR' AND INDEX_NAME = 'UX_MSDWHTKD_USR_LOGIN';

-- 0-3) LOGIN_ID가 NULL/공백인 행 — 0건이어야 아래 4)의 NOT NULL을 안전하게 적용 가능.
--      1건 이상이면 NOT NULL 적용하지 말고 해당 행을 수동 확인/보정할 것.
SELECT USR_ID, USER_NM, LOGIN_ID, LOCAL_PC_NM, LOCAL_PC_IP, UPDT_DTM
  FROM XSUP.MSDWHTKD_USR
 WHERE LOGIN_ID IS NULL OR TRIM(LOGIN_ID) IS NULL
 ORDER BY USR_ID;

-- 0-4) 같은 LOCAL_PC_NM을 공유하는 서로 다른 USR_ID들 — 과거 PC 충돌 폴백으로 신원이 뒤섞였을
--      가능성이 있는 후보. 자동 판단이 아니라 참고용 목록이다. 여러 명이 실제로 같은 공유 PC를
--      정상적으로 써온 경우도 포함되므로, 이름/이력이 부자연스러운 조합만 수동으로 검토할 것.
SELECT LOCAL_PC_NM,
       COUNT(*)                                                   AS ROW_COUNT,
       LISTAGG(USR_ID || ':' || USER_NM || '(' || LOGIN_ID || ')', ', ')
           WITHIN GROUP (ORDER BY USR_ID)                          AS USERS
  FROM XSUP.MSDWHTKD_USR
 WHERE LOCAL_PC_NM IS NOT NULL
 GROUP BY LOCAL_PC_NM
HAVING COUNT(*) > 1
 ORDER BY ROW_COUNT DESC;

-- ------------------------------------------------------------
-- 1) UK_MSDWHTKD_USR_PC 제거 — LOCAL_PC_NM은 더 이상 식별키가 아니다.
--    (재실행 안전: 제약이 없으면 ORA-02443 무시)
-- ------------------------------------------------------------
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_USR DROP CONSTRAINT UK_MSDWHTKD_USR_PC';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -2443 THEN RAISE; END IF;
END;
/

-- ------------------------------------------------------------
-- 2) LOCAL_PC_NM 조회 성능 유지 — 유니크가 아닌 일반 인덱스로 대체.
--    (재실행 안전: 이미 있으면 ORA-00955 무시)
-- ------------------------------------------------------------
BEGIN
    EXECUTE IMMEDIATE 'CREATE INDEX IX_MSDWHTKD_USR_PC ON XSUP.MSDWHTKD_USR (LOCAL_PC_NM)';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -955 THEN RAISE; END IF;
END;
/

-- ------------------------------------------------------------
-- 3) LOGIN_ID UNIQUE 인덱스 재확인 (없으면 생성 — 정상적으로는 sql/20에서 이미 존재해야 함)
-- ------------------------------------------------------------
BEGIN
    EXECUTE IMMEDIATE
        'CREATE UNIQUE INDEX UX_MSDWHTKD_USR_LOGIN ON XSUP.MSDWHTKD_USR (UPPER(TRIM(LOGIN_ID)))';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -955 THEN RAISE; END IF;
END;
/

-- ------------------------------------------------------------
-- 4) LOGIN_ID를 계정 식별키로 확정(NOT NULL).
--    ★ 위 0-3) 진단 결과가 0건일 때만 아래 블록의 주석을 해제하고 실행하세요.
--       0-3)에 행이 남아 있는 상태로 실행하면 ORA-02296(NOT NULL 위반)으로 실패합니다.
-- ------------------------------------------------------------
-- BEGIN
--     EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_USR MODIFY (LOGIN_ID NOT NULL)';
-- EXCEPTION
--     WHEN OTHERS THEN
--         IF SQLCODE != -1442 THEN RAISE; END IF; -- 이미 NOT NULL
-- END;
-- /

-- ------------------------------------------------------------
-- 5) 적용 후 재확인
-- ------------------------------------------------------------
SELECT 'UK_MSDWHTKD_USR_PC' AS OBJ_NAME,
       CASE WHEN COUNT(*) > 0 THEN 'STILL EXISTS (확인 필요)' ELSE 'REMOVED (정상)' END AS STATUS
  FROM ALL_CONSTRAINTS
 WHERE OWNER = 'XSUP' AND TABLE_NAME = 'MSDWHTKD_USR' AND CONSTRAINT_NAME = 'UK_MSDWHTKD_USR_PC'
UNION ALL
SELECT 'IX_MSDWHTKD_USR_PC',
       CASE WHEN COUNT(*) > 0 THEN 'EXISTS' ELSE 'MISSING' END
  FROM ALL_INDEXES
 WHERE OWNER = 'XSUP' AND INDEX_NAME = 'IX_MSDWHTKD_USR_PC'
UNION ALL
SELECT 'UX_MSDWHTKD_USR_LOGIN',
       CASE WHEN COUNT(*) > 0 THEN 'EXISTS' ELSE 'MISSING' END
  FROM ALL_INDEXES
 WHERE OWNER = 'XSUP' AND INDEX_NAME = 'UX_MSDWHTKD_USR_LOGIN';

-- ============================================================
-- 참고: LOCAL_PC_NM 이력이 추가로 필요하다면 (예: "이 계정이 과거에 어떤 PC들을 썼는지"
-- 감사 목적) 별도의 USER_PC_HISTORY 매핑 테이블(USR_ID, PC_NM, FIRST_SEEN_AT, LAST_SEEN_AT)을
-- 신설하는 것을 향후 과제로 검토할 것 — 이번 수정 범위에는 포함하지 않는다.
-- ============================================================
