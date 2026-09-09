-- ============================================================
-- GBC Work Hub — 개선사항 요청 댓글 소프트 삭제 지원
-- 선행: sql/24_MSDWHTKD_REQ_DDL.sql, sql/27_MSDWHTKD_REQ_COMMENT_PARENT.sql
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 댓글에 답글(대댓글)이 달릴 수 있게 되면서, 부모 댓글을 실제 DELETE 하면 그 밑에 달린
-- 답글들이 고아가 되거나 화면에서 맥락 없이 붕 뜨게 된다. 그래서 삭제는 실제 행 삭제 대신
-- IS_DELETED 플래그만 세우는 방식으로 바꾼다 — 답글은 그대로 남고, 화면에서는 그 자리에
-- "삭제된 댓글입니다"만 흐리게 표시한다.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_REQ_COMMENT ADD (IS_DELETED NUMBER(1) DEFAULT 0 NOT NULL);

-- 확인
-- SELECT COMMENT_ID, REQ_ID, PARENT_COMMENT_ID, IS_DELETED, COMMENT_TEXT FROM XSUP.MSDWHTKD_REQ_COMMENT ORDER BY REQ_ID, CREATED_AT;
