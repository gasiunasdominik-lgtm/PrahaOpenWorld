"""Doplní do existující Assets/Resources/PrahaMap.json dvory budov ("holes") bez stahování celé mapy.

Budovy jsou z OSM, dvory jsou vnitřní kruhy (role inner) relací building. Stáhne jen relace budov
ve výřezu, vnější kruh spáruje s budovou v JSON podle shodných souřadnic a připíše jí holes.
Použití: python3 Tools/add_courtyards.py
"""
import json, os, sys, urllib.parse
sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import BBOX, OUT, get, flat, courtyards

s, w, n, e = BBOX[1], BBOX[0], BBOX[3], BBOX[2]
q = f'[out:json][timeout:240];relation["building"]({s},{w},{n},{e});out geom;'
osm = get("https://overpass-api.de/api/interpreter", urllib.parse.urlencode({"data": q}).encode())["elements"]
print(f"OSM relací budov: {len(osm)}")

with open(OUT, encoding="utf-8") as fh: data = json.load(fh)
by_ring = {tuple(b["p"]): b for b in data["buildings"]}
patched = 0
for el in osm:
    for m in el.get("members", []):
        if m.get("role") != "outer" or "geometry" not in m: continue
        g = m["geometry"]
        if (g[0]["lon"], g[0]["lat"]) != (g[-1]["lon"], g[-1]["lat"]): continue
        ring = flat((p["lon"], p["lat"]) for p in g)[:-2]
        b = by_ring.get(tuple(ring))
        if b is None: continue
        holes = courtyards(el, ring)
        if holes: b["holes"] = holes; patched += 1
        else: b.pop("holes", None)
with open(OUT, "w", encoding="utf-8") as fh: json.dump(data, fh, ensure_ascii=False, separators=(",", ":"))
print(f"Budov s dvory: {patched} ({sum(len(b.get('holes', [])) for b in data['buildings'])} dvorů)")
