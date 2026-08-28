-- SEQ_MSDWHTKH 가 없을 때 (ORA-02289) 실행
-- 테이블만 개명하고 시퀀스는 안 바뀐 경우 / 시퀀스 미생성 경우

-- 1) 구 시퀀스가 남아 있으면 개명
-- RENAME SEQ_MSDWHTKD_LOG TO SEQ_MSDWHTKH;

-- 2) 아예 없으면 신규
CREATE SEQUENCE XSUP.SEQ_MSDWHTKH
    START WITH 1
    INCREMENT BY 1
    NOCACHE
    NOCYCLE;

-- 확인
-- SELECT sequence_name FROM all_sequences
--  WHERE sequence_owner = 'XSUP' AND sequence_name = 'SEQ_MSDWHTKH';
