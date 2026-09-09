-- ============================================================
-- GBC Work Hub — 요청 본문 인라인 이미지용 안정 키 컬럼 추가
-- 선행: sql/24_MSDWHTKD_REQ_DDL.sql (MSDWHTKD_REQ_ATTACH 존재)
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 요청 본문(DESCRIPTION)에 이미지 위치를 표시할 때 이전에는 첨부 생성 순번(1,2,3...)을
-- 그대로 참조했다. 이 방식은 중간 이미지를 삭제하면 뒤 순번들이 밀려서 엉뚱한 첨부를 가리키게
-- 되는 문제가 있었다. 이제 각 첨부에 작성 시점에 클라이언트가 생성한 짧은 고유 키를 저장해두고,
-- 본문에는 그 키를 참조하는 {{img:<키>}} 마커를 남긴다. 순번 참조 대신 키 참조라 삭제/재배치에
-- 영향받지 않는다.
--
-- 옛 데이터 호환: 이 컬럼이 NULL인 기존 첨부는 그대로 있어도 무방하다(옛 DESCRIPTION은 애초에
-- 숫자 순번 마커를 쓰므로 이 컬럼을 참조하지 않는다 — 앱 코드가 숫자 마커/키 마커 둘 다 파싱한다).
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_REQ_ATTACH ADD (CONTENT_KEY VARCHAR2(20));

-- 확인
-- SELECT ATTACH_ID, REQ_ID, FILE_NM, CONTENT_KEY FROM XSUP.MSDWHTKD_REQ_ATTACH ORDER BY ATTACH_ID;
