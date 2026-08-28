-- ============================================================
-- [개명] MSDWHTKD_LOG → MSDWHTKH
-- 이미 LOG 테이블을 만든 경우 이 스크립트만 실행.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_LOG RENAME TO MSDWHTKH;

-- 시퀀스 개명 (현재 스키마에서 실행)
RENAME SEQ_MSDWHTKD_LOG TO SEQ_MSDWHTKH;

-- 확인
-- SELECT * FROM XSUP.MSDWHTKH ORDER BY LOG_ID DESC;
-- SELECT XSUP.SEQ_MSDWHTKH.NEXTVAL FROM DUAL;
