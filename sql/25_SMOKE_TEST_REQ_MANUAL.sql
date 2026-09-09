-- ============================================================
-- 수동 스모크 테스트 — sql/23, sql/24 적용 후 실제 Oracle 테스트 환경에서 DBA/개발자가 직접 실행.
-- 이 저장소(개발 샌드박스)에는 실제 Oracle 접속이 없어 자동 실행/검증하지 못했습니다.
-- 각 STEP 실행 후 옆의 "기대 결과"와 실제 결과를 비교하세요. 끝에 정리(cleanup) 포함.
-- ============================================================

-- ------------------------------------------------------------
-- STEP 0. 사전 확인
-- ------------------------------------------------------------
SELECT COUNT(*) AS req_table_exists FROM USER_TABLES WHERE TABLE_NAME = 'MSDWHTKD_REQ';          -- 기대: 1
SELECT COUNT(*) AS uk_pc_should_be_gone FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME = 'UK_MSDWHTKD_USR_PC'; -- 기대: 0

-- ------------------------------------------------------------
-- STEP 1. 이미 쓰이던 PC명으로 신규 사용자 가입 (핵심 회귀 테스트)
-- ------------------------------------------------------------
-- 1-1) 기존 사용자 A가 PC01을 쓰고 있다고 가정 (앱의 UpsertUser와 동일한 흐름 — 여기선 직접 재현)
-- 앱을 통해 재현하는 것을 권장. 여기서는 DB 레벨로 최소 재현:
SELECT USR_ID, LOGIN_ID, USER_NM, LOCAL_PC_NM FROM XSUP.MSDWHTKD_USR WHERE LOGIN_ID = 'smoke_userA';
-- 기대: 앱에서 userA로 로그인/가입 후 이 행이 보여야 함 (PC01)

-- 1-2) 같은 PC01에서 userB로 신규 가입(앱에서 회원가입) 후:
SELECT USR_ID, LOGIN_ID, USER_NM, LOCAL_PC_NM FROM XSUP.MSDWHTKD_USR WHERE LOGIN_ID IN ('smoke_userA','smoke_userB');
-- 기대: 서로 다른 USR_ID 2행. userA 행의 USER_NM/LOGIN_ID가 userB로 바뀌어 있으면 실패(회귀).

-- ------------------------------------------------------------
-- STEP 2. 같은 사용자, 다른 PC에서 로그인
-- ------------------------------------------------------------
-- 앱에서 userA로 PC02에서 로그인 후:
SELECT USR_ID, LOCAL_PC_NM FROM XSUP.MSDWHTKD_USR WHERE LOGIN_ID = 'smoke_userA';
-- 기대: USR_ID는 STEP 1과 동일, LOCAL_PC_NM만 PC02로 갱신.

-- ------------------------------------------------------------
-- STEP 3. 요청 생성 (앱의 "+ 요청 등록"으로 수행 후 확인)
-- ------------------------------------------------------------
SELECT REQ_ID, REQUEST_TYPE, TITLE, USER_ID, AUTHOR_NM, STATUS, APP_VERSION
  FROM XSUP.MSDWHTKD_REQ ORDER BY REQ_ID DESC FETCH FIRST 1 ROWS ONLY;
-- 기대: STATUS='OPEN', USER_ID = 로그인한 사용자의 USR_ID, AUTHOR_NM = 그 시점 표시명 스냅샷.

-- ------------------------------------------------------------
-- STEP 4. 댓글 생성/수정/삭제 (앱에서 순서대로 수행)
-- ------------------------------------------------------------
SELECT COMMENT_ID, USER_ID, COMMENT_TEXT, CREATED_AT, UPDATED_AT
  FROM XSUP.MSDWHTKD_REQ_COMMENT WHERE REQ_ID = :reqId ORDER BY COMMENT_ID;
-- 등록 직후: 1행 존재. 수정 후: COMMENT_TEXT/UPDATED_AT 갱신, CREATED_AT은 불변.
-- 삭제 후 재조회: 0행이어야 함(물리 삭제).

-- ------------------------------------------------------------
-- STEP 5. 반응 MERGE/변경 (동일 사용자가 반응 버튼을 두 번 다른 종류로 클릭)
-- ------------------------------------------------------------
SELECT REQ_ID, USER_ID, REACTION_TYPE, CREATED_AT, UPDATED_AT
  FROM XSUP.MSDWHTKD_REQ_REACTION WHERE REQ_ID = :reqId;
-- 기대: 같은 (REQ_ID, USER_ID)에 대해 항상 1행만 존재(복합 PK), REACTION_TYPE만 바뀌고 CREATED_AT은 최초값 유지,
-- UPDATED_AT만 갱신. 행이 2개로 늘어나면 실패(MERGE 오작동).

-- ------------------------------------------------------------
-- STEP 6. 첨부 BLOB 삽입/조회 (앱에서 이미지 추가 후 상세 재조회로 썸네일 표시 확인)
-- ------------------------------------------------------------
SELECT ATTACH_ID, FILE_NM, FILE_EXT, FILE_SIZE, DBMS_LOB.GETLENGTH(FILE_DATA) AS BLOB_LEN
  FROM XSUP.MSDWHTKD_REQ_ATTACH WHERE REQ_ID = :reqId;
-- 기대: BLOB_LEN > 0 이고 FILE_SIZE와 대략 일치. 5MB(5242880) 초과 파일은 앱단에서 애초에 거부되어야 함(재확인).

-- ------------------------------------------------------------
-- STEP 7. 요청 삭제 시 자식 CASCADE 확인
-- ------------------------------------------------------------
-- 앱에서 해당 요청 삭제 실행 후:
SELECT COUNT(*) FROM XSUP.MSDWHTKD_REQ WHERE REQ_ID = :reqId;          -- 기대: 0
SELECT COUNT(*) FROM XSUP.MSDWHTKD_REQ_COMMENT WHERE REQ_ID = :reqId;  -- 기대: 0
SELECT COUNT(*) FROM XSUP.MSDWHTKD_REQ_REACTION WHERE REQ_ID = :reqId; -- 기대: 0
SELECT COUNT(*) FROM XSUP.MSDWHTKD_REQ_ATTACH WHERE REQ_ID = :reqId;   -- 기대: 0

-- ------------------------------------------------------------
-- STEP 8. 관리자 상태/해결버전 갱신
-- ------------------------------------------------------------
SELECT REQ_ID, STATUS, RESOLVED_VERSION, ADMIN_NOTE, RESOLVED_AT
  FROM XSUP.MSDWHTKD_REQ WHERE REQ_ID = :reqId2;
-- 관리자 화면에서 상태를 RESOLVED로 바꾸고 해결버전 입력 후 저장 → RESOLVED_AT이 SYSTIMESTAMP로 채워져야 함.
-- 다른 상태로 되돌리면 RESOLVED_AT은 그대로 남는지도 확인(현재 코드는 되돌릴 때 지우지 않음 — 의도한 동작인지 판단 필요).

-- ------------------------------------------------------------
-- STEP 9. 목록 조회가 FILE_DATA(BLOB)를 절대 로드하지 않는지 — SQL 트레이스로 확인 (권장)
-- ------------------------------------------------------------
-- SQL*Trace 또는 V$SQL로 앱이 실제 발행한 SELECT 문을 확인해, MSDWHTKD_REQ_ATTACH.FILE_DATA 컬럼이
-- 목록(GetPageAsync)/헤더 조회(ReqSelectBase) SQL에 전혀 나타나지 않는지 확인하십시오.
-- (코드 레벨로는 이미 확인됨: ReqSelectBase 상수와 GetPageCore/GetByIdCore의 헤더 SELECT에는
--  FILE_DATA 컬럼이 없고, GetAttachmentsCore(reqId, includeData)만 별도로 호출되며 그마저도
--  상세 화면 진입 시 해당 요청 1건에 대해서만 호출됩니다.)
SELECT SQL_TEXT FROM V$SQL WHERE UPPER(SQL_TEXT) LIKE '%MSDWHTKD_REQ_ATTACH%' AND UPPER(SQL_TEXT) LIKE '%FILE_DATA%';
-- 기대: 실행된 SQL 중 FILE_DATA를 SELECT하는 문이 "상세 조회" 용도로만 나타나고, COUNT(*)/목록 페이징 쿼리에는 없어야 함.

-- ============================================================
-- 정리 (테스트 데이터 삭제 — 실제 운영 데이터가 섞이지 않도록 STEP 3에서 만든 REQ_ID만 지정해서 실행)
-- ============================================================
-- DELETE FROM XSUP.MSDWHTKD_REQ WHERE REQ_ID IN (:reqId, :reqId2); -- 자식은 CASCADE로 함께 삭제됨
-- DELETE FROM XSUP.MSDWHTKD_USR WHERE LOGIN_ID IN ('smoke_userA','smoke_userB'); -- 필요 시에만
-- COMMIT;
