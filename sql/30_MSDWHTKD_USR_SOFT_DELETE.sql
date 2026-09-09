-- ============================================================
-- GBC Work Hub — 사용자 계정 소프트 삭제 지원
-- 선행: sql/20_MSDWHTKD_USR_AUTH.sql
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: MSDWHTKD_REQ.USER_ID, MSDWHTKD_REQ_COMMENT.USER_ID, 그리고 이번에 함께
-- 추가하는 MSDWHTKD_WRK.USR_ID가 이 테이블의 USR_ID를 FK로 참조하므로, 관리자가
-- 계정을 "삭제"해도 실제 행을 지울 수 없다(과거 기록의 작성자 정보가 날아가 버림).
-- 그래서 IS_DELETED 플래그만 세우고, 화면에서는 작성자 이름 옆에 "(삭제된 계정)"만
-- 흐리게 붙여서 표시한다 — 기록 자체는 그대로 조회 가능해야 한다.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_USR ADD (IS_DELETED NUMBER(1) DEFAULT 0 NOT NULL);

-- 확인
-- SELECT USR_ID, USER_NM, LOGIN_ID, IS_ACTIVE, IS_DELETED FROM XSUP.MSDWHTKD_USR ORDER BY USR_ID;
