-- ============================================================
-- GBC Work Hub — 업무기록(WorkLog)에 작성자 계정 FK 추가
-- 선행: sql/04_MSDWHTKD_WRK_DDL.sql, sql/30_MSDWHTKD_USR_SOFT_DELETE.sql
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 기존 AUTHOR_NM은 작성 시점 이름을 그대로 박아둔 텍스트 스냅샷일 뿐 계정과
-- 연결되어 있지 않다. 요청사항(MSDWHTKD_REQ)은 이미 USER_ID FK가 있어 탈퇴 계정
-- 표시를 정확히 판정할 수 있는데, 업무기록은 그럴 수단이 없었다. 이름 문자열
-- 매칭(동명이인 시 오작동)으로 때우는 대신 정식으로 FK를 추가한다.
--
-- 옛 데이터 호환: 이 컬럼이 NULL인 기존 행은 계정 매칭 대상에서 자연히 빠진다
-- (화면에는 이름만 그대로 보이고 "(삭제된 계정)" 표시는 안 붙는다) — 소급 반영 없음.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_WRK ADD (USR_ID NUMBER NULL);

ALTER TABLE XSUP.MSDWHTKD_WRK
    ADD CONSTRAINT FK_MSDWHTKD_WRK_USER
    FOREIGN KEY (USR_ID) REFERENCES XSUP.MSDWHTKD_USR (USR_ID);

-- 확인
-- SELECT LOG_ID, AUTHOR_NM, USR_ID FROM XSUP.MSDWHTKD_WRK ORDER BY CREATED_AT DESC;
