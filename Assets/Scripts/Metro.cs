using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// The walk-down metro. Every real entrance (OSM) is an open stairwell cut into the pavement: down 6 m to a short passage,
// through it to the station's vestibule, down the 30° escalators (moving: right lane up, left lane down) to an island
// platform at the real station spot, ~25 m under the street, its axis along the line. Trains run both ways every couple of
// minutes; [E] at a train standing with open doors picks the destination (Transit's metro menu) and you get off on that
// station's platform. Transfer stations have one hall per line at different depths, linked by a "přestup" passage.
// Looks after photos of the real stations: line A's anodised aluminium roundels (a colour per station: Malostranská green,
// Muzeum brown...), line B's glass panels, line C's ceramic and stone; 81-71M trains on A and B, M1 on C.
// The long passages between the entrances and the vestibule are abstracted (a short fade), since real ones cross each other.
public class Metro : MonoBehaviour
{
    public static Metro I;
    public class Hall
    {
        public int station, line, other = -1; public string name, lineRef; public Color lineColor;
        public Vector3 o; public Quaternion rot; public float H, z0, z1; // platform origin, rise to the vestibule, escalator ends
        public readonly List<int> exits = new(); public readonly Train[] trains = new Train[2];
        public Vector3 W(float x, float y, float z) => o + rot * new Vector3(x, y, z);
        public Vector3 Local(Vector3 p) => Quaternion.Inverse(rot) * (p - o);
        public float Yaw => rot.eulerAngles.y;
    }
    public class Entrance { public Vector3 top, u; public float g; public int hall; public string name; }
    public static readonly List<Hall> Halls = new();
    public static readonly List<Entrance> Entrances = new();
    public const float HalfLen = 50f;
    const float Rise = 6f, Riser = 0.165f, Tread = 0.3f, Width = 3.2f, Half = 50f, Tan30 = 0.57735f, Cos30 = 0.866f;
    static float Run => Rise / Riser * Tread;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { I = null; Halls.Clear(); Entrances.Clear(); Under = false; }

    public static bool Under; // the player is below the street: no sun, dim sky light (GameClock)

    // inside a station's hall, escalator shaft or vestibule (some lie under the river: not a fall into the Vltava)
    public static bool InStation(Vector3 p)
    {
        foreach (var h in Halls)
        {
            var l = h.Local(p);
            if (Mathf.Abs(l.x) < 12f && l.z > -Half - 50f && l.z < h.z1 + 20f && l.y > -3f && l.y < h.H + 6f) return true;
        }
        foreach (var e in Entrances) if ((p - PassageDoor(e)).sqrMagnitude < 12f * 12f && p.y < e.g - 1f) return true;
        return false;
    }

    // ------------------------------------------------------------------ plan (before the surface is built: its holes)
    public static void Plan()
    {
        Halls.Clear(); Entrances.Clear();
        var M = MapBuilder.D.metro; if (M == null || M.stations == null) return;
        var ss = M.stations;
        Vector3 SP(int k) => new(ss[k].p[0], MapBuilder.Height(ss[k].p[0], ss[k].p[1]), ss[k].p[1]);
        // which entrances belong to which station
        var entSt = new int[M.entrances.Length];
        for (int e = 0; e < entSt.Length; e++)
        {
            entSt[e] = -1; float bd = 450f * 450f;
            for (int k = 0; k < ss.Length; k++) { float dx = ss[k].p[0] - M.entrances[e].p[0], dz = ss[k].p[1] - M.entrances[e].p[1]; if (dx * dx + dz * dz < bd) { bd = dx * dx + dz * dz; entSt[e] = k; } }
        }
        var first = new Dictionary<int, int>();
        for (int l = 0; l < M.order.Length; l++)
        {
            var L = M.order[l];
            for (int i = 0; i < L.s.Length; i++)
            {
                int st = L.s[i]; var p = SP(st);
                var a = SP(L.s[Mathf.Min(i + 1, L.s.Length - 1)]) - SP(L.s[Mathf.Max(i - 1, 0)]); a.y = 0; a.Normalize();
                // escalators at the end facing the entrances
                Vector3 ec = Vector3.zero; int ne = 0;
                for (int e = 0; e < entSt.Length; e++) if (entSt[e] == st) { ec += new Vector3(M.entrances[e].p[0], 0, M.entrances[e].p[1]); ne++; }
                if (ne > 0 && Vector3.Dot(ec / ne - new Vector3(p.x, 0, p.z), a) < 0) a = -a;
                bool second = first.ContainsKey(st);
                var h = new Hall { station = st, line = l, name = ss[st].n, lineRef = L.@ref, rot = Quaternion.LookRotation(a) };
                ColorUtility.TryParseHtmlString(L.color, out h.lineColor);
                h.o = new Vector3(p.x, p.y - M.depth - (second ? 12f : 0f), p.z);
                h.z0 = Half - 6f;
                // the vestibule sits ~7 m under the street where it ends up: iterate its position and rise
                float vest = p.y - 7f;
                for (int it = 0; it < 4; it++)
                {
                    h.H = vest - h.o.y; h.z1 = h.z0 + h.H / Tan30;
                    var a1 = h.W(0, 0, h.z1); var a2 = h.W(0, 0, h.z1 + 18f);
                    vest = Mathf.Min(p.y, Mathf.Min(MapBuilder.Height(a1.x, a1.z), MapBuilder.Height(a2.x, a2.z))) - 7f - (second ? 5f : 0f);
                }
                h.H = vest - h.o.y; h.z1 = h.z0 + h.H / Tan30;
                if (second) { var o = Halls[first[st]]; o.other = Halls.Count; h.other = first[st]; }
                else first[st] = Halls.Count;
                Halls.Add(h);
            }
        }
        // entrances: an open stairwell, pointing where it cuts no house, road or tram track (and preferably toward the station)
        var D = MapBuilder.D;
        for (int e = 0; e < M.entrances.Length; e++)
        {
            int st = entSt[e]; if (st < 0 || !first.TryGetValue(st, out int hall)) continue;
            var en = M.entrances[e]; var top = new Vector3(en.p[0], MapBuilder.Height(en.p[0], en.p[1]), en.p[1]);
            bool dup = false; foreach (var x in Entrances) if ((x.top - top).sqrMagnitude < 9f * 9f) dup = true;
            if (dup) continue;
            var near = new List<MapBuilder.Poly>();
            foreach (var b in D.buildings) if (b.p.Length >= 2 && Mathf.Abs(b.p[0] - top.x) < 120f && Mathf.Abs(b.p[1] - top.z) < 120f) near.Add(b);
            var toSt = new Vector3(ss[st].p[0] - top.x, 0, ss[st].p[1] - top.z).normalized;
            Vector3 best = toSt; float bs = float.MaxValue;
            for (int k = 0; k < 24; k++)
            {
                float ang = k * Mathf.PI * 2f / 24f; var u = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)); var v = new Vector3(-u.z, 0, u.x);
                float score = (1f - Vector3.Dot(u, toSt)) * 1.5f;
                for (float d = 0; d <= Run + 5f; d += 1.2f)
                    for (float w = -1.8f; w <= 1.81f; w += 1.8f)
                    {
                        var q = top + u * d + v * w;
                        foreach (var b in near) if (MapBuilder.InsidePoly(b.p, q.x, q.z)) { score += d <= Run ? 10f : 1f; break; }
                        if (d > Run) continue;
                        float dh = MapBuilder.Height(q.x, q.z) - top.y; score += Mathf.Abs(dh) * 0.6f; // a flat spot
                        if (NearLine(D.streets, q, true)) score += 4f;
                        if (NearLine(D.trams, q, false)) score += 6f;
                    }
                if (score < bs) { bs = score; best = u; }
            }
            string name = !string.IsNullOrEmpty(en.n) ? en.n : StreetName(top);
            Entrances.Add(new Entrance { top = top, u = best, g = top.y, hall = hall, name = name });
            Halls[hall].exits.Add(Entrances.Count - 1);
            MeshBuilder.Holes.Add(new MeshBuilder.Hole
            {
                c = new Vector2(top.x + best.x * Run * 0.5f, top.z + best.z * Run * 0.5f), u = new Vector2(best.x, best.z),
                hu = Run * 0.5f + 0.1f, hv = Width * 0.5f + 0.05f, y0 = top.y - 3f, y1 = top.y + 4f
            });
        }
    }

    static bool NearLine(MapBuilder.Poly[] lines, Vector3 q, bool carsOnly)
    {
        foreach (var s in lines)
        {
            if (carsOnly && s.foot) continue;
            float hw = carsOnly ? s.w * 0.5f + 0.3f : 1.8f; var p = s.p;
            if (p.Length < 4) continue;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                float ax = p[i], az = p[i + 1], bx = p[i + 2], bz = p[i + 3];
                if (Mathf.Min(ax, bx) - hw > q.x || Mathf.Max(ax, bx) + hw < q.x || Mathf.Min(az, bz) - hw > q.z || Mathf.Max(az, bz) + hw < q.z) continue;
                float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz; float t = l2 > 0 ? Mathf.Clamp01(((q.x - ax) * dx + (q.z - az) * dz) / l2) : 0;
                float ex = ax + dx * t - q.x, ez = az + dz * t - q.z;
                if (ex * ex + ez * ez < hw * hw) return true;
            }
        }
        return false;
    }

    static string StreetName(Vector3 q)
    {
        string best = "Výstup"; float bd = 70f * 70f;
        foreach (var s in MapBuilder.D.streets)
        {
            if (string.IsNullOrEmpty(s.n)) continue;
            for (int i = 0; i + 1 < s.p.Length; i += 2) { float dx = s.p[i] - q.x, dz = s.p[i + 1] - q.z, d = dx * dx + dz * dz; if (d < bd) { bd = d; best = s.n; } }
        }
        return best;
    }

    // ------------------------------------------------------------------ build
    static Material floor, ceil, dark, track, rail, steel, bronze, glow, lampGlow, granite, wallLight, black, edge, handrail, signBg;

    public static void Build(Transform root)
    {
        if (Halls.Count == 0) return;
        var rootGo = new GameObject("Metro"); rootGo.transform.SetParent(root, false);
        floor = Gfx.Mat(new Color(0.62f, 0.6f, 0.58f), 0.55f, Tex("floor", new Color(0.66f, 0.63f, 0.6f)), "metroFloor");
        granite = Gfx.Pbr("Setts", 0.15f, 1.5f, new Color(0.75f, 0.72f, 0.7f));
        ceil = Gfx.Mat(new Color(0.2f, 0.2f, 0.21f), 0.2f); dark = Gfx.Mat(new Color(0.07f, 0.07f, 0.075f), 0.1f);
        track = Gfx.Mat(new Color(0.16f, 0.15f, 0.14f), 0.1f); rail = Gfx.Mat(new Color(0.55f, 0.55f, 0.56f), 0.8f);
        steel = Gfx.Mat(new Color(0.6f, 0.61f, 0.63f), 0.75f); bronze = Gfx.Mat(new Color(0.45f, 0.32f, 0.18f), 0.65f);
        glow = Gfx.Emissive(new Color(1f, 0.97f, 0.9f), 1.6f); lampGlow = Gfx.Emissive(new Color(1f, 0.85f, 0.6f), 2.2f);
        wallLight = Gfx.Mat(new Color(0.84f, 0.82f, 0.78f), 0.3f, Tex("tile", new Color(0.86f, 0.84f, 0.8f)), "metroWall");
        black = Gfx.Mat(Color.black, 0f); edge = Gfx.Mat(new Color(0.85f, 0.75f, 0.2f), 0.3f);
        handrail = Gfx.Mat(new Color(0.05f, 0.05f, 0.05f), 0.5f); signBg = Gfx.Mat(new Color(0.08f, 0.1f, 0.16f), 0.3f);
        foreach (var h in Halls) BuildHall(rootGo.transform, h);
        var emb = new MeshBuilder();
        foreach (var e in Entrances) BuildEntrance(emb, e, rootGo.transform);
        emb.Build("MetroVstupy", rootGo.transform, true);
    }

    static Material Cladding(Hall h)
    {
        string n = h.name;
        if (h.lineRef == "A")
        {
            var c = n.Contains("Malostransk") ? new Color(0.3f, 0.5f, 0.28f) : n.Contains("Muzeum") ? new Color(0.5f, 0.32f, 0.18f)
                  : n.Contains("Staroměst") ? new Color(0.72f, 0.66f, 0.55f) : n.Contains("Můstek") ? new Color(0.78f, 0.62f, 0.3f)
                  : n.Contains("Míru") ? new Color(0.62f, 0.3f, 0.22f) : n.Contains("Jiřího") ? new Color(0.5f, 0.24f, 0.36f) : new Color(0.6f, 0.55f, 0.45f);
            return Gfx.Mat(Color.white, 0.7f, Tex("bubble", c), "metroA" + n);
        }
        if (h.lineRef == "B")
        {
            var c = n.Contains("Republiky") ? new Color(0.78f, 0.7f, 0.5f) : n.Contains("Národní") ? new Color(0.62f, 0.7f, 0.66f)
                  : n.Contains("Karlovo") ? new Color(0.8f, 0.62f, 0.42f) : n.Contains("Florenc") ? new Color(0.55f, 0.62f, 0.74f)
                  : n.Contains("Křižík") ? new Color(0.85f, 0.75f, 0.45f) : new Color(0.72f, 0.66f, 0.56f);
            return Gfx.Mat(Color.white, 0.85f, Tex("glass", c), "metroB" + n);
        }
        var cc = n.Contains("Pavlova") ? new Color(0.82f, 0.8f, 0.74f) : n.Contains("Hlavní") ? new Color(0.78f, 0.8f, 0.82f) : new Color(0.8f, 0.77f, 0.72f);
        return Gfx.Mat(Color.white, 0.45f, Tex("tile", cc), "metroC" + n);
    }

    static readonly Dictionary<string, Texture2D> texCache = new();
    // small procedural textures after the real claddings (no photo data needed)
    static Texture2D Tex(string kind, Color c)
    {
        string key = kind + ColorUtility.ToHtmlStringRGB(c);
        if (texCache.TryGetValue(key, out var t) && t) return t;
        const int N = 128; t = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        var px = new Color[N * N]; var lightDir = new Vector3(-0.4f, 0.6f, 0.7f).normalized;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color o;
                if (kind == "bubble") // two by two pressed roundels (line A), alternately convex and concave
                {
                    int cx = x / 64, cy = y / 64; float u = (x % 64 - 31.5f) / 26f, v = (y % 64 - 31.5f) / 26f, r2 = u * u + v * v;
                    bool concave = ((cx + cy) & 1) == 1;
                    if (r2 < 1f)
                    {
                        var nrm = new Vector3(u, v, Mathf.Sqrt(1 - r2)); if (concave) { nrm.x = -nrm.x; nrm.y = -nrm.y; }
                        float l = 0.55f + 0.6f * Mathf.Max(0, Vector3.Dot(nrm, lightDir)) + 0.35f * Mathf.Pow(Mathf.Max(0, Vector3.Dot(nrm, lightDir)), 12);
                        o = c * l;
                    }
                    else o = c * (x % 64 == 0 || y % 64 == 0 ? 0.45f : 0.8f);
                }
                else if (kind == "glass") // large glass panels with dark joints and a vertical sheen (line B)
                {
                    bool joint = x % 128 < 2 || y % 64 < 2; float sheen = 0.85f + 0.2f * Mathf.Sin((x + y * 0.3f) * 0.05f);
                    o = joint ? c * 0.35f : c * sheen;
                }
                else if (kind == "floor") // granite slabs 64 px with fine speckle
                {
                    float n = Mathf.PerlinNoise(x * 0.37f, y * 0.37f) * 0.15f + Random.value * 0.08f;
                    o = (x % 64 < 1 || y % 64 < 1) ? c * 0.6f : c * (0.9f + n);
                }
                else // ceramic tiles 16 px with grout (line C, passages)
                {
                    bool g = x % 16 < 1 || y % 16 < 1; o = g ? c * 0.7f : c * (0.95f + Random.value * 0.05f);
                }
                o.a = 1; px[y * N + x] = o;
            }
        t.SetPixels(px); t.Apply(true); texCache[key] = t;
        return t;
    }

    static void BuildHall(Transform root, Hall h)
    {
        var mb = new MeshBuilder(); var vis = new MeshBuilder();
        var clad = Cladding(h); var R = h.rot;
        void B(MeshBuilder m, Material mat, float x, float y, float z, float sx, float sy, float sz, float tile = 0f, bool bottom = false)
            => m.Box(mat, h.W(x, y, z), new Vector3(sx, sy, sz), R, tile, true, bottom);
        const float L = Half * 2f, PW = 9f, TW = 4.6f, CH = 6.2f; float xo = PW / 2 + TW; // outer wall
        // platform, its yellow edge strips, track beds and rails (the tracks run on into the tunnels)
        B(mb, floor, 0, -0.55f, 0, PW, 1.1f, L, 4f);
        foreach (float sx in new[] { -1f, 1f })
        {
            B(vis, edge, sx * (PW / 2 - 0.25f), 0.005f, 0, 0.3f, 0.02f, L);
            B(mb, track, sx * (PW / 2 + TW / 2), -1.4f, 0, TW, 0.6f, L + 90f);
            foreach (float rx in new[] { -0.72f, 0.72f }) B(vis, rail, sx * 6.0f + rx, -1.02f, 0, 0.08f, 0.16f, L + 90f);
            B(vis, rail, sx * (PW / 2 + TW - 0.4f), -0.6f, 0, 0.1f, 0.12f, L + 90f); // the third rail
            // outer walls clad to the vault, a dark plinth, the platform-side pylons between the vaults
            B(mb, clad, sx * (xo + 0.25f), (CH - 1.4f) / 2f, 0, 0.5f, CH + 2.0f, L, 1.2f); // up into the vault: no gap under it
            B(vis, dark, sx * (xo - 0.02f), -0.6f, 0, 0.05f, 1.0f, L);
            for (float z = -Half + 6f; z <= Half - 9f; z += 8f) B(mb, clad, sx * (PW / 2 - 0.7f), CH / 2 + 0.1f, z, 1.0f, CH + 0.2f, 2.4f, 1.2f);
            // track tunnels beyond the hall
            foreach (float ez in new[] { -1f, 1f })
            {
                float zc = ez * (Half + 22f);
                B(mb, dark, sx * (xo + 0.25f), 2f, zc, 0.5f, 6.2f, 44f);
                B(mb, dark, sx * (PW / 2 - 0.25f), 2f, zc, 0.5f, 6.2f, 44f);
                B(mb, dark, sx * (PW / 2 + TW / 2), 4.0f, zc, TW + 1f, 0.4f, 44f, 0, true);
                B(mb, black, sx * (PW / 2 + TW / 2), 1.5f, ez * (Half + 44f), TW + 1f, 6f, 0.4f);
                B(mb, dark, sx * (PW / 2 + TW / 2), (CH + 3.8f) / 2f, ez * Half, TW + 0.5f, CH - 3.8f, 0.5f); // above the portal
            }
        }
        // vault ceiling, light strips, station name boards
        B(mb, ceil, 0, CH + 0.2f, 0, xo * 2 + 1f, 0.4f, L, 0, true);
        foreach (float sx in new[] { -2.2f, 2.2f, -xo + 1.5f, xo - 1.5f }) B(vis, glow, sx, CH - 0.05f, 0, 0.35f, 0.1f, L - 4f);
        // end walls: the far one whole but the track portals; the escalator one opens for the shaft
        B(mb, clad, 0, CH / 2 - 0.55f, -Half, PW, CH + 1.1f, 0.5f, 1.2f);
        B(mb, clad, -(PW / 2 + 2.5f) / 2f - 0.0f, CH / 2 - 0.55f, Half, PW / 2 - 2.5f, CH + 1.1f, 0.5f, 1.2f); // left of the shaft
        foreach (float sx in new[] { -1f, 1f }) B(mb, clad, sx * (2.5f + (PW / 2 - 2.5f) / 2f), CH / 2 - 0.55f, Half, PW / 2 - 2.5f, CH + 1.1f, 0.5f, 1.2f);
        B(mb, clad, 0, CH + 1.6f, Half, 5.2f, 3f, 0.5f, 1.2f);
        // escalators: one 30° ramp (collider) with three lanes of steps, balustrades and the bronze lamp posts of Prague's metro
        var sr = R * Quaternion.Euler(-30f, 0, 0);
        float len = (h.z1 - h.z0) / Cos30, sStart = (Half - h.z0) / Cos30;
        Vector3 S(float x, float perp, float s) => h.W(0, 0, h.z0) + sr * new Vector3(x, perp, s);
        mb.Box(steel, S(0, -0.2f, len / 2), new Vector3(4.6f, 0.4f, len + 0.3f), sr);
        for (float s = 0.3f; s < len - 0.2f; s += 0.46f)
            foreach (float lx in new[] { -1.5f, 0f, 1.5f }) vis.Box(dark, S(lx, 0.02f, s), new Vector3(1.0f, 0.04f, 0.22f), sr);
        foreach (float bx in new[] { -2.25f, -0.75f, 0.75f, 2.25f })
        {
            vis.Box(bronze, S(bx, 0.5f, len / 2), new Vector3(0.18f, 1.0f, len), sr);
            vis.Box(handrail, S(bx, 1.02f, len / 2), new Vector3(0.12f, 0.08f, len), sr);
        }
        for (float s = 3f; s < len - 2f; s += 4.5f)
            foreach (float bx in new[] { -0.75f, 0.75f })
            {
                var p = S(bx, 1.05f, s); vis.Box(bronze, p + Vector3.up * 0.45f, new Vector3(0.06f, 0.9f, 0.06f), R);
                vis.Box(lampGlow, p + Vector3.up * 0.95f, new Vector3(0.22f, 0.3f, 0.22f), R);
            }
        // the shaft: walls and ceiling from the hall's end wall up to the vestibule
        float sl = len - sStart, sm = (sStart + len) / 2;
        foreach (float sx in new[] { -1f, 1f }) mb.Box(clad, S(sx * 2.55f, 2.2f, sm), new Vector3(0.3f, 5.0f, sl + 1f), sr, 1.2f);
        mb.Box(ceil, S(0, 4.3f, sm - 1.4f), new Vector3(5.4f, 0.3f, sl - 2.4f), sr, 0, true, true);
        for (float s = sStart + 2f; s < len - 3f; s += 6f) vis.Box(glow, S(0, 4.12f, s), new Vector3(1.6f, 0.05f, 0.5f), sr);
        // vestibule: granite floor, tiled walls, exits on the far wall
        float vz = h.z1, VL = 18f, VW = 16f, VH = 4.5f, y = h.H;
        B(mb, granite, 0, y - 0.3f, vz + VL / 2, VW, 0.6f, VL, 2f);
        B(mb, ceil, 0, y + VH + 0.2f, vz + VL / 2 - 1f, VW, 0.4f, VL + 2f, 0, true);
        foreach (float sx in new[] { -1f, 1f })
        {
            B(mb, wallLight, sx * (VW / 2 + 0.2f), y + VH / 2, vz + VL / 2 - 1f, 0.4f, VH + 0.6f, VL + 2f, 1.2f);
            B(mb, wallLight, sx * (2.75f + (VW / 2 - 2.75f) / 2f), y + VH / 2, vz - 1.2f, VW / 2 - 2.75f, VH + 0.6f, 0.4f, 1.2f);
            B(vis, glow, sx * 3.5f, y + VH - 0.05f, vz + VL / 2, 0.4f, 0.1f, VL - 3f);
        }
        B(mb, wallLight, 0, y + VH / 2, vz + VL + 0.2f, VW + 0.8f, VH + 0.6f, 0.4f, 1.2f);
        int ne = Mathf.Min(h.exits.Count, 6);
        for (int i = 0; i < ne; i++)
        {
            float ex = ExitX(i, ne);
            B(vis, black, ex, y + 1.25f, vz + VL - 0.02f, 1.9f, 2.5f, 0.1f);
            B(vis, signBg, ex, y + 3.05f, vz + VL - 0.1f, 2.4f, 0.6f, 0.1f);
            Label(root, h.W(ex, y + 3.05f, vz + VL - 0.17f), R, "VÝSTUP\n" + Entrances[h.exits[i]].name, 2.0f, Color.white, 2.3f);
        }
        // validators (yellow posts) by the escalator head
        for (int i = -2; i <= 2; i++) if (i != 0) B(vis, edge, i * 1.6f, y + 0.6f, vz + 3f, 0.25f, 1.2f, 0.25f);
        // station names: on the outer walls along the platform, over the escalators, in the vestibule
        foreach (float sx in new[] { -1f, 1f })
            for (float z = -Half + 12f; z < Half - 8f; z += 22f)
            {
                B(vis, signBg, sx * (xo - 0.03f), 3.3f, z, 0.06f, 0.9f, 5.2f);
                Label(root, h.W(sx * (xo - 0.08f), 3.3f, z), R * Quaternion.Euler(0, sx * 90f, 0), h.name.ToUpperInvariant(), 3.2f, Color.white, 5f);
            }
        B(vis, signBg, 0, y + 3.4f, vz + 6f, 6f, 0.7f, 0.1f);
        Label(root, h.W(0, y + 3.4f, vz + 5.94f), R * Quaternion.Euler(0, 180f, 0), $"{h.name.ToUpperInvariant()}  ·  {h.lineRef}", 3f, h.lineColor, 6f);
        Label(root, h.W(0, y + 3.4f, vz + 6.06f), R, $"{h.lineRef}  ·  NÁSTUPIŠTĚ", 3f, h.lineColor, 6f);
        if (h.other >= 0)
        {
            B(vis, black, 0, 1.25f, -Half + 0.3f, 2.2f, 2.5f, 0.1f);
            B(vis, signBg, 0, 3.0f, -Half + 0.35f, 3.2f, 0.6f, 0.1f);
            Label(root, h.W(0, 3.0f, -Half + 0.42f), R * Quaternion.Euler(0, 180f, 0), "PŘESTUP NA LINKU " + Halls[h.other].lineRef, 2.4f, Halls[h.other].lineColor, 3.1f);
        }
        var go = mb.Build($"Metro_{h.name}_{h.lineRef}", root, true, false);
        vis.Build($"MetroDetail_{h.name}_{h.lineRef}", go.transform, false, false);
        // real lights: platform, escalator, vestibule (no shadows: cheap)
        foreach (var lp in new[] { h.W(0, CH - 0.8f, -36f), h.W(0, CH - 0.8f, -12f), h.W(0, CH - 0.8f, 12f), h.W(0, CH - 0.8f, 36f), S(0, 3.5f, len * 0.5f), h.W(0, y + VH - 0.6f, vz + VL / 2) })
        {
            var l = new GameObject("Svetlo").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.position = lp;
            l.type = LightType.Point; l.range = 40f; l.intensity = 4f; l.color = new Color(1f, 0.95f, 0.86f); l.shadows = LightShadows.None;
        }
        for (int k = 0; k < 2; k++) h.trains[k] = Train.Create(go.transform, h, k == 0 ? 1 : -1);
    }

    static float ExitX(int i, int n) => n <= 1 ? 0f : Mathf.Lerp(-6f, 6f, i / (float)(n - 1));

    static void Label(Transform root, Vector3 at, Quaternion facing, string text, float size, Color c, float width)
    {
        var t = new GameObject("Napis").AddComponent<TextMeshPro>(); t.transform.SetParent(root, false);
        t.transform.SetPositionAndRotation(at, facing); // (facing = the direction the reader looks)
        t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold;
        t.rectTransform.sizeDelta = new Vector2(width, 1.2f); t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
    }

    static void BuildEntrance(MeshBuilder mb, Entrance e, Transform root)
    {
        var u = e.u; var v = new Vector3(-u.z, 0, u.x); var rot = Quaternion.LookRotation(u); float g = e.g;
        Vector3 P(float side, float y, float d) => e.top + v * side + u * d + Vector3.up * (y - g);
        int n = Mathf.RoundToInt(Rise / Riser);
        for (int i = 0; i < n; i++)
        {
            float top = g - (i + 1) * Riser;
            mb.Box(granite, P(0, top - 0.25f, (i + 0.5f) * Tread), new Vector3(Width, 0.5f, Tread + 0.01f), rot, 1.5f);
        }
        float hiAlong = g; for (float d = 0; d <= Run; d += 1f) foreach (float s in new[] { -1.9f, 1.9f }) { var q = P(s, g, d); hiAlong = Mathf.Max(hiAlong, MapBuilder.Height(q.x, q.z) + 0.15f); }
        float wallTop = hiAlong + 0.9f, bottom = g - Rise - 0.6f;
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Box(wallLight, P(s * (Width / 2 + 0.15f), (wallTop + bottom) / 2, Run / 2 + 2.5f), new Vector3(0.3f, wallTop - bottom, Run + 5.2f), rot, 1.2f);
            mb.Box(steel, P(s * (Width / 2 - 0.05f), wallTop - 0.85f - Rise / 2, Run / 2), new Vector3(0.06f, 0.06f, Run / Cos30 * 0.98f), rot * Quaternion.Euler(-Mathf.Atan2(Rise, Run) * Mathf.Rad2Deg, 0, 0)); // handrail
            mb.Box(steel, P(s * (Width / 2 + 0.15f), wallTop + 0.45f, Run / 2), new Vector3(0.06f, 0.06f, Run), rot); // railing on the parapet
            for (float d = 0.5f; d <= Run; d += 2f) mb.Box(steel, P(s * (Width / 2 + 0.15f), wallTop + 0.22f, d), new Vector3(0.05f, 0.45f, 0.05f), rot);
        }
        // the far end: a wall up over the passage, a parapet on the street
        mb.Box(wallLight, P(0, (wallTop + g - 3.2f) / 2, Run + 0.15f), new Vector3(Width + 0.6f, wallTop - (g - 3.2f), 0.3f), rot, 1.2f);
        // the passage at the bottom: floor, ceiling, lights, and the doorway on to the station (a fade)
        float fy = g - Rise;
        mb.Box(granite, P(0, fy - 0.25f, Run + 2.5f), new Vector3(Width + 0.4f, 0.5f, 5.2f), rot, 1.5f);
        mb.Box(ceil, P(0, fy + 3.1f, Run + 2.6f), new Vector3(Width + 0.6f, 0.3f, 5f), rot, 0, true, true);
        mb.Box(glow, P(0, fy + 2.93f, Run + 2.6f), new Vector3(0.4f, 0.05f, 4f), rot);
        mb.Box(black, P(0, fy + 1.3f, Run + 5.1f), new Vector3(Width, 2.6f, 0.2f), rot);
        mb.Box(wallLight, P(0, fy + 2.85f, Run + 5.15f), new Vector3(Width + 0.6f, 0.5f, 0.3f), rot);
        var h = Halls[e.hall];
        mb.Box(signBg, P(0, fy + 2.5f, Run + 4.9f), new Vector3(2.8f, 0.5f, 0.06f), rot);
        Label(root, P(0, fy + 2.5f, Run + 4.86f), rot * Quaternion.Euler(0, 180f, 0), "METRO " + h.lineRef + " · " + h.name.ToUpperInvariant(), 1.6f, Color.white, 2.8f);
        // the pylon with the "M" by the top of the stairs, in the line colour
        var mp = P(Width / 2 + 0.8f, g, -0.6f); mp.y = MapBuilder.Height(mp.x, mp.z);
        mb.Box(steel, mp + Vector3.up * 1.6f, new Vector3(0.18f, 3.2f, 0.18f), rot);
        mb.Box(Gfx.Emissive(h.lineColor, 1.6f), mp + Vector3.up * 3.55f, new Vector3(0.75f, 0.75f, 0.2f), rot);
        Label(root, mp + Vector3.up * 3.55f - u * 0.11f, rot, "M", 6f, Color.white, 0.8f);
        Label(root, mp + Vector3.up * 3.55f + u * 0.11f, rot * Quaternion.Euler(0, 180f, 0), "M", 6f, Color.white, 0.8f);
    }

    // ------------------------------------------------------------------ runtime
    float cooldown, farOut; bool busy; Renderer river;
    readonly List<(HumanMotion m, Hall h)> crowd = new(); Hall crowdHall;

    void Awake() => I = this;

    Vector3 EntranceBottom(Entrance e) => e.top + e.u * (Run + 1.2f) + Vector3.up * (-Rise + 0.15f);
    public static Vector3 PassageDoor(Entrance e) => e.top + e.u * (Run + 4.5f) + Vector3.up * (-Rise + 1f);

    void Update()
    {
        var gm = GameManager.I; if (gm == null || gm.player == null) return;
        var pp = gm.player.transform.position;
        Under = gm.player.gameObject.activeSelf && !gm.currentCar && (pp.y < MapBuilder.Height(pp.x, pp.z) - 2.5f || InStation(pp));
        if (!river) { var w = GameObject.Find("Vltava"); if (w) river = w.GetComponent<Renderer>(); }
        if (river && river.enabled == Under) river.enabled = !Under; // the river's water plane would cut through a vestibule by the bank
        var cam = Camera.main;
        if (cam) { if (farOut <= 0) farOut = cam.farClipPlane; float want = Under ? 220f : farOut; if (cam.farClipPlane != want) cam.farClipPlane = want; } // the city above needn't draw
        foreach (var h in Halls) foreach (var t in h.trains) if (t) t.Tick(Time.deltaTime);
        UpdateCrowd(pp);
        if (!Under || busy || gm.State != GameState.Playing) return;
        if ((cooldown -= Time.deltaTime) > 0) return;
        // moving escalators: the right lane carries you up, the left one down
        var cc = gm.player.GetComponent<CharacterController>();
        foreach (var h in Halls)
        {
            var l = h.Local(pp);
            if (l.z < h.z0 - 0.5f || l.z > h.z1 + 0.5f || Mathf.Abs(l.x) > 2.3f || Mathf.Abs(l.y - (l.z - h.z0) * Tan30) > 1.2f) continue;
            float dir = l.x > 0.75f ? 1f : l.x < -0.75f ? -1f : 0f;
            if (dir != 0 && cc && cc.enabled) cc.Move(h.rot * Quaternion.Euler(-30f, 0, 0) * Vector3.forward * (dir * 0.9f * Time.deltaTime));
        }
        // doorways
        foreach (var e in Entrances)
            if ((pp - PassageDoor(e)).sqrMagnitude < 1.4f * 1.4f) { var h = Halls[e.hall]; Go(h.W(0, h.H + 0.15f, h.z1 + 13.5f), h.Yaw + 180f, h.name.ToUpperInvariant()); return; }
        foreach (var h in Halls)
        {
            var l = h.Local(pp);
            int ne = Mathf.Min(h.exits.Count, 6);
            if (Mathf.Abs(l.y - h.H) < 2f && l.z > h.z1 + 17f)
                for (int i = 0; i < ne; i++)
                    if (Mathf.Abs(l.x - ExitX(i, ne)) < 0.9f)
                    {
                        var e = Entrances[h.exits[i]];
                        Go(EntranceBottom(e), Quaternion.LookRotation(-e.u).eulerAngles.y + 180f, e.name.ToUpperInvariant()); return;
                    }
            if (h.other >= 0 && Mathf.Abs(l.y) < 2f && l.z < -Half + 1.2f && Mathf.Abs(l.x) < 1.1f)
            {
                var o = Halls[h.other]; Go(o.W(0, 0.15f, -Half + 4f), o.Yaw, "PŘESTUP · LINKA " + o.lineRef); return;
            }
        }
    }

    void Go(Vector3 at, float yaw, string toast) => StartCoroutine(GoRoutine(at, yaw, toast));

    IEnumerator GoRoutine(Vector3 at, float yaw, string toast)
    {
        var gm = GameManager.I; busy = true;
        yield return gm.hud.FadeTo(1f, 0.25f);
        gm.player.Teleport(at, yaw);
        yield return null;
        gm.hud.Toast(toast, Palette.UILight, 1.6f);
        yield return gm.hud.FadeTo(0f, 0.3f);
        busy = false; cooldown = 1.5f;
    }

    // [E] at a standing train with open doors (GameManager -> Transit.Interact asks first)
    public bool AtTrain(Vector3 pp, out Hall hall)
    {
        hall = null; if (!Under) return false;
        foreach (var h in Halls)
        {
            var l = h.Local(pp); if (Mathf.Abs(l.y) > 1.5f || Mathf.Abs(l.z) > Half) continue;
            foreach (var t in h.trains)
                if (t && t.DoorsOpen && Mathf.Abs(Mathf.Abs(l.x) - 4.5f) < 1.6f && Mathf.Sign(l.x) == t.side) { hall = h; return true; }
        }
        return false;
    }

    // where a ride ends: on the destination station's platform of that line
    public bool Platform(int station, int line, out Vector3 at, out float yaw)
    {
        foreach (var h in Halls)
            if (h.station == station && h.line == line) { at = h.W(0, 0.15f, -10f); yaw = h.Yaw; return true; }
        at = default; yaw = 0; return false;
    }

    // a few people waiting on the platform you are on
    void UpdateCrowd(Vector3 pp)
    {
        Hall here = null;
        if (Under) foreach (var h in Halls) { var l = h.Local(pp); if (Mathf.Abs(l.x) < 12f && l.z > -Half - 5 && l.z < h.z1 + 20f && l.y > -3f && l.y < h.H + 6f) { here = h; break; } }
        if (here != crowdHall)
        {
            foreach (var (m, _) in crowd) if (m) Destroy(m.gameObject);
            crowd.Clear(); crowdHall = here;
            if (here != null && People.Available)
            {
                var rng = new System.Random(here.station * 7 + here.line);
                for (int i = 0; i < 14; i++)
                {
                    string kind = rng.NextDouble() < 0.2 ? "senior" : rng.NextDouble() < 0.2 ? "teen" : "adult";
                    var m = People.Spawn(transform, rng, kind); if (!m) continue;
                    float z = (float)(rng.NextDouble() * 80 - 40), x = (float)(rng.NextDouble() * 6 - 3);
                    if (z > here.z0 - 3f && Mathf.Abs(x) < 2.6f) x = 3.4f * Mathf.Sign(x + 0.01f);
                    m.transform.SetPositionAndRotation(here.W(x, 0f, z), here.rot * Quaternion.Euler(0, rng.Next(4) * 90f + 90f, 0));
                    crowd.Add((m, here));
                }
            }
        }
        foreach (var (m, _) in crowd) if (m) m.Tick(Vector3.zero, Time.deltaTime);
    }
}

// A metro train on one track of a hall: comes out of the tunnel, stands 25 s with open doors, leaves; every 1.5-3 min.
// 81-71M (lines A, B): white body, black window band, dark front mask; M1 (line C): white with a blue-grey skirt.
public class Train : MonoBehaviour
{
    public float side; Metro.Hall h; float z, wait, state; // 0 away, 1 arriving, 2 standing, 3 leaving
    readonly System.Random rng = new();
    Transform doors; float dwell;
    public bool DoorsOpen => state == 2 && dwell > 2f;
    const float Len = 96f, Speed = 16f;

    public static Train Create(Transform parent, Metro.Hall h, float side)
    {
        var go = new GameObject("Souprava"); go.transform.SetParent(parent, false);
        var t = go.AddComponent<Train>(); t.h = h; t.side = side; t.wait = 10f + (float)t.rng.NextDouble() * 60f;
        bool m1 = h.lineRef == "C";
        var body = Gfx.Mat(new Color(0.9f, 0.9f, 0.88f), 0.6f); var band = Gfx.Mat(new Color(0.05f, 0.06f, 0.07f), 0.85f);
        var skirt = Gfx.Mat(m1 ? new Color(0.2f, 0.32f, 0.5f) : new Color(0.35f, 0.36f, 0.38f), 0.4f);
        var door = Gfx.Mat(m1 ? new Color(0.2f, 0.32f, 0.5f) : new Color(0.7f, 0.71f, 0.72f), 0.6f);
        var lit = Gfx.Emissive(new Color(1f, 0.97f, 0.9f), 1.2f); var head = Gfx.Emissive(new Color(1f, 1f, 0.92f), 3f);
        var mb = new MeshBuilder(); var dm = new MeshBuilder();
        const int Cars = 5; float cl = Len / Cars;
        for (int c = 0; c < Cars; c++)
        {
            float zc = -Len / 2 + cl * (c + 0.5f);
            mb.Box(body, new Vector3(0, 1.75f, zc), new Vector3(2.9f, 2.5f, cl - 0.6f), Quaternion.identity);
            mb.Box(skirt, new Vector3(0, 0.55f, zc), new Vector3(2.6f, 0.5f, cl - 0.8f), Quaternion.identity);
            mb.Box(band, new Vector3(0, 2.05f, zc), new Vector3(2.94f, 0.9f, cl - 1.2f), Quaternion.identity);
            mb.Box(lit, new Vector3(0, 2.05f, zc), new Vector3(2.7f, 0.8f, cl - 1.4f), Quaternion.identity); // the lit interior seen through
            for (int d = 0; d < 4; d++)
            {
                float dz = zc - cl / 2 + cl * (d + 0.5f) / 4f;
                dm.Box(door, new Vector3(-1.47f, 1.55f, dz), new Vector3(0.06f, 2.0f, 1.3f), Quaternion.identity);
                dm.Box(door, new Vector3(1.47f, 1.55f, dz), new Vector3(0.06f, 2.0f, 1.3f), Quaternion.identity);
            }
        }
        foreach (float e in new[] { -1f, 1f }) // fronts: dark mask, headlights
        {
            mb.Box(band, new Vector3(0, 1.85f, e * (Len / 2 - 0.25f)), new Vector3(2.68f, 1.6f, 0.15f), Quaternion.identity);
            foreach (float hx in new[] { -0.9f, 0.9f }) mb.Box(head, new Vector3(hx, 0.95f, e * (Len / 2 - 0.2f)), new Vector3(0.25f, 0.18f, 0.12f), Quaternion.identity);
        }
        mb.Build("Vozy", go.transform, false);
        t.doors = dm.Build("Dvere", go.transform, false).transform;
        go.SetActive(false);
        return t;
    }

    public void Tick(float dt)
    {
        float dir = side; // right-hand running: the +x track goes +z
        if (state == 0) { if ((wait -= dt) > 0) return; state = 1; z = -dir * (Metro.HalfLen + Len / 2 + 30f); gameObject.SetActive(true); }
        if (state == 1)
        {
            float left = Mathf.Abs(z); float v = Mathf.Clamp(left * 0.45f, 1.2f, Speed);
            z += dir * v * dt; if (Mathf.Sign(z) == dir || Mathf.Abs(z) < 0.05f) { z = 0; state = 2; dwell = 0; }
        }
        else if (state == 2)
        {
            dwell += dt;
            if (dwell > 27f) state = 3;
        }
        else if (state == 3)
        {
            float v = Mathf.Min(Speed, 1f + Mathf.Abs(z) * 0.5f); z += dir * v * dt;
            if (Mathf.Abs(z) > Metro.HalfLen + Len / 2 + 30f) { state = 0; wait = 90f + (float)rng.NextDouble() * 90f; gameObject.SetActive(false); }
        }
        float open = state == 2 ? Mathf.Clamp01(Mathf.Min(dwell - 1f, 26f - dwell)) : 0f;
        doors.localPosition = new Vector3(0, 0, open * 0.65f); // the leaves slide along the body
        transform.SetPositionAndRotation(h.W(side * 6.0f, -1.1f, z), h.rot * Quaternion.Euler(0, dir > 0 ? 0 : 180f, 0));
    }
}
