-- ============================================================
-- 요청사항(Improvement) 상태에 "반려"(REJECTED) 추가
-- 기존: OPEN(접수) / CHECKING(확인중) / IN_PROGRESS(수정중) / RESOLVED(해결)
-- 추가: REJECTED(반려) — 실행하지 않기로 한 요청을 종결 처리하는 상태.
-- DBA 실행용. 재실행 안전(제약 없으면 조용히 지나감).
-- ============================================================

BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_REQ DROP CONSTRAINT CK_MSDWHTKD_REQ_STS';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -2443 THEN RAISE; END IF;
END;
/

ALTER TABLE XSUP.MSDWHTKD_REQ
    ADD CONSTRAINT CK_MSDWHTKD_REQ_STS
    CHECK (STATUS IN ('OPEN','CHECKING','IN_PROGRESS','RESOLVED','REJECTED'));

-- 확인:
-- SELECT CONSTRAINT_NAME, SEARCH_CONDITION FROM ALL_CONSTRAINTS
--  WHERE OWNER = 'XSUP' AND CONSTRAINT_NAME = 'CK_MSDWHTKD_REQ_STS';
