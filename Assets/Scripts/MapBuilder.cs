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
    // the surface between vertices, as Ribbon draws it: max(terrain, lerp of Floor) on land, deck and ramps included;
    // a straight lerp of StreetY where Flat (a plain strip over water without an IPR bridge)
    public static readonly List<float[]> StreetFloor = new();
    public static readonly List<bool[]> StreetFlat = new();
    static float[] lastFloor; static bool[] lastFlat;

    public static float SurfaceY(int s, int a, int b, float t, float x, float z)
    {
        var y = StreetY[s];
        if (s >= StreetFloor.Count) return Mathf.Lerp(y[a], y[b], t);
        var f = StreetFloor[s]; var fl = StreetFlat[s];
        if (fl[a] || fl[b]) return Mathf.Lerp(y[a], y[b], t);
        return Mathf.Max(Height(x, z), Mathf.Lerp(f[a], f[b], t));
    }
    static float[] hm; static byte[] lu;
    public const float WaterY = -0.6f;
    const int Chunk = 64; // terrain cells per mesh tile

    public static float Height(float x, float z)
    {
        var t = D.terrain;
        float fx = Mathf.Clamp((x - t.x0) / t.cell, 0, t.w - 1.001f), fz = Mathf.Clamp((z - t.z0) / t.cell, 0, t.h - 1.001f);
        int i = (int)fx, j = (int)fz; float u = fx - i, v = fz - j;
        float a = hm[j * t.w + i], b = hm[j * t.w + i + 1], c = hm[(j + 1) * t.w + i], d = hm[(j + 1) * t.w + i + 1];
        // the same two triangles as the ground mesh (BuildTerrain: fan from (0,0), diagonal to (1,1)), not bilinear:
        // on steep cells the two differed by metres and roads, trees and people sank in or floated
        return v >= u ? a + u * (d - c) + v * (c - a) : a + u * (b - a) + v * (d - b);
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
    static void ResetStatics() { D = null; hm = null; lu = null; StreetY.Clear(); StreetFloor.Clear(); StreetFlat.Clear(); Ipr.Clear(); MeshCover.Clear(); } // domain reload is off

    public static void Build(Transform root, List<(Vector3 pos, float yaw)> parking)
    {
        Load();
        var mGround = Gfx.Pbr("Mosaic", 0.1f, 4f, Color.white); // terrain UV: 4 m per unit; mosaic pavement ~1 m per tile
        var mPark = Gfx.Mat(Palette.Grass, 0.05f);
        var mSquare = Gfx.Pbr("Setts", 0.12f, 3f, Color.white); // granite setts of Prague squares
        var mBank = Gfx.Mat(Palette.GothicStone, 0.08f);
        BuildTerrain(root, new[] { mGround, mBank, mPark, mSquare });

        var water = new MeshBuilder();
        var t = D.terrain;
        water.Ground(Gfx.Mat(Palette.River, 0.88f), t.x0, t.z0, t.x0 + t.w * t.cell, t.z0 + t.h * t.cell, WaterY, 10, 10);
        water.Build("Vltava", root, false, false);

        var asphalt = Gfx.Pbr("Asphalt", 0.2f, 3f, Color.white); // street UV: 6 m per unit
        var cobble = Gfx.Pbr("Setts", 0.12f, 4f, Color.white);
        var stone = Gfx.Mat(Palette.GothicStone, 0.08f);
        var rail = Gfx.Mat(Color.Lerp(Palette.Asphalt, Palette.Slate, 0.6f), 0.5f);
        BuildIprBridges(root); // before the streets: their stretches over water take the real deck's height
        var streets = new MeshBuilder();
        int k = 0;
        StreetY.Clear(); StreetFloor.Clear(); StreetFlat.Clear();
        foreach (var s in D.streets)
        {
            StreetY.Add(Ribbon(streets, s.p, s.w, 0.12f, s.foot ? cobble : asphalt, stone));
            StreetFloor.Add(lastFloor); StreetFlat.Add(lastFlat);
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
                StreetFloor.Add(new[] { -1e4f, -1e4f }); StreetFlat.Add(new bool[2]);
            }
        }
        D.streets = walk.ToArray();

        BuildBuildings(root);
        PlacesToDoors();
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
        for (int i = 0; i < n; i++) { float h = Height(p[2 * i], p[2 * i + 1]); wet[i] = h < -1f; pts[i] = new Vector3(p[2 * i], h, p[2 * i + 1]); } // the river bed is carved to -3 m; low land (Karlín, ~0 m) is not water
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
        // over water, on a real IPR bridge: walk its deck and leave the looks to the model (no flat strip, no parapet boxes).
        // The deck also carries the street over dry land next to the river (Kampa): spread along the street from a wet hit.
        var deckY = new float[n]; var onDeck = new bool[n];
        for (int i = 0; i < n; i++)
        {
            deckY[i] = float.NaN;
            var dir = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]; var sd = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0, dir.z)).normalized;
            foreach (float o in new[] { 0f, 3f, -3f }) // a point just off the deck's edge still belongs to it
                if (Physics.Raycast(new Vector3(pts[i].x + sd.x * o, 200f, pts[i].z + sd.z * o), Vector3.down, out var hit, 400f) && hit.collider.name.StartsWith("IprMosty"))
                { deckY[i] = hit.point.y; break; }
            onDeck[i] = wet[i] && !float.IsNaN(deckY[i]);
        }
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < n; k++)
            {
                int i = pass == 0 ? k : n - 1 - k, j = pass == 0 ? i - 1 : i + 1;
                if (j >= 0 && j < n && onDeck[j] && !onDeck[i] && !float.IsNaN(deckY[i])) onDeck[i] = true;
            }
        var cum = new float[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i - 1].x, pts[i - 1].z));
        // the 8 m terrain smooths away the fill at a bridgehead: the street ramps down from the deck at 12 % at most
        var ramp = new float[n];
        for (int i = 0; i < n; i++)
        {
            ramp[i] = -1e4f;
            for (int j = 0; j < n; j++)
                if (wet[j]) ramp[i] = Mathf.Max(ramp[i], (onDeck[j] ? deckY[j] - lift : pts[j].y) - Mathf.Abs(cum[i] - cum[j]) * 0.12f); // IPR deck or our own strip
        }
        for (int i = 0; i < n; i++)
            if (onDeck[i]) pts[i].y = deckY[i] - lift;
            else if (!wet[i]) pts[i].y = Mathf.Max(pts[i].y, ramp[i]);
        float hw = width / 2;
        for (int i = 0; i < n - 1; i++)
        {
            var a = pts[i] + Vector3.up * lift; var b = pts[i + 1] + Vector3.up * lift;
            var side = Vector3.Cross(Vector3.up, new Vector3(b.x - a.x, 0, b.z - a.z)).normalized * hw;
            if (side == Vector3.zero) continue;
            float len = Vector3.Distance(a, b);
            if (onDeck[i] && onDeck[i + 1]) continue;
            if ((!wet[i] || onDeck[i]) && (!wet[i + 1] || onDeck[i + 1]))
            {
                // on land: drape on the terrain in 4 m steps, each corner on its own ground. A straight strip between OSM points
                // (often 30+ m apart) or across a side slope let the terrain poke through the road. Near a bridge: the ramp.
                int k = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                float r0 = onDeck[i] ? deckY[i] - lift : ramp[i], r1 = onDeck[i + 1] ? deckY[i + 1] - lift : ramp[i + 1];
                Vector3 G(Vector3 q, float f) => new(q.x, Mathf.Max(Height(q.x, q.z), Mathf.Lerp(r0, r1, f)) + lift, q.z);
                for (int s = 0; s < k; s++)
                {
                    float f0 = (float)s / k, f1 = (float)(s + 1) / k;
                    var c0 = Vector3.Lerp(a, b, f0); var c1 = Vector3.Lerp(a, b, f1);
                    float v0 = len * s / k / 6, v1 = len * (s + 1) / k / 6;
                    Vector3 p00 = G(c0 - side, f0), p10 = G(c1 - side, f1), p11 = G(c1 + side, f1), p01 = G(c0 + side, f0);
                    mb.Poly(m, new[] { p00, p10, p11, p01 },
                        new[] { new Vector2(0, v0), new Vector2(0, v1), new Vector2(width / 6, v1), new Vector2(width / 6, v0) }, Vector3.up);
                    if (parapet == null) continue;
                    foreach (var (q0, q1, o) in new[] { (p00, p10, -side), (p01, p11, side) }) // retaining wall under a raised ramp
                    {
                        float g0 = Height(q0.x, q0.z), g1 = Height(q1.x, q1.z);
                        if (q0.y - g0 < 0.4f && q1.y - g1 < 0.4f) continue;
                        mb.Poly(parapet, new[] { new Vector3(q0.x, g0 - 0.5f, q0.z), new Vector3(q1.x, g1 - 0.5f, q1.z), q1, q0 },
                            new[] { new Vector2(0, 0), new Vector2(len / k / 3, 0), new Vector2(len / k / 3, (q1.y - g1) / 3), new Vector2(0, (q0.y - g0) / 3) }, o);
                    }
                }
                continue;
            }
            mb.Poly(m, new[] { a - side, b - side, b + side, a + side },
                new[] { new Vector2(0, 0), new Vector2(0, len / 6), new Vector2(width / 6, len / 6), new Vector2(width / 6, 0) }, Vector3.up);
            if (parapet != null && (wet[i] || wet[i + 1]))
                foreach (float s in new[] { -1f, 1f })
                    mb.Wall(parapet, a + side * s, b + side * s, a.y - 1.2f, a.y + 1f, side * s);
        }
        // joints: a bend leaves a wedge open on its outside, a junction leaves square-cut ends. A draped disc fills both.
        if (parapet != null)
            for (int i = 0; i < n; i++)
            {
                if (wet[i] || onDeck[i]) continue;
                if (i > 0 && i < n - 1)
                {
                    var d0 = new Vector2(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z); var d1 = new Vector2(pts[i + 1].x - pts[i].x, pts[i + 1].z - pts[i].z);
                    if (Vector2.Angle(d0, d1) < 8f) continue;
                }
                var ring = new Vector3[8]; var ruv = new Vector2[8];
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f; float x = pts[i].x + Mathf.Cos(a) * hw, z = pts[i].z + Mathf.Sin(a) * hw;
                    ring[k] = new Vector3(x, Mathf.Max(Height(x, z), ramp[i]) + lift - 0.01f, z); ruv[k] = new Vector2(x, z) / 6f;
                }
                mb.Poly(m, ring, ruv, Vector3.up);
            }
        var ys = new float[n];
        for (int i = 0; i < n; i++) ys[i] = pts[i].y;
        lastFloor = new float[n]; lastFlat = new bool[n];
        for (int i = 0; i < n; i++) { lastFloor[i] = onDeck[i] ? deckY[i] - lift : ramp[i]; lastFlat[i] = wet[i] && !onDeck[i]; }
        return ys;
    }

    // The bank height a bridge lands on, found by walking past the end of the street.
    static float BankBeyond(Vector3 end, Vector3 prev)
    {
        var dir = new Vector3(end.x - prev.x, 0, end.z - prev.z).normalized;
        for (float d = 5; d < 120; d += 5)
        {
            float h = Height(end.x + dir.x * d, end.z + dir.z * d);
            if (h > -1f) return h;
        }
        return 6f;
    }

    // A building from the IPR Praha 3D model (data base © IPR Praha, CC BY 4.0): what it is used for, for gameplay.
    public struct IprBuilding { public int id; public string use; public int floors, access; public Vector3 at; public float r; }
    public static readonly List<IprBuilding> Ipr = new();

    // Photoreal mesh tiles (IPR Praha, 5 cm/px; Tools/fetch_ipr_mesh.py), pilot of phase 8. Where one lies, it replaces the
    // LOD2 buildings and the hand-made pieces: MeshCover holds its 5 m cells. Looks only: the OSM footprints still collide.
    const bool UsePhotoMesh = false;
    public static readonly HashSet<Vector2Int> MeshCover = new();
    public static Vector2Int Cell5(float x, float z) => new(Mathf.FloorToInt(x / 5f), Mathf.FloorToInt(z / 5f));
    public static bool Covered(float x, float z) => MeshCover.Contains(Cell5(x, z));

    static void BuildIprMesh(Transform root)
    {
        MeshCover.Clear();
        foreach (var asset in Resources.LoadAll<TextAsset>("IprMesh"))
        {
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
            if (new string(r.ReadChars(4)) != "MSH1") continue;
            float ox = r.ReadSingle(), oz = r.ReadSingle(), z0 = r.ReadSingle(); int nv = r.ReadInt32(), ni = r.ReadInt32();
            var v = new Vector3[nv]; var uv = new Vector2[nv]; var low = new Dictionary<Vector2Int, float>();
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(ox + r.ReadInt16() * 0.01f, z0 + r.ReadInt16() * 0.01f, oz + r.ReadInt16() * 0.01f);
                uv[i] = new Vector2(r.ReadUInt16() / 65535f, r.ReadUInt16() / 65535f);
                var c = Cell5(v[i].x, v[i].z);
                if (!low.TryGetValue(c, out var y) || v[i].y < y) low[c] = v[i].y;
            }
            var idx = new int[ni];
            for (int i = 0; i < ni; i++) idx[i] = (int)r.ReadUInt32();
            // the data's heights use another datum: shift so its ground (lowest point per cell) sits on our terrain
            var diffs = new List<float>();
            foreach (var kv in low)
            {
                float x = kv.Key.x * 5f + 2.5f, z = kv.Key.y * 5f + 2.5f, h = Height(x, z);
                if (h > -0.5f) diffs.Add(kv.Value - h);
                MeshCover.Add(kv.Key);
            }
            diffs.Sort();
            float shift = diffs.Count > 0 ? diffs[diffs.Count / 2] - 0.13f : 0f; // 0.13: just over the street ribbons it hides
            for (int i = 0; i < nv; i++) v[i].y -= shift;
            var mesh = new Mesh { name = asset.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = v, uv = uv, triangles = idx };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("IprMesh_" + asset.name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Gfx.Mat(Color.white, 0f, Resources.Load<Texture2D>("IprMesh/" + asset.name), "mesh");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; // its photos have the shadows already
            Debug.Log($"[Mapa] fotorealistický mesh {asset.name}: {ni / 3} trojúhelníků, posun výšky {shift:0.0} m");
        }
    }

    // Real 3D buildings with their true roofs (Tools/fetch_ipr3d.py). False when the file is missing: OSM boxes are the fallback.
    // IPR heights are above sea level (Bpv), ours are above the river: the median building gives the shift
    static float IprDatum()
    {
        var asset = Resources.Load<TextAsset>("IprBuildings");
        if (asset == null) return float.NaN;
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
        if (new string(r.ReadChars(4)) != "IPR1") return float.NaN;
        int nl = r.ReadInt32(); for (int i = 0; i < nl; i++) r.ReadBytes(r.ReadByte());
        int count = r.ReadInt32(); var d = new List<float>(count);
        for (int k = 0; k < count; k++)
        {
            r.ReadInt32(); r.ReadBytes(3); float ox = r.ReadSingle(), oz = r.ReadSingle(), b = r.ReadSingle(); int nt = r.ReadInt32();
            r.BaseStream.Seek(nt * 18L, System.IO.SeekOrigin.Current);
            float h = Height(ox, oz); if (h > 0.5f) d.Add(h - b);
        }
        if (d.Count == 0) return float.NaN;
        d.Sort(); return d[d.Count / 2];
    }

    // Real bridges (IPR Mosty3D 2020, data base © IPR Praha, CC BY 4.0): arches, piers, towers, at their real height; walkable
    static void BuildIprBridges(Transform root)
    {
        var asset = Resources.Load<TextAsset>("IprBridges");
        float datum = IprDatum();
        if (asset == null || float.IsNaN(datum)) return;
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
        if (new string(r.ReadChars(4)) != "IPR1") return;
        int nl = r.ReadInt32(); for (int i = 0; i < nl; i++) r.ReadBytes(r.ReadByte());
        var stone = new Material(Gfx.Mat(Palette.GothicStone, 0.08f)) { name = "IprMosty_2s" }; stone.SetFloat("_Cull", 0f);
        var deck = new Material(Gfx.Pbr("Setts", 0.12f, 4f, Color.white)) { name = "IprMostyDeck_2s" }; // deck UV: 6 m per unit, like the streets deck.SetFloat("_Cull", 0f);
        const float Q = 0.05f; var pts = new Vector3[3]; var uvs = new Vector2[3];
        int count = r.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            r.ReadInt32(); r.ReadBytes(3); float ox = r.ReadSingle(), oz = r.ReadSingle(), b = r.ReadSingle(); int nt = r.ReadInt32();
            var mb = new MeshBuilder();
            for (int t = 0; t < nt; t++)
            {
                for (int i = 0; i < 3; i++) pts[i] = new Vector3(ox + r.ReadInt16() * Q, b + datum + r.ReadInt16() * Q, oz + r.ReadInt16() * Q);
                var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                if (n.sqrMagnitude < 1e-6f) continue;
                n.Normalize(); if (Mathf.Abs(n.y) > 0.35f && n.y < 0) n = -n;
                bool top = n.y > 0.9f;
                for (int i = 0; i < 3; i++) uvs[i] = top ? new Vector2(pts[i].x, pts[i].z) / 6f : new Vector2(pts[i].x + pts[i].z, pts[i].y) / 4f;
                mb.Poly(top ? deck : stone, pts, uvs, n);
            }
            mb.Build($"IprMosty_{k}", root, true);
        }
        Physics.SyncTransforms();
        Debug.Log($"[Mapa] IPR mosty: {count}, datum {datum:0.0} m");
    }

    public static bool IprCollision; // buildings collide by IPR ground outlines; the OSM blocks are then not built at all

    static Dictionary<int, List<Vector2[]>> LoadFootprints()
    {
        var asset = Resources.Load<TextAsset>("IprFootprints");
        if (asset == null) return null;
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
        if (new string(r.ReadChars(4)) != "FPR1") return null;
        int n = r.ReadInt32(); var d = new Dictionary<int, List<Vector2[]>>(n);
        for (int k = 0; k < n; k++)
        {
            int id = r.ReadInt32(); int nr = r.ReadInt16(); var rings = new List<Vector2[]>(nr);
            for (int i = 0; i < nr; i++)
            {
                int np = r.ReadInt16(); var pts = new Vector2[np];
                for (int j = 0; j < np; j++) pts[j] = new Vector2(r.ReadSingle(), r.ReadSingle());
                if (np > 1 && pts[0] == pts[np - 1]) Array.Resize(ref pts, np - 1); // closed rings repeat the first point
                if (pts.Length >= 3) rings.Add(pts);
            }
            if (rings.Count > 0) d[id] = rings;
        }
        return d;
    }

    static bool BuildIpr(Transform root)
    {
        IprCollision = false;
        Ipr.Clear();
        var asset = Resources.Load<TextAsset>("IprBuildings");
        if (asset == null) return false;
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(asset.bytes));
        if (new string(r.ReadChars(4)) != "IPR1") return false;
        var labels = new string[r.ReadInt32()];
        for (int i = 0; i < labels.Length; i++) labels[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
        // two-sided copies (the cached ones are shared with the rest of the city): the data's winding and normals do not say
        // reliably which side is out, so faces are oriented by geometry below and a courtyard wall facing "inward" still shows
        static Material TwoSided(Material m) { var c = new Material(m) { name = m.name + "_2s" }; c.SetFloat("_Cull", 0f); c.doubleSidedGI = true; return c; }
        int nf = Gfx.FacadeCount; var facades = new Material[nf > 0 ? nf : Palette.Facades.Length];
        for (int i = 0; i < facades.Length; i++) facades[i] = TwoSided(nf > 0 ? Gfx.FacadeTone(i) : Gfx.Facade(Palette.Facades[i]));
        // churches: tall Gothic ones (Týn, St Vitus) are dark weathered stone under slate; the rest light plaster
        var churchDark = TwoSided(Gfx.Pbr("Wall", 0.08f, 1f / 8f, new Color(0.40f, 0.38f, 0.36f))); // wall UV: 3.2 m per unit
        var churchLight = TwoSided(Gfx.Pbr("Wall", 0.08f, 1f / 8f, new Color(0.93f, 0.90f, 0.83f)));
        var tiled = TwoSided(Gfx.Pbr("Roof", 0.15f, 2f, Color.white));
        var slate = TwoSided(Gfx.Pbr("Roof", 0.2f, 2.01f, new Color(0.33f, 0.34f, 0.36f))); // roof UV: 4 m per unit; ~2 m of tiles per texture
        var flat = TwoSided(Gfx.Mat(Color.Lerp(Palette.Slate, Palette.Plinth, 0.5f), 0.1f));
        var tiles = new Dictionary<Vector2Int, MeshBuilder>();
        // collision from the same buildings' ground outlines (IprFootprints: Tools/fetch_ipr3d.py footprints): OSM outlines
        // missed or undersized ~1 in 6 of the buildings you see, so you walked through them
        var foot = LoadFootprints(); var coll = new Dictionary<Vector2Int, MeshBuilder>(); var cmat = Gfx.Mat(Color.white, 0f);
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
            float maxY = 0;
            for (int i = 0; i < q.Length; i += 3) { minX = Mathf.Min(minX, q[i]); maxX = Mathf.Max(maxX, q[i]); minZ = Mathf.Min(minZ, q[i + 2]); maxZ = Mathf.Max(maxZ, q[i + 2]); maxY = Mathf.Max(maxY, q[i + 1]); }
            float ground = Mathf.Min(Mathf.Min(Height(ox, oz), Height(ox + minX * Q, oz + minZ * Q)), Mathf.Min(Height(ox + maxX * Q, oz + maxZ * Q),
                           Mathf.Min(Height(ox + minX * Q, oz + maxZ * Q), Height(ox + maxX * Q, oz + minZ * Q))));
            if (Height(ox, oz) < -0.5f) continue; // in the river
            float y0 = ground - 0.3f;
            Ipr.Add(new IprBuilding { id = id, use = use, floors = floors, access = access, at = new Vector3(ox, ground, oz),
                r = Mathf.Max(Mathf.Max(-minX, maxX), Mathf.Max(-minZ, maxZ)) * Q });
            var key = new Vector2Int(Mathf.FloorToInt(ox / 300f), Mathf.FloorToInt(oz / 300f));
            if (foot != null && foot.TryGetValue(id, out var rings))
            {
                if (!coll.TryGetValue(key, out var cb)) coll[key] = cb = new MeshBuilder();
                float top = y0 + Mathf.Max(3f, maxY * Q);
                for (int ri = 0; ri < rings.Count; ri++)
                {
                    var rg = rings[ri]; var ring = new Vector3[rg.Length]; float area = 0;
                    for (int i = 0; i < rg.Length; i++) { ring[i] = new Vector3(rg[i].x, top, rg[i].y); int k2 = (i + 1) % rg.Length; area += rg[i].x * rg[k2].y - rg[k2].x * rg[i].y; }
                    float sgn = Mathf.Sign(area);
                    Walls(cb, cmat, ring, sgn, y0 - 1f, top); Walls(cb, cmat, ring, -sgn, y0 - 1f, top); // both faces: no side lets you in
                    if (ri == 0) foreach (var tri in Triangulate(ring, sgn)) cb.Poly(cmat, tri, null, Vector3.up); // roof: PlacesToDoors looks for it
                }
            }
            if (Covered(ox, oz)) continue; // the photoreal mesh shows it already (still known to the game for Q and buying)
            if (!tiles.TryGetValue(key, out var mb)) tiles[key] = mb = new MeshBuilder();
            bool gothic = use == "církev" && maxY * Q > 40f;
            var facade = use == "církev" ? (gothic ? churchDark : churchLight) : facades[(id * 7) % facades.Length]; // a church is not a tenement
            var roofM = gothic ? slate : tiled;
            // window rows from the register's floor count: old houses have 4-5 m storeys, not a fixed 3.4 m
            float wallTop = 0;
            for (int t = 0; t < nt; t++)
            {
                int o = t * 9; var a = new Vector3(q[o], q[o + 1], q[o + 2]); var b = new Vector3(q[o + 3], q[o + 4], q[o + 5]); var c = new Vector3(q[o + 6], q[o + 7], q[o + 8]);
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1f || Mathf.Abs(nn.normalized.y) > 0.35f) continue;
                wallTop = Mathf.Max(wallTop, Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
            }
            float storey = floors > 0 && wallTop > 0 ? Mathf.Clamp(wallTop * Q / floors, 2.8f, 5.5f) : 3.4f;
            for (int t = 0; t < nt; t++)
            {
                for (int i = 0; i < 3; i++) { int o = t * 9 + i * 3; pts[i] = new Vector3(ox + q[o] * Q, y0 + q[o + 1] * Q, oz + q[o + 2] * Q); }
                var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                if (n.sqrMagnitude < 1e-6f) continue;
                n.Normalize();
                // roofs face up, walls away from the building's middle (light comes from the right side; the material is two-sided)
                if (Mathf.Abs(n.y) > 0.35f) { if (n.y < 0) n = -n; }
                else
                {
                    var mid = (pts[0] + pts[1] + pts[2]) / 3f;
                    if (n.x * (mid.x - ox) + n.z * (mid.z - oz) < 0) n = -n;
                }
                Material m;
                if (n.y > 0.97f) { m = flat; for (int i = 0; i < 3; i++) uvs[i] = new Vector2(pts[i].x, pts[i].z) * 0.25f; }
                else if (n.y > 0.35f) { m = roofM; for (int i = 0; i < 3; i++) uvs[i] = new Vector2(pts[i].x, pts[i].z) * 0.25f; }
                else
                {
                    m = facade; var side = new Vector3(-n.z, 0, n.x).normalized; // windows: 3.2 m wide, one floor 3.4 m high
                    for (int i = 0; i < 3; i++) uvs[i] = new Vector2(Vector3.Dot(pts[i], side) / 3.2f, (pts[i].y - y0 - 0.3f) / storey);
                }
                mb.Poly(m, pts, uvs, n);
            }
        }
        // looks only: IPR walls hang from the roof edge (eaves, filled-in arcades), so they would narrow the streets; OSM footprints collide
        foreach (var kv in tiles) kv.Value.Build($"IprDomy_{kv.Key.x}_{kv.Key.y}", root, false);
        foreach (var kv in coll) { var go = kv.Value.Build($"DomyIpr_{kv.Key.x}_{kv.Key.y}", root, true, false); go.GetComponent<MeshRenderer>().enabled = false; }
        IprCollision = coll.Count > 0;
        Debug.Log($"[Mapa] IPR 3D budovy: {Ipr.Count}");
        return true;
    }

    // OSM puts a shop's point inside its building, where nobody can stand: a courier had to get within 9 m of it,
    // and for 1 in 20 places that was impossible. Move every such point to the nearest open ground: its street door.
    static void PlacesToDoors()
    {
        Physics.SyncTransforms();
        static bool Roof(float x, float z) => Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out var h, 600f, ~0, QueryTriggerInteraction.Ignore)
                                               && h.collider.name.StartsWith("Domy");
        foreach (var p in D.places)
        {
            if (!Roof(p.p[0], p.p[1]) && Height(p.p[0], p.p[1]) > -0.5f) continue; // islands and boats are river to us: to the bank
            for (float r = 1f; r <= 60f; r += 1f)
            {
                int k = 0;
                for (; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    float x = p.p[0] + Mathf.Cos(a) * r, z = p.p[1] + Mathf.Sin(a) * r;
                    if (!Roof(x, z) && Height(x, z) > -0.5f) break; // open ground, not the river behind an embankment house
                }
                if (k == 16) continue;
                float b = k * Mathf.PI / 8f, out1 = r + 1f; // one more metre out, onto the pavement (unless that is the river)
                if (Height(p.p[0] + Mathf.Cos(b) * out1, p.p[1] + Mathf.Sin(b) * out1) <= -0.5f) out1 = r;
                p.p[0] += Mathf.Cos(b) * out1; p.p[1] += Mathf.Sin(b) * out1;
                break;
            }
        }
    }

    static void BuildBuildings(Transform root)
    {
        // aerial photogrammetry melts at street level (blobby corners, black trees, smeared towers): off, crisp LOD2 everywhere.
        // Tiles from Tools/fetch_ipr_mesh.py stay outside Resources (Hry/ipr-mesh/tiles) so builds do not ship 460 MB of them.
        if (UsePhotoMesh) BuildIprMesh(root);
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
            if (ipr && IprCollision) continue; // IPR outlines collide instead (OSM ones missed or cut through real buildings)
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
                    float gy = Height(x, z); if (gy < -0.5f || Covered(x, z)) continue; // the photoreal mesh has its own trees
                    var key = new Vector2Int(Mathf.FloorToInt(x / 300f), Mathf.FloorToInt(z / 300f));
                    if (!tiles.TryGetValue(key, out var tm)) tiles[key] = tm = new MeshBuilder();
                    var p = new Vector3(x, gy, z); float crown = Mathf.Clamp(h * 0.3f, 1.2f, 6f); // crowns about a third as wide as tall
                    // low-poly on purpose: 36 000 real trees, ~20 triangles each (a two-dome crown cost 1.7 M on screen)
                    var cc = p + Vector3.up * (h - crown * 1.1f); float yaw = k * 37f;
                    // trunk from 1 m under the ground (slopes, coarse 8 m terrain) up into the crown: with the 6 m crown cap a tall
                    // tree's crown started far above a 0.45 h trunk and floated
                    tm.Cylinder(trunk, p + Vector3.down, Mathf.Clamp(h * 0.025f, 0.15f, 0.6f), cc.y - p.y + 1f, 4, false);
                    tm.Cone(leaves, cc, crown, crown * 1.1f, 6, yaw);
                    tm.Cone(leaves, cc, crown, -crown * 0.6f, 6, yaw); // underside, so a crown is not an umbrella from below
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
        // the ground mesh ends at the last height sample, (w-1)*cell, not w*cell: walls stood 8 m out over nothing and the player fell.
        // Inner faces sit 4 m inside the ground's edge; 20 m thick so a car at full speed cannot tunnel through in one physics step.
        var t = D.terrain; const float in4 = 4f, th = 20f;
        float x0 = t.x0 + in4, z0 = t.z0 + in4, x1 = t.x0 + (t.w - 1) * t.cell - in4, z1 = t.z0 + (t.h - 1) * t.cell - in4;
        void Wall(Vector3 c, Vector3 size) { var go = new GameObject("Hranice"); go.transform.SetParent(root, false); go.transform.position = c; go.AddComponent<BoxCollider>().size = size; }
        float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, sx = x1 - x0 + 2 * th, sz = z1 - z0 + 2 * th;
        Wall(new Vector3(x0 - th / 2, 100, cz), new Vector3(th, 600, sz));
        Wall(new Vector3(x1 + th / 2, 100, cz), new Vector3(th, 600, sz));
        Wall(new Vector3(cx, 100, z0 - th / 2), new Vector3(sx, 600, th));
        Wall(new Vector3(cx, 100, z1 + th / 2), new Vector3(sx, 600, th));
    }
}
