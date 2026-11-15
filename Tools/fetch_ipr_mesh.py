"""Photoreal 3D mesh of Prague (IPR Praha, 5 cm/px textures) -> Assets/Resources/IprMesh/<tile>.bytes + .jpg

Phase 8, step B: only the landmarks (TARGETS), simplified for integrated GPUs. Licence CC BY 4.0, "data base © IPR Praha".
The sheet ZIPs are stored (not compressed), so single tiles are pulled out by HTTP range instead of 1.3 GB downloads.
Coordinates: S-JTSK (EPSG:5514) -> game metres by an affine fit to IPR building corners served in both systems
(error < 0.1 m inside a sheet). Heights stay in the data's datum; MapBuilder grounds the tile on our terrain.

Format MSH1: ox, oz (game origin), z0 (lowest height), nv, ni, then nv x (int16 x, y, z in cm from origin; uint16 u, v),
then ni x uint32 indices. Vertices are unique (position, uv) pairs: OBJ indexes them separately.

Usage: python3 Tools/fetch_ipr_mesh.py              (every tile touching TARGETS)
"""
import json, os, shutil, struct, subprocess, sys, tempfile, urllib.parse, urllib.request

sys.path.insert(0, os.path.dirname(__file__))
from fetch_map import proj

SHEETS = "https://services5.arcgis.com/SBTXIEUGWbqzUecw/arcgis/rest/services/3D_mesh_klad/FeatureServer/0/query"
BUILDINGS = "https://services5.arcgis.com/SBTXIEUGWbqzUecw/arcgis/rest/services/Budovy3D_2020/FeatureServer/0/query"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "ipr-mesh", "tiles")  # outside Resources: photomesh is off in game (melts at street level)
UA = "PrahaOpenWorld-game/0.1"  # the share link wants its session cookie first (see Zip), not a browser
# landmark: game-space circles (x, z, radius m); every tile touching one is fetched
TARGETS = {
    "Staroměstské náměstí": [(700, 100, 90)],
    "Pražský hrad": [(-774, 484, 110), (-640, 470, 70)],
    "Karlův most": [(x, 7 - x * 0.1, 35) for x in range(-250, 200, 60)],
}
CELL = 1.2  # m: vertices this close merge (0.35 m removed ~1 %: the scan is already ~0.6 m apart); window depth is in the photo anyway
CACHE = os.path.join(os.path.dirname(__file__), "..", "..", "ipr-mesh-cache")  # raw OBJ/JPG outside the project: re-runs skip the download
ORIGIN, KZ = (14.41138, 50.08647), 110540.0  # Tools/fetch_map.proj, inverted for lookups


def get_json(url, params):
    req = urllib.request.Request(url + "?" + urllib.parse.urlencode(params), headers={"User-Agent": "PrahaOpenWorld-game/0.1"})
    return json.loads(urllib.request.urlopen(req, timeout=120).read())


def affine(lon0, lat0, lon1, lat1):
    """game (x, z) = A (X - X0, Y - Y0, 1) from building corners the server gives in both 4326 and 5514."""
    def q(sr):
        p = {"where": "1=1", "geometry": f"{lon0},{lat0},{lon1},{lat1}", "geometryType": "esriGeometryEnvelope", "inSR": 4326,
             "outFields": "OBJECTID", "returnGeometry": "true", "outSR": sr, "resultRecordCount": 80, "orderByFields": "OBJECTID", "f": "json"}
        return {f["attributes"]["OBJECTID"]: f["geometry"]["rings"][0][0] for f in get_json(BUILDINGS, p)["features"]}
    a, b = q(4326), q(5514)
    pairs = [(b[k], proj(*a[k])) for k in a if k in b]
    X0, Y0 = pairs[0][0]
    rows = [(s[0] - X0, s[1] - Y0, 1.0) for s, _ in pairs]

    def solve(rhs):
        M = [[sum(r[i] * r[j] for r in rows) for j in range(3)] for i in range(3)]
        v = [sum(r[i] * t for r, t in zip(rows, rhs)) for i in range(3)]
        for c in range(3):
            p = max(range(c, 3), key=lambda r: abs(M[r][c])); M[c], M[p] = M[p], M[c]; v[c], v[p] = v[p], v[c]
            for r in range(3):
                if r != c:
                    f = M[r][c] / M[c][c]; M[r] = [x - f * y for x, y in zip(M[r], M[c])]; v[r] -= f * v[c]
        return [v[i] / M[i][i] for i in range(3)]
    ax, az = solve([g[0] for _, g in pairs]), solve([g[1] for _, g in pairs])
    err = max(abs(ax[0] * r[0] + ax[1] * r[1] + ax[2] - g[0]) + abs(az[0] * r[0] + az[1] * r[1] + az[2] - g[1]) for r, (_, g) in zip(rows, pairs))
    assert err < 0.5, f"affine fit too loose: {err:.2f} m"
    return lambda X, Y: (ax[0] * (X - X0) + ax[1] * (Y - Y0) + ax[2], az[0] * (X - X0) + az[1] * (Y - Y0) + az[2])


class Zip:
    """Range reads from a stored ZIP behind a SharePoint share link (needs its cookie first)."""
    def __init__(self, url):
        self.url, self.jar = url, tempfile.mktemp()
        subprocess.run(["curl", "-sL", "-A", UA, "-c", self.jar, "-o", os.devnull, url.split("?")[0]], check=True)
        self.entries = self._central()

    def read(self, a, b, out=None):
        cmd = ["curl", "-sfL", "--retry", "5", "--retry-all-errors", "-A", UA, "-b", self.jar, "-c", self.jar, "-r", f"{a}-{b}", self.url] + (["-o", out] if out else [])
        return subprocess.run(cmd, capture_output=not out, check=True).stdout

    def _central(self):
        h = subprocess.run(["curl", "-sIL", "-A", UA, "-b", self.jar, self.url], capture_output=True, text=True, check=True).stdout
        n = int([l for l in h.splitlines() if l.lower().startswith("content-length")][-1].split(":")[1])
        tail = self.read(n - 70000, n - 1)
        i = tail.rfind(b"PK\x05\x06"); cdsize, cdoff = struct.unpack_from("<II", tail, i + 12)
        j = tail.rfind(b"PK\x06\x06")
        if j >= 0: cdsize, cdoff = struct.unpack_from("<QQ", tail, j + 40)
        cd, o, out = self.read(cdoff, cdoff + cdsize - 1), 0, {}
        while cd[o:o + 4] == b"PK\x01\x02":
            csz, = struct.unpack_from("<I", cd, o + 20); nl, el, cl = struct.unpack_from("<HHH", cd, o + 28); lho, = struct.unpack_from("<I", cd, o + 42)
            name = cd[o + 46:o + 46 + nl].decode(); ex = cd[o + 46 + nl:o + 46 + nl + el]; k = 0
            while k + 4 <= len(ex):  # zip64 sizes / offsets
                hid, hs = struct.unpack_from("<HH", ex, k); d = ex[k + 4:k + 4 + hs]; p = 0
                if hid == 1:
                    if struct.unpack_from("<I", cd, o + 24)[0] == 0xFFFFFFFF: p += 8
                    if csz == 0xFFFFFFFF: csz, = struct.unpack_from("<Q", d, p); p += 8
                    if lho == 0xFFFFFFFF: lho, = struct.unpack_from("<Q", d, p)
                k += 4 + hs
            out[name] = (lho, csz); o += 46 + nl + el + cl
        return out

    def extract(self, name, path):
        off, size = self.entries[name]
        if os.path.exists(path) and os.path.getsize(path) == size: return
        h = self.read(off, off + 29); assert h[:4] == b"PK\x03\x04" and h[8:10] == b"\0\0", "entry not stored"
        nl, el = struct.unpack_from("<HH", h, 26); start = off + 30 + nl + el
        self.read(start, start + size - 1, path)


def convert(obj_path, to_game, out_path):
    """Vertex clustering on a CELL grid: positions in one cell merge (to their mean), each corner keeps its own uv,
    triangles that collapse are dropped. Plain and robust; a texture seam may smear by up to CELL."""
    P, T, faces = [], [], []
    with open(obj_path) as f:
        for line in f:
            if line.startswith("v "):
                _, x, y, z = line.split()[:4]; gx, gz = to_game(float(x), float(y)); P.append((gx, float(z), gz))
            elif line.startswith("vt "):
                _, u, v = line.split()[:3]; T.append((float(u), float(v)))
            elif line.startswith("f "):
                faces.append([tuple(int(i) - 1 for i in c.split("/")[:2]) for c in line.split()[1:4]])
    cl, sums = [], {}
    for x, y, z in P:
        k = (round(x / CELL), round(y / CELL), round(z / CELL)); cl.append(k)
        s_ = sums.setdefault(k, [0.0, 0.0, 0.0, 0]); s_[0] += x; s_[1] += y; s_[2] += z; s_[3] += 1
    mean = {k: (a / n, b / n, c / n) for k, (a, b, c, n) in sums.items()}
    V, verts, key, idx = [], [], {}, []
    for face in faces:
        ks = [cl[v] for v, _ in face]
        if ks[0] == ks[1] or ks[1] == ks[2] or ks[0] == ks[2]: continue
        c = []
        for (v, t), k in zip(face, ks):
            kk = (k, t)
            if kk not in key: key[kk] = len(verts); verts.append(kk)
            c.append(key[kk])
        idx += [c[0], c[2], c[1]]  # OBJ is counter-clockwise, Unity wants clockwise front faces
    V = [mean[k] for k, _ in verts]
    xs, ys, zs = [p[0] for p in V], [p[1] for p in V], [p[2] for p in V]
    ox, oz, z0 = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2, min(ys)
    with open(out_path, "wb") as f:
        f.write(b"MSH1"); f.write(struct.pack("<fffii", ox, oz, z0, len(verts), len(idx)))
        buf = bytearray()
        for (x, y, z), (_, ti) in zip(V, verts):
            u, v = T[ti]
            buf += struct.pack("<hhhHH", round((x - ox) * 100), round((y - z0) * 100), round((z - oz) * 100),
                               min(65535, max(0, round(u * 65535))), min(65535, max(0, round(v * 65535))))
        f.write(buf); f.write(struct.pack(f"<{len(idx)}I", *idx))
    print(os.path.basename(out_path), "vertices", len(verts), "triangles", len(idx) // 3, "of", len(faces), "MB", round(os.path.getsize(out_path) / 1e6, 1),
          "x", round(min(xs)), round(max(xs)), "z", round(min(zs)), round(max(zs)))


def lonlat(x, z):
    import math
    return ORIGIN[0] + x / (math.cos(math.radians(ORIGIN[1])) * 111320.0), ORIGIN[1] + z / KZ


def tiles_for(circles, zips, to_game):
    """Names of tiles (S-JTSK min corner) whose footprint touches one of the circles, with their sheet's Zip."""
    pts = [(x + dx * r, z + dz * r) for x, z, r in circles for dx, dz in ((-1, -1), (1, 1))]
    lo, hi = lonlat(min(p[0] for p in pts), min(p[1] for p in pts)), lonlat(max(p[0] for p in pts), max(p[1] for p in pts))
    sheets = get_json(SHEETS, {"where": "1=1", "geometry": f"{lo[0]},{lo[1]},{hi[0]},{hi[1]}", "geometryType": "esriGeometryEnvelope",
                               "inSR": 4326, "spatialRel": "esriSpatialRelIntersects", "outFields": "CML,direct_download_url",
                               "returnGeometry": "false", "f": "json"})["features"]
    out = []
    for sh in sheets:
        a = sh["attributes"]
        if a["CML"] not in zips: zips[a["CML"]] = Zip(a["direct_download_url"])
        z = zips[a["CML"]]
        names = sorted({n[:-len("-mesh.obj")] for n in z.entries if n.endswith("-mesh.obj")})
        XY = [(n, *map(float, n[1:].split("--"))) for n in names]  # "-742994--1043315" -> X=742994, Y=1043315 (signs below)
        # rows are not aligned (X differs by a few metres row to row): size a tile by its own row / column neighbours
        step = lambda v, vs, d: next((w - v for w in sorted(vs) if w > v + 50), d)
        for n, x, y in XY:
            X, Y = -x, -y
            w = step(X, [-a for _, a, b in XY if b == y], 170); h = step(Y, [-b for _, a, b in XY if a == x], 320)
            corners = [to_game(X + i * w, Y + j * h) for i in (0, 1) for j in (0, 1)]
            gx0, gx1 = min(c[0] for c in corners), max(c[0] for c in corners); gz0, gz1 = min(c[1] for c in corners), max(c[1] for c in corners)
            if any((max(gx0, min(cx, gx1)) - cx) ** 2 + (max(gz0, min(cz, gz1)) - cz) ** 2 < r * r for cx, cz, r in circles):
                out.append((n, z))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for old in os.listdir(OUT):  # the set follows TARGETS
        if old.endswith((".bytes", ".jpg")): os.remove(os.path.join(OUT, old))
    zips, done = {}, set()
    tmp = CACHE; os.makedirs(tmp, exist_ok=True)
    if True:
        for name, circles in TARGETS.items():
            x, z = circles[0][0], circles[0][1]; lo, hi = lonlat(x - 600, z - 600), lonlat(x + 600, z + 600)
            to_game = affine(lo[0], lo[1], hi[0], hi[1])  # fitted around each landmark
            for t, zp in tiles_for(circles, zips, to_game):
                if t in done: continue
                done.add(t); print(name, t)
                obj = os.path.join(tmp, t + ".obj"); zp.extract(t + "-mesh.obj", obj)
                convert(obj, to_game, os.path.join(OUT, t + ".bytes"))
                jpg = os.path.join(tmp, t + ".jpg"); zp.extract(t + "-texture-0.jpg", jpg); shutil.copyfile(jpg, os.path.join(OUT, t + ".jpg"))
    print("tiles:", len(done))


if __name__ == "__main__":
    main()
