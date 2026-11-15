"""Public stairways (OSM highway=steps: Zámecké schody, Radniční schody, the Letná and Vyšehrad flights...) ->
Assets/Resources/PrahaSteps.json: [{"n": name, "w": width m, "c": step count or 0, "p": [x, z, ...] game metres}].
© OpenStreetMap contributors (ODbL). Usage: python3 Tools/fetch_steps.py
"""
import json, os, sys, urllib.parse

sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import BBOX, get, proj

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "PrahaSteps.json")


WIDE = {"Zámecké schody": 6.0, "Staré zámecké schody": 4.5, "Radnické schody": 3.5, "Nebozízek - Sady": 3.0}


def num(v, d):
    try: return float(str(v).replace(",", ".").split()[0])
    except Exception: return d


def main():
    s, w, n, e = BBOX[1], BBOX[0], BBOX[3], BBOX[2]
    q = f'[out:json][timeout:120];way["highway"="steps"]({s},{w},{n},{e});out geom;'
    els = get("https://overpass-api.de/api/interpreter", urllib.parse.urlencode({"data": q}).encode())["elements"]
    out = []
    for el in els:
        t = el.get("tags", {})
        if t.get("indoor") == "yes" or t.get("level", "0") not in ("0", "") or t.get("tunnel") == "yes": continue
        if t.get("access") in ("private", "no"): continue
        pts = [v for g in el["geometry"] for v in proj(g["lon"], g["lat"])]
        name = t.get("name", "")
        # widths OSM doesn't give: the big named flights are wider than a back-street stair (Zámecké schody ~6 m)
        default = WIDE.get(name, 4.0 if name else 2.5)
        out.append({"n": name, "w": max(1.2, min(12.0, num(t.get("width"), default))), "c": int(num(t.get("step_count"), 0)), "p": pts})
    json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False)
    print("schodišť:", len(out), "pojmenovaných:", sum(1 for o in out if o["n"]))


if __name__ == "__main__":
    main()
