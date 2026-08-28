# -*- coding: utf-8 -*-
import pathlib
import urllib.request
import urllib.error

uml = pathlib.Path(r"c:\Users\ezcare\OneDrive - ezcaretech\바탕 화면\GBCWorkHub\docs\uml")
png = uml / "png"
png.mkdir(exist_ok=True)
ok = fail = 0
for f in sorted(uml.glob("*.puml")):
    body = f.read_text(encoding="utf-8")
    body2 = "\n".join(
        line for line in body.splitlines() if not line.strip().startswith("!theme")
    )
    req = urllib.request.Request(
        "https://kroki.io/plantuml/png",
        data=body2.encode("utf-8"),
        headers={"Content-Type": "text/plain; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=90) as resp:
            data = resp.read()
        out = png / (f.stem + ".png")
        out.write_bytes(data)
        print("OK", f.name, len(data))
        ok += 1
    except urllib.error.HTTPError as e:
        err = e.read().decode("utf-8", errors="replace")[:400]
        print("FAIL", f.name, e.code, err)
        fail += 1
    except Exception as e:
        print("FAIL", f.name, type(e).__name__, e)
        fail += 1
print(f"done ok={ok} fail={fail}")
