-- ============================================================
-- MSDWHTKD_PCMAP.PC_DOMAIN / PC_NOTE / PC_COMMENT — vpn.xlsx
-- PC_DOMAIN : domain\user, user@host (카드 표시)
-- PC_NOTE   : [ID] / [Password] / [VPN ID] / [VPN Password]
-- PC_COMMENT: 엑셀 Comment (앱에서 수정 가능; 재시드 시 기존값 유지)
-- DBA 실행용. 앱에서 ALTER 하지 않음.
-- ============================================================

-- PL/SQL Developer / SQL*Plus: 비번에 & 가 있으면 치환변수 창이 뜸
SET DEFINE OFF;

BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_PCMAP ADD (PC_DOMAIN VARCHAR2(500))';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_PCMAP MODIFY (PC_DOMAIN VARCHAR2(500))';
EXCEPTION
    WHEN OTHERS THEN
        NULL; -- already wide enough / type issue — ignore
END;
/

BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_PCMAP ADD (PC_NOTE VARCHAR2(4000))';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE XSUP.MSDWHTKD_PCMAP ADD (PC_COMMENT VARCHAR2(2000))';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -1430 THEN RAISE; END IF;
END;
/


-- CMC: 9 rows
MERGE INTO XSUP.MSDWHTKD_PCMAP t
USING (
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH1' AS PC_NM, q'{dxbcmc\bcarep.admin
dxbcmc\Hong.red}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin
dxbcmc\Hong.red

[Password]
1234qwer!

[VPN ID]
park.eg

[VPN Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH2' AS PC_NM, q'{dxbcmc\bcarep.admin}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin

[Password]
1234qwer!

[VPN ID]
Yoo.jj

[VPN Password]
1234qwer!}' AS PC_NOTE, q'{prd : Yang.ss
1234Qwer!!!}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH3' AS PC_NM, q'{dxbcmc\bcarep.admin}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin

[Password]
1234qwer!

[VPN ID]
lee.dk
kim.hw

[VPN Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH4' AS PC_NM, q'{dxbcmc\bcarep.admin}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin

[Password]
1234qwer!

[VPN ID]
Yang.ss
lee.es

[VPN Password]
1234Qwer!!!
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH5' AS PC_NM, q'{dxbcmc\Im.hs}' AS PC_DOMAIN, q'{[ID]
dxbcmc\Im.hs

[Password]
1234qwer!

[VPN ID]
Im.hs

[VPN Password]
1234qwer!}' AS PC_NOTE, q'{Yang.ss
1234qwer!}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH6' AS PC_NM, q'{dxbcmc\bcarep.admin
dxbcmc\shin.dm}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin
dxbcmc\shin.dm

[Password]
1234qwer!

[VPN ID]
shin.dm

[VPN Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH7' AS PC_NM, q'{dxbcmc\Jo.jm}' AS PC_DOMAIN, q'{[ID]
dxbcmc\Jo.jm

[Password]
1234Qwer!!!

[VPN ID]
jo.jm

[VPN Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH8' AS PC_NM, q'{dxbcmc\bcarep.admin}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin

[Password]
1234Qwer!!!

[VPN ID]
jo.jj

[VPN Password]
1234qwer!}' AS PC_NOTE, q'{로컬 실행시, transaction 에러 발생}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'CMC' AS SITE_CD, 'P-BCTECH9' AS PC_NM, q'{dxbcmc\bcarep.admin
dxbcmc\bcared.admin}' AS PC_DOMAIN, q'{[ID]
dxbcmc\bcarep.admin
dxbcmc\bcared.admin

[Password]
1234qwer!

[VPN ID]
jeong.wj

[VPN Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
) s
ON (UPPER(TRIM(t.SITE_CD)) = UPPER(TRIM(s.SITE_CD))
AND UPPER(TRIM(t.PC_NM)) = UPPER(TRIM(s.PC_NM)))
WHEN MATCHED THEN UPDATE SET
    t.PC_DOMAIN = s.PC_DOMAIN,
    t.PC_NOTE = s.PC_NOTE,
    t.PC_COMMENT = NVL(t.PC_COMMENT, s.PC_COMMENT),
    t.UPDT_DTM = SYSTIMESTAMP
WHEN NOT MATCHED THEN INSERT (MAP_ID, SITE_CD, PC_NM, PC_DOMAIN, PC_NOTE, PC_COMMENT, UPDT_DTM)
VALUES (XSUP.SEQ_MSDWHTKD_PCMAP.NEXTVAL, s.SITE_CD, s.PC_NM, s.PC_DOMAIN, s.PC_NOTE, s.PC_COMMENT, SYSTIMESTAMP);


-- RC: 26 rows
MERGE INTO XSUP.MSDWHTKD_PCMAP t
USING (
    SELECT 'RC' AS SITE_CD, 'HO-01-DTT-03' AS PC_NM, q'{rchsp\heebeomkim
rchj\heebeomkim
heebeomkim@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchj\heebeomkim

[Password]
Hk@@102030

[VPN ID]
rchsp\heebeomkim
heebeomkim@rchsp.med.sa

[VPN Password]
Hk@9080100}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-01-DTT-04' AS PC_NM, q'{rchsp\hotaeryu}' AS PC_DOMAIN, q'{[ID]
rchsp\hotaeryu

[Password]
Ht@#102030

[VPN ID]
hotaeryu

[VPN Password]
Hr##102030}' AS PC_NOTE, q'{DA팀 사용, 8월 12일 변경}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-01-DTT-56' AS PC_NM, q'{sunghoikim@rchsp.med.sa
rchj\sunghoikim}' AS PC_DOMAIN, q'{[ID]
sunghoikim@rchsp.med.sa
rchj\sunghoikim

[Password]
Sk@@102030
Si@9080100

[VPN ID]
sunghoikim
leees

[VPN Password]
sl@@102030
ll@@102030}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-01-HIS-11' AS PC_NM, q'{rchj\sangsoolee
rchsp\sangsoolee}' AS PC_DOMAIN, q'{[ID]
rchj\sangsoolee

[Password]
Sl@@102030

[VPN ID]
rchsp\sangsoolee

[VPN Password]
Se@9080100}' AS PC_NOTE, q'{쥬베일에에서 얀부로그인 2016014/11111
20211101_RCHSP Phase3팀 이준민 책임으로 부터 공유 받음}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-01-HIS-95' AS PC_NM, q'{rchj\hyeongjin}' AS PC_DOMAIN, q'{[ID]
rchj\hyeongjin

[Password]
Hp@9080100

[VPN ID]
hyeongjin

[VPN Password]
Hp@9080100}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-01-TRN-03' AS PC_NM, q'{rchsp\leees}' AS PC_DOMAIN, q'{[ID]
rchsp\leees

[Password]
RcH@$102030}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-01' AS PC_NM, q'{rchsp\youngjaekim}' AS PC_DOMAIN, q'{[ID]
rchsp\youngjaekim

[Password]
Yk@@102030
Yk@9080100 아니고 Yk@#9080100

[VPN ID]
youngjaekim

[VPN Password]
1234Qwer!!!}' AS PC_NOTE, q'{TA 사용}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-07' AS PC_NM, q'{rchsp\sukhoonyoon
(sukhoonyoon@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchsp\sukhoonyoon

[VPN ID]
rchsp\sukhoonyoon

[VPN Password]
Sy@@102030}' AS PC_NOTE, q'{골든 운영기 : 
 -xsup/xsup98557kiAKG
 -hsup/hsup44350cLfRe
 -xmed/xmed74840EFmjF
 -hmed / hmed94756uEiTI
 -xbil/ xbil62026NpinT
 -xmis/xmis84056CXljR
- psup/ 1234qwer!
- pcom / pcom21
- pmed / 1234qwer!
- hbil / hbil95083ELPBS
- xgab / xgab38388TFMXv}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-09' AS PC_NM, q'{rchsp\jongheunkim
sunhayoon@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchsp\jongheunkim

[Password]
Jk@@102030

[VPN ID]
sunhayoon@rchsp.med.sa

[VPN Password]
Sy@#102030}' AS PC_NOTE, q'{3/31 추가 (RC3차->운영),
2022/09/15 비밀번호 만료 -> 변경 - BC@Rch@123123 -> BC@Rch123123}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-10' AS PC_NM, q'{rchj\hongys
rchsp\leesc}' AS PC_DOMAIN, q'{[ID]
rchj\hongys

[Password]
RcH@$102030

[VPN ID]
rchsp\leesc

[VPN Password]
RcH@$102030}' AS PC_NOTE, q'{운영팀 진료간호 (이승철)
->(2026-03-06, 정규봉) 접속여부 확인필요
   RC 접속 확인되면, 오기영
->(2026-05-28, 정규봉) VPN 계정 리셋완료}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-11' AS PC_NM, q'{rchsp\sunghoikim
rchj\jeongjunyoo}' AS PC_DOMAIN, q'{[ID]
rchsp\sunghoikim

[Password]
Sk@@102030

[VPN ID]
sunghoikim

[VPN Password]
Qwer1234!
Si@9080100}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-12' AS PC_NM, q'{rchsp\kwanjoongkim}' AS PC_DOMAIN, q'{[ID]
rchsp\kwanjoongkim

[Password]
Kk@@102030

[VPN ID]
rchsp\kwanjoongkim}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-13' AS PC_NM, q'{rchj\sunhayoon
rchsp\sunhayoon
kimeh@rchsp.med.sa
sunhayoon@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchj\sunhayoon
rchsp\sunhayoon

[Password]
rchj\sunhayoon   // Sy$%654987
Sy@#102030

[VPN ID]
kimeh@rchsp.med.sa
sunhayoon@rchsp.med.sa

[VPN Password]
Kh@9080100
Sn@9080100}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-14' AS PC_NM, q'{rchsp\hanwoolkim}' AS PC_DOMAIN, q'{[ID]
rchsp\hanwoolkim

[Password]
1234Qwer!!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BCARE-15' AS PC_NM, q'{rchsp\kwanjoongkim}' AS PC_DOMAIN, q'{[ID]
rchsp\kwanjoongkim

[Password]
Kk@@102030}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-01' AS PC_NM, q'{rchj\hongys
rchsp\yangss}' AS PC_DOMAIN, q'{[ID]
rchj\hongys

[Password]
Hs@@102030

[VPN ID]
rchsp\yangss

[VPN Password]
Ys@@102030}' AS PC_NOTE, q'{골든 운영기 : 
 -xsup/xsup98557kiAKG
 -hsup/hsup44350cLfRe
 -xmed/xmed74840EFmjF
 -hmed / hmed94756uEiTI
 -xbil/ xbil62026NpinT
 -xmis/xmis84056CXljR
- psup/ 1234qwer!
- pcom / pcom21
- pmed / 1234qwer!
- hbil / hbil95083ELPBS
- xgab / xgab38388TFMXv}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-02' AS PC_NM, q'{rchj\sangsoolee}' AS PC_DOMAIN, q'{[ID]
rchj\sangsoolee

[Password]
Sl@@102030
Se@9080100}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-04' AS PC_NM, q'{rchsp\sukyeonglee
sukyeonglee@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchsp\sukyeonglee

[Password]
Sl@9080100

[VPN ID]
sukyeonglee@rchsp.med.sa

[VPN Password]
sl@@102030}' AS PC_NOTE, q'{3/31 추가 (RC3차->운영)}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-05' AS PC_NM, q'{rchsp\youngjaekim}' AS PC_DOMAIN, q'{[ID]
rchsp\youngjaekim

[Password]
1234Qwer!!!

[VPN ID]
youngsikjeon

[VPN Password]
RcH@$102030}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-06' AS PC_NM, q'{rchj\sunhayoon(1648013
rchsp\kimsj}' AS PC_DOMAIN, q'{[ID]
rchj\sunhayoon

[Password]
Sy@#102030

[VPN ID]
rchsp\kimsj}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-BESTCARE-07' AS PC_NM, q'{rchj\noleawoo
hongys@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchj\noleawoo

[Password]
Wdddt#2111 \ Tk@@102030

[VPN ID]
hongys@rchsp.med.sa

[VPN Password]
Wdddt#2111}' AS PC_NOTE, q'{3/31 추가 (RC3차->운영)}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HO-DTT-05' AS PC_NM, q'{rchj\jeongjinjoe}' AS PC_DOMAIN, q'{[ID]
rchj\jeongjinjoe

[Password]
Jj@9080100

[VPN ID]
rchj\jeongjinjoe

[VPN Password]
Qwer1234!}' AS PC_NOTE, q'{인터페이스 모니터링 용}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'HOS-ITD-HIS-02' AS PC_NM, q'{rchsp\youngsikjeon}' AS PC_DOMAIN, q'{[ID]
rchsp\youngsikjeon

[Password]
RcH@$102030}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'JM-HIS-RCJ-01' AS PC_NM, q'{rchj\yunhokim
rchsp\yunhokim
yunhokim@rchsp.med.sa}' AS PC_DOMAIN, q'{[ID]
rchj\yunhokim
rchsp\yunhokim

[Password]
Y@@9080100
[1234qwer!!]

[VPN ID]
yunhokim@rchsp.med.sa

[VPN Password]
Y@@9080100}' AS PC_NOTE, q'{12월 21일 IP 변경됨(62->97)}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'JM-HIS-RCJ-10' AS PC_NM, q'{rchj\dongguenlee
rchsp\dongguenlee}' AS PC_DOMAIN, q'{[ID]
rchj\dongguenlee

[Password]
1234Qwer!!!
PC 접속 비번은 그대로
게이트웨이 자격증명 비밀번호만 변경됨

[VPN ID]
rchsp\dongguenlee

[VPN Password]
RcH@$102030}' AS PC_NOTE, q'{https://sts.rchsp.med.sa/adfs/portal/updatepassword
VPN 패스워드 변경
RCHSP VPN 접속후 > 해당 URL.에서 비번 변경 (ex. kwanjoongkim@rchsp.med.sa)}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'RC' AS SITE_CD, 'JM-HIS-RCJ-25' AS PC_NM, q'{rchj\jeongjunyoo}' AS PC_DOMAIN, q'{[ID]
rchj\jeongjunyoo

[Password]
Jj@102030

[VPN ID]
Taesungkim

[VPN Password]
N#7b1bAM$m&rf90}' AS PC_NOTE, q'{<NIC 로그인 정보>
NHIC@RCHSP.MED.SA
NhRcApi$658}' AS PC_COMMENT FROM DUAL
) s
ON (UPPER(TRIM(t.SITE_CD)) = UPPER(TRIM(s.SITE_CD))
AND UPPER(TRIM(t.PC_NM)) = UPPER(TRIM(s.PC_NM)))
WHEN MATCHED THEN UPDATE SET
    t.PC_DOMAIN = s.PC_DOMAIN,
    t.PC_NOTE = s.PC_NOTE,
    t.PC_COMMENT = NVL(t.PC_COMMENT, s.PC_COMMENT),
    t.UPDT_DTM = SYSTIMESTAMP
WHEN NOT MATCHED THEN INSERT (MAP_ID, SITE_CD, PC_NM, PC_DOMAIN, PC_NOTE, PC_COMMENT, UPDT_DTM)
VALUES (XSUP.SEQ_MSDWHTKD_PCMAP.NEXTVAL, s.SITE_CD, s.PC_NM, s.PC_DOMAIN, s.PC_NOTE, s.PC_COMMENT, SYSTIMESTAMP);


-- AURORA: 15 rows
MERGE INTO XSUP.MSDWHTKD_PCMAP t
USING (
    SELECT 'AURORA' AS SITE_CD, 'KEB-3TYYVP2' AS PC_NM, q'{aurorabehaviora\min.ryan}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\min.ryan

[Password]
1234qwer!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-3VNZVP2' AS PC_NM, q'{aurorabehaviora\lee.hyejung}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.hyejung

[Password]
P@SSw0rd1!}' AS PC_NOTE, q'{NA00002005}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-505F8R2' AS PC_NM, q'{aurorabehaviora\lee.eunsol}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.eunsol

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-71V73Q3' AS PC_NM, q'{AURORABEHAVIORA\yang.seungsub}' AS PC_DOMAIN, q'{[ID]
AURORABEHAVIORA\yang.seungsub

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-7VY98V3' AS PC_NM, q'{aurorabehaviora\yang.junhwan}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\yang.junhwan

[Password]
P@SSw0rd1!}' AS PC_NOTE, q'{DB
개발기 : xsup/xsup21
스테이징/운영기 : xsup/xsupAurora!23}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-7ZT73Q3' AS PC_NM, q'{aurorabehaviora\lee.eunsol}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.eunsol

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-84Y73Q3' AS PC_NM, q'{aurorabehaviora\shin.dongmyung}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\shin.dongmyung

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-B2V73Q3' AS PC_NM, q'{aurorabehaviora\bcared.admin}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\bcared.admin

[Password]
Bestc@re!123d}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-BC-6TSD0F2' AS PC_NM, q'{aurorabehaviora\im.hyungsoon}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\im.hyungsoon

[Password]
P@SSw0rd1!}' AS PC_NOTE, q'{DB : xsup/xsupAurora!23}' AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-BC-8TSD0F2' AS PC_NM, q'{aurorabehaviora\lee.hyejung}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.hyejung

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'keb-bc-cssd0f1' AS PC_NM, q'{aurorabehaviora\bcared.admin}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\bcared.admin

[Password]
.20.0.94}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-BC-HTSD0F2' AS PC_NM, q'{aurorabehaviora\lee.eunsol}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.eunsol

[Password]
P@SSw0rd1!

[VPN Password]
ID : ez200064 / Password : k!DBG5'qo&)8aFm}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-CRY98V3' AS PC_NM, q'{aurorabehaviora\hong.yooseung}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\hong.yooseung

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-DRY98V3' AS PC_NM, q'{AURORABEHAVIORA\yang.seungsub}' AS PC_DOMAIN, q'{[ID]
AURORABEHAVIORA\yang.seungsub

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
    UNION ALL
    SELECT 'AURORA' AS SITE_CD, 'KEB-HYT73Q3' AS PC_NM, q'{aurorabehaviora\lee.eunsol}' AS PC_DOMAIN, q'{[ID]
aurorabehaviora\lee.eunsol

[Password]
P@SSw0rd1!}' AS PC_NOTE, NULL AS PC_COMMENT FROM DUAL
) s
ON (UPPER(TRIM(t.SITE_CD)) = UPPER(TRIM(s.SITE_CD))
AND UPPER(TRIM(t.PC_NM)) = UPPER(TRIM(s.PC_NM)))
WHEN MATCHED THEN UPDATE SET
    t.PC_DOMAIN = s.PC_DOMAIN,
    t.PC_NOTE = s.PC_NOTE,
    t.PC_COMMENT = NVL(t.PC_COMMENT, s.PC_COMMENT),
    t.UPDT_DTM = SYSTIMESTAMP
WHEN NOT MATCHED THEN INSERT (MAP_ID, SITE_CD, PC_NM, PC_DOMAIN, PC_NOTE, PC_COMMENT, UPDT_DTM)
VALUES (XSUP.SEQ_MSDWHTKD_PCMAP.NEXTVAL, s.SITE_CD, s.PC_NM, s.PC_DOMAIN, s.PC_NOTE, s.PC_COMMENT, SYSTIMESTAMP);

COMMIT;

-- SELECT SITE_CD, PC_NM, PC_DOMAIN, SUBSTR(PC_NOTE,1,80), SUBSTR(PC_COMMENT,1,40)
--   FROM XSUP.MSDWHTKD_PCMAP
--  WHERE PC_DOMAIN IS NOT NULL OR PC_NOTE IS NOT NULL OR PC_COMMENT IS NOT NULL
--  ORDER BY SITE_CD, PC_NM;