using System;
using System.Collections.Generic;
using UnityEngine;

// The real mainline railways (Tools/fetch_rail.py, OSM): ballast with sleepers and rails on the ground, on IPR's bridge
// models where they exist (Železniční most, Negrelli viaduct) or on our own deck and piers, hidden in the tunnels (Vítkov,
// Vyšehrad). Trains after photos of the ones that run here: ČD class 471 CityElefant (double-deck, three cars, ČD two-tone
// blue) and the class 640 RegioPanter (single-deck, three cars); they stop at Praha hl. n. and Masarykovo nádraží.
public class Rail : MonoBehaviour
{
    [Serializable] public class Track { public float[] p; public bool b, t, s; }
    [Serializable] public class Station { public string n; public float[] p; }
    [Serializable] public class Data { public Track[] tracks; public Station[] stations; }

    public static Rail I;
    public readonly Net net = new();
    public Vector3[] stations = new Vector3[0]; public string[] stationNames = new string[0];
    readonly HashSet<Vector2Int> tunnel = new();
    readonly List<RailTrain> trains = new();
    readonly System.Random rng = new(5);
    float spawn = 3f;
    const float TCell = 6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { I = null; }

    public bool InTunnel(Vector3 p) => tunnel.Contains(new Vector2Int(Mathf.FloorToInt(p.x / TCell), Mathf.FloorToInt(p.z / TCell)));

    void Start()
    {
        I = this;
        var a = Resources.Load<TextAsset>("PrahaRail"); if (a == null) return;
        var d = JsonUtility.FromJson<Data>(a.text);
        Physics.SyncTransforms();
        stations = new Vector3[d.stations.Length]; stationNames = new string[d.stations.Length];
        for (int k = 0; k < stations.Length; k++) { var p = d.stations[k].p; stations[k] = new Vector3(p[0], MapBuilder.Height(p[0], p[1]), p[1]); stationNames[k] = d.stations[k].n; }

        var ballast = Gfx.Mat(Color.white, 0.08f, BallastTex(), "railBallast");
        var steel = Gfx.Mat(new Color(0.5f, 0.48f, 0.46f), 0.75f); var deckM = Gfx.Mat(new Color(0.48f, 0.46f, 0.43f), 0.15f);
        var tiles = new Dictionary<Vector2Int, MeshBuilder>();
        MeshBuilder Tile(Vector3 p) { var k = new Vector2Int(Mathf.FloorToInt(p.x / 300f), Mathf.FloorToInt(p.z / 300f)); if (!tiles.TryGetValue(k, out var m)) tiles[k] = m = new MeshBuilder(); return m; }
        int drawn = 0;
        foreach (var tr in d.tracks)
        {
            int n = tr.p.Length / 2; if (n < 2) continue;
            var y = new float[n]; var own = new bool[n]; // own: on our deck (no IPR bridge under it)
            for (int i = 0; i < n; i++) y[i] = MapBuilder.Height(tr.p[2 * i], tr.p[2 * i + 1]) + 0.35f;
            if (tr.b || tr.t)
            {
                // straight from bank to bank (or portal to portal); on a real IPR bridge model, its deck
                float y0 = y[0], y1 = y[n - 1], len = 0, run = 0; for (int i = 1; i < n; i++) len += Dist(tr.p, i - 1, i);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) run += Dist(tr.p, i - 1, i);
                    float lin = Mathf.Lerp(y0, y1, len > 0 ? run / len : 0);
                    if (tr.t) { y[i] = Mathf.Min(lin, y[i]); continue; }
                    y[i] = Mathf.Max(lin, y[i]); own[i] = true;
                    if (Physics.Raycast(new Vector3(tr.p[2 * i], 300f, tr.p[2 * i + 1]), Vector3.down, out var hit, 600f) && hit.collider.name.StartsWith("IprMosty"))
                    { y[i] = hit.point.y + 0.35f; own[i] = false; }
                }
            }
            if (!tr.s) net.Add(tr.p, y, 0f);
            if (tr.t)
            {
                for (int i = 0; i + 1 < n; i++)
                {
                    var a0 = P(tr.p, i); var b0 = P(tr.p, i + 1); float l = Vector3.Distance(a0, b0);
                    for (float s = 0; s <= l; s += 2f) { var q = Vector3.Lerp(a0, b0, l > 0 ? s / l : 0); for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) tunnel.Add(new Vector2Int(Mathf.FloorToInt(q.x / TCell) + dx, Mathf.FloorToInt(q.z / TCell) + dz)); }
                }
                continue; // nothing to draw in a tunnel
            }
            float v = 0;
            for (int i = 0; i + 1 < n; i++)
            {
                var a0 = P(tr.p, i); a0.y = y[i]; var b0 = P(tr.p, i + 1); b0.y = y[i + 1];
                var f = b0 - a0; f.y = 0; float l = f.magnitude; if (l < 0.05f) continue; f /= l;
                var sd = new Vector3(f.z, 0, -f.x) * 1.6f; var mb = Tile(a0); float v1 = v + l / 2.4f;
                mb.Poly(ballast, new[] { a0 - sd, a0 + sd, b0 + sd, b0 - sd }, new[] { new Vector2(0, v), new Vector2(1, v), new Vector2(1, v1), new Vector2(0, v1) }, Vector3.up);
                // ballast shoulders down to the ground
                foreach (float e in new[] { -1f, 1f })
                {
                    var o = sd * e; var out2 = sd * e * 1.6f;
                    mb.Poly(ballast, new[] { a0 + o, b0 + o, b0 + out2 + Vector3.down * 0.6f, a0 + out2 + Vector3.down * 0.6f }, null, o + Vector3.up);
                }
                var rot = Quaternion.LookRotation(b0 - a0);
                foreach (float rx in new[] { -0.75f, 0.75f }) mb.Box(steel, (a0 + b0) / 2f + new Vector3(f.z, 0, -f.x) * rx + Vector3.up * 0.08f, new Vector3(0.08f, 0.15f, (b0 - a0).magnitude + 0.02f), rot);
                if (own[i] && own[i + 1]) // our own bridge: a deck slab, and piers down to the ground
                {
                    mb.Box(deckM, (a0 + b0) / 2f + Vector3.down * 0.7f, new Vector3(4.6f, 0.8f, (b0 - a0).magnitude + 0.05f), rot, 0, true, true);
                    for (float s = 0; s < l; s += 22f)
                    {
                        var q = Vector3.Lerp(a0, b0, s / l); float g = MapBuilder.Height(q.x, q.z); float h = q.y - 1.1f - g;
                        if (h > 1.5f) mb.Box(deckM, new Vector3(q.x, g + h / 2f, q.z), new Vector3(2.2f, h, 1.6f), rot);
                    }
                }
                v = v1; drawn++;
            }
        }
        net.Finish();
        foreach (var kv in tiles) kv.Value.Build($"Koleje_{kv.Key.x}_{kv.Key.y}", transform, true, false);
        Debug.Log($"[Železnice] {d.tracks.Length} kolejí, {drawn} úseků, stanice: {string.Join(", ", stationNames)}");
    }

    static Vector3 P(float[] p, int i) => new(p[2 * i], 0, p[2 * i + 1]);
    static float Dist(float[] p, int i, int j) { float dx = p[2 * i] - p[2 * j], dz = p[2 * i + 1] - p[2 * j + 1]; return Mathf.Sqrt(dx * dx + dz * dz); }

    // ballast with concrete sleepers every 0.6 m (u across 3.2 m, v along 2.4 m)
    static Texture2D BallastTex()
    {
        const int W = 128, H = 256; var t = new Texture2D(W, H, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
        var px = new Color[W * H]; var r = new System.Random(3);
        for (int yy = 0; yy < H; yy++)
            for (int x = 0; x < W; x++)
            {
                float n = (float)r.NextDouble();
                var c = new Color(0.36f, 0.33f, 0.3f) * (0.65f + 0.55f * n); // granite ballast, rust-stained
                bool sleeper = yy % 64 < 18 && x > 10 && x < 118;
                if (sleeper) c = new Color(0.62f, 0.6f, 0.57f) * (0.9f + 0.1f * n);
                if (yy % 64 == 18 && sleeper) c *= 0.6f;
                px[yy * W + x] = c;
            }
        t.SetPixels(px); t.Apply(true); return t;
    }

    void Update()
    {
        var gm = GameManager.I; if (gm == null || gm.State != GameState.Playing || net.P.Count == 0) return;
        trains.RemoveAll(t => t == null);
        if ((spawn -= Time.deltaTime) <= 0f && trains.Count < 4)
        {
            spawn = 25f + (float)rng.NextDouble() * 30f;
            var focus = gm.player.transform.position;
            if (net.RandomVertex(focus, 250f, 1200f, rng, out int s, out int v) && !Net.InView(net.V(s, v)))
                trains.Add(RailTrain.Create(transform, net, s, v, rng.Next(2) == 0 ? 1 : -1, rng, rng.NextDouble() < 0.55));
        }
    }
}

public class RailTrain : MonoBehaviour
{
    Net net; System.Random rng; int s, i, dir; Vector3 front; float speed, dwell, gone; int lastStop = -1;
    readonly List<Vector3> trail = new(); Transform[] cars; Renderer[][] rends;
    const float Step = 0.5f, CarLen = 26.4f, Cruise = 16f; // ~58 km/h through the city

    public static RailTrain Create(Transform parent, Net net, int s, int v, int dir, System.Random rng, bool elefant)
    {
        var go = new GameObject(elefant ? "CityElefant" : "RegioPanter"); go.transform.SetParent(parent, false);
        var t = go.AddComponent<RailTrain>(); t.net = net; t.rng = rng; t.s = s; t.i = v; t.dir = dir; t.speed = Cruise;
        // ČD two-tone blue (photos of 471 and 640 in today's livery): dark blue below, sky blue band, white top, black glazing
        var dark = Gfx.Mat(new Color(0.06f, 0.15f, 0.38f), 0.55f); var sky = Gfx.Mat(new Color(0.3f, 0.6f, 0.88f), 0.55f);
        var white = Gfx.Mat(new Color(0.92f, 0.93f, 0.94f), 0.5f); var glass = Gfx.Mat(new Color(0.05f, 0.06f, 0.08f), 0.9f);
        var grey = Gfx.Mat(new Color(0.25f, 0.25f, 0.26f), 0.3f); var doorM = Gfx.Mat(new Color(0.3f, 0.6f, 0.88f), 0.6f);
        var head = Gfx.Emissive(Palette.HeadLight, 2f); var tail = Gfx.Emissive(new Color(0.9f, 0.08f, 0.05f), 1.5f);
        t.cars = new Transform[3]; t.rends = new Renderer[3][];
        for (int k = 0; k < 3; k++)
        {
            var car = new GameObject("Vuz" + k); car.transform.SetParent(go.transform, false); car.layer = Traffic.CarLayer; car.tag = "Player";
            var mb = new MeshBuilder(); float L = CarLen - 0.6f, top = elefant ? 4.6f : 4.0f;
            mb.Box(grey, new Vector3(0, 0.55f, 0), new Vector3(2.6f, 0.7f, L - 1f)); // bogies, underframe
            // hollow shells around see-through window bands, the seats inside (blue-grey ČD upholstery)
            var seatC = new Color(0.22f, 0.3f, 0.45f); float zs = -L / 2f + (k == 2 ? 3.2f : 4.4f), ze = L / 2f - (k == 0 ? 3.2f : 4.4f);
            if (elefant) // two decks: low floor between the bogies, the upper deck over it
            {
                Vehicles.Shell(mb, dark, new Vector3(0, (0.9f + 1.2f) / 2f, 0), new Vector3(2.82f, 0.3f, L));
                Vehicles.GlassBand(mb, new Vector3(0, 1.55f, 0), new Vector3(2.84f, 0.7f, L - 7f));
                Vehicles.Shell(mb, dark, new Vector3(0, 1.55f, 0), new Vector3(2.82f, 0.7f, L)); // the ends beyond the band are door bays
                Vehicles.Shell(mb, sky, new Vector3(0, 2.0f, 0), new Vector3(2.83f, 0.22f, L));
                Vehicles.Shell(mb, white, new Vector3(0, 2.48f, 0), new Vector3(2.82f, 0.74f, L));
                Vehicles.GlassBand(mb, new Vector3(0, 3.25f, 0), new Vector3(2.84f, 0.8f, L - 7f));
                Vehicles.Shell(mb, white, new Vector3(0, 4.12f, 0), new Vector3(2.82f, 0.95f, L));
                Vehicles.Saloon(mb, 2.7f, zs, ze, 0.95f, 2.35f, seatC, false, 1.9f);
                Vehicles.Saloon(mb, 2.7f, zs, ze, 2.6f, 4.1f, seatC, false, 1.9f);
            }
            else
            {
                Vehicles.Shell(mb, dark, new Vector3(0, (0.9f + 1.9f) / 2f, 0), new Vector3(2.82f, 1.0f, L));
                Vehicles.Shell(mb, sky, new Vector3(0, 2.0f, 0), new Vector3(2.83f, 0.22f, L));
                Vehicles.Shell(mb, white, new Vector3(0, 2.19f, 0), new Vector3(2.82f, 0.16f, L));
                Vehicles.GlassBand(mb, new Vector3(0, 2.75f, 0), new Vector3(2.84f, 0.95f, L - 2f));
                Vehicles.Shell(mb, white, new Vector3(0, 3.61f, 0), new Vector3(2.82f, 0.78f, L));
                Vehicles.Saloon(mb, 2.7f, zs, ze, 1.25f, 3.3f, seatC, false, 1.9f);
            }
            mb.Box(grey, new Vector3(0, top + 0.1f, 0), new Vector3(2.4f, 0.25f, L - 2f));
            foreach (float sx in new[] { -1f, 1f }) foreach (float dz in new[] { -L / 2f + 3.5f, L / 2f - 3.5f })
                mb.Box(doorM, new Vector3(sx * 1.42f, elefant ? 1.55f : 1.9f, dz), new Vector3(0.04f, elefant ? 1.9f : 2.0f, 1.4f));
            if (k == 1) // pantograph
            {
                var pz = new Vector3(0, top + 0.25f, 3f);
                t.Pant(mb, grey, pz);
            }
            if (k != 1) // the cab: a sloped nose, big windscreen, lights
            {
                float e = k == 0 ? 1f : -1f, z = e * L / 2f;
                mb.Box(dark, new Vector3(0, 1.4f, z + e * 0.6f), new Vector3(2.7f, 1.6f, 1.2f));
                Vehicles.GlassBand(mb, new Vector3(0, 2.6f, z + e * 0.45f), new Vector3(2.5f, 1.3f, 0.9f), Quaternion.Euler(e * -25f, 0, 0));
                mb.Box(grey, new Vector3(0, 1.75f, z - e * 0.25f), new Vector3(2.4f, 0.9f, 0.6f)); // the driver's desk
                mb.Box(grey, new Vector3(0, 2.6f, z - e * 1.9f), new Vector3(2.7f, 2.6f, 0.05f)); // cab back wall
                mb.Box(white, new Vector3(0, top - 0.6f, z + e * 0.1f), new Vector3(2.6f, 1.0f, 0.4f));
                foreach (float hx in new[] { -0.95f, 0.95f }) mb.Box(k == 0 ? head : tail, new Vector3(hx, 1.25f, z + e * 1.21f), new Vector3(0.3f, 0.18f, 0.05f));
                mb.Box(head, new Vector3(0, top - 0.4f, z + e * 0.31f), new Vector3(0.3f, 0.18f, 0.05f));
            }
            var m = mb.Build("Mesh", car.transform, false); m.isStatic = false;
            if (k != 1) { var drv = Vehicles.Driver(car.transform, new Vector3(0, 1.75f, (k == 0 ? 1f : -1f) * (L / 2f - 0.95f)), rng.Next()); if (drv && k == 2) drv.transform.parent.localRotation = Quaternion.Euler(0, 180f, 0); }
            var col = car.AddComponent<BoxCollider>(); col.center = new Vector3(0, top / 2f, 0); col.size = new Vector3(2.85f, top, CarLen);
            var rb = car.AddComponent<Rigidbody>(); rb.isKinematic = true;
            t.cars[k] = car.transform; t.rends[k] = car.GetComponentsInChildren<Renderer>();
        }
        t.front = net.V(s, v); t.trail.Add(t.front);
        t.Advance(3 * CarLen + 2f); t.Pose();
        return t;
    }

    void Pant(MeshBuilder mb, Material m, Vector3 pz)
    {
        mb.Box(m, pz + new Vector3(0, 0.45f, -0.4f), new Vector3(0.06f, 0.06f, 1.4f), Quaternion.Euler(-40, 0, 0));
        mb.Box(m, pz + new Vector3(0, 0.95f, 0.05f), new Vector3(0.06f, 0.06f, 1.3f), Quaternion.Euler(45, 0, 0));
        mb.Box(m, pz + new Vector3(0, 1.4f, -0.2f), new Vector3(1.8f, 0.06f, 0.14f));
    }

    bool dead;
    void Advance(float rem)
    {
        for (int guard = 0; rem > 1e-4f && guard < 64; guard++)
        {
            int j = i + dir;
            if (j < 0 || j >= net.N(s))
            {
                var (ns, nd, at) = net.Next(s, dir, rng);
                if (ns < 0) { dead = true; speed = 0f; break; } // the buffers, or the edge of the map
                s = ns; dir = nd; i = at; continue;
            }
            var b = net.V(s, j); var seg = b - front; float l = seg.magnitude;
            if (l <= rem) { front = b; rem -= l; i = j; } else { front += seg / l * rem; rem = 0f; }
        }
        var last = trail[^1];
        while ((front - last).magnitude >= Step) { last += (front - last).normalized * Step; trail.Add(last); }
        if (trail.Count > 240) trail.RemoveRange(0, trail.Count - 240);
    }

    Vector3 Back(float d) => trail[Mathf.Clamp(trail.Count - 1 - Mathf.RoundToInt(d / Step), 0, trail.Count - 1)];

    void Pose()
    {
        for (int k = 0; k < 3; k++)
        {
            float c = CarLen * (k + 0.5f);
            Vector3 a = Back(c - 9f), b = Back(c + 9f), f = a - b;
            if (f.sqrMagnitude < 1e-4f) f = cars[k].forward;
            var mid = Back(c);
            cars[k].SetPositionAndRotation(mid, Quaternion.LookRotation(f.normalized));
            bool hide = Rail.I != null && Rail.I.InTunnel(mid);
            foreach (var r in rends[k]) if (r.enabled == hide) r.enabled = !hide;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dead)
        {
            // at the buffers (Masarykovo) or the map's edge: stand, then go once nobody looks
            if ((gone += dt) > 40f && !Net.InView(cars[1].position)) Destroy(gameObject);
            return;
        }
        // stations: brake into the platforms, stand 40 s
        var R = Rail.I; float target = Cruise;
        if (R != null)
            for (int k = 0; k < R.stations.Length; k++)
            {
                float d = (new Vector2(front.x, front.z) - new Vector2(R.stations[k].x, R.stations[k].z)).magnitude;
                if (k == lastStop) continue;
                if (d < 260f) target = Mathf.Min(target, Mathf.Lerp(3f, Cruise, (d - 90f) / 170f));
                if (d < 90f) { lastStop = k; dwell = 40f; }
            }
        if (dwell > 0f) { dwell -= dt; speed = Mathf.MoveTowards(speed, 0f, 1.2f * dt); }
        else speed = Mathf.MoveTowards(speed, target, (speed < target ? 0.8f : 1.5f) * dt);
        if (speed > 0.01f) { Advance(speed * dt); Pose(); }
        // far away and unseen: recycle
        var gm = GameManager.I;
        if (gm != null && (front - gm.player.transform.position).sqrMagnitude > 1600f * 1600f && !Net.InView(front)) Destroy(gameObject);
    }
}
