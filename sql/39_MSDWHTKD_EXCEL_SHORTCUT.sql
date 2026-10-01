-- ============================================================
-- GBC Work Hub — "엑셀시트" 탭 바로가기 목록 (모두에게 공유되는 링크 모음)
-- DBA 실행용. 앱에서 CREATE/ALTER 하지 않음.
-- 기존에 앱 코드(MainViewModel)에 하드코딩돼 있던 8개 시드를 그대로 옮겨서,
-- 이 테이블이 유일한 소스가 되게 한다 — 이후 "추가"는 이 테이블에 INSERT만 하면 된다.
-- ============================================================

-- URL에 &file=, &action= 등 '&'로 시작하는 값이 있어 SQL*Plus 치환변수로 오인되는 것을 방지.
SET DEFINE OFF

CREATE SEQUENCE XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE;

CREATE TABLE XSUP.MSDWHTKD_EXCEL_SHORTCUT (
    SHORTCUT_ID   NUMBER          NOT NULL,
    NAME          VARCHAR2(200)   NOT NULL,
    URL           VARCHAR2(1000)  NOT NULL,
    ICON_KEY      VARCHAR2(50)    DEFAULT 'ExcelBrandIcon' NOT NULL,
    SORT_ORDER    NUMBER          DEFAULT 0 NOT NULL,
    CREATED_BY    VARCHAR2(200),
    CREATED_AT    TIMESTAMP       DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT PK_MSDWHTKD_EXCEL_SHORTCUT PRIMARY KEY (SHORTCUT_ID)
);
CREATE INDEX IX_MSDWHTKD_EXCEL_SHORTCUT_SORT ON XSUP.MSDWHTKD_EXCEL_SHORTCUT (SORT_ORDER);

INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '해외사업 - 주요 진행 업무',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7B7565C6B8-7A14-4831-A7CE-BF3A850EED48%7D&file=%ED%95%B4%EC%99%B8%EC%82%AC%EC%97%85%20-%EC%A3%BC%EC%9A%94%20%EC%A7%84%ED%96%89%20%EC%97%85%EB%AC%B4.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 10);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, 'VPN 및 PC 접속정보',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7BB9BDFF44-630E-47D9-842D-3C4A2F51567F%7D&file=VPN%20%EB%B0%8F%20PC%20%EC%A0%91%EC%86%8D%20%EC%A0%95%EB%B3%B4.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 20);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '해외사업본부 운영-PIS 업무',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7BEEDACC7E-3143-419F-82C9-5A53A1BA2C4D%7D&file=%ED%95%B4%EC%99%B8%EC%82%AC%EC%97%85%EB%B3%B8%EB%B6%80%20%EC%9A%B4%EC%98%81-PIS%20%EC%97%85%EB%AC%B4.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 30);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '프로그램 작업내역',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7BE2B8D005-00F4-4564-A309-C0A8285F3B9F%7D&file=%ED%95%B4%EC%99%B8%EC%82%AC%EC%97%85%EC%9A%B4%EC%98%81%ED%8C%80%20%ED%94%84%EB%A1%9C%EA%B7%B8%EB%9E%A8%20%EC%9E%91%EC%97%85%EB%82%B4%EC%97%AD.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 40);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '배포리스트 (MNGHA 외 나머지)',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7B723CC5C5-73EC-4121-8021-4BAEDB636B0B%7D&file=%ED%95%B4%EC%99%B8%EC%82%AC%EC%97%85-%EB%B0%B0%ED%8F%AC%EB%A6%AC%EC%8A%A4%ED%8A%B8(MNGHA%EC%A0%9C%EC%99%B8).xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 50);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '배포리스트 (MNGHA)',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7B0E4CBA08-FA2D-45C7-9F37-DA1184327B40%7D&file=MNGHA%20%EC%9A%B4%EC%98%81%EA%B8%B0%20%EB%B0%B0%ED%8F%AC%20%EB%A6%AC%EC%8A%A4%ED%8A%B8.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 60);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, '주간보고',
     'https://ezcaretechcom-my.sharepoint.com/:x:/r/personal/sangsuri_ezcaretech_com/_layouts/15/Doc.aspx?sourcedoc=%7BE3ED647C-E3C0-4422-B792-9C857926E2E7%7D&file=%EC%9A%B4%EC%98%81%ED%8C%80_%EC%A3%BC%EA%B0%84%EB%B3%B4%EA%B3%A0_2025%EB%85%84.xlsx&action=default&mobileredirect=true',
     'ExcelBrandIcon', 70);
INSERT INTO XSUP.MSDWHTKD_EXCEL_SHORTCUT (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER) VALUES
    (XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT.NEXTVAL, 'Freshdesk (티켓)',
     'https://bestcare.freshdesk.com/a/tickets/filters/mentioned_tickets?orderBy=updated_at',
     'FreshdeskBrandIcon', 80);
COMMIT;
