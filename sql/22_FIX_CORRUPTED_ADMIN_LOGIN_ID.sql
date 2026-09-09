-- ============================================================
-- 김수현 계정 복구 + 임시 비번(ADMIN)으로 맞춤
-- 일지/이력은 이름 기준이라 USR 행 UPDATE만 하면 됨 (DELETE 금지)
-- USR_ID=139 ADMIN 시드는 건드리지 말 것
-- ============================================================

-- 1) 현재 상태
SELECT USR_ID, USER_NM, LOGIN_ID, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP,
       CASE
         WHEN PASSWORD_HASH IS NULL THEN 'NO_PW'
         WHEN PASSWORD_HASH = '835d6dc88b708bc646d6db82c853ef4182fabbd4a8de59c213f2b5ab3ae7d9be'
         THEN 'PW=ADMIN'
         ELSE 'PW=OTHER'
       END AS PW
  FROM XSUP.MSDWHTKD_USR
 WHERE USR_ID = 1
    OR UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) = 'DESKTOP-8FHREA0';

-- 2) 이름 + 임시 비번(ADMIN) 복구
UPDATE XSUP.MSDWHTKD_USR
   SET USER_NM = '김수현',
       LOGIN_ID = '김수현',
       TEAM_NM = NVL(TEAM_NM, '진료지원'),
       PASSWORD_HASH = '835d6dc88b708bc646d6db82c853ef4182fabbd4a8de59c213f2b5ab3ae7d9be',
       IS_ACTIVE = 'Y',
       UPDT_DTM = SYSTIMESTAMP
 WHERE USR_ID = 1
   AND UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) = 'DESKTOP-8FHREA0';

COMMIT;

-- 3) 앱 로그인: 이름 김수현 / 비밀번호 ADMIN
--    들어간 뒤 마이페이지 → 비밀번호 변경
