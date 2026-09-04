-- 업무기록 작성자 소속 (팀 검색용). 예: 진료지원
-- DBA 배포용. SITE_CD(04c)와 같이 이번 배포 때 한 번만 실행.
-- 앱에서 실행하지 않음. 병원 사용자가 실행하지 않음.

ALTER TABLE XSUP.MSDWHTKD_WRK ADD (
    TEAM_NM VARCHAR2(100)
);

CREATE INDEX IX_MSDWHTKD_WRK_TEAM ON XSUP.MSDWHTKD_WRK (TEAM_NM);

-- 확인:
-- SELECT TEAM_NM, COUNT(*) FROM XSUP.MSDWHTKD_WRK GROUP BY TEAM_NM;
