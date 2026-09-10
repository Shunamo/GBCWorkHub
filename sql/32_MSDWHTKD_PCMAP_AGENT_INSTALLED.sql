-- ============================================================
-- GBC Work Hub — PC별 에이전트 설치 완료 여부
-- 선행: sql/14_MSDWHTKD_USR_PCMAP.sql
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
--
-- 배경: 관리자 페이지 PC 관리에서 "AGENT 설치까지 완료된 PC"를 체크로 표시하고,
-- 목록에서는 PC 아이콘 우측 상단에 노란 점 배지로 보여주기 위한 플래그.
-- ============================================================

ALTER TABLE XSUP.MSDWHTKD_PCMAP ADD (AGENT_INSTALLED NUMBER(1) DEFAULT 0 NOT NULL);

-- 확인
-- SELECT SITE_CD, PC_NM, AGENT_INSTALLED FROM XSUP.MSDWHTKD_PCMAP ORDER BY SITE_CD, PC_NM;
