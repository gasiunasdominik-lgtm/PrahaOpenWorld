using System;
using System.Collections.Generic;
using UnityEngine;

// Builds real Prague from Resources/PrahaMap.json (made by Tools/fetch_map.py):
// terrain from IPR contours, streets/squares/parks from the Metropolitan Plan, buildings, trams
// and metro from OpenStreetMap. 1 unit = 1 m, origin = middle of Charles Bridge, y 0 = Vltava level.
public static class MapBuilder
{
    [Serializable] public class Ring { public float[] p; }
    [Serializable] public class Poly { public string n, k; public float w; public bool foot; public float h; public float[] p; public Ring[] holes; }
    [Serializable] public class Terrain { public float x0, z0, cell; public int w, h; public string dm, lu; }
    [Serializable] public class Line { public string @ref, color; public int[] s; }
    [Serializable] public class Metro { public float depth; public Poly[] stations, entrances; public Line[] order; }
    [Serializable] public class Data { public string credits; public Terrain terrain; public Poly[] streets, squares, parks, districts, trams, buildings, places, signals, tramStops; public Metro metro; }

    public static Data D;
    public static readonly List<float[]> StreetY = new(); // walkable height per street vertex (bridges included)
    static float[] hm; static byte[] lu;
    public const float WaterY = -0.6f;
    const int Chunk = 64; // terrain cells per mesh tile

    public static float Height(float x, float z)
    {
        var t = D.terrain;
        float fx = Mathf.Clamp((x - t.x0) / t.cell, 0, t.w - 1.001f), fz = Mathf.Clamp((z - t.z0) / t.cell, 0, t.h - 1.001f);
        int i = (int)fx, j = (int)fz; float u = fx - i, v = fz - j;
        float a = hm[j * t.w + i], b = hm[j * t.w + i + 1], c = hm[(j + 1) * t.w + i], d = hm[(j + 1) * t.w + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    public static string District(Vector3 p)
    {
        if (D == null) return "Praha";
        if (Height(p.x, p.z) < 0 && p.y < 3) return "Vltava";
        foreach (var d in D.districts) if (Inside(d.p, p.x, p.z)) return d.n;
        return "Praha";
    }

    static bool Inside(float[] r, float x, float z)
    {
        bool c = false; int n = r.Length / 2;
        for (int i = 0, k = n - 1; i < n; k = i++)
            if ((r[2 * i + 1] > z) != (r[2 * k + 1] > z) &&
                x < (r[2 * k] - r[2 * i]) * (z - r[2 * i + 1]) / (r[2 * k + 1] - r[2 * i + 1]) + r[2 * i]) c = !c;
        return c;
    }

    public static void Load()
    {
        if (D != null) return;
        D = JsonUtility.FromJson<Data>(Resources.Load<TextAsset>("PrahaMap").text);
        var raw = Convert.FromBase64String(D.terrain.dm);
        hm = new float[raw.Length / 2];
        for (int i = 0; i < hm.Length; i++) hm[i] = BitConverter.ToInt16(raw, i * 2) / 10f;
        lu = Convert.FromBase64String(D.terrain.lu);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { D = null; hm = null; lu = null; StreetY.Clear(); Ipr.Clear(); } // domain reload is off

    public static void Build(Transform root, List<(Vector3 pos, float yaw)> parking)
    {
        Load();
        var mGround = Gfx.Mat(Color.Lerp(Palette.Cobble, Palette.Plinth, 0.5f), 0.1f, Gfx.CobbleTex);
        var mPark = Gfx.Mat(Palette.Grass, 0.05f);
        var mSquare = Gfx.Mat(Color.Lerp(Palette.Cobble, Palette.Sandstone, 0.35f), 0.1f, Gfx.CobbleTex);
        var mBank = Gfx.Mat(Palette.GothicStone, 0.08f);
        BuildTerrain(root, new[] { mGround, mBank, mPark, mSquare });

        var water = new MeshBuilder();
        var t = D.terrain;
        water.Ground(Gfx.Mat(Palette.River, 0.88f), t.x0, t.z0, t.x0 + t.w * t.cell, t.z0 + t.h * t.cell, WaterY, 10, 10);
        water.Build("Vltava", root, false, false);

        var asphalt = Gfx.Mat(Palette.Asphalt, 0.18f);
        var cobble = Gfx.Mat(Palette.CobbleDark, 0.1f, Gfx.CobbleTex);
        var stone = Gfx.Mat(Palette.GothicStone, 0.08f);
        var rail = Gfx.Mat(Color.Lerp(Palette.Asphalt, Palette.Slate, 0.6f), 0.5f);
        var streets = new MeshBuilder();
        int k = 0;
        StreetY.Clear();
        foreach (var s in D.streets)
        {
            StreetY.Add(Ribbon(streets, s.p, s.w, 0.12f, s.foot ? cobble : asphalt, stone));
            // parked cars every few streets, on the kerb side of drivable ones
            if (!s.foot && s.w >= 7.5f && s.p.Length >= 4 && (k++ % 9) == 0)
            {
                var a = new Vector3(s.p[0], 0, s.p[1]); var b = new Vector3(s.p[2], 0, s.p[3]);
                var dir = (b - a).normalized; var mid = (a + b) / 2 + Vector3.Cross(Vector3.up, dir) * (s.w / 2 - 1.4f);
                if ((b - a).magnitude > 20 && Height(mid.x, mid.z) > 0.5f)
                    parking.Add((new Vector3(mid.x, Height(mid.x, mid.z) + 0.6f, mid.z), Quaternion.LookRotation(dir).eulerAngles.y));
            }
        }
        foreach (var tr in D.trams) Ribbon(streets, tr.p, 1.6f, 0.16f, rail, null);
        streets.Build("Ulice", root, true, false);

        // squares are areas, not lines: give pedestrians a few invisible paths across each one
        var walk = new List<Poly>(D.streets);
        foreach (var sq in D.squares)
        {
            int n = sq.p.Length / 2; if (n < 4) continue;
            for (int i = 0; i < n / 2; i += Mathf.Max(1, n / 8))
            {
                int j = (i + n / 2) % n;
                var line = new[] { sq.p[2 * i], sq.p[2 * i + 1], sq.p[2 * j], sq.p[2 * j + 1] };
                walk.Add(new Poly { w = 4f, foot = true, p = line });
                StreetY.Add(new[] { Height(line[0], line[1]), Height(line[2], line[3]) });
            }
        }
        D.streets = walk.ToArray();

        BuildBuildings(root);
        BuildMetroEntrances(root);
        BuildTrees(root);
        BuildBounds(root);
    }

    static void BuildTerrain(Transform root, Material[] mats)
    {
        var t = D.terrain;
        for (int cj = 0; cj < t.h - 1; cj += Chunk)
        for (int ci = 0; ci < t.w - 1; ci += Chunk)
        {
            var mb = new MeshBuilder();
            for (int j = cj; j < Mathf.Min(cj + Chunk, t.h - 1); j++)
            for (int i = ci; i < Mathf.Min(ci + Chunk, t.w - 1); i++)
            {
                float x = t.x0 + i * t.cell, z = t.z0 + j * t.cell;
                var m = mats[lu[j * t.w + i]];
                Vector3 V(int di, int dj) => new(x + di * t.cell, hm[(j + dj) * t.w + i + di], z + dj * t.cell);
                mb.Poly(m, new[] { V(0, 0), V(0, 1), V(1, 1), V(1, 0) },
                    new[] { new Vector2(0, 0), new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 0) }, Vector3.up);
            }
            mb.Build($"Teren_{ci}_{cj}", root, true, false);
        }
    }

    // A flat strip along a polyline draped on the terrain; stretches over water become bridges
    // that run straight between the banks, with stone parapets.
    static float[] Ribbon(MeshBuilder mb, float[] p, float width, float lift, Material m, Material parapet)
    {
        int n = p.Length / 2; if (n < 2) return new float[n];
        var pts = new Vector3[n]; var wet = new bool[n];
        for (int i = 0; i < n; i++) { float h = Height(p[2 * i], p[2 * i + 1]); wet[i] = h < 0.5f; pts[i] = new Vector3(p[2 * i], h, p[2 * i + 1]); }
        for (int i = 0; i < n; i++)
        {
            if (!wet[i]) continue;
            int a = i; while (a >= 0 && wet[a]) a--;
            int b = i; while (b < n && wet[b]) b++;
            float ha = a >= 0 ? pts[a].y : BankBeyond(pts[0], pts[Mathf.Min(1, n - 1)]);
            float hb = b < n ? pts[b].y : BankBeyond(pts[n - 1], pts[Mathf.Max(n - 2, 0)]);
            float da = a >= 0 ? Vector3.Distance(pts[a], pts[i]) : 0, db = b < n ? Vector3.Distance(pts[b], pts[i]) : 0;
            pts[i].y = Mathf.Max(1.5f, Mathf.Lerp(ha, hb, da + db > 0 ? da / (da + db) : 0.5f));
        }
        float hw = width / 2;
        for (int i = 0; i < n - 1; i++)
        {
            var a = pts[i] + Vector3.up * lift; var b = pts[i + 1] + Vector3.up * lift;
            var side = Vector3.Cross(Vector3.up, new Vector3(b.x - a.x, 0, b.z - a.z)).normalized * hw;
            if (side == Vector3.zero) continue;
            float len = Vector3.Distance(a, b);
            mb.Poly(m, new[] { a - side, b - side, b + side, a + side },
                new[] { new Vector2(0, 0), new Vector2(0, len / 6), new Vector2(width / 6, len / 6), new Vector2(width / 6, 0) }, Vector3.up);
            if (parapet != null && (wet[i] || wet[i + 1]))
                foreach (float s in new[] { -1f, 1f })
                    mb.Wall(parapet, a + side * s, b + side * s, a.y - 1.2f, a.y + 1f, side * s);
        }
        var ys = new float[n];
        for (int i = 0; i < n; i++) ys[i] = pts[i].y;
        return ys;
    }

    // The bank height a bridge lands on, found by walking past the end of the street.
    static float BankBeyond(Vector3 end, Vector3 prev)
    {
        var dir = new Vector3(end.x - prev.x, 0, end.z - prev.z).normalized;
        for (float d = 5; d < 120; d += 5)
        {
            float h = Height(end.x + dir.x * d, end.z + dir.z * d);
            if (h > 0.5f) return h;
        }
        return 6f;
    }

    // A building from the IPR Praha 3D model (data base © IPR Praha, CC BY 4.0): what it is used for, for gameplay.
    public struct IprBuilding { public int id; public string use; public int floors, access; public Vector3 at; public float r; }
    public static readonly List<IprBuilding> Ipr = new();

    // Real 3D buildings with their true roofs (Tools/fetch_ipr3d.py). False when the file is missing: OSM boxes are the fallback.
    static bool BuildIpr(Transform root)
    {
        Ipr.Clear();
        var asset = Resources.Load<TextAsset>("IprBuildings");
        if (asset == null) return false;
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
        if (new string(r.ReadChars(4)) != "IPR1") return false;
        var labels = new string[r.ReadInt32()];
        for (int i = 0; i < labels.Length; i++) labels[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
        var facades = new Material[Palette.Facades.Length];
        for (int i = 0; i < facades.Length; i++) facades[i] = Gfx.Facade(Palette.Facades[i]);
        var tiled = Gfx.Mat(Palette.Terracotta, 0.1f);
        var flat = Gfx.Mat(Color.Lerp(Palette.Slate, Palette.Plinth, 0.5f), 0.1f);
        var tiles = new Dictionary<Vector2Int, MeshBuilder>();
        const float Q = 0.05f;
        var pts = new Vector3[3]; var uvs = new Vector2[3];
        int count = r.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            int id = r.ReadInt32(); string use = labels[r.ReadByte()]; int floors = r.ReadByte(), access = r.ReadByte();
            float ox = r.ReadSingle(), oz = r.ReadSingle(); r.ReadSingle(); int nt = r.ReadInt32();
            var q = new short[nt * 9];
            for (int i = 0; i < q.Length; i++) q[i] = r.ReadInt16();
            // stand on our terrain: the lowest ground under the building's extremes (the data's own heights use another datum)
            float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
            for (int i = 0; i < q.Length; i += 3) { minX = Mathf.Min(minX, q[i]); maxX = Mathf.Max(maxX, q[i]); minZ = Mathf.Min(minZ, q[i + 2]); maxZ = Mathf.Max(maxZ, q[i + 2]); }
            float ground = Mathf.Min(Mathf.Min(Height(ox, oz), Height(ox + minX * Q, oz + minZ * Q)), Mathf.Min(Height(ox + maxX * Q, oz + maxZ * Q),
                           Mathf.Min(Height(ox + minX * Q, oz + maxZ * Q), Height(ox + maxX * Q, oz + minZ * Q))));
            if (Height(ox, oz) < -0.5f) continue; // in the river
            float y0 = ground - 0.3f;
            Ipr.Add(new IprBuilding { id = id, use = use, floors = floors, access = access, at = new Vector3(ox, ground, oz),
                r = Mathf.Max(Mathf.Max(-minX, maxX), Mathf.Max(-minZ, maxZ)) * Q });
            var key = new Vector2Int(Mathf.FloorToInt(ox / 300f), Mathf.FloorToInt(oz / 300f));
            if (!tiles.TryGetValue(key, out var mb)) tiles[key] = mb = new MeshBuilder();
            var facade = facades[(id * 7) % facades.Length];
            for (int t = 0; t < nt; t++)
            {
                for (int i = 0; i < 3; i++) { int o = t * 9 + i * 3; pts[i] = new Vector3(ox + q[o] * Q, y0 + q[o + 1] * Q, oz + q[o + 2] * Q); }
                var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                if (n.sqrMagnitude < 1e-6f) continue;
                n.Normalize();
                Material m;
                if (n.y > 0.97f) { m = flat; for (int i = 0; i < 3; i++) uvs[i] = new Vector2(pts[i].x, pts[i].z) * 0.25f; }
                else if (n.y > 0.35f) { m = tiled; for (int i = 0; i < 3; i++) uvs[i] = new Vector2(pts[i].x, pts[i].z) * 0.25f; }
                else
                {
                    m = facade; var side = new Vector3(-n.z, 0, n.x).normalized; // windows: 3.2 m wide, one floor 3.4 m high
                    for (int i = 0; i < 3; i++) uvs[i] = new Vector2(Vector3.Dot(pts[i], side) / 3.2f, (pts[i].y - y0) / 3.4f);
                }
                mb.Poly(m, pts, uvs, n);
            }
        }
        // looks only: IPR walls hang from the roof edge (eaves, filled-in arcades), so they would narrow the streets; OSM footprints collide
        foreach (var kv in tiles) kv.Value.Build($"IprDomy_{kv.Key.x}_{kv.Key.y}", root, false);
        Debug.Log($"[Mapa] IPR 3D budovy: {Ipr.Count}");
        return true;
    }

    static void BuildBuildings(Transform root)
    {
        bool ipr = BuildIpr(root); // when true, the OSM blocks below are invisible collision only
        var facades = new Material[Palette.Facades.Length];
        for (int i = 0; i < facades.Length; i++) facades[i] = Gfx.Facade(Palette.Facades[i]);
        var terracotta = Gfx.Mat(Palette.Terracotta, 0.1f);
        var flat = Gfx.Mat(Color.Lerp(Palette.Slate, Palette.Plinth, 0.5f), 0.1f);
        var tiles = new Dictionary<Vector2Int, MeshBuilder>();
        foreach (var b in D.buildings)
        {
            int n = b.p.Length / 2;
            if (n < 3) continue;
            float mx = 0, mz = 0; for (int i = 0; i < n; i++) { mx += b.p[2 * i]; mz += b.p[2 * i + 1]; }
            if (!ipr && CityBuilder.KeptOut(mx / n, mz / n)) continue; // by the footprint's middle: a corner touching a landmark must not delete a whole block
            float lo = float.MaxValue, hi = float.MinValue, area = 0;
            for (int i = 0; i < n; i++)
            {
                float h = Height(b.p[2 * i], b.p[2 * i + 1]); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                int k = (i + 1) % n; area += b.p[2 * i] * b.p[2 * k + 1] - b.p[2 * k] * b.p[2 * i + 1];
            }
            if (lo < -0.5f) continue; // footprint in the river (boats, OSM noise)
            float top = hi + Mathf.Clamp(b.h, 3f, 120f);
            var key = new Vector2Int(Mathf.FloorToInt(b.p[0] / 300f), Mathf.FloorToInt(b.p[1] / 300f));
            if (!tiles.TryGetValue(key, out var mb)) tiles[key] = mb = new MeshBuilder();
            var facade = facades[Mathf.Abs((int)(b.p[0] * 7 + b.p[1] * 13)) % facades.Length];
            float sgn = Mathf.Sign(area); // footprint winding from OSM is arbitrary
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++) ring[i] = new Vector3(b.p[2 * i], top, b.p[2 * i + 1]);
            Walls(mb, facade, ring, sgn, lo - 1f, top);
            var outline = ring;
            if (b.holes != null)
                foreach (var hole in b.holes)
                {
                    int hn = hole.p.Length / 2; if (hn < 3) continue;
                    var hr = new Vector3[hn]; float ha = 0;
                    for (int i = 0; i < hn; i++)
                    {
                        hr[i] = new Vector3(hole.p[2 * i], top, hole.p[2 * i + 1]);
                        int k = (i + 1) % hn; ha += hole.p[2 * i] * hole.p[2 * k + 1] - hole.p[2 * k] * hole.p[2 * i + 1];
                    }
                    if (Mathf.Sign(ha) == sgn) Array.Reverse(hr); // a hole runs against the outer ring
                    Walls(mb, facade, hr, sgn, lo - 1f, top); // reversed winding: walls face into the courtyard
                    outline = Bridge(outline, hr);
                }
            var roof = b.h > 24f ? flat : terracotta;
            foreach (var tri in Triangulate(outline, sgn))
                mb.Poly(roof, tri, new[] { Vector2.zero, Vector2.up, Vector2.one }, Vector3.up);
        }
        foreach (var kv in tiles)
        {
            var go = kv.Value.Build($"Domy_{kv.Key.x}_{kv.Key.y}", root, true);
            if (ipr) go.GetComponent<MeshRenderer>().enabled = false;
        }
    }

    static void Walls(MeshBuilder mb, Material m, Vector3[] ring, float sgn, float y0, float y1)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var c = ring[(i + 1) % ring.Length];
            mb.Wall(m, a, c, y0, y1, sgn * new Vector3(c.z - a.z, 0, -(c.x - a.x)));
        }
    }

    // Cuts a hole into the outline along a seam from the hole's rightmost vertex to the nearest outline vertex
    // right of it, so ear clipping sees one simple polygon. shortcut: nearest vertex, not a visibility check.
    static Vector3[] Bridge(Vector3[] outer, Vector3[] hole)
    {
        int h = 0; for (int i = 1; i < hole.Length; i++) if (hole[i].x > hole[h].x) h = i;
        int o = -1; float best = float.MaxValue;
        for (int i = 0; i < outer.Length; i++)
        {
            float d = (outer[i] - hole[h]).sqrMagnitude;
            if (outer[i].x >= hole[h].x && d < best) { best = d; o = i; }
        }
        if (o < 0) return outer;
        var res = new List<Vector3>(outer.Length + hole.Length + 2);
        for (int i = 0; i <= o; i++) res.Add(outer[i]);
        for (int i = 0; i <= hole.Length; i++) res.Add(hole[(h + i) % hole.Length]);
        for (int i = o; i < outer.Length; i++) res.Add(outer[i]);
        return res.ToArray();
    }

    // Ear clipping for simple polygons (building footprints); falls back to a fan when stuck.
    static List<Vector3[]> Triangulate(Vector3[] ring, float sgn)
    {
        var res = new List<Vector3[]>(); var idx = new List<int>();
        for (int i = 0; i < ring.Length; i++) idx.Add(i);
        float Cross(Vector3 a, Vector3 b, Vector3 c) => sgn * ((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
        int guard = 0;
        while (idx.Count > 3 && guard++ < 4 * ring.Length)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count; i++)
            {
                var a = ring[idx[(i + idx.Count - 1) % idx.Count]]; var b = ring[idx[i]]; var c = ring[idx[(i + 1) % idx.Count]];
                if (Cross(a, b, c) <= 0) continue;
                bool inside = false;
                foreach (int q in idx)
                {
                    var p = ring[q]; if (p == a || p == b || p == c) continue;
                    if (Cross(a, b, p) > 0 && Cross(b, c, p) > 0 && Cross(c, a, p) > 0) { inside = true; break; }
                }
                if (inside) continue;
                res.Add(new[] { a, b, c }); idx.RemoveAt(i); cut = true; break;
            }
            if (!cut) break;
        }
        for (int i = 1; i + 1 < idx.Count; i++) res.Add(new[] { ring[idx[0]], ring[idx[i]], ring[idx[i + 1]] });
        return res;
    }

    static void BuildMetroEntrances(Transform root)
    {
        var mb = new MeshBuilder();
        var body = Gfx.Mat(Palette.Concrete, 0.35f);
        var sign = Gfx.Emissive(Palette.Accent, 2.5f);
        foreach (var e in D.metro.entrances)
        {
            var c = new Vector3(e.p[0], Height(e.p[0], e.p[1]), e.p[1]);
            mb.Box(body, c + Vector3.up * 0.5f, new Vector3(3f, 1f, 4f));       // stairwell parapet
            mb.Box(body, c + new Vector3(1.6f, 1.8f, 0), new Vector3(0.25f, 3.6f, 0.25f));
            mb.Box(sign, c + new Vector3(1.6f, 3.9f, 0), new Vector3(0.9f, 0.9f, 0.9f)); // the "M" cube
        }
        mb.Build("MetroVstupy", root, true);
    }

    static void BuildTrees(Transform root)
    {
        var t = D.terrain;
        var mb = new MeshBuilder();
        var leaves = Gfx.Mat(Palette.Foliage, 0.05f); var trunk = Gfx.Mat(Palette.Trunk, 0.05f);
        var real = Resources.Load<TextAsset>("IprTrees"); // every real tree with its measured height (DTM Praha, data base © IPR Praha)
        if (real != null)
        {
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(real.bytes));
            if (new string(r.ReadChars(4)) == "TRE1")
            {
                var tiles = new Dictionary<Vector2Int, MeshBuilder>();
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++)
                {
                    float x = r.ReadSingle(), z = r.ReadSingle(), h = Mathf.Clamp(r.ReadUInt16() / 100f, 2f, 35f);
                    float gy = Height(x, z); if (gy < -0.5f) continue;
                    var key = new Vector2Int(Mathf.FloorToInt(x / 300f), Mathf.FloorToInt(z / 300f));
                    if (!tiles.TryGetValue(key, out var tm)) tiles[key] = tm = new MeshBuilder();
                    var p = new Vector3(x, gy, z); float crown = Mathf.Clamp(h * 0.3f, 1.2f, 6f); // crowns about a third as wide as tall
                    tm.Cylinder(trunk, p, Mathf.Clamp(h * 0.025f, 0.15f, 0.6f), h * 0.45f, 5);
                    var cc = p + Vector3.up * (h - crown * 1.1f);
                    tm.Dome(leaves, cc, crown, 1.1f, 6, 2);
                    tm.Dome(leaves, cc, crown, -0.6f, 6, 1); // underside, so a crown is not an umbrella from below
                }
                foreach (var kv in tiles) kv.Value.Build($"Stromy_{kv.Key.x}_{kv.Key.y}", root, false);
                return;
            }
        }
        for (int j = 0; j < t.h; j++)
        for (int i = 0; i < t.w; i++)
        {
            if (lu[j * t.w + i] != 2 || ((i * 73856093) ^ (j * 19349663)) % 5 != 0) continue;
            float x = t.x0 + (i + 0.5f) * t.cell, z = t.z0 + (j + 0.5f) * t.cell;
            var p = new Vector3(x, Height(x, z), z);
            float s = 0.8f + ((i * 31 + j * 17) % 10) / 20f;
            mb.Cylinder(trunk, p, 0.3f * s, 3f * s, 5);
            mb.Dome(leaves, p + Vector3.up * 3.2f * s, 2.6f * s, 1.3f, 6, 3);
        }
        // shortcut: trees have no colliders; add trunk colliders if driving through parks feels wrong
        mb.Build("Stromy", root, false);
    }

    static void BuildBounds(Transform root)
    {
        var t = D.terrain; float x0 = t.x0, z0 = t.z0, x1 = t.x0 + t.w * t.cell, z1 = t.z0 + t.h * t.cell;
        void Wall(Vector3 c, Vector3 size) { var go = new GameObject("Hranice"); go.transform.SetParent(root, false); go.transform.position = c; go.AddComponent<BoxCollider>().size = size; }
        Wall(new Vector3(x0, 100, (z0 + z1) / 2), new Vector3(2, 400, z1 - z0));
        Wall(new Vector3(x1, 100, (z0 + z1) / 2), new Vector3(2, 400, z1 - z0));
        Wall(new Vector3((x0 + x1) / 2, 100, z0), new Vector3(x1 - x0, 400, 2));
        Wall(new Vector3((x0 + x1) / 2, 100, z1), new Vector3(x1 - x0, 400, 2));
    }
}
