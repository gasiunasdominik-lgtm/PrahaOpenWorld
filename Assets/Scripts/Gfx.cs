using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Palette-driven materials and procedural textures. Nothing here uses literal colors:
// every tint comes from Palette.
public static class Gfx
{
    static readonly Dictionary<string, Material> cache = new();
    static Material baseMat;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    public static Texture2D[] GlowLevels;
    public static Texture2D WindowTex, WindowGlowTex, CobbleTex, RoadTex, RoundedTex;
    public static Sprite RoundedSprite, CircleSprite, ArrowSprite;

    // Domain reload is disabled (Enter Play Mode Options), so statics survive between
    // play sessions while Unity destroys the objects they point to. Stale cached
    // materials come back as null and render pink, so start every session clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cache.Clear();
        baseMat = null;
        WindowTex = WindowGlowTex = CobbleTex = RoadTex = RoundedTex = null; GlowLevels = null; litLevel = -1;
        RoundedSprite = CircleSprite = ArrowSprite = null;
    }

    public static void Init()
    {
        if (baseMat != null && WindowTex != null) return;
        // a shipped material, not the pipeline's defaultMaterial (editor-only: null in a build) or Shader.Find (stripped);
        // Resources also holds BaseLitEmissive / TmpUnderlay so the build keeps the _EMISSION and UNDERLAY_ON variants
        baseMat = Resources.Load<Material>("BaseLit");
        if (baseMat == null) Debug.LogError("[Gfx] Resources/BaseLit.mat missing — materials would render pink.");

        WindowTex = MakeWindowTex();
        WindowGlowTex = MakeWindowGlow();
        CobbleTex = MakeCobbleTex();
        RoadTex = MakeRoadTex();
        RoundedTex = MakeRounded(48, 14);
        RoundedSprite = Sprite.Create(RoundedTex, new Rect(0, 0, 48, 48), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
        var circle = MakeRounded(64, 32);
        CircleSprite = Sprite.Create(circle, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        var arrow = MakeArrow(64);
        ArrowSprite = Sprite.Create(arrow, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
    }

    public static Material Mat(Color c, float smooth = 0.12f, Texture tex = null, string tag = "")
    {
        Init();
        string key = ColorUtility.ToHtmlStringRGBA(c) + smooth.ToString("0.00") + (tex ? tex.name : "") + tag;
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(baseMat) { name = "M_" + key };
        m.SetColor(BaseColor, c);
        m.SetFloat(Smoothness, smooth);
        if (tex) m.SetTexture(BaseMap, tex);
        m.enableInstancing = true;
        cache[key] = m;
        return m;
    }

    public static Material Emissive(Color c, float intensity)
    {
        Init();
        string key = "E" + ColorUtility.ToHtmlStringRGBA(c) + intensity;
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(baseMat) { name = "M_" + key };
        m.SetColor(BaseColor, c);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor(EmissionColor, c * intensity);
        cache[key] = m;
        return m;
    }

    static Texture2D T(string name) => Resources.Load<Texture2D>("Tex/" + name);

    // A photo surface from Resources/Tex (Tools/make_textures.py, ambientCG CC0) with its normal map; tile = texture
    // repeats per UV unit of the mesh it goes on. Falls back to the flat colour if the textures are missing.
    public static Material Pbr(string tex, float smooth, float tile, Color tint)
    {
        var t = T(tex);
        if (t == null) return Mat(tint, smooth);
        var m = Mat(tint, smooth, t, "pbr" + tile.ToString("0.##"));
        m.mainTextureScale = Vector2.one * tile;
        var n = T(tex + "_n");
        if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        return m;
    }

    // Facade<i> textures from Tools/make_textures.py (real Old Town tones, ground floor shopfronts / rustication)
    public static int FacadeCount { get { int n = 0; while (n < 32 && T("Facade" + n) != null) n++; return n; } }
    public static Material FacadeTone(int i)
    {
        var f = Mat(Color.white, 0.08f, T("Facade" + i), i + "facade"); // name ends in "facade": night windows light up
        f.mainTextureScale = Vector2.one / Grid;
        var n = T("Facade_n" + (i % 2));
        if (n != null) { f.SetTexture("_BumpMap", n); f.EnableKeyword("_NORMALMAP"); }
        return f;
    }

    public static Material Facade(Color c)
    {
        int idx = System.Array.IndexOf(Palette.Facades, c);
        var ft = idx >= 0 ? T("Facade" + idx) : null;
        if (ft != null) // our Prague facade on a plaster photo, colour baked in (a tint would also tint the white frames)
        {
            var f = Mat(Color.white, 0.08f, ft, idx + "facade");
            f.mainTextureScale = Vector2.one / Grid;
            var n = T("Facade_n" + (idx % 2));
            if (n != null) { f.SetTexture("_BumpMap", n); f.EnableKeyword("_NORMALMAP"); }
            return f;
        }
        var m = Mat(c, 0.08f, WindowTex, "facade");
        m.mainTextureScale = Vector2.one / Grid; // textures hold Grid x Grid windows, so neighbours light up differently
        return m;
    }

    static float Noise(int x, int y, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + seed * 2147483647;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }
    }

    static Texture2D NewTex(int w, int h, string name)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4
        };
    }

    // One window cell (3.2 m x 3.4 m). White wall so the material tint gives the facade color.
    // Emission mask matching the glass panes of WindowTex: lit windows at night.
    // Windows light up one by one at dusk in random order: each window in the Grid x Grid tile has its own
    // threshold and colour (mostly warm, some cool white, a few TV blue); level L lights those below L / Levels.
    const int Grid = 8, Levels = 6;
    static Texture2D MakeWindowGlow()
    {
        const int S = 64;
        GlowLevels = new Texture2D[Levels + 1];
        for (int L = 0; L <= Levels; L++)
        {
            var t = NewTex(S * Grid, S * Grid, "WindowGlow" + L);
            var px = new Color[S * S * Grid * Grid];
            for (int cy = 0; cy < Grid; cy++)
            for (int cx = 0; cx < Grid; cx++)
            {
                float thr = Noise(cx, cy, 7), hue = Noise(cx, cy, 11);
                if (thr * Levels >= L) continue;
                Color c = hue < 0.8f ? new Color(1f, 0.72f, 0.38f) : hue < 0.95f ? new Color(0.95f, 0.93f, 0.85f) : new Color(0.5f, 0.62f, 0.95f);
                c *= 0.7f + Noise(cx, cy, 13) * 0.5f;
                for (int y = 18; y <= 48; y++)
                for (int x = 21; x <= 42; x++)
                    if (x != 31 && x != 32 && y != 37) px[(cy * S + y) * S * Grid + cx * S + x] = c;
            }
            t.SetPixels(px); t.Apply(true);
            GlowLevels[L] = t;
        }
        return GlowLevels[Levels];
    }

    static int litLevel = -1;
    /// lit: 0 = all windows dark, 1 = all lit. Swaps the emission map only when the level changes.
    public static void SetNightWindows(float lit)
    {
        int L = Mathf.Clamp(Mathf.RoundToInt(lit * Levels), 0, Levels);
        if (L == litLevel || GlowLevels == null) return;
        litLevel = L;
        foreach (var m in cache.Values)
        {
            if (m == null || !m.name.EndsWith("facade")) continue;
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", GlowLevels[L]);
            m.SetColor(EmissionColor, Color.white * 1.5f);
        }
    }

    static Texture2D MakeWindowTex()
    {
        const int S = 64;
        var t = NewTex(S * Grid, S * Grid, "WindowTex");
        var px = new Color[S * S];
        Color glassTop = Color.Lerp(Palette.Glass, Palette.SkyTop, 0.35f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float wall = 0.94f + Noise(x, y, 1) * 0.06f;
            Color c = new Color(wall, wall, wall, 1);
            bool frame = x >= 18 && x <= 45 && y >= 15 && y <= 51;
            bool glass = x >= 21 && x <= 42 && y >= 18 && y <= 48;
            bool mullion = (x == 31 || x == 32) || (y == 37);
            bool sill = x >= 16 && x <= 47 && y >= 12 && y <= 14;
            bool band = y >= 60;
            if (band) c *= 0.9f;
            if (sill) c = new Color(0.78f, 0.78f, 0.78f, 1);
            if (frame) c = new Color(0.98f, 0.98f, 0.98f, 1);
            if (glass && !mullion)
            {
                float g = Mathf.InverseLerp(18, 48, y);
                c = Color.Lerp(Palette.Glass, glassTop, g * g);
                c.a = 1;
            }
            px[y * S + x] = c;
        }
        var big = new Color[S * S * Grid * Grid]; // the same window repeated Grid x Grid times
        for (int y = 0; y < S * Grid; y++) for (int x = 0; x < S * Grid; x++) big[y * S * Grid + x] = px[(y % S) * S + x % S];
        t.SetPixels(big); t.Apply(true);
        return t;
    }

    static Texture2D MakeCobbleTex()
    {
        const int S = 64;
        var t = NewTex(S, S, "CobbleTex");
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int row = y / 8;
            int cx = (x + (row % 2) * 4) / 8;
            bool grout = (y % 8 == 0) || ((x + (row % 2) * 4) % 8 == 0);
            float v = 0.86f + Noise(cx, row, 7) * 0.14f;
            if (grout) v = 0.72f;
            px[y * S + x] = new Color(v, v, v, 1);
        }
        t.SetPixels(px); t.Apply(true);
        return t;
    }

    // 18 m wide road; texture spans the width (u) and 18 m of length (v). Absolute colors baked from Palette.
    static Texture2D MakeRoadTex()
    {
        const int S = 64;
        var t = NewTex(S, S, "RoadTex");
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float n = 0.92f + Noise(x, y, 3) * 0.08f;
            Color c = Palette.Asphalt * n;
            bool dash = (x == 31 || x == 32) && (y % 32) < 18;
            bool edge = x == 4 || x == 59;
            if (dash || edge) c = Palette.RoadLine * 0.95f;
            c.a = 1;
            px[y * S + x] = c;
        }
        t.SetPixels(px); t.Apply(true);
        return t;
    }

    static Texture2D MakeRounded(int S, float radius)
    {
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (S - radius)));
            float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (S - radius)));
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(radius - d + 0.5f);
            px[y * S + x] = new Color(1, 1, 1, a);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D MakeArrow(int S)
    {
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Vector2 a = new(S * 0.5f, S * 0.92f), b = new(S * 0.15f, S * 0.1f), c = new(S * 0.85f, S * 0.1f), notch = new(S * 0.5f, S * 0.32f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            bool inside = InTri(p, a, b, notch) || InTri(p, a, notch, c);
            px[y * S + x] = new Color(1, 1, 1, inside ? 1 : 0);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        float s2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
        float s3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
        bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
        return !(neg && pos);
    }
}
