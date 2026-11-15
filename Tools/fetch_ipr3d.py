"""3D model budov IPR Praha (2020, detailní střechy) -> Assets/Resources/IprBuildings.bytes.

Zdroj: veřejná scénová služba I3S + atributy z Feature Service, licence CC BY 4.0 („data base © IPR Praha“).
Každá budova: počátek (x, z v herních metrech, spodní výška), trojúhelníky jako int16 po 5 cm od počátku,
orientace podle normál z dat. Formát čte MapBuilder.BuildIpr.
"""
import gzip, json, os, struct, sys, urllib.parse, urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import BBOX, proj, ORIGIN, KX, KZ  # stejná projekce a výřez jako zbytek mapy
import math


def projf(lon, lat):  # jako proj, ale bez zaokrouhlení na 10 cm (zubaté střechy)
    return (lon - ORIGIN[1]) * KX, (lat - ORIGIN[0]) * KZ

HOST = "https://services5.arcgis.com/SBTXIEUGWbqzUecw/arcgis/rest/services/Budovy3D_2020"
SCENE, FEAT = HOST + "/SceneServer/layers/0", HOST + "/FeatureServer/0/query"
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "IprBuildings.bytes")
Q = 0.05  # m na jednotku int16


def get(url):
    for attempt in range(4):
        try:
            b = urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "PrahaOpenWorld-game/0.1"}), timeout=180).read()
            return gzip.decompress(b) if b[:2] == b"\x1f\x8b" else b
        except Exception as e:
            if attempt == 3: raise
            print("  znovu:", e)


def leaves():
    nodes, page = [], 0
    while True:
        d = json.loads(get(f"{SCENE}/nodepages/{page}?f=json"))
        nodes += d.get("nodes", [])
        if len(d.get("nodes", [])) < 64: break
        page += 1
    m = 0.004  # uzel přesahuje výřez: budovy filtrujeme až podle těžiště
    return [n for n in nodes if not n.get("children") and "mesh" in n
            and BBOX[0] - m < n["obb"]["center"][0] < BBOX[2] + m and BBOX[1] - m < n["obb"]["center"][1] < BBOX[3] + m]


def node_buildings(n):
    """[(id, [(ax,ay,az,bx,...)...lon/lat/z trojúhelníky])] z nekomprimovaného bufferu uzlu."""
    b = get(f"{SCENE}/nodes/{n['mesh']['geometry']['resource']}/geometries/0")
    vc, fc = struct.unpack_from("<II", b, 0); o = 8
    pos = struct.unpack_from(f"<{vc * 3}f", b, o); o += vc * 12
    nrm = struct.unpack_from(f"<{vc * 3}f", b, o); o += vc * 12 + vc * 8 + vc * 4  # přeskočit uv0 a color
    fid = struct.unpack_from(f"<{fc}Q", b, o); o += fc * 8
    fr = struct.unpack_from(f"<{fc * 2}I", b, o)
    cx, cy, cz = n["obb"]["center"]
    # normály jsou v geocentrických souřadnicích (ECEF), ne v místních: převod do (východ, sever, nahoru)
    lo, la = math.radians(cx), math.radians(cy)
    R = ((-math.sin(lo), math.cos(lo), 0.0),
         (-math.sin(la) * math.cos(lo), -math.sin(la) * math.sin(lo), math.cos(la)),
         (math.cos(la) * math.cos(lo), math.cos(la) * math.sin(lo), math.sin(la)))
    out = []
    for k in range(fc):
        tris = []
        for f in range(fr[2 * k], fr[2 * k + 1] + 1):  # faceRange je včetně konce
            v = [pos[3 * (3 * f + i): 3 * (3 * f + i) + 3] for i in range(3)]
            p = [(projf(cx + a[0], cy + a[1]), cz + a[2]) for a in v]
            P = [(xz[0], h, xz[1]) for xz, h in p]  # x, y(výška), z
            # orientace: geometrická normála musí souhlasit s normálou z dat (po převodu z ECEF)
            ux, uy, uz = (P[1][i] - P[0][i] for i in range(3)); wx, wy, wz = (P[2][i] - P[0][i] for i in range(3))
            gx, gy, gz = uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx
            # data jsou v (lon, lat, z) = (východ, sever, nahoru); ve hře (x, y, z) = (východ, nahoru, sever)
            e = [sum(nrm[3 * (3 * f + i) + j] for i in range(3)) for j in range(3)]
            dn = [sum(R[r][j] * e[j] for j in range(3)) for r in range(3)]
            d = gx * dn[0] + gy * dn[2] + gz * dn[1]
            # 98,6 % trojúhelníků má v datech opačné pořadí vrcholů; nejasné případy (normála skoro v rovině) otočit taky
            if d < 0.5 * math.sqrt(gx * gx + gy * gy + gz * gz) * math.sqrt(sum(x * x for x in dn)): P[1], P[2] = P[2], P[1]
            tris.append(P)
        out.append((fid[k], tris))
    return out


def attributes():
    """OBJECTID -> (funkce, podlaží, veřejný přístup) pro budovy ve výřezu."""
    res, off = {}, 0
    while True:
        q = {"where": "1=1", "geometry": ",".join(map(str, BBOX)), "geometryType": "esriGeometryEnvelope", "inSR": 4326,
             "spatialRel": "esriSpatialRelIntersects", "outFields": "OBJECTID,KOD_label,POCET_PODLAZI,VEREJ_PRISTUP",
             "returnGeometry": "false", "resultOffset": off, "resultRecordCount": 2000, "f": "json"}
        d = json.loads(get(FEAT + "?" + urllib.parse.urlencode(q)))
        for f in d.get("features", []):
            a = f["attributes"]; res[a["OBJECTID"]] = (a["KOD_label"] or "", a["POCET_PODLAZI"] or 0, a["VEREJ_PRISTUP"] or 0)
        off += len(d.get("features", []))
        if not d.get("exceededTransferLimit"): return res


def main(out=OUT, with_attrs=True):
    ls = leaves(); print("uzlů:", len(ls))
    attrs = attributes() if with_attrs else {}; print("atributů:", len(attrs))
    x0, z0 = proj(BBOX[0], BBOX[1]); x1, z1 = proj(BBOX[2], BBOX[3])
    labels, blds, seen = [], [], set()
    with ThreadPoolExecutor(8) as ex:
        for i, nb in enumerate(ex.map(node_buildings, ls)):
            for oid, tris in nb:
                if oid in seen or not tris: continue
                pts = [p for t in tris for p in t]
                mx = sum(p[0] for p in pts) / len(pts); mz = sum(p[2] for p in pts) / len(pts)
                if not (x0 <= mx <= x1 and z0 <= mz <= z1): continue
                seen.add(oid)
                lab, floors, acc = attrs.get(oid, ("" if with_attrs else "most", 0, 0))
                if lab not in labels: labels.append(lab)
                blds.append((oid, labels.index(lab), floors, acc, mx, mz, min(p[1] for p in pts), tris))
            if i % 20 == 0: print(f"  {i}/{len(ls)} uzlů, {len(blds)} budov")
    with open(out, "wb") as f:
        f.write(b"IPR1"); f.write(struct.pack("<i", len(labels)))
        for s in labels:
            e = s.encode("utf-8"); f.write(struct.pack("<B", len(e))); f.write(e)
        f.write(struct.pack("<i", len(blds)))
        for oid, li, fl, acc, ox, oz, base, tris in blds:
            f.write(struct.pack("<iBBBfffi", oid, li, min(fl, 255), min(acc, 255), ox, oz, base, len(tris)))
            q = []
            for t in tris:
                for x, y, z in t:
                    q += [max(-32767, min(32767, round((x - ox) / Q))), max(-32767, min(32767, round((y - base) / Q))),
                          max(-32767, min(32767, round((z - oz) / Q)))]
            f.write(struct.pack(f"<{len(q)}h", *q))
    print("budov:", len(blds), "trojúhelníků:", sum(len(b[7]) for b in blds), "MB:", round(os.path.getsize(out) / 1e6, 1),
          "bez atributů:", sum(1 for b in blds if b[0] not in attrs))
    print("funkce:", labels)


TREES = "https://services5.arcgis.com/SBTXIEUGWbqzUecw/arcgis/rest/services/stromy_b_DTM_PRO_3D_2d_wgs/FeatureServer/1/query"
TREES_OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "IprTrees.bytes")


def trees():
    """Jednotlivé stromy (DTM Praha, výška z laserového skenování) -> IprTrees.bytes: x, z, výška v cm."""
    pts, off = [], 0
    while True:
        q = {"where": "1=1", "geometry": ",".join(map(str, BBOX)), "geometryType": "esriGeometryEnvelope", "inSR": 4326,
             "spatialRel": "esriSpatialRelIntersects", "outFields": "vyska_rel", "outSR": 4326, "returnGeometry": "true",
             "resultOffset": off, "resultRecordCount": 2000, "f": "json"}
        d = json.loads(get(TREES + "?" + urllib.parse.urlencode(q)))
        fs = d.get("features", [])
        for f in fs:
            h = f["attributes"]["vyska_rel"] or 0
            if h < 1.5: continue  # keře a šum
            x, z = proj(f["geometry"]["x"], f["geometry"]["y"]); pts.append((x, z, min(int(h * 100), 65535)))
        off += len(fs)
        if not d.get("exceededTransferLimit") or not fs: break
    with open(TREES_OUT, "wb") as f:
        f.write(b"TRE1"); f.write(struct.pack("<i", len(pts)))
        for x, z, h in pts: f.write(struct.pack("<ffH", x, z, h))
    print("stromů:", len(pts), "průměrná výška m:", round(sum(p[2] for p in pts) / max(1, len(pts)) / 100, 1))


if __name__ == "__main__":
    if sys.argv[1:] == ["trees"]: trees()
    elif sys.argv[1:] == ["bridges"]:  # Mosty3D_2020: same I3S layout, no attributes; python3 Tools/fetch_ipr3d.py bridges
        SCENE = HOST.replace("Budovy3D", "Mosty3D") + "/SceneServer/layers/0"
        main(os.path.join(os.path.dirname(OUT), "IprBridges.bytes"), with_attrs=False)
    else: main()
