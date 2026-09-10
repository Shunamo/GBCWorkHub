-- ============================================================
-- GBC Work Hub — 이전(비-태그) 업무기록 이력 마이그레이션 삭제
-- sql/33~36을 처음 실행했을 때는 CLIENT_KEY 태그가 없었으므로,
-- CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀' 조건으로 그 배치만
-- 정확히 골라 삭제한다. 실제 앱에서 사용자가 저장한 업무기록은 항상
-- CLIENT_KEY가 채워지므로(WorkLogDbMapper.ToDto) 영향받지 않는다.
-- 삭제 후 새로 생성된 sql/33~36(CLIENT_KEY 태그 + 중복 스킵 포함)을
-- 다시 실행하면 된다.
-- ============================================================

-- 삭제 전 확인 (몇 건 지워질지 미리 확인):
SELECT COUNT(*) FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀';

DELETE FROM XSUP.MSDWHTKD_WRK_SRC
 WHERE LOG_ID IN (
     SELECT LOG_ID FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀'
 );

DELETE FROM XSUP.MSDWHTKD_WRK_PRJ
 WHERE LOG_ID IN (
     SELECT LOG_ID FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀'
 );

DELETE FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀';

COMMIT;

-- 삭제 후 확인 (0건이어야 함):
SELECT COUNT(*) FROM XSUP.MSDWHTKD_WRK WHERE CLIENT_KEY IS NULL AND TEAM_NM = '해외사업운영팀';