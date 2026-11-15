"""Stáhne data skutečné Prahy a uloží je jako Assets/Resources/PrahaMap.json pro MapBuilder.

Zdroje: Metropolitní plán Prahy a vrstevnice IPR (CC BY 4.0, "data base © IPR Praha"),
OpenStreetMap (ODbL, "© přispěvatelé OpenStreetMap"). Jen standardní knihovna Pythonu.
Použití: python3 Tools/fetch_map.py   (výřez měníš v BBOX)
"""
import base64, collections, json, math, os, struct, sys, time, urllib.error, urllib.parse, urllib.request

BBOX = (14.388, 50.073, 14.456, 50.095)          # lon_min, lat_min, lon_max, lat_max (Hradčany–Žižkov)
ORIGIN = (50.08647, 14.41138)                      # střed Karlova mostu = (0, 0, 0)
RIVER = 186.0                                      # hladina Vltavy v centru (Staroměstský jez), m n. m.
METRO_DEPTH = 25.0                                 # shortcut: skutečná hloubka tunelů v datech není, bereme typických 25 m pod terénem
CELL = 8.0                                         # krok výškové mapy v metrech
UA = {"User-Agent": "PrahaOpenWorld-game/0.1 (offline map build)"}
MPP = "https://mp.iprpraha.cz/arcgis/rest/services/Hosted/{}/FeatureServer/0/query"
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "PrahaMap.json")
STREET_WIDTH = {1: 22.0, 2: 15.0, 3: 11.0, 4: 7.5}  # hierarchie MPP: metropolitní..místní

KX = math.cos(math.radians(ORIGIN[0])) * 111320.0
KZ = 110540.0
def proj(lon, lat): return (round((lon - ORIGIN[1]) * KX, 1), round((lat - ORIGIN[0]) * KZ, 1))

def get(url, data=None):
    for attempt in range(3):  # Overpass občas vrátí 502/504, když je přetížený
        try:
            req = urllib.request.Request(url, data=data, headers=UA)
            with urllib.request.urlopen(req, timeout=300) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            if e.code < 500 or attempt == 2: raise
            print(f"  server {e.code}, zkouším znovu…"); time.sleep(20)

def mpp(layer, fields="*", generalize=0.0):
    """Všechny prvky vrstvy MPP ve výřezu, stránkovaně (server vrací max ~2000 na dotaz)."""
    feats, offset = [], 0
    while True:
        q = {"where": "1=1", "geometry": ",".join(map(str, BBOX)), "geometryType": "esriGeometryEnvelope",
             "inSR": 4326, "outSR": 4326, "spatialRel": "esriSpatialRelIntersects", "outFields": fields,
             "returnGeometry": "true", "resultOffset": offset, "resultRecordCount": 2000, "f": "json"}
        if generalize: q["maxAllowableOffset"] = generalize
        d = get(MPP.format(layer) + "?" + urllib.parse.urlencode(q))
        if "error" in d: sys.exit(f"{layer}: {d['error']}")
        feats += d.get("features", [])
        offset += len(d.get("features", []))
        if not d.get("exceededTransferLimit"): break
    print(f"  {layer}: {len(feats)}")
    return feats

def flat(coords): return [v for lon, lat in coords for v in proj(lon, lat)]

def polys(feats):  # ArcGIS rings -> [[x,z,...], ...]; vnitřní kruhy (dvory) ignorujeme
    return [(f["attributes"], flat(r)) for f in feats for r in f["geometry"].get("rings", [])[:1]]

def inside(px, pz, ring):
    c, n = False, len(ring) // 2
    for i in range(n):
        x1, z1, x2, z2 = ring[2*i], ring[2*i+1], ring[2*((i+1) % n)], ring[2*((i+1) % n)+1]
        if (z1 > pz) != (z2 > pz) and px < (x2 - x1) * (pz - z1) / (z2 - z1) + x1: c = not c
    return c

def main():
    print("Metropolitní plán…")
    streets = mpp("MPP_CUR_MPP_200_ULICECESTY_L", "typ,hierarchie", 0.00002)
    squares = polys(mpp("MPP_CUR_MPP_200_NAMESTI_P", "nazev", 0.00002))
    parks = polys(mpp("MPP_CUR_MPP_100_PARKY_P", "nazev", 0.00002))
    districts = polys(mpp("MPP_CUR_MPP_300_LOKALITY_P", "nazev", 0.00005))
    heightreg = polys(mpp("MPP_CUR_MPP_200_VYSKOVAREG_P", "rnp_hladina", 0.00005))
    print("Vrstevnice IPR…")
    contours = mpp("MAP_CUR_MAP_VRSTEVNICE_2M_L", "*", 0.00004)

    print("OpenStreetMap…")
    s, w, n, e = BBOX[1], BBOX[0], BBOX[3], BBOX[2]
    q = f"""[out:json][timeout:240];(way["building"]({s},{w},{n},{e});relation["building"]({s},{w},{n},{e});
way["natural"="water"]({s},{w},{n},{e});relation["natural"="water"]({s},{w},{n},{e});
way["railway"="tram"]({s},{w},{n},{e});
relation["route"="subway"]({s},{w},{n},{e});node["railway"="subway_entrance"]({s},{w},{n},{e});
node["station"="subway"]({s},{w},{n},{e});
node["amenity"~"^(pub|bar|restaurant|cafe|fast_food|biergarten)$"]({s},{w},{n},{e});
node["shop"]({s},{w},{n},{e});node["office"]({s},{w},{n},{e}););out geom;"""
    osm = get("https://overpass-api.de/api/interpreter", urllib.parse.urlencode({"data": q}).encode())["elements"]
    print(f"  OSM prvků: {len(osm)}")

    # --- výšková mapa z vrstevnic: rasterizace, BFS na nejbližší vrstevnici, vyhlazení
    x0, z0 = proj(BBOX[0], BBOX[1]); x1, z1 = proj(BBOX[2], BBOX[3])
    W, H = int((x1 - x0) / CELL) + 1, int((z1 - z0) / CELL) + 1
    hm = [None] * (W * H)
    zfield = None
    for f in contours:
        a = f["attributes"]
        if zfield is None:
            zfield = next(k for k in a if k.lower() in ("vyska", "h", "z", "elevation", "nadm_vyska", "vyska_m", "contour"))
        v = float(a[zfield])
        for path in f["geometry"].get("paths", []):
            pts = [proj(lon, lat) for lon, lat in path]
            for (ax, az), (bx, bz) in zip(pts, pts[1:]):
                steps = int(max(abs(bx - ax), abs(bz - az)) / (CELL / 2)) + 1
                for k in range(steps + 1):
                    t = k / steps
                    i, j = int((ax + (bx - ax) * t - x0) / CELL), int((az + (bz - az) * t - z0) / CELL)
                    if 0 <= i < W and 0 <= j < H: hm[j * W + i] = v
    river = RIVER
    dq = collections.deque(i for i, v in enumerate(hm) if v is not None)
    while dq:
        c = dq.popleft(); i, j = c % W, c // W
        for ni, nj in ((i+1, j), (i-1, j), (i, j+1), (i, j-1)):
            if 0 <= ni < W and 0 <= nj < H and hm[nj * W + ni] is None:
                hm[nj * W + ni] = hm[c]; dq.append(nj * W + ni)
    for _ in range(4):  # box blur smaže 2m schody mezi vrstevnicemi
        hm = [sum(hm[min(H-1, max(0, j+dj)) * W + min(W-1, max(0, i+di))] for di in (-1, 0, 1) for dj in (-1, 0, 1)) / 9
              for j in range(H) for i in range(W)]
    hm = [v - river for v in hm]  # 0 = hladina Vltavy

    # --- povrchy (0 terén, 1 voda, 2 park, 3 náměstí) scanline vyplněním sudo-lichým pravidlem,
    #     takže ostrovy a dvory vyjdou samy a polygony se nemusí skládat
    lu = bytearray(W * H)
    def fill(segs, val):
        for j in range(H):
            pz = z0 + (j + 0.5) * CELL
            xs = sorted(ax + (bx - ax) * (pz - az) / (bz - az) for (ax, az), (bx, bz) in segs if (az > pz) != (bz > pz))
            for a, b in zip(xs[0::2], xs[1::2]):
                for i in range(max(0, int((a - x0) / CELL)), min(W, int((b - x0) / CELL) + 1)):
                    lu[j * W + i] = val
                    if val == 1: hm[j * W + i] = min(hm[j * W + i], -3.0)  # koryto pod hladinu
    def ring_segs(r): pts = list(zip(r[0::2], r[1::2])); return list(zip(pts, pts[1:] + pts[:1]))
    fill([sg for _, r in parks for sg in ring_segs(r)], 2)
    fill([sg for _, r in squares for sg in ring_segs(r)], 3)
    water = []
    for el in osm:
        if el.get("tags", {}).get("natural") != "water": continue
        ways = [el["geometry"]] if el["type"] == "way" else [m["geometry"] for m in el.get("members", []) if "geometry" in m]
        for g in ways:
            pts = [proj(p["lon"], p["lat"]) for p in g]
            water += list(zip(pts, pts[1:]))
    fill(water, 1)
    heights = base64.b64encode(struct.pack(f"<{W*H}h", *(max(-32000, min(32000, round(v * 10))) for v in hm))).decode()

    # --- budovy: výška z OSM, jinak z výškové regulace MPP (hladina ~ podlaží), jinak 12 m
    def osm_h(t):
        try: return float(t["height"].split()[0])
        except (KeyError, ValueError): pass
        try: return float(t["building:levels"]) * 3.2 + float(t.get("roof:levels", 0)) * 2.5
        except (KeyError, ValueError): return None
    buildings = []
    for el in osm:
        t = el.get("tags", {})
        if "building" not in t: continue
        rings = [el["geometry"]] if el["type"] == "way" else [m["geometry"] for m in el.get("members", []) if m.get("role") == "outer" and "geometry" in m]
        for g in rings:
            if (g[0]["lon"], g[0]["lat"]) != (g[-1]["lon"], g[-1]["lat"]): continue  # shortcut: kus obvodu relace, skládat neumíme
            ring = flat((p["lon"], p["lat"]) for p in g)[:-2]  # bez opakovaného posledního bodu
            if len(ring) < 6: continue
            h = osm_h(t)
            if h is None:
                cx, cz = sum(ring[0::2]) / (len(ring) // 2), sum(ring[1::2]) / (len(ring) // 2)
                lvl = next((a["rnp_hladina"] for a, r in heightreg if a.get("rnp_hladina") and inside(cx, cz, r)), None)
                h = lvl * 3.3 if lvl else 12.0
            buildings.append({"h": round(h, 1), "p": ring})

    data = {
        "credits": "data base © IPR Praha (CC BY 4.0) · © přispěvatelé OpenStreetMap (ODbL)",
        "origin": ORIGIN, "bbox": BBOX, "riverLevel": river,
        "terrain": {"x0": x0, "z0": z0, "cell": CELL, "w": W, "h": H, "dm": heights, "lu": base64.b64encode(bytes(lu)).decode()},
        "streets": [{"w": STREET_WIDTH.get(f["attributes"].get("hierarchie"), 6.0), "foot": f["attributes"].get("typ") in (3, 4, 5),
                     "p": flat(path)} for f in streets for path in f["geometry"].get("paths", [])],
        "squares": [{"n": a.get("nazev") or "", "p": r} for a, r in squares],
        "parks": [{"n": a.get("nazev") or "", "p": r} for a, r in parks],
        "districts": [{"n": a.get("nazev") or "", "p": r} for a, r in districts],
        "trams": [{"p": flat((p["lon"], p["lat"]) for p in el["geometry"])} for el in osm
                  if el["type"] == "way" and el.get("tags", {}).get("railway") == "tram"],
        "metro": {
            "depth": METRO_DEPTH,
            "lines": [{"ref": el["tags"].get("ref", ""), "color": el["tags"].get("colour", ""),
                       "p": flat((p["lon"], p["lat"]) for p in m["geometry"])}
                      for el in osm if el["type"] == "relation" and el.get("tags", {}).get("route") == "subway"
                      for m in el.get("members", []) if m["type"] == "way" and "geometry" in m],
            "stations": [{"n": el["tags"].get("name", ""), "p": list(proj(el["lon"], el["lat"]))} for el in osm
                         if el["type"] == "node" and el.get("tags", {}).get("station") == "subway"],
            "entrances": [{"n": el["tags"].get("name", ""), "p": list(proj(el["lon"], el["lat"]))} for el in osm
                          if el["type"] == "node" and el.get("tags", {}).get("railway") == "subway_entrance"],
        },
        "buildings": buildings,
        # Místa: atom s funkcí (druh) a jménem; shortcut: jen body, podniky zakreslené jako plochy chybí
        "places": [{"n": el["tags"].get("name", ""), "k": el["tags"].get("amenity") or ("shop" if "shop" in el["tags"] else "office"),
                    "p": list(proj(el["lon"], el["lat"]))} for el in osm if el["type"] == "node"
                   and ("shop" in el.get("tags", {}) or "office" in el.get("tags", {})
                        or el.get("tags", {}).get("amenity") in ("pub", "bar", "restaurant", "cafe", "fast_food", "biergarten"))],
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh: json.dump(data, fh, ensure_ascii=False, separators=(",", ":"))
    print(f"Hotovo: {OUT} ({os.path.getsize(OUT)/1e6:.1f} MB), mapa {W}x{H} buněk, budov {len(buildings)}, metro úseků {len(data['metro']['lines'])} / stanic {len(data['metro']['stations'])} / vstupů {len(data['metro']['entrances'])}, ulic {len(data['streets'])}, míst {len(data['places'])}, hladina řeky {river} m n. m.")

if __name__ == "__main__":
    main()
