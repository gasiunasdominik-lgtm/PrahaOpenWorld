"""Per-building colours measured on the IPR photo mesh (Tools/sample_colors.py -> IprColors.bytes) turned into what the game
draws: the facade colours clustered into NF tones, each a Facade<i> texture (make_textures.facade_set: the windows stay
white), the roofs into NR tints of the roof-tile photo. Writes Assets/Resources/IprTones.bytes:
"TON1", nf int32, nr int32, nf x facade RGB, nr x (tile tint RGB, flat roof RGB), count int32, then per building <iBB> OBJECTID,
facade tone (255 = not measured), roof tone (255 = not measured).

The aerial photos are dark and flat (exposure, and a facade in shadow reads near black), so the photo colour is taken as
a hue: walls keep their chromaticity but their lightness is compressed into the range of real plaster, roofs are lifted
and given back some saturation. Usage: python3 Tools/color_tones.py
"""
import os, struct, sys
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_textures as T

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, "..", "Assets", "Resources")
NF, NR = 24, 12
LUM = np.array([0.3, 0.59, 0.11])


def mute(ch, green, blue):
    """Trees in front of a facade or over a roof read as green, sky in the windows as blue: take most of that excess out."""
    ch = ch.copy()
    g = ch[:, 1] - np.maximum(ch[:, 0], ch[:, 2]); ch[:, 1] -= green * np.maximum(g, 0)
    bl = ch[:, 2] - np.maximum(ch[:, 0], ch[:, 1]); ch[:, 2] -= blue * np.maximum(bl, 0)
    return ch


def wall_albedo(c):
    L = c @ LUM
    trust = np.clip((L - 35) / 60, 0, 1)[:, None]  # in deep shadow the hue is mostly blue sky: fade it to a warm neutral
    chroma = mute(c / np.maximum(L, 1)[:, None], 0.9, 0.7)
    chroma = chroma * trust + np.array([1.04, 1.0, 0.92]) * (1 - trust)
    chroma = 1 + (chroma - 1) * 1.25  # the photos are washed out
    L2 = 118 + 0.55 * L  # 0 -> 118, 100 -> 173, 200 -> 228
    return np.clip(chroma * L2[:, None], 30, 245)


def roof_albedo(c):
    L = c @ LUM
    chroma = 1 + (mute(c / np.maximum(L, 1)[:, None], 0.7, 0.8) - 1) * 1.5
    trust = np.clip((L - 25) / 45, 0, 1)[:, None]  # a roof in shadow: its hue is the sky's, a dark neutral is safer
    chroma = chroma * trust + 1 * (1 - trust)
    L2 = 48 + 0.9 * L
    return np.clip(chroma * L2[:, None], 15, 235)


def kmeans(X, k, it=50, seed=0):
    rng = np.random.default_rng(seed)
    C = X[rng.choice(len(X), k, replace=False)]
    for _ in range(it):
        lab = ((X[:, None] - C[None]) ** 2).sum(2).argmin(1)
        C = np.array([X[lab == j].mean(0) if (lab == j).any() else C[j] for j in range(k)])
    lab = ((X[:, None] - C[None]) ** 2).sum(2).argmin(1)
    return C, lab


def main():
    b = open(os.path.join(RES, "IprColors.bytes"), "rb").read(); assert b[:4] == b"COL1"
    n, = struct.unpack_from("<i", b, 4)
    rows = [struct.unpack_from("<i7B", b, 8 + 11 * i) for i in range(n)]
    oid = np.array([r[0] for r in rows]); flags = np.array([r[7] for r in rows])
    wall = wall_albedo(np.array([r[1:4] for r in rows], float)); roof = roof_albedo(np.array([r[4:7] for r in rows], float))
    hw, hr = flags & 1 > 0, flags & 2 > 0
    CW, lw = kmeans(wall[hw], NF); CR, lr = kmeans(roof[hr], NR)
    ow = np.argsort(CW @ LUM); CW = CW[ow]; lw = np.argsort(ow)[lw]  # tones sorted dark -> light
    orr = np.argsort(CR @ LUM); CR = CR[orr]; lr = np.argsort(orr)[lr]
    ft = np.full(n, 255); ft[hw] = lw; rt = np.full(n, 255); rt[hr] = lr
    # the roof-tile photo has its own colour: the material tint is measured / photo mean
    tile_mean = np.asarray(Image.open(os.path.join(T.OUT, "Roof.jpg")).convert("RGB"), float).reshape(-1, 3).mean(0)
    tint = np.clip(CR / tile_mean, 0, 1.6)
    with open(os.path.join(RES, "IprTones.bytes"), "wb") as f:
        f.write(b"TON1"); f.write(struct.pack("<ii", NF, NR))
        for c in CW: f.write(struct.pack("<3B", *[int(v) for v in c]))
        for t, c in zip(tint, CR): f.write(struct.pack("<6B", *[int(round(min(255, v * 160))) for v in t], *[int(v) for v in c]))
        f.write(struct.pack("<i", n))
        for i in range(n): f.write(struct.pack("<iBB", int(oid[i]), int(ft[i]), int(rt[i])))
    print("fasády:", [("#%02X%02X%02X" % tuple(int(v) for v in c), int((lw == j).sum())) for j, c in enumerate(CW)])
    print("střechy:", [("#%02X%02X%02X" % tuple(int(v) for v in c), int((lr == j).sum())) for j, c in enumerate(CR)])
    T.facade_set(["#%02X%02X%02X" % tuple(int(v) for v in c) for c in CW])
    for i in range(NF, 64):  # tones from an earlier, larger set
        for ext in (".jpg", ".jpg.meta"):
            p = os.path.join(T.OUT, f"Facade{i}{ext}")
            if os.path.exists(p): os.remove(p)
    print("zapsáno", n, "budov ->", os.path.join(RES, "IprTones.bytes"))


if __name__ == "__main__":
    main()
