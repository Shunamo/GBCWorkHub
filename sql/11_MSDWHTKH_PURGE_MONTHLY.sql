-- ============================================================
-- 접속 이력(XSUP.MSDWHTKH) 월간 정리
-- 앱도 마이페이지 새로고침 시 12시간에 한 번 같은 DELETE 를 돌린다.
-- DBA 스케줄로 돌릴 때는 이 스크립트를 사용.
-- 진행 중(IN_USE / CONNECTING) 세션은 남긴다.
-- ============================================================

DELETE FROM XSUP.MSDWHTKH
 WHERE REQUESTED_AT < ADD_MONTHS(SYSTIMESTAMP, -1)
   AND (SESSION_STATUS IS NULL
     OR UPPER(TRIM(SESSION_STATUS)) NOT IN ('IN_USE', 'CONNECTING'));

COMMIT;

-- 예: 매월 1일 03:00
-- BEGIN
--   DBMS_SCHEDULER.CREATE_JOB(
--     job_name        => 'XSUP.JOB_PURGE_MSDWHTKH_MONTHLY',
--     job_type        => 'PLSQL_BLOCK',
--     job_action      => 'BEGIN
--       DELETE FROM XSUP.MSDWHTKH
--        WHERE REQUESTED_AT < ADD_MONTHS(SYSTIMESTAMP, -1)
--          AND (SESSION_STATUS IS NULL
--            OR UPPER(TRIM(SESSION_STATUS)) NOT IN (''IN_USE'', ''CONNECTING''));
--       COMMIT;
--     END;',
--     start_date      => SYSTIMESTAMP,
--     repeat_interval => 'FREQ=MONTHLY; BYMONTHDAY=1; BYHOUR=3; BYMINUTE=0',
--     enabled         => TRUE);
-- END;
-- /
