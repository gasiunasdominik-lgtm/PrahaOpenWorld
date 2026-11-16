"""Mainline railways of central Prague (Hlavní nádraží, Masarykovo nádraží, the Negrelli viaduct, the Vyšehrad - Smíchov line,
Nové spojení under Vítkov) -> Assets/Resources/PrahaRail.json:
{"tracks": [{"p": [x, z, ...], "b": bridge, "t": tunnel, "s": service track}], "stations": [{"n": name, "p": [x, z]}]}.
© OpenStreetMap contributors (ODbL). Usage: python3 Tools/fetch_rail.py
"""
import json, os, sys, urllib.parse

sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import BBOX, get, proj

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "PrahaRail.json")


def main():
    s, w, n, e = BBOX[1] - 0.004, BBOX[0] - 0.006, BBOX[3] + 0.004, BBOX[2] + 0.006  # a margin: trains come in from outside
    q = (f'[out:json][timeout:120];(way["railway"="rail"]({s},{w},{n},{e});'
         f'node["railway"="station"]["train"!="no"]({s},{w},{n},{e});node["railway"="halt"]({s},{w},{n},{e}););out geom;')
    els = get("https://overpass-api.de/api/interpreter", urllib.parse.urlencode({"data": q}).encode())["elements"]
    tracks, stations = [], []
    for el in els:
        t = el.get("tags", {})
        if el["type"] == "node":
            if t.get("subway") == "yes" or t.get("station") in ("subway", "light_rail", "funicular") or t.get("funicular") == "yes": continue
            stations.append({"n": t.get("name", ""), "p": list(proj(el["lon"], el["lat"]))})
            continue
        if t.get("usage") == "industrial" or t.get("railway:preserved") == "yes": continue
        pts = [v for g in el["geometry"] for v in proj(g["lon"], g["lat"])]
        tracks.append({"p": pts, "b": t.get("bridge") in ("yes", "viaduct"), "t": t.get("tunnel") in ("yes", "building_passage"),
                       "s": t.get("service") in ("yard", "siding", "spur")})
    json.dump({"tracks": tracks, "stations": stations}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False)
    print("kolejí:", len(tracks), "mostů:", sum(t["b"] for t in tracks), "tunelů:", sum(t["t"] for t in tracks),
          "vleček/kolejiště:", sum(t["s"] for t in tracks), "stanic:", [x["n"] for x in stations])


if __name__ == "__main__":
    main()
