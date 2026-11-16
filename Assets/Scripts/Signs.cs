using System;
using System.Collections.Generic;
using UnityEngine;

// Traffic signs and zebra crossings where OSM has them (Tools/fetch_signs.py): IP6 at every marked crossing (both kerbs) with
// its zebra across the carriageway, P4 "give way", P6 "STOP", IP4b at the start of every one-way street, B2, B20a speed
// limits... The sign faces are drawn here after the Czech designs (vyhláška 294/2015 Sb.), cut out of the plate by alpha.
public class Signs : MonoBehaviour
{
    [Serializable] class S { public string k; public float[] p; public float v; }
    [Serializable] class L { public S[] items; }

    readonly Dictionary<Vector2Int, List<(Vector2 a, Vector2 b, float hw)>> segs = new();
    readonly Dictionary<string, Material> faces = new();
    Material pole, back, zebra;

    void Start()
    {
        var a = Resources.Load<TextAsset>("PrahaSigns"); if (a == null || MapBuilder.D == null) return;
        var list = JsonUtility.FromJson<L>("{\"items\":" + a.text + "}").items;
        foreach (var st in MapBuilder.D.streets)
        {
            if (st.foot || st.w < 4f) continue;
            for (int i = 0; i + 3 < st.p.Length; i += 2)
            {
                var p0 = new Vector2(st.p[i], st.p[i + 1]); var p1 = new Vector2(st.p[i + 2], st.p[i + 3]);
                var c = Cell((p0 + p1) / 2f); if (!segs.TryGetValue(c, out var l)) segs[c] = l = new();
                l.Add((p0, p1, st.w / 2f));
            }
        }
        pole = Gfx.Mat(new Color(0.55f, 0.56f, 0.57f), 0.5f); zebra = Gfx.Mat(new Color(0.92f, 0.92f, 0.9f), 0.25f);
        back = Cutout(Face("back"), "back");
        var tiles = new Dictionary<Vector2Int, MeshBuilder>();
        MeshBuilder T(Vector2 p) { var k = new Vector2Int(Mathf.FloorToInt(p.x / 250f), Mathf.FloorToInt(p.y / 250f)); if (!tiles.TryGetValue(k, out var m)) tiles[k] = m = new MeshBuilder(); return m; }
        var placed = new List<(Vector2 p, string k)>(); int n = 0;
        foreach (var s in list)
        {
            var at = new Vector2(s.p[0], s.p[1]);
            if (!Nearest(at, out var dir, out float hw, out var foot)) continue;
            bool dup = false; float sep = s.k == "oneway" ? 30f : 6f;
            foreach (var (pp, kk) in placed) if (kk == s.k && (pp - at).sqrMagnitude < sep * sep) { dup = true; break; }
            if (dup) continue;
            placed.Add((at, s.k));
            var nrm = new Vector2(-dir.y, dir.x); var mb = T(at);
            switch (s.k)
            {
                case "crossing":
                    // the zebra: 0.5 m bars along the street, across the whole carriageway, 4 m wide
                    for (float x = -hw + 0.25f; x < hw - 0.2f; x += 1f)
                    {
                        var c = foot + nrm * (x + 0.25f);
                        Quad(mb, zebra, c, dir, 4f, nrm, 0.5f);
                    }
                    if (s.v == 0) { Sign(mb, "IP6", foot + nrm * (hw + 0.5f) + dir * 2.4f, -dir, 0f); Sign(mb, "IP6", foot - nrm * (hw + 0.5f) - dir * 2.4f, dir, 0f); }
                    break;
                case "give_way": Sign(mb, "P4", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "stop": Sign(mb, "P6", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "noentry": Sign(mb, "B2", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "mainroad": Sign(mb, "P2", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "noparking": Sign(mb, "B28", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "parking": Sign(mb, "IP11", foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "speed": Sign(mb, "B20a" + (int)s.v, foot + nrm * (hw + 0.6f), dir, 0f); break;
                case "oneway":
                {
                    float h = s.v * Mathf.Deg2Rad; var head = new Vector2(Mathf.Sin(h), Mathf.Cos(h));
                    if (Vector2.Dot(head, dir) < 0) dir = -dir; nrm = new Vector2(-dir.y, dir.x);
                    var right = new Vector2(dir.y, -dir.x); // drivers read it on their right, a few metres in
                    Sign(mb, "IP4b", foot + right * (hw + 0.6f) + dir * 6f, -dir, 0f);
                    break;
                }
            }
            n++;
        }
        foreach (var kv in tiles) kv.Value.Build($"Znacky_{kv.Key.x}_{kv.Key.y}", transform, false);
        Debug.Log($"[Značky] {n} značek a přechodů");
    }

    static Vector2Int Cell(Vector2 p) => new(Mathf.FloorToInt(p.x / 40f), Mathf.FloorToInt(p.y / 40f));

    bool Nearest(Vector2 p, out Vector2 dir, out float hw, out Vector2 foot)
    {
        dir = Vector2.up; hw = 3f; foot = p; float bd = 15f * 15f; bool ok = false; var c = Cell(p);
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            if (segs.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var (a, b, w) in l)
                {
                    var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    var q = a + ab * t; float d = (q - p).sqrMagnitude;
                    if (d < bd) { bd = d; dir = ab.normalized; hw = w; foot = q; ok = true; }
                }
        return ok;
    }

    // a flat marking on the carriageway: length along `along`, width along `across`
    static void Quad(MeshBuilder mb, Material m, Vector2 c, Vector2 along, float len, Vector2 across, float wid)
    {
        Vector3 P(Vector2 q) => new(q.x, MapBuilder.RoadY(q.x, q.y) + 0.012f, q.y);
        var a = along * len / 2f; var w = across * wid / 2f;
        mb.Poly(m, new[] { P(c - a - w), P(c + a - w), P(c + a + w), P(c - a + w) }, null, Vector3.up);
    }

    // a pole with a sign plate facing `facing` (the direction its reader looks along, i.e. the plate's front points -facing)
    void Sign(MeshBuilder mb, string code, Vector2 at, Vector2 facing, float _)
    {
        var g = new Vector3(at.x, MapBuilder.Height(at.x, at.y), at.y);
        mb.Box(pole, g + Vector3.up * 1.3f, new Vector3(0.06f, 2.6f, 0.06f));
        var f3 = new Vector3(facing.x, 0, facing.y).normalized; var r = Vector3.Cross(Vector3.up, f3) * 0.35f; var up = Vector3.up * 0.35f;
        var c = g + Vector3.up * 2.25f - f3 * 0.05f;
        if (!faces.TryGetValue(code, out var fm)) faces[code] = fm = Cutout(Face(code), code);
        // front, readable from where the driver comes: the face looks along -facing
        mb.Poly(fm, new[] { c - r - up, c - r + up, c + r + up, c + r - up }, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) }, -f3);
        var cb = c + f3 * 0.01f;
        mb.Poly(back, new[] { cb - r - up, cb - r + up, cb + r + up, cb + r - up }, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) }, f3);
    }

    static Material Cutout(Texture2D t, string key)
    {
        var m = Gfx.Mat(Color.white, 0.35f, t, "sign" + key);
        m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 2f);
        return m;
    }

    // ------------------------------------------------------------------ sign faces (128 px, alpha = plate shape)
    static readonly Color Red = new(0.8f, 0.08f, 0.1f), Blue = new(0.0f, 0.32f, 0.66f), White = new(0.96f, 0.96f, 0.95f),
        Black = new(0.06f, 0.06f, 0.06f), Yellow = new(0.98f, 0.78f, 0.05f), Grey = new(0.6f, 0.61f, 0.62f);

    static Texture2D Face(string code)
    {
        const int N = 128; var px = new Color[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
        void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < N && y < N) { c.a = 1; px[y * N + x] = c; } }
        void Fill(Func<float, float, bool> inside, Color c) { for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) if (inside((x + 0.5f) / N * 2 - 1, (y + 0.5f) / N * 2 - 1)) Set(x, y, c); }
        bool Circle(float x, float y, float r) => x * x + y * y < r * r;
        bool Tri(float x, float y, float s, bool down) { float yy = down ? -y : y; return yy > -s * 0.5f && yy < s * 0.9f && Mathf.Abs(x) < (s * 0.9f - yy) * 0.66f; }
        bool Oct(float x, float y, float s) => Mathf.Abs(x) < s && Mathf.Abs(y) < s && Mathf.Abs(x) + Mathf.Abs(y) < s * 1.42f;
        void Text(string t, int cx, int cy, int sc, Color c) // 5x7 bitmap capitals and digits
        {
            int w = t.Length * 6 * sc - sc, x0 = cx - w / 2, y0 = cy - 7 * sc / 2;
            for (int k = 0; k < t.Length; k++)
            {
                if (!Font.TryGetValue(t[k], out var rows)) continue;
                for (int ry = 0; ry < 7; ry++) for (int rx = 0; rx < 5; rx++)
                    if ((rows[ry] >> (4 - rx) & 1) == 1)
                        for (int sy = 0; sy < sc; sy++) for (int sx = 0; sx < sc; sx++) Set(x0 + k * 6 * sc + rx * sc + sx, y0 + (6 - ry) * sc + sy, c);
            }
        }
        void Line(float x0, float y0, float x1, float y1, float th, Color c)
        {
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                var ab = new Vector2(x1 - x0, y1 - y0); float t = Mathf.Clamp01(Vector2.Dot(new Vector2(u - x0, v - y0), ab) / ab.sqrMagnitude);
                if ((new Vector2(x0, y0) + ab * t - new Vector2(u, v)).sqrMagnitude < th * th) Set(x, y, c);
            }
        }
        if (code == "back") Fill((x, y) => Mathf.Abs(x) < 0.97f && Mathf.Abs(y) < 0.97f, Grey);
        else if (code == "IP6") // blue square, white triangle, a walker on a zebra
        {
            Fill((x, y) => Mathf.Abs(x) < 0.97f && Mathf.Abs(y) < 0.97f, Blue);
            Fill((x, y) => Tri(x, y + 0.12f, 0.85f, false), White);
            for (int k = -2; k <= 2; k++) Fill((x, y) => y > -0.48f && y < -0.36f && Mathf.Abs(x - k * 0.16f) < 0.05f, Black);
            Fill((x, y) => Circle(x - 0.02f, y - 0.42f, 0.07f), Black);
            Line(0f, 0.32f, -0.04f, 0.02f, 0.045f, Black); Line(-0.04f, 0.02f, -0.18f, -0.3f, 0.04f, Black); Line(-0.04f, 0.02f, 0.14f, -0.3f, 0.04f, Black);
            Line(0f, 0.26f, -0.17f, 0.08f, 0.035f, Black); Line(0f, 0.26f, 0.16f, 0.1f, 0.035f, Black);
        }
        else if (code == "P4") { Fill((x, y) => Tri(x, y - 0.25f, 1.05f, true), Red); Fill((x, y) => Tri(x, y - 0.25f, 0.78f, true), White); }
        else if (code == "P6") { Fill((x, y) => Oct(x, y, 0.95f), White); Fill((x, y) => Oct(x, y, 0.88f), Red); Text("STOP", 64, 64, 4, White); }
        else if (code == "P2") { Fill((x, y) => Mathf.Abs(x) + Mathf.Abs(y) < 0.98f, White); Fill((x, y) => Mathf.Abs(x) + Mathf.Abs(y) < 0.7f, Yellow); }
        else if (code == "B2") { Fill((x, y) => Circle(x, y, 0.97f), Red); Fill((x, y) => Mathf.Abs(x) < 0.68f && Mathf.Abs(y) < 0.16f, White); }
        else if (code == "B28") { Fill((x, y) => Circle(x, y, 0.97f), Red); Fill((x, y) => Circle(x, y, 0.78f), Blue); Line(-0.55f, -0.55f, 0.55f, 0.55f, 0.1f, Red); Line(-0.55f, 0.55f, 0.55f, -0.55f, 0.1f, Red); }
        else if (code == "IP11") { Fill((x, y) => Mathf.Abs(x) < 0.97f && Mathf.Abs(y) < 0.97f, Blue); Text("P", 64, 64, 11, White); }
        else if (code == "IP4b") { Fill((x, y) => Mathf.Abs(x) < 0.7f && Mathf.Abs(y) < 0.97f, Blue); Line(0, -0.7f, 0, 0.45f, 0.12f, White); Fill((x, y) => Tri(x * 0.8f, y - 0.45f, 0.45f, false), White); }
        else if (code.StartsWith("B20a")) { Fill((x, y) => Circle(x, y, 0.97f), Red); Fill((x, y) => Circle(x, y, 0.76f), White); Text(code.Substring(4), 64, 64, 6, Black); }
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
        t.SetPixels(px); t.Apply(true); return t;
    }

    static readonly Dictionary<char, int[]> Font = new()
    {
        ['S'] = new[] { 15, 16, 16, 14, 1, 1, 30 }, ['T'] = new[] { 31, 4, 4, 4, 4, 4, 4 }, ['O'] = new[] { 14, 17, 17, 17, 17, 17, 14 },
        ['P'] = new[] { 30, 17, 17, 30, 16, 16, 16 }, ['0'] = new[] { 14, 17, 19, 21, 25, 17, 14 }, ['1'] = new[] { 4, 12, 4, 4, 4, 4, 14 },
        ['2'] = new[] { 14, 17, 1, 2, 4, 8, 31 }, ['3'] = new[] { 31, 2, 4, 2, 1, 17, 14 }, ['4'] = new[] { 2, 6, 10, 18, 31, 2, 2 },
        ['5'] = new[] { 31, 16, 30, 1, 1, 17, 14 }, ['6'] = new[] { 6, 8, 16, 30, 17, 17, 14 }, ['7'] = new[] { 31, 1, 2, 4, 8, 8, 8 },
        ['8'] = new[] { 14, 17, 17, 14, 17, 17, 14 }, ['9'] = new[] { 14, 17, 17, 15, 1, 2, 12 },
    };
}
