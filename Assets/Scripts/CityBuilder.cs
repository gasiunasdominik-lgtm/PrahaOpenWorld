using System.Collections.Generic;
using UnityEngine;

// Builds Prague: the real city comes from MapBuilder (IPR + Metropolitan Plan + OSM data);
// this class adds the hand-made landmark models on their real spots and owns landmarks/districts.
public static class CityBuilder
{
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
    }




    // real positions (m from the middle of Charles Bridge) taken from OpenStreetMap
    public static readonly Landmark[] Landmarks =
    {
        new() { name = "Karlův most",            pos = new Vector3(0, 0, 0),        radius = 60, minY = -100 },
        new() { name = "Pražský hrad",           pos = new Vector3(-773, 0, 486),   radius = 110, minY = -100 },
        new() { name = "Staroměstské náměstí",   pos = new Vector3(686, 0, 109),    radius = 60, minY = -100 },
        new() { name = "Chrám sv. Mikuláše",     pos = new Vector3(-570, 0, 164),   radius = 45, minY = -100 },
        new() { name = "Žižkovská věž",          pos = new Vector3(2834, 0, -603),  radius = 60, minY = -100 },
        new() { name = "Petřínská rozhledna",    pos = new Vector3(-1166, 0, -326), radius = 45, minY = -100 },
    };

    // Real OSM buildings removed where a hand-made landmark model stands (or the real one would double it).
    static readonly (Vector2 c, float r)[] KeepOut =
    {
        // castle: only the real cathedral footprint (the castle outline stays, its courtyards are holes in the data)
        (new Vector2(-774, 484), 6),
        (new Vector2(-570, 164), 45), (new Vector2(2834, -603), 30), (new Vector2(-1166, -326), 14),
        (new Vector2(808, 114), 28), (new Vector2(663.5f, 63.5f), 9), // real Týn church nave, real Old Town Hall tower (its wings stay real)
    };
    public static bool KeptOut(float x, float z)
    {
        foreach (var (c, r) in KeepOut) if ((new Vector2(x, z) - c).sqrMagnitude < r * r) return true;
        return false;
    }

    public static string District(Vector3 p) => MapBuilder.District(p);

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

        MapBuilder.Build(root, ParkingSpots);
        // landmark models were authored around fixed anchors; move each onto its real spot and ground
        Place(root, "Hrad", BuildCastle, new Vector3(-445, PlateauH, 215), Landmarks[1].pos);
        // Old Town Square: each piece on its own real spot (OSM footprints), not the square model as one block
        Place(root, "TynskyChram", t => BuildOldTownSquare(t, 0), new Vector3(318, 0, 0), new Vector3(786, 0, 124)); // west front behind the Týn school
        Place(root, "Radnice", t => BuildOldTownSquare(t, 1), new Vector3(191, 0, -29), new Vector3(663.5f, 0, 63.5f), 90f); // tower; orloj faces south
        Place(root, "HusuvPomnik", t => BuildOldTownSquare(t, 2), new Vector3(255, 0, 0), new Vector3(700, 0, 123));
        Place(root, "SvMikulas", BuildStNicholas, new Vector3(-210, 0, -45), Landmarks[3].pos);
        Place(root, "ZizkovskaVez", BuildZizkov, new Vector3(705, 0, 240), Landmarks[4].pos);
        Place(root, "Petrin", t => BuildPetrin(t, rng), new Vector3(-450, 0, -150), Landmarks[5].pos);
        return root;
    }

    static void Place(Transform root, string name, System.Action<Transform> build, Vector3 anchor, Vector3 real, float yaw = 0f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        build(t);
        t.rotation = Quaternion.Euler(0, yaw, 0);
        t.position = new Vector3(real.x, MapBuilder.Height(real.x, real.z), real.z) - t.rotation * anchor;
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
        float Y = PlateauH;

        // the palace around it is the real map's buildings (the made-up square courtyard did not match the real castle)
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
    // part 0 = Týn church, 1 = Old Town Hall tower with the orloj, 2 = Jan Hus memorial (model coords, placed by Place)
    static void BuildOldTownSquare(Transform root, int part)
    {
        var mb = new MeshBuilder();
        if (part == 0)
        {
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

        }
        if (part == 1)
        {
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
        }
        if (part == 2)
        {
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
        }
        mb.Build(root.name, root, true);
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
        // the square itself comes from the real map (MPP), only the church is modelled
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


}
