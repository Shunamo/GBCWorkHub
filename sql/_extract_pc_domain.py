# -*- coding: utf-8 -*-
"""Rebuild 19_ALTER_PCMAP_PC_DOMAIN.sql from vpn.xlsx.
PC_DOMAIN  = domain\\user / user@host tokens (no spaces)
PC_NOTE    = [ID] / [Password] only (clean lines)
PC_COMMENT = excel Comment (user-editable; seed fills only if null)
"""
import zipfile
import re
import xml.etree.ElementTree as ET
from collections import defaultdict, OrderedDict

z = zipfile.ZipFile("vpn.xlsx")
ns = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
ss = ET.fromstring(z.read("xl/sharedStrings.xml"))


def shared_string_text(si):
    """Shared string without strikethrough runs (엑셀 중앙선 삭제분 제외)."""
    runs = list(si.findall("m:r", ns))
    if runs:
        parts = []
        for r in runs:
            rPr = r.find("m:rPr", ns)
            if rPr is not None and rPr.find("m:strike", ns) is not None:
                continue
            for t in r.findall(".//m:t", ns):
                parts.append(t.text or "")
        return "".join(parts)
    return "".join(t.text or "" for t in si.findall(".//m:t", ns))


strings = [shared_string_text(si) for si in ss.findall("m:si", ns)]
sheet = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
styles = ET.fromstring(z.read("xl/styles.xml"))
_fonts = styles.findall("m:fonts/m:font", ns)
_strike_fonts = {
    i for i, f in enumerate(_fonts) if f.find("m:strike", ns) is not None
}
_strike_xfs = set()
for i, xf in enumerate(styles.findall("m:cellXfs/m:xf", ns)):
    fi = xf.get("fontId")
    if fi is not None and int(fi) in _strike_fonts:
        _strike_xfs.add(i)


def col_letters(ref):
    return "".join(c for c in ref if c.isalpha())


def cell_val(c):
    """Cell text; whole-cell strikethrough → empty."""
    s = c.get("s")
    if s is not None and s.isdigit() and int(s) in _strike_xfs:
        return ""
    t = c.get("t")
    v = c.find("m:v", ns)
    if v is None:
        return ""
    val = v.text or ""
    if t == "s" and val.isdigit():
        return strings[int(val)]
    return val


def map_site(s):
    u = (s or "").upper()
    if "CMC" in u or "CLEMENCEAU" in u or "DUBAI" in u:
        return "CMC"
    if "AURORA" in u:
        return "AURORA"
    if "MNGHA" in u or "NGHA" in u or "NATIONAL GUARD" in u:
        return "MNGHA"
    if "RC" in u or "RCJY" in u or "RCYMC" in u or "JEJU" in u or "RCH" in u:
        return "RC"
    return None


IP_RE = re.compile(r"\b(\d{1,3}(?:\.\d{1,3}){3})\b")
# no whitespace around \ or @
CRED_RE = re.compile(r"(?<!\S)([^\s\\/]+\\[^\s\\/]+|[^\s@]+@[^\s@]+)(?!\S)")


def extract_pcs_and_ip(b):
    text = (b or "").replace('"', "")
    ips = IP_RE.findall(text)
    lines = [x.strip() for x in text.split("\n") if x.strip()]
    found = []
    seen = set()
    for line in lines:
        line2 = re.sub(r"^[\d\.\s\->?]+", "", line).strip()
        for m in re.finditer(r"[A-Za-z][A-Za-z0-9._-]{2,}", line2):
            cand = m.group(0).strip(".,;")
            if IP_RE.fullmatch(cand):
                continue
            if cand.lower() in ("pc", "id", "vpn", "his", "ram", "gb", "cpu", "intel", "xeon"):
                continue
            cu = cand.upper()
            if not (
                "-" in cand
                or cu.startswith(("P-", "HO-", "JM-", "HOS-", "NUR", "KEB", "WKS", "SRV"))
                or any(k in cu for k in ("HIS", "BCARE", "TECH", "NURS"))
            ):
                continue
            if cu in seen:
                continue
            seen.add(cu)
            found.append(cand)
    ip = ips[-1] if ips else None
    return found, ip


def extract_creds(text):
    """All domain\\user or user@host tokens without spaces, order preserved."""
    if not text:
        return []
    out = []
    seen = set()
    for m in CRED_RE.finditer(text.replace('"', "").replace("[", "").replace("]", "")):
        tok = m.group(1).strip(".,;:)")
        # skip obvious non-accounts
        if "@" in tok and "." not in tok.split("@", 1)[-1]:
            continue
        key = tok.lower()
        if key in seen:
            continue
        seen.add(key)
        out.append(tok)
    return out


def norm_cell(s):
    if not s:
        return ""
    t = s.replace("\r\n", "\n").replace("\r", "\n").strip()
    t = t.strip('"').strip()
    return t


ARROW_RE = re.compile(r"\s*(?:->|→|->)\s*")
NUMBERED_FULL_RE = re.compile(r"^\s*\d+\s*[\.\)]\s*([^:：]*)[:：]\s*(.+)\s*$")
SIMPLE_USER_RE = re.compile(r"^[A-Za-z][A-Za-z0-9._-]{1,40}$")


def is_instruction(line):
    t = (line or "").strip()
    if not t:
        return True
    if t.startswith("순서는"):
        return True
    if "진행한다" in t and ":" not in t and "：" not in t:
        return True
    return False


def is_vpn_noise(line):
    t = (line or "").strip()
    if not t:
        return True
    low = t.lower()
    if low in ("forticlient", "horizon vpn", "cisco", "vpn", "id", "(ad account)", "password"):
        return True
    if low.startswith("otp"):
        return True
    if "http://" in low or "https://" in low:
        return True
    if "omnissa" in low or "passtrix" in low:
        return True
    return False


def final_password(payload):
    parts = [p.strip().strip('"') for p in ARROW_RE.split(payload or "")]
    for p in reversed(parts):
        if p and "안되면" not in p:
            # drop trailing parenthetical notes
            p2 = re.split(r"\s*[\(（]", p, maxsplit=1)[0].strip()
            if p2:
                return p2
    return None


def extract_pw_lines(text):
    """Clean password list; numbered rows take last value after -> chain."""
    out, seen = [], set()
    lines = (text or "").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    i = 0
    while i < len(lines):
        line = lines[i].strip().strip('"')
        if is_instruction(line):
            i += 1
            continue
        m = NUMBERED_FULL_RE.match(line)
        if m:
            payload = m.group(2).strip()
            while i + 1 < len(lines):
                nxt = lines[i + 1].strip()
                if not nxt or NUMBERED_FULL_RE.match(nxt):
                    break
                if nxt.startswith("->") or nxt.startswith("→"):
                    payload = payload + " " + nxt
                    i += 1
                    continue
                break
            pw = final_password(payload)
        elif line.startswith("->") or line.startswith("→"):
            i += 1
            continue
        else:
            pw = final_password(line)
        if pw and pw not in seen:
            seen.add(pw)
            out.append(pw)
        i += 1
    return out


def extract_id_lines(text):
    """PC/계정 ID 토큰 (domain\\user, email)."""
    out, seen = [], set()
    for raw in (text or "").split("\n"):
        line = raw.strip().strip('"').strip("[]")
        if is_instruction(line) or is_vpn_noise(line):
            continue
        m = NUMBERED_FULL_RE.match(line)
        candidate = m.group(2).strip() if m else line
        candidate = re.split(r"\s*[\(（]", candidate, maxsplit=1)[0].strip()
        found = extract_creds(candidate)
        if not found and ("\\" in candidate or "@" in candidate) and " " not in candidate:
            found = [candidate]
        for tok in found:
            key = tok.lower()
            if key in seen:
                continue
            seen.add(key)
            out.append(tok)
    return out


def extract_vpn_ids(text):
    """VPN 계정: domain\\user / email / jo.jj 형태."""
    out, seen = [], set()
    for raw in (text or "").split("\n"):
        line = raw.strip().strip('"')
        if is_instruction(line) or is_vpn_noise(line):
            continue
        m = NUMBERED_FULL_RE.match(line)
        candidate = m.group(2).strip() if m else line
        candidate = re.split(r"\s*[\(（]", candidate, maxsplit=1)[0].strip()
        if not candidate:
            continue
        found = extract_creds(candidate)
        if found:
            for tok in found:
                key = tok.lower()
                if key in seen:
                    continue
                seen.add(key)
                out.append(tok)
            continue
        # first token only (jo.jj / sunghoikim)
        token = candidate.split()[0].strip(".,;:")
        if SIMPLE_USER_RE.match(token):
            key = token.lower()
            if key not in seen:
                seen.add(key)
                out.append(token)
    return out


def split_numbered_payloads(text):
    """numbered 'VPN…' vs 'PC…' payloads. Returns (vpn_payloads, pc_payloads, has_split)."""
    vpn, pc = [], []
    has = False
    for raw in (text or "").replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = raw.strip().strip('"')
        m = NUMBERED_FULL_RE.match(line)
        if not m:
            continue
        label = (m.group(1) or "").strip().upper()
        payload = m.group(2).strip()
        if "VPN" in label:
            has = True
            vpn.append(payload)
        elif "PC" in label or "도메인" in label:
            has = True
            pc.append(payload)
    return vpn, pc, has


def build_note(cells):
    """[ID]/[Password]/[VPN ID]/[VPN Password] for RC/CMC detail panel."""
    c = norm_cell(cells.get("C"))
    d = norm_cell(cells.get("D"))
    h = norm_cell(cells.get("H"))
    i = norm_cell(cells.get("I"))

    c_vpn, c_pc, c_split = split_numbered_payloads(c)
    d_vpn, d_pc, d_split = split_numbered_payloads(d)

    if c_split:
        ids = []
        for payload in c_pc:
            for tok in extract_id_lines(payload):
                if tok.lower() not in {x.lower() for x in ids}:
                    ids.append(tok)
        # numbered 외 줄(이메일 등)은 PC ID로 유지
        for raw in c.split("\n"):
            line = raw.strip()
            if not line or NUMBERED_FULL_RE.match(line) or is_instruction(line):
                continue
            for tok in extract_id_lines(line):
                if tok.lower() not in {x.lower() for x in ids}:
                    ids.append(tok)
    else:
        ids = extract_id_lines(c)

    if d_split:
        pws = []
        for payload in d_pc:
            for tok in extract_pw_lines(payload):
                if tok not in pws:
                    pws.append(tok)
    else:
        pws = extract_pw_lines(d)

    vpn_ids = []
    if c_split:
        for payload in c_vpn:
            for tok in extract_id_lines(payload) or extract_vpn_ids(payload):
                if tok.lower() not in {x.lower() for x in vpn_ids}:
                    vpn_ids.append(tok)
    for tok in extract_vpn_ids(h):
        if tok.lower() not in {x.lower() for x in vpn_ids}:
            vpn_ids.append(tok)

    vpn_pws = []
    if d_split:
        for payload in d_vpn:
            for tok in extract_pw_lines(payload):
                if tok not in vpn_pws:
                    vpn_pws.append(tok)
    for tok in extract_pw_lines(i):
        if tok not in vpn_pws:
            vpn_pws.append(tok)

    parts = []
    if ids:
        parts.append("[ID]\n" + "\n".join(ids))
    if pws:
        parts.append("[Password]\n" + "\n".join(pws))
    if vpn_ids:
        parts.append("[VPN ID]\n" + "\n".join(vpn_ids))
    if vpn_pws:
        parts.append("[VPN Password]\n" + "\n".join(vpn_pws))
    note = "\n\n".join(parts)
    if len(note) > 3900:
        note = note[:3900] + "\n…"
    return note


def build_comment(cells):
    chunks = []
    for key in ("L", "M"):
        body = norm_cell(cells.get(key))
        if body:
            chunks.append(body)
    if not chunks:
        return None
    comment = "\n".join(chunks)
    if len(comment) > 1900:
        comment = comment[:1900] + "\n…"
    return comment


def sql_escape(s):
    return (s or "").replace("'", "''")


current_site_raw = ""
# (site, pc_upper) -> dict
rows = OrderedDict()

for r in sheet.findall("m:sheetData/m:row", ns):
    cells = {}
    for cell in r.findall("m:c", ns):
        cells[col_letters(cell.get("r", ""))] = cell_val(cell)

    a = cells.get("A", "")
    b = cells.get("B", "")
    c = cells.get("C", "")
    if a and a.strip():
        head = a.strip().split("\n")[0].strip()
        if head and len(head) < 120 and not head.lower().startswith("http"):
            if map_site(head) or any(
                k in head.upper() for k in ("CMC", "AURORA", "MNGHA", "RC", "RCYMC", "JEJU", "NGHA")
            ):
                current_site_raw = head

    if not b:
        continue
    if b.strip() in ("PC",):
        continue

    site = map_site(current_site_raw)
    if not site:
        continue

    pcs, ip = extract_pcs_and_ip(b)
    # credentials from ID + AD only (Password 셀의 Hk@… 는 계정 아님)
    cred_src = "\n".join(
        [
            norm_cell(cells.get("C")),
            norm_cell(cells.get("H")),
        ]
    )
    creds = extract_creds(cred_src)
    # Prefer ID cell lines that already contain creds — also keep whole ID lines that have creds
    id_cell = norm_cell(c)
    domain_lines = []
    if id_cell:
        for line in id_cell.split("\n"):
            line = line.strip()
            if not line:
                continue
            if extract_creds(line):
                domain_lines.append(line)
    # compact domain field for cards: unique creds joined
    if creds:
        pc_domain = "\n".join(creds)
    elif domain_lines:
        pc_domain = "\n".join(domain_lines)
    else:
        pc_domain = None

    note = build_note(cells)
    comment = build_comment(cells)
    if not pc_domain and not note and not comment:
        continue
    if not pcs and not ip:
        continue

    targets = list(pcs) if pcs else []
    # if only IP, try match later via seed; still stash by ip key
    payload = {
        "site": site,
        "domain": pc_domain,
        "note": note if note else None,
        "comment": comment,
        "ip": ip,
    }

    if targets:
        for pc in targets:
            key = (site, pc.upper())
            prev = rows.get(key)
            # keep richer note / more creds
            if prev is None:
                rows[key] = dict(payload, pc=pc)
            else:
                if payload["domain"] and (
                    not prev.get("domain") or len(payload["domain"]) > len(prev.get("domain") or "")
                ):
                    prev["domain"] = payload["domain"]
                if payload["note"] and (
                    not prev.get("note") or len(payload["note"]) > len(prev.get("note") or "")
                ):
                    prev["note"] = payload["note"]
                if payload["comment"] and (
                    not prev.get("comment") or len(payload["comment"]) > len(prev.get("comment") or "")
                ):
                    prev["comment"] = payload["comment"]
    elif ip:
        key = ("IP", site, ip)
        rows[key] = dict(payload, pc=None)

print("row keys", len(rows))
# resolve IP-only via 18 seed
seed = open("18_VPN_XLSX_SEED.sql", encoding="utf-8").read()
m = re.search(r"MERGE INTO XSUP\.MSDWHTKD_PCMAP[\s\S]*?USING \(([\s\S]*?)\)\s*s", seed)
block = m.group(1) if m else ""
ip_to_pc = {}
seed_pcs = set()
for line in block.splitlines():
    qs = re.findall(r"'([^']*)'", line)
    if len(qs) >= 3 and "DUAL" in line.upper() and qs[0] in ("CMC", "RC", "AURORA", "MNGHA"):
        ip_to_pc[(qs[0].upper(), qs[2].strip())] = qs[1]
        seed_pcs.add((qs[0].upper(), qs[1].upper()))

resolved = OrderedDict()
for key, val in rows.items():
    if key[0] == "IP":
        _, site, ip = key
        pc = ip_to_pc.get((site, ip))
        if not pc:
            continue
        nkey = (site, pc.upper())
        if nkey not in resolved:
            resolved[nkey] = dict(val, pc=pc)
        continue
    resolved[key] = val

print("resolved", len(resolved))
counts = defaultdict(int)
for (site, _), _v in resolved.items():
    counts[site] += 1
print(dict(counts))

# sample DTT-03 / HIS-11
for (site, pku), v in resolved.items():
    if "DTT-03" in pku:
        print("DTT-03 domain:", repr(v.get("domain")))
        print("DTT-03 note:", repr(v.get("note")))
        print("DTT-03 comment:", repr(v.get("comment")))
    if "HIS-11" in pku:
        print("HIS-11 note:", repr(v.get("note")))
        print("HIS-11 comment:", repr((v.get("comment") or "")[:120]))

header = """-- ============================================================
-- MSDWHTKD_PCMAP.PC_DOMAIN / PC_NOTE / PC_COMMENT — vpn.xlsx
-- PC_DOMAIN : domain\\user, user@host (카드 표시)
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
"""

parts = [header]
for site in ("CMC", "RC", "AURORA", "MNGHA"):
    items = [(v["pc"], v) for (s, _), v in resolved.items() if s == site and v.get("pc")]
    items.sort(key=lambda x: x[0].upper())
    if not items:
        continue
    parts.append("")
    parts.append("-- {0}: {1} rows".format(site, len(items)))
    parts.append("MERGE INTO XSUP.MSDWHTKD_PCMAP t")
    parts.append("USING (")
    lines = []
    for pc, v in items:
        def qstr(s):
            if s is None:
                return "NULL"
            if "{" not in s and "}" not in s:
                return "q'{" + s + "}'"
            if "[" not in s and "]" not in s:
                return "q'[" + s + "]'"
            return "'" + s.replace("'", "''") + "'"

        lines.append(
            "    SELECT '{0}' AS SITE_CD, '{1}' AS PC_NM, {2} AS PC_DOMAIN, {3} AS PC_NOTE, {4} AS PC_COMMENT FROM DUAL".format(
                site,
                sql_escape(pc),
                qstr(v.get("domain")),
                qstr(v.get("note")),
                qstr(v.get("comment")),
            )
        )
    parts.append("\n    UNION ALL\n".join(lines))
    parts.append(") s")
    parts.append("ON (UPPER(TRIM(t.SITE_CD)) = UPPER(TRIM(s.SITE_CD))")
    parts.append("AND UPPER(TRIM(t.PC_NM)) = UPPER(TRIM(s.PC_NM)))")
    parts.append("WHEN MATCHED THEN UPDATE SET")
    parts.append("    t.PC_DOMAIN = s.PC_DOMAIN,")
    parts.append("    t.PC_NOTE = s.PC_NOTE,")
    parts.append("    t.PC_COMMENT = NVL(t.PC_COMMENT, s.PC_COMMENT),")
    parts.append("    t.UPDT_DTM = SYSTIMESTAMP")
    parts.append("WHEN NOT MATCHED THEN INSERT (MAP_ID, SITE_CD, PC_NM, PC_DOMAIN, PC_NOTE, PC_COMMENT, UPDT_DTM)")
    parts.append("VALUES (XSUP.SEQ_MSDWHTKD_PCMAP.NEXTVAL, s.SITE_CD, s.PC_NM, s.PC_DOMAIN, s.PC_NOTE, s.PC_COMMENT, SYSTIMESTAMP);")
    parts.append("")

parts.append("COMMIT;")
parts.append("")
parts.append("-- SELECT SITE_CD, PC_NM, PC_DOMAIN, SUBSTR(PC_NOTE,1,80), SUBSTR(PC_COMMENT,1,40)")
parts.append("--   FROM XSUP.MSDWHTKD_PCMAP")
parts.append("--  WHERE PC_DOMAIN IS NOT NULL OR PC_NOTE IS NOT NULL OR PC_COMMENT IS NOT NULL")
parts.append("--  ORDER BY SITE_CD, PC_NM;")

with open("19_ALTER_PCMAP_PC_DOMAIN.sql", "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(parts))
print("wrote 19_ALTER_PCMAP_PC_DOMAIN.sql bytes", sum(len(p) for p in parts))
