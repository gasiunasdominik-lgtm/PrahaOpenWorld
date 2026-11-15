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

    public static Texture2D WindowTex, CobbleTex, RoadTex, RoundedTex;
    public static Sprite RoundedSprite, CircleSprite, ArrowSprite;

    // Domain reload is disabled (Enter Play Mode Options), so statics survive between
    // play sessions while Unity destroys the objects they point to. Stale cached
    // materials come back as null and render pink, so start every session clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cache.Clear();
        baseMat = null;
        WindowTex = CobbleTex = RoadTex = RoundedTex = null;
        RoundedSprite = CircleSprite = ArrowSprite = null;
    }

    public static void Init()
    {
        if (baseMat != null && WindowTex != null) return;
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null || rp.defaultMaterial == null)
        {
            Debug.LogError("[Gfx] No active URP pipeline asset / default material — materials would render pink.");
            baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }
        else baseMat = rp.defaultMaterial;

        WindowTex = MakeWindowTex();
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

    public static Material Facade(Color c) => Mat(c, 0.08f, WindowTex, "facade");

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
    static Texture2D MakeWindowTex()
    {
        const int S = 64;
        var t = NewTex(S, S, "WindowTex");
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
        t.SetPixels(px); t.Apply(true);
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
