-- ============================================================
-- ADMIN 계정 시드 (LOGIN_ID / PASSWORD = ADMIN, 표시명 관리자)
-- 선행: sql/20_MSDWHTKD_USR_AUTH.sql (LOGIN_ID, PASSWORD_HASH, IS_ACTIVE)
-- PASSWORD_HASH = SHA256("ADMIN") hex lower
-- ※ MERGE ON 컬럼 UPDATE 금지(ORA-38104) → UPDATE + INSERT 사용
-- ============================================================

-- ------------------------------------------------------------
-- 1) 먼저 확인 (1행 나오면 이미 있음)
-- ------------------------------------------------------------
SELECT USR_ID, USER_NM, LOGIN_ID, TEAM_NM, LOCAL_PC_NM, IS_ACTIVE,
       CASE WHEN PASSWORD_HASH IS NULL THEN 'NO_PW' ELSE 'HAS_PW' END AS PW
  FROM XSUP.MSDWHTKD_USR
 WHERE UPPER(TRIM(LOGIN_ID)) = 'ADMIN'
    OR UPPER(TRIM(USER_NM)) = 'ADMIN'
    OR UPPER(TRIM(LOCAL_PC_NM)) = 'ADMIN';

-- ------------------------------------------------------------
-- 2) 있으면 UPDATE, 없으면 INSERT
-- ------------------------------------------------------------
UPDATE XSUP.MSDWHTKD_USR
   SET USER_NM = '관리자',
       TEAM_NM = '관리',
       LOGIN_ID = 'ADMIN',
       LOCAL_PC_NM = NVL(LOCAL_PC_NM, 'ADMIN'),
       PASSWORD_HASH = '835d6dc88b708bc646d6db82c853ef4182fabbd4a8de59c213f2b5ab3ae7d9be',
       IS_ACTIVE = 'Y',
       UPDT_DTM = SYSTIMESTAMP
 WHERE UPPER(TRIM(LOGIN_ID)) = 'ADMIN'
    OR UPPER(TRIM(USER_NM)) = 'ADMIN'
    OR UPPER(TRIM(LOCAL_PC_NM)) = 'ADMIN';

INSERT INTO XSUP.MSDWHTKD_USR (
  USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
  LOGIN_ID, PASSWORD_HASH, IS_ACTIVE, CREATED_AT, UPDT_DTM
)
SELECT
  XSUP.SEQ_MSDWHTKD_USR.NEXTVAL,
  '관리자',
  '관리',
  'ADMIN',
  NULL,
  NULL,
  'ADMIN',
  '835d6dc88b708bc646d6db82c853ef4182fabbd4a8de59c213f2b5ab3ae7d9be',
  'Y',
  SYSTIMESTAMP,
  SYSTIMESTAMP
  FROM DUAL
 WHERE NOT EXISTS (
         SELECT 1
           FROM XSUP.MSDWHTKD_USR
          WHERE UPPER(TRIM(LOGIN_ID)) = 'ADMIN'
             OR UPPER(TRIM(USER_NM)) = 'ADMIN'
             OR UPPER(TRIM(LOCAL_PC_NM)) = 'ADMIN'
       );

COMMIT;

-- ------------------------------------------------------------
-- 3) 적용 후 재확인 (USER_NM=관리자, LOGIN_ID=ADMIN, HAS_PW)
-- ------------------------------------------------------------
SELECT USR_ID, USER_NM, LOGIN_ID, TEAM_NM, LOCAL_PC_NM, IS_ACTIVE,
       CASE WHEN PASSWORD_HASH IS NULL THEN 'NO_PW' ELSE 'HAS_PW' END AS PW
  FROM XSUP.MSDWHTKD_USR
 WHERE UPPER(TRIM(LOGIN_ID)) = 'ADMIN'
    OR UPPER(TRIM(USER_NM)) IN ('ADMIN', '관리자')
    OR UPPER(TRIM(LOCAL_PC_NM)) = 'ADMIN';
