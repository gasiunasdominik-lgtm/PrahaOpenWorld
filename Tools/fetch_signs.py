"""Traffic signs and crossings of central Prague (OSM) -> Assets/Resources/PrahaSigns.json:
[{"k": kind, "p": [x, z], "v": value}] with kinds: crossing (zebra + IP6 signs), give_way (P4), stop (P6), signals,
speed (B20a, v = km/h), noentry (B2), mainroad (P2), noparking (B28), parking (IP11a), oneway (IP4b at a one-way street's
start, v = heading in degrees). © OpenStreetMap contributors (ODbL). Usage: python3 Tools/fetch_signs.py
"""
import json, math, os, re, sys, urllib.parse

sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import BBOX, get, proj

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "PrahaSigns.json")
CODES = [(r"B20a\[(\d+)\]", "speed"), (r"\bB2\b", "noentry"), (r"\bP2\b", "mainroad"), (r"\bP4\b", "give_way"),
         (r"\bP6\b", "stop"), (r"\bB28\b", "noparking"), (r"\bIP11", "parking"), (r"\bIP6\b", "crossing")]
CAR = "motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service"


def main():
    s, w, n, e = BBOX[1], BBOX[0], BBOX[3], BBOX[2]
    q = (f'[out:json][timeout:120];(node["highway"~"crossing|give_way|stop|traffic_signals"]({s},{w},{n},{e});'
         f'node["traffic_sign"]({s},{w},{n},{e});way["oneway"="yes"]["highway"~"{CAR}"]({s},{w},{n},{e}););out geom;')
    els = get("https://overpass-api.de/api/interpreter", urllib.parse.urlencode({"data": q}).encode())["elements"]
    out = []
    for el in els:
        t = el.get("tags", {})
        if el["type"] == "way":
            g = el["geometry"]
            if len(g) < 2: continue
            a, b = proj(g[0]["lon"], g[0]["lat"]), proj(g[1]["lon"], g[1]["lat"])
            out.append({"k": "oneway", "p": list(a), "v": round(math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])), 1)})
            continue
        p = list(proj(el["lon"], el["lat"])); h = t.get("highway", "")
        if h == "crossing" and t.get("crossing") in ("marked", "zebra", "uncontrolled", "traffic_signals"):
            out.append({"k": "crossing", "p": p, "v": 1 if t.get("crossing") == "traffic_signals" else 0})
        elif h in ("give_way", "stop"): out.append({"k": h, "p": p, "v": 0})
        ts = t.get("traffic_sign", "")
        for pat, kind in CODES:
            m = re.search(pat, ts)
            if m: out.append({"k": kind, "p": p, "v": int(m.group(1)) if m.groups() else 0})
    json.dump(out, open(OUT, "w", encoding="utf-8"))
    from collections import Counter
    print("značek:", len(out), Counter(o["k"] for o in out))


if __name__ == "__main__":
    main()
