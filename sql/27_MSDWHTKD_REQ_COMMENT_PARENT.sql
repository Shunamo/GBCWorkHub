-- ============================================================
-- GBC Work Hub — 개선사항 요청 댓글에 대댓글(답글) 지원 컬럼 추가
-- 선행: sql/24_MSDWHTKD_REQ_DDL.sql (MSDWHTKD_REQ_COMMENT 존재)
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 지금까지 댓글은 REQ_ID에 대해 평면 목록으로만 저장됐다. 답글 기능을 위해 각 댓글이
-- 자신의 부모 댓글(COMMENT_ID)을 가리킬 수 있도록 PARENT_COMMENT_ID를 추가한다. NULL이면
-- 최상위 댓글, 값이 있으면 그 COMMENT_ID에 대한 답글이다. 답글의 답글(2단계 이상 중첩)은
-- UI에서 만들지 않지만, 데이터 구조 자체는 제한하지 않는다.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_REQ_COMMENT ADD (PARENT_COMMENT_ID NUMBER(19));

ALTER TABLE XSUP.MSDWHTKD_REQ_COMMENT
    ADD CONSTRAINT FK_REQ_COMMENT_PARENT
    FOREIGN KEY (PARENT_COMMENT_ID) REFERENCES XSUP.MSDWHTKD_REQ_COMMENT (COMMENT_ID);

-- 확인
-- SELECT COMMENT_ID, REQ_ID, PARENT_COMMENT_ID, AUTHOR_NM, COMMENT_TEXT FROM XSUP.MSDWHTKD_REQ_COMMENT ORDER BY REQ_ID, CREATED_AT;
