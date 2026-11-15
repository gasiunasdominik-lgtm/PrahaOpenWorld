"""Real facade and roof colours of every building, measured on IPR Praha's photogrammetric mesh (CC BY 4.0, "data base © IPR Praha").

The mesh itself is not used in the game (it melts at street level); its photo textures are the reference: for each mesh tile
the wall triangles are matched to a building footprint (a point 0.8 m behind the wall lies inside it) and the roof triangles
to the footprint under them, and the texture is sampled there. Per building the lit facade colour is a median over the
brighter half of the samples (shadows and windows are dark), the roof a median of the middle.

Output COL1 -> Assets/Resources/IprColors.bytes: count, then per building: OBJECTID int32, wall RGB, roof RGB (bytes),
flags byte (1 = wall measured, 2 = roof measured). Tiles are downloaded one at a time and deleted after use.
Usage: python3 Tools/sample_colors.py   (needs numpy + Pillow; resumable: per-tile results cached in ipr-colors-cache/)
"""
import json, math, os, struct, sys
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
sys.path.insert(0, os.path.dirname(__file__))
import fetch_ipr_mesh as M

HERE = os.path.dirname(os.path.abspath(__file__))
FOOT = os.path.join(HERE, "..", "Assets", "Resources", "IprFootprints.bytes")
OUT = os.path.join(HERE, "..", "Assets", "Resources", "IprColors.bytes")
CACHE = os.path.join(HERE, "..", "..", "..", "ipr-colors-cache")  # Hry/ipr-colors-cache
GRID = 25.0


def load_footprints():
    b = open(FOOT, "rb").read(); assert b[:4] == b"FPR1"
    n, = struct.unpack_from("<i", b, 4); o = 8; out = {}
    for _ in range(n):
        oid, nr = struct.unpack_from("<ih", b, o); o += 6; rings = []
        for _ in range(nr):
            k, = struct.unpack_from("<h", b, o); o += 2
            rings.append(np.frombuffer(b, "<f4", 2 * k, o).reshape(k, 2).astype(np.float64)); o += 8 * k
        out[oid] = rings
    return out


def inside(rings, x, z):
    c = False
    for r in rings:  # even-odd over all rings (holes count)
        xs, zs = r[:, 0], r[:, 1]; xj, zj = np.roll(xs, 1), np.roll(zs, 1)
        cond = ((zs > z) != (zj > z)) & (x < (xj - xs) * (z - zs) / (zj - zs + 1e-12) + xs)
        c ^= bool(np.count_nonzero(cond) & 1)
    return c


def main():
    os.makedirs(CACHE, exist_ok=True)
    foot = load_footprints()
    grid, bbox = {}, {}
    for oid, rings in foot.items():
        a = np.vstack(rings); x0, z0 = a.min(0); x1, z1 = a.max(0); bbox[oid] = (x0, z0, x1, z1)
        for gx in range(int(math.floor(x0 / GRID)), int(math.floor(x1 / GRID)) + 1):
            for gz in range(int(math.floor(z0 / GRID)), int(math.floor(z1 / GRID)) + 1):
                grid.setdefault((gx, gz), []).append(oid)
    allb = np.array(list(bbox.values())); X0, Z0 = allb[:, 0].min(), allb[:, 1].min(); X1, Z1 = allb[:, 2].max(), allb[:, 3].max()
    R = raster(foot); print("raster", R[0].shape)
    print("budov", len(foot), "oblast", round(X0), round(Z0), round(X1), round(Z1))

    # every tile over the built-up area (circles on a 150 m lattice where buildings are)
    circles = sorted({(round(((b[0] + b[2]) / 2) / 150) * 150, round(((b[1] + b[3]) / 2) / 150) * 150) for b in bbox.values()})
    circles = [(x, z, 110) for x, z in circles]
    cx, cz = (X0 + X1) / 2, (Z0 + Z1) / 2
    zips, tiles = {}, []
    # fit the S-JTSK -> game affine per ~1.2 km block (the projection is not linear over the whole city)
    blocks = sorted({(round(x / 1200), round(z / 1200)) for x, z, _ in circles})
    seen = set()
    for bx, bz in blocks:
        mine = [c for c in circles if (round(c[0] / 1200), round(c[1] / 1200)) == (bx, bz)]
        lo, hi = M.lonlat(bx * 1200 - 600, bz * 1200 - 600), M.lonlat(bx * 1200 + 600, bz * 1200 + 600)
        to_game = M.affine(lo[0], lo[1], hi[0], hi[1])
        for t, zp in M.tiles_for(mine, zips, to_game):
            if t not in seen: seen.add(t); tiles.append((t, zp, to_game))
    print("dlaždic", len(tiles), flush=True)

    for k, (t, zp, to_game) in enumerate(tiles):
        done = os.path.join(CACHE, t + ".json")
        if os.path.exists(done): continue
        obj, jpg = os.path.join(CACHE, t + ".obj"), os.path.join(CACHE, t + ".jpg")
        try:
            zp.extract(t + "-mesh.obj", obj); zp.extract(t + "-texture-0.jpg", jpg)
        except Exception as e:
            print("chyba", t, e, flush=True); continue
        res = sample_tile(obj, jpg, to_game, R)
        json.dump(res, open(done, "w"))
        os.remove(obj); os.remove(jpg)
        print(k + 1, "/", len(tiles), t, "budov", len(res), flush=True)
    write(foot)


RES = 0.5  # m per pixel of the footprint id raster


def raster(foot):
    """Every footprint painted with its index + 1 into one integer image (game x right, z up)."""
    from PIL import ImageDraw
    allp = np.vstack([np.vstack(r) for r in foot.values()])
    x0, z0 = allp.min(0) - 5; x1, z1 = allp.max(0) + 5
    w, h = int((x1 - x0) / RES) + 1, int((z1 - z0) / RES) + 1
    img = Image.new("I", (w, h), 0); d = ImageDraw.Draw(img)
    ids = list(foot.keys())
    for k, oid in enumerate(ids):
        for r in foot[oid]:
            d.polygon([((x - x0) / RES, (z - z0) / RES) for x, z in r], fill=k + 1)
    return np.asarray(img, dtype=np.int32), x0, z0, np.array(ids)


def lookup(R, x, z):
    img, x0, z0, ids = R
    i = ((x - x0) / RES).astype(int); j = ((z - z0) / RES).astype(int)
    ok = (i >= 0) & (j >= 0) & (i < img.shape[1]) & (j < img.shape[0])
    out = np.zeros(len(x), np.int32); out[ok] = img[j[ok], i[ok]]
    return out  # 0 = no building, else index + 1


def sample_tile(obj, jpg, to_game, R):
    P, T, F = [], [], []
    with open(obj) as f:
        for line in f:
            if line.startswith("v "):
                _, x, y, z = line.split()[:4]; P.append((float(x), float(y), float(z)))
            elif line.startswith("vt "):
                _, u, v = line.split()[:3]; T.append((float(u), float(v)))
            elif line.startswith("f "):
                c = line.split()[1:4]; F.append([int(i) - 1 for a in c for i in a.split("/")[:2]])
    P = np.array(P); T = np.array(T); F = np.array(F)
    gx, gz = to_game(P[:, 0], P[:, 1]); G = np.stack([gx, P[:, 2], gz], 1)  # game x, height, z
    tex = Image.open(jpg).convert("RGB"); W, H = tex.size
    tex = np.asarray(tex.resize((max(1, W // 4), max(1, H // 4)), Image.BILINEAR)); h4, w4 = tex.shape[:2]  # 20 cm/px is plenty for colour
    a, b, c = G[F[:, 0]], G[F[:, 2]], G[F[:, 4]]
    n = np.cross(b - a, c - a); area = np.linalg.norm(n, axis=1) / 2; n = n / (2 * area[:, None] + 1e-9)
    cen = (a + b + c) / 3
    # three samples per triangle (around the centroid) against texture noise
    cols = []
    for wts in ((1/3, 1/3, 1/3), (0.6, 0.2, 0.2), (0.2, 0.2, 0.6)):
        uv = wts[0] * T[F[:, 1]] + wts[1] * T[F[:, 3]] + wts[2] * T[F[:, 5]]
        px = np.clip((uv[:, 0] * w4).astype(int), 0, w4 - 1); py = np.clip(((1 - uv[:, 1]) * h4).astype(int), 0, h4 - 1)
        cols.append(tex[py, px].astype(np.float32))
    col = sum(cols) / 3
    ok = area > 0.05
    hl = np.hypot(n[:, 0], n[:, 2]) + 1e-9; ux, uz = n[:, 0] / hl, n[:, 2] / hl
    wall = ok & (np.abs(n[:, 1]) < 0.3)
    i1 = lookup(R, cen[:, 0] - ux * 0.8, cen[:, 2] - uz * 0.8); i2 = lookup(R, cen[:, 0] + ux * 0.8, cen[:, 2] + uz * 0.8)
    wid = np.where(wall & (i1 > 0) & (i2 == 0), i1, np.where(wall & (i2 > 0) & (i1 == 0), i2, 0))
    roof = ok & (np.abs(n[:, 1]) > 0.55)
    rid = np.where(roof, lookup(R, cen[:, 0], cen[:, 2]), 0)
    ids = R[3]; res = {}
    for kind, idx in (("w", wid), ("r", rid)):
        sel = np.nonzero(idx)[0]
        if len(sel) == 0: continue
        rng = np.random.default_rng(1); sel = sel[rng.permutation(len(sel))]
        order = np.argsort(idx[sel], kind="stable"); sel = sel[order]
        bid = idx[sel]; starts = np.r_[0, np.nonzero(np.diff(bid))[0] + 1, len(sel)]
        for s0, s1 in zip(starts[:-1], starts[1:]):
            part = sel[s0:min(s1, s0 + 600)]
            oid = int(ids[bid[s0] - 1])
            e = res.setdefault(str(oid), {"w": [], "r": []})
            e[kind] = np.c_[np.round(cen[part, 1], 1), np.round(col[part]).astype(int)].tolist()
    return res


def write(foot):
    acc = {}
    for f in os.listdir(CACHE):
        if not f.endswith(".json"): continue
        for oid, e in json.load(open(os.path.join(CACHE, f))).items():
            d = acc.setdefault(int(oid), {"w": [], "r": []}); d["w"] += e["w"]; d["r"] += e["r"]
    out = []
    for oid, d in acc.items():
        wall = roof = None; flags = 0
        roofs = np.array(d["r"]) if d["r"] else None
        if len(d["w"]) >= 12:
            w = np.array(d["w"], float)
            # facade band: above the ground floor (shopfronts, cars, people), below the eaves
            y0 = np.percentile(w[:, 0], 3); y1 = np.percentile(roofs[:, 0], 10) if roofs is not None and len(roofs) > 5 else w[:, 0].max()
            band = w[(w[:, 0] > y0 + 4) & (w[:, 0] < y1 - 0.5)]
            if len(band) < 8: band = w
            lum = band[:, 1:] @ [0.3, 0.59, 0.11]
            lo, hi = np.percentile(lum, 50), np.percentile(lum, 92)
            sel = band[(lum >= lo) & (lum <= hi), 1:]
            if len(sel): wall = np.median(sel, 0); flags |= 1
        if roofs is not None and len(roofs) >= 8:
            lum = roofs[:, 1:] @ [0.3, 0.59, 0.11]
            lo, hi = np.percentile(lum, 25), np.percentile(lum, 85)
            sel = roofs[(lum >= lo) & (lum <= hi), 1:]
            if len(sel): roof = np.median(sel, 0); flags |= 2
        if flags:
            w = wall if wall is not None else (0, 0, 0); r = roof if roof is not None else (0, 0, 0)
            out.append((oid, [int(v) for v in w], [int(v) for v in r], flags))
    with open(OUT, "wb") as f:
        f.write(b"COL1"); f.write(struct.pack("<i", len(out)))
        for oid, w, r, fl in out: f.write(struct.pack("<i7B", oid, *w, *r, fl))
    print("zapsáno", len(out), "budov z", len(foot), "->", OUT)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "write": write(load_footprints())
    else: main()
