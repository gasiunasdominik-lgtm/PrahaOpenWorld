"""Realistic surface textures -> Assets/Resources/Tex/*.jpg|png (needs Pillow: pip install pillow).

Photo sources: ambientCG (CC0 1.0, https://ambientcg.com): RoofingTiles012A, PavingStones147 (small granite mosaic of
Prague pavements), PavingStones048 (granite setts), Asphalt025C, Plaster001 (wall plaster under the facades).
Downloaded once into Hry/cc0-src (outside the project).

Facades are our own design on the plaster photo: Prague tenement windows (white double casements with a cross,
stucco surrounds, a header cornice, stone sill) and a cornice band per floor. Layout = Gfx.WindowGlow: 8 x 8 cells,
one cell = one window bay (3.2 m) x one floor (3.4 m), glass at x 21..42 / y 18..48 of 64 (bottom origin), so the
night glow masks still match. The facade colour is baked in (a material tint would also tint the white frames).
"""
import io, os, random, urllib.request, zipfile
from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Assets", "Resources", "Tex")
SRC = os.path.join(HERE, "..", "..", "cc0-src")
UA = {"User-Agent": "PrahaOpenWorld-game/0.1"}
# Real Old Town tones (compared with the IPR photo mesh of Old Town Square): mostly off-whites, creams and pale
# pastels, far less saturated than a toy palette. Facade<i>.jpg, style = i % 2.
FACADES = ["#EEE8DA", "#E9DCBC", "#E6D0A2", "#DCCBBE", "#D3D6C8", "#E2D9CC", "#E8CFA8", "#D8D3CA"]
C, G = 128, 8  # px per window cell, cells per side
N = C * G


def acg(asset, res):
    """Path prefix of an ambientCG material (downloads the zip once)."""
    d = os.path.join(SRC, asset)
    if not os.path.isdir(d):
        os.makedirs(d)
        b = urllib.request.urlopen(urllib.request.Request(f"https://ambientcg.com/get?file={asset}_{res}-JPG.zip", headers=UA), timeout=300).read()
        zipfile.ZipFile(io.BytesIO(b)).extractall(d)
    return os.path.join(d, f"{asset}_{res}-JPG_")


def surface(name, asset, res, tint=None):
    p = acg(asset, res)
    col = Image.open(p + "Color.jpg").convert("RGB").resize((1024, 1024), Image.LANCZOS)
    if os.path.exists(p + "AmbientOcclusion.jpg"):  # bake the crevices in: the Lit shader has no AO map here
        ao = Image.open(p + "AmbientOcclusion.jpg").convert("L").resize((1024, 1024), Image.LANCZOS)
        col = ImageChops.multiply(col, Image.merge("RGB", [ao] * 3))
    if tint: col = ImageChops.multiply(col, Image.new("RGB", col.size, tint))
    col.save(os.path.join(OUT, name + ".jpg"), quality=90)
    Image.open(p + "NormalGL.jpg").convert("RGB").resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, name + "_n.png"))


def hexrgb(h): return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def rect(d, box, fill):  # box in 64-cell units, bottom origin, inclusive -> PIL
    x0, y0, x1, y1 = box
    d.rectangle([x0, N - 1 - y1, x1, N - 1 - y0], fill=fill)


def facade_layers(style):
    """(paint: L mask per part, height map) for one style. Parts: wall, stucco, frame, glass, sill."""
    H = Image.new("F", (N, N), 0.0); hd = ImageDraw.Draw(H)
    masks = {k: Image.new("L", (N, N), 0) for k in ("stucco", "frame", "glass", "sill", "shadow", "dark", "post")}
    md = {k: ImageDraw.Draw(v) for k, v in masks.items()}
    rng = random.Random(7)
    for cy in range(G):
        for cx in range(G):
            ox, oy = cx * C, cy * C
            def R(k, x0, y0, x1, y1, h=None):
                b = (ox + x0, oy + y0, ox + x1, oy + y1)
                if k: rect(md[k], b, 255)
                if h is not None: rect(hd, b, h)
            # cornice band at the top of every floor, with its shadow below
            R("stucco", 0, 118, 127, 127, 0.7); R("shadow", 0, 112, 127, 117)
            if cy == 0:  # ground floor (UV v 0..1 from the street): stone plinth and shopfronts, or a rusticated base
                R("sill", 0, 0, 127, 12, 0.4)
                if style == 0 and rng.random() < 0.75:
                    R("dark", 14, 12, 113, 104, -0.2)            # shop frame (dark green/brown joinery)
                    R("glass", 20, 18, 107, 92, -0.7)           # display window
                    R("post", 62, 18, 65, 92, -0.2)              # its middle post
                    R("stucco", 10, 104, 117, 114, 0.5)          # fascia for the shop sign
                else:
                    for y in range(14, 112, 14): R("shadow", 0, y, 127, y + 1)   # rustication joints
                    R("sill", 30, 22, 97, 29, 0.6)
                    R("frame", 36, 30, 91, 102, -0.45); R("glass", 42, 36, 85, 97, -0.8)
                    rect(md["frame"], (ox + 42, oy + 36, ox + 85, oy + 97), 0)
                    R("frame", 62, 36, 65, 97, -0.45); R("frame", 42, 73, 85, 76, -0.45)
                continue
            if style == 0:  # stucco surround + straight header cornice
                R("stucco", 30, 24, 97, 104, 0.35)
                R("stucco", 26, 104, 101, 111, 0.8); R("shadow", 30, 98, 97, 103)
            else:  # plain reveal + triangular-ish header (stepped) and pilaster strips
                R("stucco", 0, 0, 7, 117, 0.45); R("stucco", 120, 0, 127, 117, 0.45)
                for i in range(4): R("stucco", 30 + i * 6, 102 + i * 2, 97 - i * 6, 104 + i * 2, 0.6 + i * 0.1)
                R("shadow", 32, 96, 95, 101)
            R("sill", 30, 22, 97, 29, 0.6); R("shadow", 32, 16, 95, 21)
            R("frame", 36, 30, 91, 102, -0.45)  # frame sits in the reveal
            R("glass", 42, 36, 85, 97, -0.8)
            rect(md["frame"], (ox + 42, oy + 36, ox + 85, oy + 97), 0)  # the frame is a ring around the glass
            R("frame", 62, 36, 65, 97, -0.45); R("frame", 42, 73, 85, 76, -0.45)  # cross: mullion + transom
            if rng.random() < 0.35:  # a drawn curtain behind the glass (paint only)
                side = rng.random() < 0.5
                rect(md["frame"], (ox + (42 if side else 66), oy + 40, ox + (61 if side else 85), oy + 72), 120)
    return masks, H


def facade(i, color, style, masks, plaster):
    base = ImageChops.multiply(plaster, Image.new("RGB", (N, N), hexrgb(color)))
    stucco_col = tuple(min(255, int(c * 0.55 + 255 * 0.45)) for c in hexrgb(color))
    img = base.copy()
    img = Image.composite(ImageChops.multiply(plaster, Image.new("RGB", (N, N), stucco_col)), img, masks["stucco"])
    img = Image.composite(ImageChops.multiply(img, Image.new("RGB", (N, N), (150, 150, 150))), img, masks["shadow"].filter(ImageFilter.GaussianBlur(3)))
    img = Image.composite(ImageChops.multiply(plaster, Image.new("RGB", (N, N), (226, 222, 214))), img, masks["sill"])
    # glass: dark, the sky reflected towards the top of each pane
    glass = Image.new("RGB", (N, N)); gd = ImageDraw.Draw(glass)
    for y in range(N):
        t = 1 - (y % C) / C; t = max(0.0, min(1.0, (t - 0.25) / 0.5))
        gd.line([(0, y), (N, y)], fill=(int(38 + 60 * t * t), int(46 + 70 * t * t), int(58 + 90 * t * t)))
    img = Image.composite(Image.new("RGB", (N, N), (48, 54, 44)), img, masks["dark"])  # shop joinery under its glass
    img = Image.composite(glass, img, masks["glass"])
    img = Image.composite(Image.new("RGB", (N, N), (48, 54, 44)), img, masks["post"])
    img = Image.composite(Image.new("RGB", (N, N), (236, 232, 222)), img, masks["frame"].point(lambda v: 255 if v > 200 else 0))
    curtains = masks["frame"].point(lambda v: 255 if 100 < v < 140 else 0)
    img = Image.composite(Image.new("RGB", (N, N), (196, 186, 166)), img, ImageChops.multiply(curtains, masks["glass"]))
    img.save(os.path.join(OUT, f"Facade{i}.jpg"), quality=90)


def normal_from(H, plaster_n, strength=6.0):
    """Normal map (OpenGL, +Y up in texture = up the wall) from a height field, over the plaster's own normals."""
    w, h = H.size; px = H.load(); pn = plaster_n.load()
    out = Image.new("RGB", (w, h)); po = out.load()
    for y in range(h):
        for x in range(w):
            dx = (px[(x + 1) % w, y] - px[x - 1, y]) * strength
            dy = (px[x, y - 1] - px[x, (y + 1) % h]) * strength  # PIL rows go down; GL green points up
            r, g, b = pn[x, y]
            nx = (r / 127.5 - 1) - dx; ny = (g / 127.5 - 1) - dy; nz = b / 127.5 - 1
            l = (nx * nx + ny * ny + nz * nz) ** 0.5 or 1
            po[x, y] = (int((nx / l + 1) * 127.5), int((ny / l + 1) * 127.5), int((nz / l + 1) * 127.5))
    return out


def water():
    """Tileable ripple normal map (our own): sum of waves with whole-number frequencies, so it repeats seamlessly."""
    import math
    S = 512; rng = random.Random(3); waves = []
    for _ in range(48):
        f = rng.choice(range(3, 28)); a = rng.gauss(0, 0.6)  # mostly across the flow (the river runs north = texture v)
        fx, fy = round(f * math.cos(a)), round(f * math.sin(a) * 0.6)
        if fx == 0 and fy == 0: continue
        waves.append((fx, fy, 1.0 / (fx * fx + fy * fy) ** 0.75, rng.random() * 6.283))
    H = [[0.0] * S for _ in range(S)]
    for y in range(S):
        for x in range(S):
            H[y][x] = sum(a * math.sin(6.283 * (fx * x + fy * y) / S + ph) for fx, fy, a, ph in waves)
    out = Image.new("RGB", (S, S)); po = out.load(); k = 9.0
    for y in range(S):
        for x in range(S):
            dx = (H[y][(x + 1) % S] - H[y][x - 1]) * k; dy = (H[y - 1][x] - H[(y + 1) % S][x]) * k
            l = (dx * dx + dy * dy + 1) ** 0.5
            po[x, y] = (int((-dx / l + 1) * 127.5), int((-dy / l + 1) * 127.5), int((1 / l + 1) * 127.5))
    out.save(os.path.join(OUT, "Water_n.png"))


def facade_set(colors):
    """Facade<i>.jpg for every colour (style = i % 2) plus the plain Wall plaster and the two facade normal maps."""
    p = acg("Plaster001", "2K")
    plaster = Image.open(p + "Color.jpg").convert("RGB").resize((N // 2, N // 2), Image.LANCZOS)
    plaster = plaster.point(lambda v: min(255, int(v * 255 / 215)))  # mean ~215 -> white, so the colour comes out true
    tile = Image.new("RGB", (N, N))
    for ty in range(2):
        for tx in range(2): tile.paste(plaster, (tx * N // 2, ty * N // 2))
    pn_small = Image.open(p + "NormalGL.jpg").convert("RGB").resize((N // 2, N // 2), Image.LANCZOS)
    pn = Image.new("RGB", (N, N))
    for ty in range(2):
        for tx in range(2): pn.paste(pn_small, (tx * N // 2, ty * N // 2))
    tile.save(os.path.join(OUT, "Wall.jpg"), quality=90)  # plain plaster: churches, towers (no tenement windows)
    pn.save(os.path.join(OUT, "Wall_n.png"))
    for style in (0, 1):
        masks, H = facade_layers(style)
        # (PIL cannot blur float images; the sharp steps read as crisp stucco edges)
        normal_from(H, pn).save(os.path.join(OUT, f"Facade_n{style}.png"))
        for i, c in enumerate(colors):
            if i % 2 == style: facade(i, c, style, masks, tile)


def main():
    os.makedirs(OUT, exist_ok=True)
    water()
    surface("Roof", "RoofingTiles012A", "1K")
    surface("Mosaic", "PavingStones147", "2K")
    surface("Setts", "PavingStones048", "2K")
    surface("Asphalt", "Asphalt025C", "1K")
    facade_set(FACADES)
    print("hotovo:", sorted(os.listdir(OUT)))


if __name__ == "__main__":
    main()
