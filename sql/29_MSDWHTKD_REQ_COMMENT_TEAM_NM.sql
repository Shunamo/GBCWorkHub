-- ============================================================
-- GBC Work Hub — 댓글에도 작성자 소속(팀) 스냅샷 저장
-- 선행: sql/24_MSDWHTKD_REQ_DDL.sql (MSDWHTKD_REQ_COMMENT 존재)
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 요청(MSDWHTKD_REQ)은 작성 시점 소속(TEAM_NM)을 이미 스냅샷으로 저장하는데(AUTHOR_NM과
-- 같은 관례), 댓글(MSDWHTKD_REQ_COMMENT)에는 그 컬럼이 없었다. 댓글 목록에서도 "김수현 · 진료지원"
-- 처럼 소속을 같이 보여주기 위해 추가한다. 기존 댓글은 NULL로 남는다(소급 반영 없음).
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_REQ_COMMENT ADD (TEAM_NM VARCHAR2(100));

-- 확인
-- SELECT COMMENT_ID, REQ_ID, AUTHOR_NM, TEAM_NM, COMMENT_TEXT FROM XSUP.MSDWHTKD_REQ_COMMENT ORDER BY REQ_ID, CREATED_AT;
