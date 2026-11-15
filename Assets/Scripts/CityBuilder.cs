using System.Collections.Generic;
using UnityEngine;

// Procedurally builds a compressed, stylized Prague:
//   Vltava runs north–south through x = 0.
//   West bank: Malá Strana, Petřín park + lookout tower, Hradčany plateau with the castle and cathedral.
//   East bank: Staré Město with Old Town Square (Týn church, Town Hall tower), Nové Město, Žižkov tower.
//   Bridges: Charles Bridge (z = 0, with towers and statues) and two road bridges.
public static class CityBuilder
{
    public const float RiverHalf = 45f;
    public const float WaterY = -2.5f;
    public const float WorldX = 900f, WorldZ = 780f;
    public const float RoadW = 18f, Grid = 90f;
    public const float PlateauH = 28f;

    public struct Landmark
    {
        public string name; public Vector3 pos; public float radius; public float minY;
    }

    public struct Rect2
    {
        public float x0, z0, x1, z1;
        public Rect2(float x0, float z0, float x1, float z1) { this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1; }
        public bool Contains(float x, float z) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
        public bool Overlaps(Rect2 o) => x0 < o.x1 && x1 > o.x0 && z0 < o.z1 && z1 > o.z0;
    }

    public static readonly Rect2 Plateau = new(-560, 100, -330, 330);
    public static readonly Rect2 Square = new(192, -62, 318, 62);
    public static readonly Rect2 PetrinPark = new(-560, -330, -345, 60);
    public static readonly Rect2 ZizkovPark = new(640, 180, 770, 300);

    static readonly Rect2[] BuildingKeepOut =
    {
        Square,
        new(316, -20, 340, 20),      // Týn church
        new(174, -46, 199, -4),      // Old Town Hall
        ZizkovPark,
        new(-570, 90, -322, 340),    // castle plateau + margin
        new(-340, 196, -196, 234),   // castle ramp corridor
        PetrinPark,
        new(-246, -81, -174, -9),    // St. Nicholas block
        new(-250, 196, -170, 234),   // ramp landing road
    };

    public static readonly float[] BridgeZ = { -360f, 0f, 360f };

    public static readonly Landmark[] Landmarks =
    {
        new() { name = "Karlův most",            pos = new Vector3(0, 0, 0),        radius = 42, minY = -1 },
        new() { name = "Pražský hrad",           pos = new Vector3(-440, 28, 215),  radius = 95, minY = 20 },
        new() { name = "Staroměstské náměstí",   pos = new Vector3(255, 0, 0),      radius = 55, minY = -1 },
        new() { name = "Chrám sv. Mikuláše",     pos = new Vector3(-210, 0, -45),   radius = 40, minY = -1 },
        new() { name = "Žižkovská věž",          pos = new Vector3(705, 0, 240),    radius = 55, minY = -1 },
        new() { name = "Petřínská rozhledna",    pos = new Vector3(-450, 0, -150),  radius = 40, minY = -1 },
    };

    public static string District(Vector3 p)
    {
        if (Plateau.Contains(p.x, p.z) && p.y > 15) return "Hradčany";
        if (Mathf.Abs(p.x) < RiverHalf + 1)
        {
            if (Mathf.Abs(p.z) < 7) return "Karlův most";
            if (Mathf.Abs(p.z - 360) < 11) return "Mánesův most";
            if (Mathf.Abs(p.z + 360) < 11) return "Most Legií";
            return "Vltava";
        }
        if (PetrinPark.Contains(p.x, p.z)) return "Petřín";
        if (p.x < 0) return "Malá Strana";
        if (Square.Contains(p.x, p.z)) return "Staroměstské náměstí";
        if (p.x > 560) return "Žižkov";
        if (p.z < -100) return "Nové Město";
        return "Staré Město";
    }

    // Materials
    static Material mCobble, mCobbleLight, mCobbleDark, mRoad, mAsphalt, mWater, mGrass, mStone, mSlate,
        mCopper, mTerracotta, mCornice, mPlinth, mConcrete, mGold, mFoliage, mTrunk, mRoofFlat, mGothicFacade,
        mClockBlue, mLampGlow, mWarnLight, mStatue, mSteel;
    static Material[] mFacades;

    static void InitMaterials()
    {
        mCobble = Gfx.Mat(Palette.Cobble, 0.1f, Gfx.CobbleTex);
        mCobbleLight = Gfx.Mat(Color.Lerp(Palette.Cobble, Palette.Sandstone, 0.35f), 0.1f, Gfx.CobbleTex);
        mCobbleDark = Gfx.Mat(Palette.CobbleDark, 0.1f, Gfx.CobbleTex);
        mRoad = Gfx.Mat(Color.white, 0.18f, Gfx.RoadTex);
        mAsphalt = Gfx.Mat(Palette.Asphalt, 0.18f);
        mWater = Gfx.Mat(Palette.River, 0.88f);
        mGrass = Gfx.Mat(Palette.Grass, 0.05f);
        mStone = Gfx.Mat(Palette.GothicStone, 0.08f);
        mSlate = Gfx.Mat(Palette.Slate, 0.25f);
        mCopper = Gfx.Mat(Palette.Copper, 0.3f);
        mTerracotta = Gfx.Mat(Palette.Terracotta, 0.1f);
        mCornice = Gfx.Mat(Palette.Cornice, 0.1f);
        mPlinth = Gfx.Mat(Palette.Plinth, 0.1f);
        mConcrete = Gfx.Mat(Palette.Concrete, 0.35f);
        mGold = Gfx.Mat(Palette.Gold, 0.6f);
        mFoliage = Gfx.Mat(Palette.Foliage, 0.05f);
        mTrunk = Gfx.Mat(Palette.Trunk, 0.05f);
        mRoofFlat = Gfx.Mat(Color.Lerp(Palette.Slate, Palette.Plinth, 0.5f), 0.1f);
        mGothicFacade = Gfx.Mat(Palette.GothicStone, 0.08f, Gfx.WindowTex, "gothic");
        mClockBlue = Gfx.Mat(Palette.Jacket, 0.5f);
        mLampGlow = Gfx.Emissive(Palette.HeadLight, 2.2f);
        mWarnLight = Gfx.Emissive(Palette.Accent, 4f);
        mStatue = Gfx.Mat(Color.Lerp(Palette.GothicStone, Palette.Slate, 0.5f), 0.15f);
        mSteel = Gfx.Mat(Color.Lerp(Palette.Terracotta, Palette.Slate, 0.45f), 0.2f);
        mFacades = new Material[Palette.Facades.Length];
        for (int i = 0; i < mFacades.Length; i++) mFacades[i] = Gfx.Facade(Palette.Facades[i]);
    }

    public static List<(Vector3 pos, float yaw)> ParkingSpots = new();

    public static Transform Build(int seed = 1357)
    {
        Gfx.Init();
        InitMaterials();
        ParkingSpots.Clear();
        var rng = new System.Random(seed);
        var root = new GameObject("Praha").transform;

        BuildGroundAndRiver(root);
        BuildRoads(root);
        BuildBlocks(root, rng);
        BuildBridges(root);
        BuildCastle(root);
        BuildOldTownSquare(root);
        BuildStNicholas(root);
        BuildZizkov(root);
        BuildPetrin(root, rng);
        BuildTrees(root, rng);
        BuildHorizon(root);
        return root;
    }

    static void BoxCol(Transform parent, Vector3 center, Vector3 size, string name = "Col")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.isStatic = true;
        go.AddComponent<BoxCollider>().size = size;
    }

    // ---------------------------------------------------------------- ground & river
    static void BuildGroundAndRiver(Transform root)
    {
        var mb = new MeshBuilder();
        mb.Ground(mCobble, -WorldX, -WorldZ, -RiverHalf, WorldZ, 0, 3, 3);
        mb.Ground(mCobble, RiverHalf, -WorldZ, WorldX, WorldZ, 0, 3, 3);
        // embankment walls
        mb.Poly(mStone, new[] { new Vector3(-RiverHalf, -5, -WorldZ), new Vector3(-RiverHalf, 0, -WorldZ),
            new Vector3(-RiverHalf, 0, WorldZ), new Vector3(-RiverHalf, -5, WorldZ) }, null, Vector3.right);
        mb.Poly(mStone, new[] { new Vector3(RiverHalf, -5, -WorldZ), new Vector3(RiverHalf, 0, -WorldZ),
            new Vector3(RiverHalf, 0, WorldZ), new Vector3(RiverHalf, -5, WorldZ) }, null, Vector3.left);
        // parks
        mb.Ground(mGrass, PetrinPark.x0, PetrinPark.z0, PetrinPark.x1, PetrinPark.z1, 0.04f, 1, 1);
        mb.Ground(mGrass, ZizkovPark.x0, ZizkovPark.z0, ZizkovPark.x1, ZizkovPark.z1, 0.04f, 1, 1);
        mb.Build("Ground", root, false);

        var water = new MeshBuilder();
        water.Ground(mWater, -RiverHalf, -WorldZ - 400, RiverHalf, WorldZ + 400, WaterY, 10, 10);
        water.Build("Vltava", root, false, false);

        // colliders: banks, river bed, world bounds
        BoxCol(root, new Vector3(-(WorldX + RiverHalf) / 2, -5, 0), new Vector3(WorldX - RiverHalf, 10, WorldZ * 2), "BankWest");
        BoxCol(root, new Vector3((WorldX + RiverHalf) / 2, -5, 0), new Vector3(WorldX - RiverHalf, 10, WorldZ * 2), "BankEast");
        BoxCol(root, new Vector3(0, -9, 0), new Vector3(RiverHalf * 2, 10, WorldZ * 2), "RiverBed");
        BoxCol(root, new Vector3(-WorldX - 5, 30, 0), new Vector3(10, 120, WorldZ * 2), "Bound");
        BoxCol(root, new Vector3(WorldX + 5, 30, 0), new Vector3(10, 120, WorldZ * 2), "Bound");
        BoxCol(root, new Vector3(0, 30, -WorldZ - 5), new Vector3(WorldX * 2, 120, 10), "Bound");
        BoxCol(root, new Vector3(0, 30, WorldZ + 5), new Vector3(WorldX * 2, 120, 10), "Bound");

        // river parapets with gaps at the bridges
        var par = new MeshBuilder();
        foreach (float side in new[] { -1f, 1f })
        {
            float z = -WorldZ;
            var gaps = new List<(float a, float b)>();
            foreach (float bz in BridgeZ) { float hw = (bz == 0 ? 5f : 9f) + 0.4f; gaps.Add((bz - hw, bz + hw)); }
            gaps.Sort((p, q) => p.a.CompareTo(q.a));
            foreach (var g in gaps)
            {
                Parapet(par, side * (RiverHalf + 0.3f), z, g.a);
                z = g.b;
            }
            Parapet(par, side * (RiverHalf + 0.3f), z, WorldZ);
        }
        par.Build("Parapets", root, true);
    }

    static void Parapet(MeshBuilder mb, float x, float z0, float z1)
    {
        if (z1 - z0 < 0.5f) return;
        mb.Box(mStone, new Vector3(x, 0.55f, (z0 + z1) / 2), new Vector3(0.6f, 1.1f, z1 - z0));
    }

    // ---------------------------------------------------------------- roads
    public static List<float> RoadXs()
    {
        var xs = new List<float>();
        for (int k = 0; k < 10; k++) { xs.Add(75 + Grid * k); xs.Add(-75 - Grid * k); }
        xs.Sort();
        return xs;
    }

    public static List<float> RoadZs()
    {
        var zs = new List<float>();
        for (int k = 0; k <= 16; k++) zs.Add(-720 + Grid * k);
        return zs;
    }

    static bool RoadBlocked(float x, float z) => Plateau.Contains(x, z);

    static void BuildRoads(Transform root)
    {
        var xs = RoadXs(); var zs = RoadZs();
        var mb = new MeshBuilder();
        float h = RoadW / 2, y = 0.08f;
        foreach (float x in xs)
        {
            for (int j = 0; j < zs.Count; j++)
            {
                float z = zs[j];
                if (!RoadBlocked(x, z) && !Square.Contains(x, z))
                    mb.Ground(mAsphalt, x - h, z - h, x + h, z + h, y, 18, 18);
                if (j + 1 < zs.Count)
                {
                    float za = z + h, zb = zs[j + 1] - h, zc = (za + zb) / 2;
                    if (!RoadBlocked(x, zc) && !Square.Contains(x, zc))
                    {
                        mb.Ground(mRoad, x - h, za, x + h, zb, y, 18, 18);
                        AddParking(x, za, zb, true);
                    }
                }
            }
        }
        foreach (float z in zs)
        {
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float xa = xs[i] + h, xb = xs[i + 1] - h, xc = (xa + xb) / 2;
                if (xa < 0 && xb > 0) continue; // the river
                if (RoadBlocked(xc, z) || Square.Contains(xc, z)) continue;
                mb.Ground(mRoad, xa, z - h, xb, z + h, y, 18, 18, true);
                AddParking(z, xa, xb, false);
            }
        }
        // road bridge approaches across the promenades
        foreach (float bz in BridgeZ)
        {
            var m = bz == 0 ? mCobbleDark : mRoad;
            float hw = bz == 0 ? 5f : 9f;
            mb.Ground(m, RiverHalf, bz - hw, 66, bz + hw, y, 18, 18, true);
            mb.Ground(m, -66, bz - hw, -RiverHalf, bz + hw, y, 18, 18, true);
        }
        // ramp landing towards the castle
        mb.Ground(mAsphalt, -205, 206, -174, 224, y, 18, 18);
        mb.Build("Roads", root, false, false);
    }

    static int parkCounter;
    static void AddParking(float along, float a, float b, bool roadAlongZ)
    {
        parkCounter++;
        if (parkCounter % 7 != 0) return;
        float t = Mathf.Lerp(a + 12, b - 12, (parkCounter * 0.37f) % 1f);
        float side = (parkCounter % 2 == 0) ? 1 : -1;
        if (roadAlongZ) ParkingSpots.Add((new Vector3(along + side * 6.2f, 0.2f, t), side > 0 ? 0 : 180));
        else ParkingSpots.Add((new Vector3(t, 0.2f, along + side * 6.2f), side > 0 ? 270 : 90));
    }

    // ---------------------------------------------------------------- city blocks
    static void BuildBlocks(Transform root, System.Random rng)
    {
        var xs = RoadXs(); var zs = RoadZs();
        var blocksRoot = new GameObject("Bloky").transform;
        blocksRoot.SetParent(root, false);
        var xEdges = new List<float>(xs);
        xEdges.Insert(0, -WorldX + 9); xEdges.Add(WorldX - 9);
        var zEdges = new List<float>(zs);
        zEdges.Insert(0, -WorldZ + 9); zEdges.Add(WorldZ - 9);
        float h = RoadW / 2;
        for (int i = 0; i + 1 < xEdges.Count; i++)
        for (int j = 0; j + 1 < zEdges.Count; j++)
        {
            float x0 = xEdges[i] + h, x1 = xEdges[i + 1] - h, z0 = zEdges[j] + h, z1 = zEdges[j + 1] - h;
            if (x0 < 0 && x1 > 0) continue; // river strip
            if (x1 - x0 < 20 || z1 - z0 < 20) continue;
            var mb = new MeshBuilder();
            bool oldTown = x0 > 0 && x0 < 500 && Mathf.Abs(z0) < 400;
            Block(mb, x0, z0, x1, z1, rng, oldTown);
            if (mb.VertexCount > 0) mb.Build($"Blok_{i}_{j}", blocksRoot, true);
        }
    }

    static void Block(MeshBuilder mb, float x0, float z0, float x1, float z1, System.Random rng, bool oldTown)
    {
        const float inset = 2.5f;
        float ax0 = x0 + inset, ax1 = x1 - inset, az0 = z0 + inset, az1 = z1 - inset;
        float D = 12f + (float)rng.NextDouble() * 3f;
        // south & north rows run along X
        Row(mb, rng, oldTown, ax0, ax1, az0, az0 + D, true, -1);
        Row(mb, rng, oldTown, ax0, ax1, az1 - D, az1, true, +1);
        // west & east rows run along Z (between the north/south rows)
        Row(mb, rng, oldTown, az0 + D, az1 - D, ax0, ax0 + D, false, -1);
        Row(mb, rng, oldTown, az0 + D, az1 - D, ax1 - D, ax1, false, +1);
    }

    // along = a..b on the row axis; across = c..d on the depth axis.
    static void Row(MeshBuilder mb, System.Random rng, bool oldTown, float a, float b, float c, float d, bool alongX, int outwardSign)
    {
        float t = a;
        while (b - t > 6f)
        {
            float w = 10f + (float)rng.NextDouble() * 8f;
            if (b - (t + w) < 9f) w = b - t;
            Rect2 r = alongX ? new Rect2(t, c, t + w, d) : new Rect2(c, t, d, t + w);
            bool blocked = false;
            foreach (var k in BuildingKeepOut) if (k.Overlaps(r)) { blocked = true; break; }
            if (!blocked)
            {
                int floors = oldTown ? 3 + rng.Next(4) : 4 + rng.Next(4);
                var mat = mFacades[rng.Next(mFacades.Length)];
                bool corner = (t == a) || (t + w >= b - 0.01f);
                Building(mb, rng, r, floors, mat, alongX, corner && rng.NextDouble() < 0.25);
            }
            t += w;
        }
    }

    static void Building(MeshBuilder mb, System.Random rng, Rect2 r, int floors, Material facade, bool ridgeAlongX, bool turret, float baseY = 0f)
    {
        float H = floors * 3.4f;
        float cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, w = r.x1 - r.x0, d = r.z1 - r.z0;
        float y0 = baseY, y1 = baseY + H;
        mb.Wall(facade, new Vector3(r.x0, 0, r.z0), new Vector3(r.x1, 0, r.z0), y0, y1, Vector3.back);
        mb.Wall(facade, new Vector3(r.x1, 0, r.z1), new Vector3(r.x0, 0, r.z1), y0, y1, Vector3.forward);
        mb.Wall(facade, new Vector3(r.x0, 0, r.z1), new Vector3(r.x0, 0, r.z0), y0, y1, Vector3.left);
        mb.Wall(facade, new Vector3(r.x1, 0, r.z0), new Vector3(r.x1, 0, r.z1), y0, y1, Vector3.right);
        mb.Ground(mRoofFlat, r.x0, r.z0, r.x1, r.z1, y1, 1, 1);
        mb.Box(mPlinth, new Vector3(cx, y0 + 0.4f, cz), new Vector3(w + 0.2f, 0.8f, d + 0.2f), 0, false);
        mb.Box(mCornice, new Vector3(cx, y1 + 0.25f, cz), new Vector3(w + 0.8f, 0.5f, d + 0.8f));
        var roof = rng.NextDouble() < 0.15 ? mSlate : mTerracotta;
        float rh = 3.5f + (float)rng.NextDouble() * 2.5f;
        if (ridgeAlongX) mb.Prism(roof, facade, new Vector3(cx, y1 + 0.5f, cz), d + 0.9f, w + 0.9f, rh, 90);
        else mb.Prism(roof, facade, new Vector3(cx, y1 + 0.5f, cz), w + 0.9f, d + 0.9f, rh, 0);
        if (turret)
        {
            var corner = new Vector3(rng.NextDouble() < 0.5 ? r.x0 + 0.5f : r.x1 - 0.5f, y0, rng.NextDouble() < 0.5 ? r.z0 + 0.5f : r.z1 - 0.5f);
            mb.Cylinder(facade, corner, 2.4f, H + 2.5f, 10);
            mb.Box(mCornice, corner + Vector3.up * (H + 2.7f), new Vector3(5.2f, 0.4f, 5.2f));
            mb.Cone(mCopper, corner + Vector3.up * (H + 2.9f), 2.7f, 6f, 10);
        }
        // dormers on long roofs
        if (Mathf.Max(w, d) > 13f && rng.NextDouble() < 0.5)
        {
            float len = Mathf.Max(w, d);
            int n = Mathf.FloorToInt(len / 6f);
            for (int i = 0; i < n; i++)
            {
                float t = -len / 2 + (i + 0.5f) * len / n;
                Vector3 p = ridgeAlongX ? new Vector3(cx + t, y1 + 1.6f, r.z0 + d * 0.28f) : new Vector3(r.x0 + w * 0.28f, y1 + 1.6f, cz + t);
                Vector3 p2 = ridgeAlongX ? new Vector3(cx + t, y1 + 1.6f, r.z1 - d * 0.28f) : new Vector3(r.x1 - w * 0.28f, y1 + 1.6f, cz + t);
                mb.Box(mCornice, p, new Vector3(1.6f, 1.8f, 1.6f));
                mb.Box(mCornice, p2, new Vector3(1.6f, 1.8f, 1.6f));
            }
        }
    }

    // ---------------------------------------------------------------- bridges
    static void BuildBridges(Transform root)
    {
        foreach (float bz in BridgeZ)
        {
            bool charles = bz == 0;
            float hw = charles ? 5f : 9f;
            var mb = new MeshBuilder();
            var deck = charles ? mCobbleDark : mAsphalt;
            mb.Box(deck, new Vector3(0, -0.8f, bz), new Vector3(RiverHalf * 2, 1.6f, hw * 2), 3f);
            if (!charles) mb.Ground(mRoad, -RiverHalf, bz - hw, RiverHalf, bz + hw, 0.02f, 18, 18, true);
            var pierMat = charles ? mStone : mConcrete;
            for (float x = -30; x <= 30; x += 15)
            {
                mb.Box(pierMat, new Vector3(x, -6.5f, bz), new Vector3(4.5f, 10f, hw * 2 + 1f), 0, false);
                foreach (float s in new[] { -1f, 1f })
                    mb.Box(pierMat, new Vector3(x, -5.5f, bz + s * (hw + 0.5f)), new Vector3(3.2f, 8f, 3.2f), Quaternion.Euler(0, 45, 0));
            }
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Box(charles ? mStone : mConcrete, new Vector3(0, 0.55f, bz + s * (hw - 0.3f)), new Vector3(RiverHalf * 2, 1.1f, 0.6f));
                if (!charles) continue;
                for (int i = 0; i < 6; i++)
                {
                    float x = -37.5f + i * 15f;
                    bool here = (i % 2 == 0) == (s > 0);
                    var basePos = new Vector3(x, 1.1f, bz + s * (hw - 0.3f));
                    if (here) Statue(mb, basePos);
                    else Lamp(mb, basePos);
                }
            }
            mb.Build(charles ? "KarluvMost" : (bz > 0 ? "ManesuvMost" : "MostLegii"), root, true);
            if (charles)
            {
                BridgeTower(root, 52f, 32f, true);
                BridgeTower(root, -52f, 26f, false);
            }
        }
    }

    static void Statue(MeshBuilder mb, Vector3 p)
    {
        mb.Box(mStone, p + Vector3.up * 0.7f, new Vector3(1.6f, 1.4f, 1.4f));
        mb.Cylinder(mStatue, p + Vector3.up * 1.4f, 0.45f, 1.9f, 7, true, false, 0.3f);
        mb.Dome(mStatue, p + Vector3.up * 3.45f, 0.3f, 1.2f, 7, 3);
        mb.Dome(mStatue, p + Vector3.up * 3.45f, 0.3f, -1.0f, 7, 3);
        mb.Cylinder(mGold, p + Vector3.up * 3.9f, 0.45f, 0.06f, 10); // halo
    }

    static void Lamp(MeshBuilder mb, Vector3 p)
    {
        mb.Cylinder(mStatue, p, 0.09f, 3.2f, 6);
        mb.Box(mLampGlow, p + Vector3.up * 3.45f, new Vector3(0.45f, 0.55f, 0.45f));
        mb.Cone(mSlate, p + Vector3.up * 3.72f, 0.42f, 0.45f, 4);
    }

    static void BridgeTower(Transform root, float cx, float H, bool east)
    {
        var mb = new MeshBuilder();
        float hx = 6f, hz = 9f, passH = 9f, passHalf = 5f;
        // side piers flanking the passage
        mb.Box(mStone, new Vector3(cx, passH / 2, (passHalf + hz) / 2), new Vector3(hx * 2, passH, hz - passHalf), 0, false);
        mb.Box(mStone, new Vector3(cx, passH / 2, -(passHalf + hz) / 2), new Vector3(hx * 2, passH, hz - passHalf), 0, false);
        // upper body
        var r = new Rect2(cx - hx, -hz, cx + hx, hz);
        mb.Wall(mGothicFacade, new Vector3(r.x0, 0, r.z0), new Vector3(r.x1, 0, r.z0), passH, H, Vector3.back, 4f, 7f);
        mb.Wall(mGothicFacade, new Vector3(r.x1, 0, r.z1), new Vector3(r.x0, 0, r.z1), passH, H, Vector3.forward, 4f, 7f);
        mb.Wall(mGothicFacade, new Vector3(r.x0, 0, r.z1), new Vector3(r.x0, 0, r.z0), passH, H, Vector3.left, 4f, 7f);
        mb.Wall(mGothicFacade, new Vector3(r.x1, 0, r.z0), new Vector3(r.x1, 0, r.z1), passH, H, Vector3.right, 4f, 7f);
        mb.Poly(mStone, new[] { new Vector3(r.x0, passH, r.z0), new Vector3(r.x1, passH, r.z0), new Vector3(r.x1, passH, r.z1), new Vector3(r.x0, passH, r.z1) }, null, Vector3.down);
        mb.Box(mStone, new Vector3(cx, H + 0.4f, 0), new Vector3(hx * 2 + 1.2f, 0.8f, hz * 2 + 1.2f));
        mb.Prism(mSlate, mStone, new Vector3(cx, H + 0.8f, 0), hx * 2, hz * 2, east ? 13f : 10f, 0);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            var c = new Vector3(cx + sx * (hx + 0.2f), H + 0.8f, sz * (hz + 0.2f));
            mb.Cylinder(mStone, c, 0.9f, 3f, 8);
            mb.Cone(mSlate, c + Vector3.up * 3f, 1.1f, 6f, 8);
            mb.Dome(mGold, c + Vector3.up * 9.1f, 0.25f, 1f, 6, 2);
        }
        mb.Build(east ? "StaromestskaMosteckaVez" : "MalostranskaMosteckaVez", root, true);
    }

    // ---------------------------------------------------------------- castle
    static void BuildCastle(Transform root)
    {
        var P = Plateau; float Y = PlateauH;
        var mb = new MeshBuilder();
        // plateau: retaining walls + paved top
        mb.Box(mStone, new Vector3((P.x0 + P.x1) / 2, Y / 2, (P.z0 + P.z1) / 2), new Vector3(P.x1 - P.x0, Y, P.z1 - P.z0), 4f, false);
        mb.Ground(mCobbleLight, P.x0, P.z0, P.x1, P.z1, Y, 3, 3);
        // edge parapet (gap where the ramp arrives)
        mb.Box(mStone, new Vector3((P.x0 + P.x1) / 2, Y + 0.5f, P.z0 + 0.3f), new Vector3(P.x1 - P.x0, 1f, 0.6f));
        mb.Box(mStone, new Vector3((P.x0 + P.x1) / 2, Y + 0.5f, P.z1 - 0.3f), new Vector3(P.x1 - P.x0, 1f, 0.6f));
        mb.Box(mStone, new Vector3(P.x0 + 0.3f, Y + 0.5f, (P.z0 + P.z1) / 2), new Vector3(0.6f, 1f, P.z1 - P.z0));
        mb.Box(mStone, new Vector3(P.x1 - 0.3f, Y + 0.5f, (P.z0 + 207) / 2), new Vector3(0.6f, 1f, 207 - P.z0));
        mb.Box(mStone, new Vector3(P.x1 - 0.3f, Y + 0.5f, (223 + P.z1) / 2), new Vector3(0.6f, 1f, P.z1 - 223));
        // ramp down to Malá Strana
        float rx0 = P.x1, rx1 = -205f, len = Mathf.Sqrt((rx1 - rx0) * (rx1 - rx0) + Y * Y);
        float ang = Mathf.Atan2(Y, rx1 - rx0) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0, 0, -ang);
        var rc = new Vector3((rx0 + rx1) / 2, Y / 2, 215f) + rot * new Vector3(0, -0.5f, 0);
        mb.Box(mAsphalt, rc, new Vector3(len, 1f, 14f), rot, 0f, true, true);
        mb.Box(mStone, new Vector3((rx0 + rx1) / 2, Y / 2, 207.7f) + rot * new Vector3(0, 0.5f, 0), new Vector3(len, 1f, 0.6f), rot);
        mb.Box(mStone, new Vector3((rx0 + rx1) / 2, Y / 2, 222.3f) + rot * new Vector3(0, 0.5f, 0), new Vector3(len, 1f, 0.6f), rot);
        mb.Build("Hradcany", root, true);

        var cb = new MeshBuilder();
        var cream = Gfx.Facade(Palette.Cornice);
        var rngC = new System.Random(77);
        // palace wings around the courtyards (gate in the east wing)
        Building(cb, rngC, new Rect2(-372, 150, -342, 206), 5, cream, false, false, Y);
        Building(cb, rngC, new Rect2(-372, 224, -342, 300), 5, cream, false, false, Y);
        cb.Box(mStone, new Vector3(-357, Y + 13.5f, 215), new Vector3(30, 7, 18)); // gate bridge over the passage
        cb.Box(mGold, new Vector3(-341.8f, Y + 12.5f, 215), new Vector3(0.3f, 2.5f, 6f));
        Building(cb, rngC, new Rect2(-530, 300, -372, 318), 4, cream, true, false, Y);
        Building(cb, rngC, new Rect2(-530, 112, -372, 130), 4, cream, true, false, Y);
        Building(cb, rngC, new Rect2(-548, 112, -530, 318), 4, cream, false, false, Y);
        cb.Build("PrazskyHrad", root, true);

        // St. Vitus Cathedral
        var sv = new MeshBuilder();
        float nx0 = -490, nx1 = -405, nz0 = 196, nz1 = 228, NH = 32;
        sv.Wall(mGothicFacade, new Vector3(nx0, 0, nz0), new Vector3(nx1, 0, nz0), Y, Y + NH, Vector3.back, 6f, 12f);
        sv.Wall(mGothicFacade, new Vector3(nx1, 0, nz1), new Vector3(nx0, 0, nz1), Y, Y + NH, Vector3.forward, 6f, 12f);
        sv.Wall(mGothicFacade, new Vector3(nx0, 0, nz1), new Vector3(nx0, 0, nz0), Y, Y + NH, Vector3.left, 6f, 12f);
        sv.Wall(mStone, new Vector3(nx1, 0, nz0), new Vector3(nx1, 0, nz1), Y, Y + NH, Vector3.right);
        sv.Prism(mSlate, mStone, new Vector3((nx0 + nx1) / 2, Y + NH, (nz0 + nz1) / 2), nz1 - nz0 + 1, nx1 - nx0 + 1, 15, 90);
        for (float x = nx0 + 8; x < nx1; x += 9)
            foreach (float z in new[] { nz0 - 0.5f, nz1 + 0.5f })
            {
                sv.Box(mStone, new Vector3(x, Y + NH / 2, z), new Vector3(1.6f, NH, 2.2f));
                sv.Cone(mCopper, new Vector3(x, Y + NH, z), 1.1f, 5f, 4);
            }
        // apse
        sv.Cylinder(mGothicFacade, new Vector3(nx1, Y, (nz0 + nz1) / 2), 16f, NH - 2, 10);
        sv.Cone(mSlate, new Vector3(nx1, Y + NH - 2, (nz0 + nz1) / 2), 16.5f, 11f, 10);
        // west towers with spires
        foreach (float tz in new[] { 196f, 218f })
        {
            var tc = new Vector3(nx0 - 4, Y, tz + 5);
            sv.Box(mGothicFacade, tc + Vector3.up * 26f, new Vector3(10, 52, 10), 6f);
            sv.Box(mStone, tc + Vector3.up * 52.4f, new Vector3(11, 0.8f, 11));
            sv.Cone(mSlate, tc + Vector3.up * 52.8f, 7.4f, 30f, 4);
            sv.Dome(mGold, tc + Vector3.up * 83f, 0.5f, 1f, 6, 2);
            foreach (float sx in new[] { -4.6f, 4.6f })
            foreach (float sz in new[] { -4.6f, 4.6f })
                sv.Cone(mCopper, tc + new Vector3(sx, 52.8f, sz), 0.8f, 6f, 6);
        }
        // great south tower with copper helmet
        var gt = new Vector3(-438, Y, 236);
        sv.Box(mGothicFacade, gt + Vector3.up * 28f, new Vector3(14, 56, 14), 6f);
        sv.Box(mStone, gt + Vector3.up * 56.4f, new Vector3(15, 0.8f, 15));
        sv.Cylinder(mCopper, gt + Vector3.up * 56.8f, 6.5f, 4f, 12);
        sv.Dome(mCopper, gt + Vector3.up * 60.8f, 6.5f, 1.5f, 12, 5);
        sv.Cylinder(mCopper, gt + Vector3.up * 70.5f, 1.8f, 4f, 8);
        sv.Cone(mCopper, gt + Vector3.up * 74.5f, 2.2f, 7f, 8);
        sv.Dome(mGold, gt + Vector3.up * 81.5f, 0.6f, 1f, 6, 2);
        sv.Build("KatedralaSvVita", root, true);
    }

    // ---------------------------------------------------------------- Old Town Square
    static void BuildOldTownSquare(Transform root)
    {
        var mb = new MeshBuilder();
        mb.Ground(mCobbleLight, Square.x0, Square.z0, Square.x1, Square.z1, 0.06f, 3, 3);

        // Týn church: twin towered front facing the square (west), nave behind
        float fx0 = 318, fx1 = 326;
        mb.Wall(mGothicFacade, new Vector3(fx0, 0, -6), new Vector3(fx0, 0, 6), 0, 30, Vector3.left, 4f, 8f);
        mb.Box(mStone, new Vector3((fx0 + fx1) / 2, 15, 0), new Vector3(fx1 - fx0 - 0.2f, 30, 12), 0, true);
        mb.Prism(mSlate, mStone, new Vector3((fx0 + fx1) / 2, 30, 0), 12, fx1 - fx0, 9, 90);
        mb.Box(mStone, new Vector3(331, 13, 0), new Vector3(10, 26, 24), 0, false);
        mb.Prism(mSlate, mStone, new Vector3(331, 26, 0), 25, 11, 13, 90);
        foreach (float tz in new[] { -10f, 10f })
        {
            var tc = new Vector3((fx0 + fx1) / 2, 0, tz);
            mb.Box(mGothicFacade, tc + Vector3.up * 22f, new Vector3(8, 44, 8), 4f);
            mb.Cone(mSlate, tc + Vector3.up * 44f, 5.9f, 26f, 4);
            mb.Dome(mGold, tc + Vector3.up * 70.3f, 0.6f, 1f, 6, 2);
            foreach (float sx in new[] { -4f, 4f })
            foreach (float sz in new[] { -4f, 4f })
            {
                mb.Cone(mSlate, tc + new Vector3(sx, 44f, sz), 1.2f, 9f, 8);
                mb.Dome(mGold, tc + new Vector3(sx, 53.2f, sz), 0.3f, 1f, 6, 2);
            }
            foreach (float sx in new[] { -2f, 2f })
                mb.Cone(mSlate, tc + new Vector3(sx, 50f, 0), 0.8f, 7f, 8);
        }

        // Old Town Hall: tower with gallery, corner turrets and the astronomical clock
        var t0 = new Vector3(191, 0, -29);
        var hall = new Rect2(186, -34, 196, -24);
        mb.Wall(mGothicFacade, new Vector3(hall.x0, 0, hall.z0), new Vector3(hall.x1, 0, hall.z0), 0, 52, Vector3.back, 3.4f, 6.5f);
        mb.Wall(mGothicFacade, new Vector3(hall.x1, 0, hall.z1), new Vector3(hall.x0, 0, hall.z1), 0, 52, Vector3.forward, 3.4f, 6.5f);
        mb.Wall(mGothicFacade, new Vector3(hall.x0, 0, hall.z1), new Vector3(hall.x0, 0, hall.z0), 0, 52, Vector3.left, 3.4f, 6.5f);
        mb.Wall(mStone, new Vector3(hall.x1, 0, hall.z0), new Vector3(hall.x1, 0, hall.z1), 0, 52, Vector3.right);
        mb.Box(mStone, t0 + Vector3.up * 52.5f, new Vector3(12.5f, 1f, 12.5f));
        mb.Box(mStone, t0 + Vector3.up * 54f, new Vector3(9.4f, 2f, 9.4f));
        mb.Cone(mSlate, t0 + Vector3.up * 55f, 6.8f, 13f, 4);
        mb.Dome(mGold, t0 + Vector3.up * 68.2f, 0.5f, 1f, 6, 2);
        foreach (float sx in new[] { -5.6f, 5.6f })
        foreach (float sz in new[] { -5.6f, 5.6f })
        {
            mb.Cylinder(mStone, t0 + new Vector3(sx, 53f, sz), 0.9f, 3.5f, 8);
            mb.Cone(mSlate, t0 + new Vector3(sx, 56.5f, sz), 1.15f, 5f, 8);
        }
        ClockFace(mb, new Vector3(196.12f, 11f, -29f), 3.1f, true);
        ClockFace(mb, new Vector3(196.12f, 4.6f, -29f), 2.4f, false);
        mb.Box(mStone, new Vector3(196.5f, 16.5f, -29f), new Vector3(1f, 2.6f, 8.5f));
        mb.Prism(mSlate, mStone, new Vector3(197.4f, 17.8f, -29f), 2f, 8.5f, 2f, 0);
        var rngH = new System.Random(5);
        Building(mb, rngH, new Rect2(178, -24, 196, -6), 4, mFacades[2], false, false);
        Building(mb, rngH, new Rect2(178, -46, 196, -34), 4, mFacades[0], false, false);

        // Jan Hus memorial
        var hc = new Vector3(255, 0.06f, 0);
        mb.Box(mStone, hc + Vector3.up * 0.75f, new Vector3(14, 1.5f, 8));
        mb.Box(mStatue, hc + Vector3.up * 2.4f, new Vector3(11, 1.8f, 5.5f));
        mb.Cylinder(mStatue, hc + Vector3.up * 3.3f, 1.1f, 4.4f, 8, true, false, 0.7f);
        mb.Dome(mStatue, hc + Vector3.up * 8.1f, 0.6f, 1.2f, 8, 3);
        mb.Dome(mStatue, hc + Vector3.up * 8.1f, 0.6f, -0.8f, 8, 3);
        for (int i = 0; i < 6; i++)
        {
            float x = -4.5f + i * 1.8f;
            mb.Cylinder(mStatue, hc + new Vector3(x, 3.3f, (i % 2 == 0) ? 1.4f : -1.4f), 0.45f, 2.1f, 6, true, false, 0.3f);
            mb.Dome(mStatue, hc + new Vector3(x, 5.75f, (i % 2 == 0) ? 1.4f : -1.4f), 0.3f, 1.1f, 6, 2);
        }
        mb.Build("StaromestskeNamesti", root, true);
    }

    static void ClockFace(MeshBuilder mb, Vector3 c, float r, bool astro)
    {
        int n = 16;
        Vector3[] Ring(float rad, float dx)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                pts[i] = c + new Vector3(dx, Mathf.Sin(a) * rad, Mathf.Cos(a) * rad);
            }
            return pts;
        }
        mb.Box(mGold, c + new Vector3(0.05f, 0, 0), new Vector3(0.1f, r * 2.3f, r * 2.3f));
        mb.Poly(mClockBlue, Ring(r, 0.12f), null, Vector3.right);
        if (astro)
        {
            mb.Poly(mGold, Ring(r * 0.62f, 0.14f), null, Vector3.right);
            mb.Poly(mClockBlue, Ring(r * 0.5f, 0.16f), null, Vector3.right);
            mb.Box(mGold, c + new Vector3(0.2f, r * 0.35f, 0), new Vector3(0.05f, r * 0.8f, 0.12f));
        }
        else mb.Poly(mGold, Ring(r * 0.25f, 0.14f), null, Vector3.right);
    }

    // ---------------------------------------------------------------- St. Nicholas (Malá Strana)
    static void BuildStNicholas(Transform root)
    {
        var mb = new MeshBuilder();
        mb.Ground(mCobbleLight, -246, -81, -174, -9, 0.06f, 3, 3);
        var facade = Gfx.Facade(Color.Lerp(Palette.Cornice, Palette.DustyPink, 0.3f));
        var rng = new System.Random(11);
        Building(mb, rng, new Rect2(-236, -60, -200, -30), 5, facade, true, false);
        var dc = new Vector3(-196, 0, -45);
        mb.Cylinder(facade, dc, 11f, 24f, 14);
        mb.Box(mCornice, dc + Vector3.up * 24.3f, new Vector3(23.5f, 0.6f, 23.5f));
        mb.Cylinder(facade, dc + Vector3.up * 24.6f, 8.5f, 6f, 14);
        mb.Dome(mCopper, dc + Vector3.up * 30.6f, 8.5f, 1.25f, 14, 6);
        mb.Cylinder(mCornice, dc + Vector3.up * 41f, 1.8f, 4f, 8);
        mb.Dome(mCopper, dc + Vector3.up * 45f, 2.1f, 1.3f, 8, 3);
        mb.Cone(mCopper, dc + Vector3.up * 47.5f, 0.9f, 3.5f, 8);
        var bt = new Vector3(-180, 0, -24);
        mb.Box(facade, bt + Vector3.up * 21f, new Vector3(8, 42, 8), 3.4f);
        mb.Box(mCornice, bt + Vector3.up * 42.3f, new Vector3(9, 0.6f, 9));
        mb.Dome(mCopper, bt + Vector3.up * 42.6f, 4.4f, 1.4f, 8, 4);
        mb.Cylinder(mCopper, bt + Vector3.up * 48.5f, 1.2f, 3f, 8);
        mb.Cone(mCopper, bt + Vector3.up * 51.5f, 1.5f, 5f, 8);
        mb.Build("ChramSvMikulase", root, true);
    }

    // ---------------------------------------------------------------- Žižkov TV tower
    static void BuildZizkov(Transform root)
    {
        var mb = new MeshBuilder();
        var c = new Vector3(705, 0, 240);
        mb.Box(mConcrete, c + new Vector3(0, 3, 0), new Vector3(34, 6, 26));
        mb.Cylinder(mConcrete, c, 4.8f, 132f, 16);
        mb.Cylinder(mConcrete, c + new Vector3(7.5f, 0, -6), 3.3f, 118f, 14);
        mb.Cylinder(mConcrete, c + new Vector3(-7.5f, 0, -6), 3.3f, 96f, 14);
        mb.Cylinder(mConcrete, c + Vector3.up * 62f, 9.5f, 10f, 16, true, true, 11f);
        mb.Cylinder(mConcrete, c + Vector3.up * 84f, 8.5f, 9f, 16, true, true, 9.5f);
        mb.Cylinder(mConcrete, c + Vector3.up * 112f, 6.5f, 7f, 16, true, true, 7.5f);
        mb.Cylinder(mSlate, c + Vector3.up * 66f, 11.05f, 2.4f, 16, false);
        mb.Cylinder(mSlate, c + Vector3.up * 87.5f, 9.6f, 2.2f, 16, false);
        mb.Cylinder(mConcrete, c + Vector3.up * 132f, 1.2f, 22f, 8);
        mb.Box(mWarnLight, c + Vector3.up * 154.5f, new Vector3(1.2f, 1f, 1.2f));
        mb.Box(mWarnLight, c + new Vector3(7.5f, 118.5f, -6), new Vector3(1f, 1f, 1f));
        mb.Build("ZizkovskaVez", root, true);
    }

    // ---------------------------------------------------------------- Petřín lookout tower
    static void BuildPetrin(Transform root, System.Random rng)
    {
        var mb = new MeshBuilder();
        var c = new Vector3(-450, 0, -150);
        float H = 58f;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            var a = c + new Vector3(sx * 7f, 0, sz * 7f);
            var b = c + new Vector3(sx * 1.4f, H, sz * 1.4f);
            var dir = b - a;
            mb.Box(mSteel, (a + b) / 2, new Vector3(0.7f, dir.magnitude, 0.7f), Quaternion.FromToRotation(Vector3.up, dir));
        }
        foreach (float y in new[] { 1.5f, 20f, 40f })
        {
            float half = Mathf.Lerp(7.3f, 1.7f, y / H);
            mb.Box(mSteel, c + Vector3.up * y, new Vector3(half * 2, 0.5f, half * 2));
        }
        for (float y = 6f; y < H - 4; y += 7f)
        {
            float half = Mathf.Lerp(7f, 1.4f, y / H);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Box(mSteel, c + new Vector3(0, y, s * half), new Vector3(half * 2, 0.25f, 0.25f), Quaternion.Euler(0, 0, 38));
                mb.Box(mSteel, c + new Vector3(s * half, y, 0), new Vector3(0.25f, 0.25f, half * 2), Quaternion.Euler(38, 0, 0));
            }
        }
        mb.Box(mCornice, c + Vector3.up * (H + 1.5f), new Vector3(4, 3, 4));
        mb.Cone(mSlate, c + Vector3.up * (H + 3f), 3f, 4f, 4);
        mb.Box(mWarnLight, c + Vector3.up * (H + 7.2f), new Vector3(0.5f, 0.5f, 0.5f));
        mb.Build("PetrinskaRozhledna", root, true);
    }

    // ---------------------------------------------------------------- trees
    static void BuildTrees(Transform root, System.Random rng)
    {
        var mb = new MeshBuilder();
        var cols = new GameObject("TreeColliders").transform;
        cols.SetParent(root, false);
        void Tree(Vector3 p, float s)
        {
            mb.Cylinder(mTrunk, p, 0.28f * s, 2.6f * s, 6);
            var cc = p + Vector3.up * (3.9f * s);
            mb.Dome(mFoliage, cc, 2.5f * s, 1.15f, 7, 3);
            mb.Dome(mFoliage, cc, 2.5f * s, -0.6f, 7, 2);
            BoxCol(cols, p + Vector3.up * 1.3f * s, new Vector3(0.6f * s, 2.6f * s, 0.6f * s), "Tree");
        }
        // riverside promenades
        foreach (float side in new[] { -1f, 1f })
            for (float z = -WorldZ + 10; z < WorldZ - 10; z += 14f)
            {
                bool nearBridge = false;
                foreach (float bz in BridgeZ) if (Mathf.Abs(z - bz) < 22) nearBridge = true;
                if (nearBridge) continue;
                Tree(new Vector3(side * 57f, 0, z), 0.9f + (float)rng.NextDouble() * 0.3f);
            }
        // parks
        void Scatter(Rect2 r, int count, Vector3 avoid, float avoidR)
        {
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(Mathf.Lerp(r.x0 + 4, r.x1 - 4, (float)rng.NextDouble()), 0,
                    Mathf.Lerp(r.z0 + 4, r.z1 - 4, (float)rng.NextDouble()));
                if ((p - avoid).sqrMagnitude < avoidR * avoidR) continue;
                bool onRoad = false;
                foreach (float x in RoadXs()) if (Mathf.Abs(p.x - x) < 11) onRoad = true;
                foreach (float z in RoadZs()) if (Mathf.Abs(p.z - z) < 11) onRoad = true;
                if (onRoad) continue;
                Tree(p, 0.9f + (float)rng.NextDouble() * 0.6f);
            }
        }
        Scatter(PetrinPark, 260, new Vector3(-450, 0, -150), 14);
        Scatter(ZizkovPark, 50, new Vector3(705, 0, 240), 22);
        // Old Town Square corners
        foreach (var p in new[] { new Vector3(205, 0, 52), new Vector3(305, 0, 52), new Vector3(225, 0, -52), new Vector3(300, 0, -52) })
            Tree(p, 1f);
        mb.Build("Stromy", root, false);
    }

    // ---------------------------------------------------------------- distant hills
    static void BuildHorizon(Transform root)
    {
        var mb = new MeshBuilder();
        var hill = Gfx.Mat(Color.Lerp(Palette.Grass, Palette.SkyTop, 0.3f), 0f);
        var rng = new System.Random(3);
        for (int i = 0; i < 40; i++)
        {
            float a = i / 40f * Mathf.PI * 2;
            float r = 1250f + (float)rng.NextDouble() * 200f;
            var p = new Vector3(Mathf.Sin(a) * r * 1.1f, -20f, Mathf.Cos(a) * r);
            mb.Dome(hill, p, 180f + (float)rng.NextDouble() * 140f, 0.32f + (float)rng.NextDouble() * 0.2f, 10, 4);
        }
        var field = Gfx.Mat(Color.Lerp(Palette.Grass, Palette.SkyTop, 0.15f), 0f);
        const float F = 2200f, y = -0.6f;
        mb.Ground(field, -F, -F, -WorldX, F, y, 50, 50);
        mb.Ground(field, WorldX, -F, F, F, y, 50, 50);
        mb.Ground(field, -WorldX, WorldZ, -RiverHalf, F, y, 50, 50);
        mb.Ground(field, RiverHalf, WorldZ, WorldX, F, y, 50, 50);
        mb.Ground(field, -WorldX, -F, -RiverHalf, -WorldZ, y, 50, 50);
        mb.Ground(field, RiverHalf, -F, WorldX, -WorldZ, y, 50, 50);
        mb.Build("Horizont", root, false, false);
    }
}
