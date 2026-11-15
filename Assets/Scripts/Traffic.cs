using System.Collections.Generic;
using UnityEngine;

// A network of polylines joined where their ends meet (streets for cars, rails for trams).
public class Net
{
    public readonly List<float[]> P = new(), Y = new(); public readonly List<float> W = new(), Len = new();
    readonly Dictionary<Vector2Int, List<(int s, int end)>> ends = new();
    readonly Dictionary<Vector2Int, List<(int s, int v)>> verts = new();
    readonly List<(int, int)> scratch = new();
    const float Cell = 25f;

    readonly List<(List<Vector2> p, List<float> y, float w)> raw = new();

    public void Add(float[] p, float[] y, float w)
    {
        if (p.Length < 4) return;
        var pts = new List<Vector2>(); for (int k = 0; k < p.Length; k += 2) pts.Add(new Vector2(p[k], p[k + 1]));
        raw.Add((pts, new List<float>(y), w));
    }

    // Source lines (MPP streets, OSM rails) run through junctions instead of ending there. Make real junctions:
    // a line end that touches another line's middle gets a vertex there, then every line is cut wherever it meets another.
    public void Finish()
    {
        const float Join = 3f, Same = 2f;
        var grid = new Dictionary<Vector2Int, List<(int l, int v)>>();
        void Index() { grid.Clear(); for (int l = 0; l < raw.Count; l++) for (int v = 0; v < raw[l].p.Count; v++) { var c = CellOf(raw[l].p[v].x, raw[l].p[v].y); if (!grid.TryGetValue(c, out var g)) grid[c] = g = new(); g.Add((l, v)); } }
        Index();
        var inserts = new List<(int l, int after, Vector2 at, float y)>();
        for (int l = 0; l < raw.Count; l++)
            foreach (int e in new[] { 0, raw[l].p.Count - 1 })
            {
                var E = raw[l].p[e]; var c = CellOf(E.x, E.y); float best = Join; (int, int, Vector2, float)? pick = null;
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var g))
                        foreach (var (o, v) in g)
                        {
                            if (o == l || v + 1 >= raw[o].p.Count) continue;
                            Vector2 A = raw[o].p[v], B = raw[o].p[v + 1], ab = B - A;
                            float t = Vector2.Dot(E - A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f);
                            if (t <= 0f || t >= 1f) continue;
                            var q = A + ab * t; float d = (q - E).magnitude;
                            if (d < best && (q - A).magnitude > Same && (q - B).magnitude > Same) { best = d; pick = (o, v, q, Mathf.Lerp(raw[o].y[v], raw[o].y[v + 1], t)); }
                        }
                if (pick.HasValue) inserts.Add(pick.Value);
            }
        // X-crossings at the same height: both lines get a vertex at the crossing point
        var segs = new Dictionary<Vector2Int, List<(int l, int v)>>();
        for (int l = 0; l < raw.Count; l++)
            for (int v = 0; v + 1 < raw[l].p.Count; v++)
            {
                Vector2 A = raw[l].p[v], B = raw[l].p[v + 1];
                var c0 = CellOf(Mathf.Min(A.x, B.x), Mathf.Min(A.y, B.y)); var c1 = CellOf(Mathf.Max(A.x, B.x), Mathf.Max(A.y, B.y));
                for (int cx = c0.x; cx <= c1.x; cx++)
                for (int cz = c0.y; cz <= c1.y; cz++)
                { var k = new Vector2Int(cx, cz); if (!segs.TryGetValue(k, out var g)) segs[k] = g = new(); g.Add((l, v)); }
            }
        var tagged = new List<(int l, int after, Vector2 at, float y, float t)>();
        foreach (var (l, after, at, y) in inserts) tagged.Add((l, after, at, y, ((at - raw[l].p[after]).magnitude)));
        var done = new HashSet<(int, int, int, int)>();
        foreach (var g in segs.Values)
            for (int i = 0; i < g.Count; i++)
            for (int j = i + 1; j < g.Count; j++)
            {
                var (l1, v1) = g[i]; var (l2, v2) = g[j];
                if (l1 == l2 || !done.Add((l1, v1, l2, v2))) continue;
                Vector2 a1 = raw[l1].p[v1], b1 = raw[l1].p[v1 + 1], a2 = raw[l2].p[v2], b2 = raw[l2].p[v2 + 1];
                Vector2 r = b1 - a1, q = b2 - a2; float den = r.x * q.y - r.y * q.x;
                if (Mathf.Abs(den) < 1e-4f) continue;
                Vector2 w0 = a2 - a1; float t = (w0.x * q.y - w0.y * q.x) / den, u = (w0.x * r.y - w0.y * r.x) / den;
                if (t <= 0f || t >= 1f || u <= 0f || u >= 1f) continue;
                float y1 = Mathf.Lerp(raw[l1].y[v1], raw[l1].y[v1 + 1], t), y2 = Mathf.Lerp(raw[l2].y[v2], raw[l2].y[v2 + 1], u);
                if (Mathf.Abs(y1 - y2) > 3f) continue; // bridge over a street: no junction
                var x = a1 + r * t;
                if ((x - a1).magnitude > Same && (x - b1).magnitude > Same) tagged.Add((l1, v1, x, y1, (x - a1).magnitude));
                if ((x - a2).magnitude > Same && (x - b2).magnitude > Same) tagged.Add((l2, v2, x, y2, (x - a2).magnitude));
            }
        // back to front per line (and per segment, farthest first) so earlier indices stay valid
        tagged.Sort((a, b) => a.l != b.l ? a.l.CompareTo(b.l) : a.after != b.after ? b.after.CompareTo(a.after) : b.t.CompareTo(a.t));
        foreach (var (l, after, at, y, _) in tagged) { raw[l].p.Insert(after + 1, at); raw[l].y.Insert(after + 1, y); }
        Index();
        foreach (var (p, y, w) in raw)
        {
            int start = 0;
            for (int v = 1; v < p.Count; v++)
            {
                bool cut = v == p.Count - 1;
                if (!cut)
                {
                    var c = CellOf(p[v].x, p[v].y);
                    for (int dx = -1; dx <= 1 && !cut; dx++)
                    for (int dz = -1; dz <= 1 && !cut; dz++)
                        if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var g))
                            foreach (var (o, ov) in g)
                                if (!ReferenceEquals(raw[o].p, p) && (raw[o].p[ov] - p[v]).sqrMagnitude < Same * Same) { cut = true; break; }
                }
                if (!cut) continue;
                var fp = new float[(v - start + 1) * 2]; var fy = new float[v - start + 1];
                for (int k = start; k <= v; k++) { fp[2 * (k - start)] = p[k].x; fp[2 * (k - start) + 1] = p[k].y; fy[k - start] = y[k]; }
                Put(fp, fy, w); start = v;
            }
        }
        raw.Clear();
    }

    void Put(float[] p, float[] y, float w)
    {
        int s = P.Count; P.Add(p); Y.Add(y); W.Add(w);
        float len = 0; for (int k = 2; k < p.Length; k += 2) len += new Vector2(p[k] - p[k - 2], p[k + 1] - p[k - 1]).magnitude; Len.Add(len);
        for (int v = 0; v < p.Length / 2; v++)
        {
            var c = CellOf(p[2 * v], p[2 * v + 1]);
            if (!verts.TryGetValue(c, out var l)) verts[c] = l = new(); l.Add((s, v));
            if (v != 0 && v != p.Length / 2 - 1) continue;
            if (!ends.TryGetValue(c, out var e)) ends[c] = e = new(); e.Add((s, v));
        }
    }

    static Vector2Int CellOf(float x, float z) => new(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
    public int N(int s) => P[s].Length / 2;
    public Vector3 V(int s, int i) => new(P[s][2 * i], Y[s][i], P[s][2 * i + 1]);

    // Where to go from the far end of (s, dir): another polyline starting there, or (-1,0) at a dead end.
    public (int s, int dir, int at) Next(int s, int dir, System.Random rng, Vector3? goal = null)
    {
        var e = V(s, dir > 0 ? N(s) - 1 : 0); var c = CellOf(e.x, e.z); scratch.Clear();
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            if (ends.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var (t, v) in l)
                    if (t != s && (V(t, v) - e).sqrMagnitude < 16f) scratch.Add((t, v));
        if (scratch.Count > 0)
        {
            int pick = rng.Next(scratch.Count);
            if (goal.HasValue) // head for the goal: the street whose far end gets closest
            {
                float best = float.MaxValue;
                for (int k = 0; k < scratch.Count; k++)
                {
                    var (t, v) = scratch[k]; float d = (V(t, v == 0 ? N(t) - 1 : 0) - goal.Value).sqrMagnitude;
                    if (d < best) { best = d; pick = k; }
                }
            }
            var (ns, nv) = scratch[pick]; return (ns, nv == 0 ? 1 : -1, nv);
        }
        // joins the middle of another line (e.g. a tram switch): carry on along it, either way
        if (verts.TryGetValue(c, out var mid))
            foreach (var (t, v) in mid)
                if (t != s && v > 0 && v < N(t) - 1 && (V(t, v) - e).sqrMagnitude < 2.5f) return (t, rng.Next(2) == 0 ? 1 : -1, v);
        return (-1, 0, 0);
    }

    // Every way on from the far end of (s, dir): (street, dir, entry vertex).
    readonly List<(int s, int dir, int at)> outs = new();
    List<(int s, int dir, int at)> Outs(int s, int dir)
    {
        outs.Clear();
        var e = V(s, dir > 0 ? N(s) - 1 : 0); var c = CellOf(e.x, e.z);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            if (ends.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var (t, v) in l)
                    if (t != s && (V(t, v) - e).sqrMagnitude < 16f) outs.Add((t, v == 0 ? 1 : -1, v));
        if (verts.TryGetValue(c, out var mid))
            foreach (var (t, v) in mid)
                if (t != s && v > 0 && v < N(t) - 1 && (V(t, v) - e).sqrMagnitude < 2.5f) { outs.Add((t, 1, v)); outs.Add((t, -1, v)); }
        return outs;
    }


    // A* over (street, direction) from the end of (s, dir) to the street nearest `goal`. Returns the streets to take, in order.
    public List<(int s, int dir, int at)> Route(int s, int dir, Vector3 goal)
    {
        var path = new List<(int, int, int)>();
        if (!Nearest(goal, out int gs, out _)) return path;
        var g = new Dictionary<(int, int), float> { [(s, dir)] = 0f };
        var came = new Dictionary<(int, int), ((int, int) from, int at)>();
        var open = new List<(int s, int dir)> { (s, dir) }; var closed = new HashSet<(int, int)>();
        while (open.Count > 0 && closed.Count < 6000)
        {
            int bi = 0; float bf = float.MaxValue;
            for (int k = 0; k < open.Count; k++)
            {
                var o = open[k]; float f = g[o] + Vector3.Distance(V(o.s, o.dir > 0 ? N(o.s) - 1 : 0), goal);
                if (f < bf) { bf = f; bi = k; }
            }
            var cur = open[bi]; open.RemoveAt(bi);
            if (cur.s == gs)
            {
                for (var st = cur; came.TryGetValue(st, out var p); st = p.from) path.Add((st.s, st.dir, p.at));
                path.Reverse(); return path;
            }
            closed.Add(cur);
            foreach (var (t, d, at) in Outs(cur.s, cur.dir))
            {
                var nx = (t, d); if (closed.Contains(nx)) continue;
                float ng = g[cur] + Len[t];
                bool known = g.TryGetValue(nx, out var old);
                if (known && old <= ng) continue;
                g[nx] = ng; came[nx] = (cur, at); if (!known) open.Add(nx); // known and not closed = already open
            }
        }
        return path; // unreachable: empty, caller falls back
    }

    public bool Nearest(Vector3 p, out int s, out int v)
    {
        s = v = -1; float best = 120f * 120f; var c = CellOf(p.x, p.z); // pedestrian squares can be ~100 m from a road
        for (int dx = -5; dx <= 5; dx++)
        for (int dz = -5; dz <= 5; dz++)
            if (verts.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var (ls, lv) in l)
                {
                    float ex = P[ls][2 * lv] - p.x, ez = P[ls][2 * lv + 1] - p.z;
                    if (ex * ex + ez * ez < best) { best = ex * ex + ez * ez; s = ls; v = lv; }
                }
        return s >= 0;
    }

    public bool RandomVertex(Vector3 focus, float min, float max, System.Random rng, out int s, out int v)
    {
        // reservoir sample over every vertex in the ring (sparse nets like tram tracks need this)
        s = v = -1; int seen = 0; var c0 = CellOf(focus.x - max, focus.z - max); var c1 = CellOf(focus.x + max, focus.z + max);
        for (int cx = c0.x; cx <= c1.x; cx++)
        for (int cz = c0.y; cz <= c1.y; cz++)
            if (verts.TryGetValue(new Vector2Int(cx, cz), out var l))
                foreach (var (ls, lv) in l)
                {
                    float dx = P[ls][2 * lv] - focus.x, dz = P[ls][2 * lv + 1] - focus.z, d2 = dx * dx + dz * dz;
                    if (d2 < min * min || d2 > max * max) continue;
                    if (rng.Next(++seen) == 0) { s = ls; v = lv; }
                }
        return s >= 0;
    }

    public static bool InView(Vector3 p)
    {
        var cam = Camera.main; if (!cam) return false;
        var q = cam.WorldToViewportPoint(p);
        return q.z > 0 && q.x > -0.15f && q.x < 1.15f && q.y > -0.15f && q.y < 1.15f;
    }
}

// Cars driven by the city: keep right in their lane on real streets, brake for whatever is ahead,
// stop on red at real (OSM) traffic lights, and stop to honk when hit. Only exist around the player.
public class Traffic : MonoBehaviour
{
    public static Traffic I { get; private set; }
    public int maxCars = 28;
    public readonly Net net = new();
    public Vector3[] signals;
    readonly List<Driver> live = new(); readonly Stack<Driver> pool = new();
    readonly System.Random rng = new(3);
    Material[] lamps; // ns red, ns green, ew red, ew green
    float spawnTimer;
    public const int CarLayer = 8, BodyLayer = 9;

    // Two-phase cycle shared by all junctions: N–S green, all red, E–W green, all red.
    public static bool Green(Vector3 fwd)
    {
        float t = Time.time % 32f; bool ns = Mathf.Abs(fwd.z) >= Mathf.Abs(fwd.x);
        return ns ? t < 14f : t >= 16f && t < 30f;
    }

    void Awake()
    {
        I = this;
        var D = MapBuilder.D;
        for (int s = 0; s < D.streets.Length; s++)
            if (!D.streets[s].foot) net.Add( // all non-pedestrian streets: the narrow ones are what keeps the network connected
                D.streets[s].p, MapBuilder.StreetY[s], D.streets[s].w);
        net.Finish();
        signals = new Vector3[D.signals.Length];
        for (int i = 0; i < signals.Length; i++)
        {
            float x = D.signals[i].p[0], z = D.signals[i].p[1];
            signals[i] = new Vector3(x, MapBuilder.Height(x, z), z);
        }
        BuildSignals();
#if UNITY_EDITOR
        // self-check: a route across the centre exists and every step joins the previous street's end
        // shortcut: road bridges are not joined to the embankments in the MPP data, so no route crosses the Vltava yet
        net.Nearest(new Vector3(686f, 0, 80f), out int ps, out _); int pd = 1; // Old Town Square → Žižkov
        var r = net.Route(ps, pd, new Vector3(2000f, 0, -300f));
        Debug.Assert(r.Count > 0, "Net.Route found no path");
        foreach (var (rs, rd, at) in r)
        {
            var end = net.V(ps, pd > 0 ? net.N(ps) - 1 : 0);
            Debug.Assert((net.V(rs, at) - end).sqrMagnitude < 16f, $"Net.Route gap at street {rs}");
            ps = rs; pd = rd;
        }
#endif
    }

    void Start()
    {
        for (int i = 0; i < maxCars; i++)
        {
            var car = CarController.Build(new Vector3(0, -50, 0), 0, Palette.CarPaints[i % Palette.CarPaints.Length], i % 5 == 0 ? 2 : i % 2);
            var d = car.gameObject.AddComponent<Driver>(); d.car = car;
            car.OnCrash = imp => d.Shock();
            car.gameObject.SetActive(false);
            pool.Push(d);
            GameManager.I.cars.Add(car);
        }
    }

    void BuildSignals()
    {
        lamps = new[] { new Material(Gfx.Emissive(Color.red, 2.5f)), new Material(Gfx.Emissive(Color.green, 2.5f)),
                        new Material(Gfx.Emissive(Color.red, 2.5f)), new Material(Gfx.Emissive(Color.green, 2.5f)) };
        var mb = new MeshBuilder(); var pole = Gfx.Mat(Palette.Tire, 0.3f);
        foreach (var s in signals)
        {
            mb.Box(pole, s + Vector3.up * 1.6f, new Vector3(0.14f, 3.2f, 0.14f));
            mb.Box(pole, s + Vector3.up * 3.0f, new Vector3(0.34f, 0.8f, 0.34f));
            foreach (var (n, f) in new[] { (Vector3.forward, 0), (Vector3.back, 0), (Vector3.right, 2), (Vector3.left, 2) })
            {
                var size = f == 0 ? new Vector3(0.2f, 0.2f, 0.04f) : new Vector3(0.04f, 0.2f, 0.2f);
                mb.Box(lamps[f], s + Vector3.up * 3.22f + n * 0.18f, size);
                mb.Box(lamps[f + 1], s + Vector3.up * 2.8f + n * 0.18f, size);
            }
        }
        mb.Build("Semafory", transform, false);
    }

    void Update()
    {
        var ns = Green(Vector3.forward); var ew = Green(Vector3.right);
        SetLamp(0, !ns); SetLamp(1, ns); SetLamp(2, !ew); SetLamp(3, ew);

        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        Vector3 focus = gm.player.transform.position;
        if (gm.currentCar) focus = gm.currentCar.transform.position;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var d = live[i];
            if (d.car == gm.currentCar) { d.Release(); live.RemoveAt(i); continue; } // carjacked: now the player's car
            float dist = (d.transform.position - focus).magnitude;
            bool gone = dist > 280f || d.car.IsFlipped || d.stuck > 40f;
            if (gone && (dist > 450f || !Net.InView(d.transform.position))) { d.gameObject.SetActive(false); live.RemoveAt(i); pool.Push(d); }
        }
        int target = Mathf.RoundToInt(maxCars * Mathf.Max(0.25f, Pedestrians.Activity(GameClock.I.hour)));
        if ((spawnTimer -= Time.deltaTime) <= 0f && live.Count < target && pool.Count > 0)
        {
            spawnTimer = 0.2f;
            if (!net.RandomVertex(focus, 110f, 260f, rng, out int s, out int v)) return;
            int dir = v == net.N(s) - 1 ? -1 : v == 0 ? 1 : (rng.Next(2) == 0 ? 1 : -1);
            var a = net.V(s, v); var b = net.V(s, v + dir);
            var fwd = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
            float lane = Driver.LaneOf(net.W[s]);
            var pos = a + Vector3.Cross(Vector3.up, fwd) * lane + Vector3.up * 0.45f;
            if (a.y < -0.3f || ((pos - focus).sqrMagnitude < 200f * 200f && Net.InView(pos))) return;
            if (Physics.CheckSphere(pos + Vector3.up * 0.6f, 4f, 1 << CarLayer, QueryTriggerInteraction.Ignore)) return;
            var d = pool.Pop();
            d.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
            d.gameObject.SetActive(true);
            d.Begin(net, s, v, dir, rng);
            live.Add(d);
        }
    }

    void SetLamp(int i, bool on) => lamps[i].SetColor("_EmissionColor", (i % 2 == 0 ? Color.red : Color.green) * (on ? 2.5f : 0.04f));
}

public class Driver : MonoBehaviour
{
    public CarController car;
    public float stuck;
    public bool police; public Vector3 goal; public Transform chase; // police: drive toward goal, or straight at chase
    Net net; System.Random rng; Rigidbody rb;
    int s, i, dir, ns, nd, nat; float rejoin, overtake, laneSign = 1f, blockedAny, lane, cruise, shock, honk, blocked, wedged, reverse;

    public static float LaneOf(float w) => w >= 14f ? w / 4f : 1.3f; // narrow old-town streets: keep near the middle

    public void Begin(Net n, int street, int v, int d, System.Random r)
    {
        net = n; rng = r; rb = car.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        s = street; i = v; dir = d; stuck = shock = blocked = wedged = reverse = 0f; route.Clear();
        Plan(); Setup();
        car.ai = true; enabled = true;
    }

    // Lane width and speed limit (km/h) by the real street's width: wide = 50, others 30–40.
    void Setup() { lane = LaneOf(net.W[s]); cruise = (police ? 50f : net.W[s] >= 20f ? 50f : net.W[s] >= 14f ? 40f : net.W[s] >= 8f ? 30f : 20f) / 3.6f; } // 20 in old-town lanes
    void Plan()
    {
        if (police)
        {
            if (route.Count == 0 || (goal - routeGoal).sqrMagnitude > 30f * 30f) { route = net.Route(s, dir, goal); routeGoal = goal; }
            if (route.Count > 0 && route[0].s != s) { (ns, nd, nat) = route[0]; route.RemoveAt(0); return; }
            if (route.Count > 0) route.RemoveAt(0);
        }
        (ns, nd, nat) = net.Next(s, dir, rng, police ? goal : null);
    }
    List<(int s, int dir, int at)> route = new(); Vector3 routeGoal;

    int Entry(int st, int d, int at) => at > 0 ? at : d > 0 ? 0 : net.N(st) - 1;

    public void Release() { car.ai = false; car.aiThrottle = car.aiSteer = 0f; enabled = false; }
    public void Shock() { if (enabled && shock <= 0f) { shock = 4f; honk = 0f; } }

    Vector3 Lane(int cs, int a, int b, bool atB)
    {
        var A = net.V(cs, a); var B = net.V(cs, b);
        var r = Vector3.Cross(Vector3.up, new Vector3(B.x - A.x, 0, B.z - A.z)).normalized * LaneOf(net.W[cs]) * laneSign;
        return (atB ? B : A) + r;
    }

    Vector3 Ahead(Vector3 from, float dist)
    {
        int cs = s, ci = i, cd = dir; bool onNext = false; var p = from;
        for (int k = 0; k < 32; k++)
        {
            int j = ci + cd;
            if (j < 0 || j >= net.N(cs))
            {
                if (onNext || ns < 0) return p;
                cs = ns; cd = nd; ci = Entry(ns, nd, nat); onNext = true; continue;
            }
            var b = Lane(cs, ci, j, true); var seg = b - p; float l = seg.magnitude;
            if (l >= dist) return p + seg / Mathf.Max(l, 1e-3f) * dist;
            dist -= l; p = b; ci = j;
        }
        return p;
    }

    void Update()
    {
        var pos = transform.position; float v = Vector3.Dot(rb.linearVelocity, transform.forward);
        // follow the polyline: project onto the current lane segment, move on at its end
        Vector3 A = Lane(s, i, i + dir, false), B = Lane(s, i, i + dir, true), proj = A;
        for (int k = 0; k < 8; k++)
        {
            A = Lane(s, i, i + dir, false); B = Lane(s, i, i + dir, true);
            var ab = B - A; float t = Vector3.Dot(pos - A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-3f);
            proj = A + ab * Mathf.Clamp01(t);
            if (t < 1f) break;
            i += dir;
            if (i + dir < 0 || i + dir >= net.N(s))
            {
                if (ns < 0) { dir = -dir; } // dead end: turn round
                else { s = ns; dir = nd; i = Entry(ns, nd, nat); Setup(); }
                Plan();
            }
        }
        // straight at the suspect only while they are in sight; otherwise along the streets
        bool chasing = police && chase && (chase.position - pos).sqrMagnitude < 70f * 70f && Law.Sees(pos + Vector3.up * 1.4f, chase.position);
        if (police && !chasing && (rejoin -= Time.deltaTime) <= 0f && (proj - pos).sqrMagnitude > 20f * 20f && net.Nearest(pos, out int rs, out int rv))
        { // left the road during a chase: rejoin at the nearest street
            rejoin = 2f; s = rs; i = rv; dir = rv < net.N(rs) - 1 ? 1 : -1; Setup(); route.Clear(); Plan();
        }
        var target = chasing ? chase.position : Ahead(proj, 3f + Mathf.Abs(v) * 0.4f);
        var local = transform.InverseTransformPoint(target);
        car.aiSteer = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 25f, -1f, 1f);

        // speed: limit, slow for bends, stop for things ahead and red lights
        var far = Ahead(proj, 25f) - pos; far.y = 0;
        var fwd = transform.forward; fwd.y = 0; fwd.Normalize();
        var look = target - pos; look.y = 0; look = look.sqrMagnitude > 0.01f ? look.normalized : fwd;
        float want = chasing ? 17f * Mathf.Lerp(1f, 0.4f, Vector3.Angle(fwd, look) / 90f) : cruise * Mathf.Lerp(1f, 0.35f, Vector3.Angle(fwd, far) / 90f);
        float range = 8f + v * v / 8f;
        bool obstacle = false;
        if (Physics.BoxCast(pos + Vector3.up * 1f + transform.forward * 2.5f, new Vector3(0.85f, 0.45f, 0.2f), look, out var hit,
                Quaternion.LookRotation(look), range, ~0, QueryTriggerInteraction.Ignore)
            && ((hit.rigidbody != null && hit.rigidbody != rb) || hit.collider is CharacterController))
        {
            obstacle = true;
            want = Mathf.Min(want, Mathf.Sqrt(2f * 4.5f * Mathf.Max(0f, hit.distance - 2f)));
            if (hit.collider is CharacterController || (GameManager.I.currentCar && hit.rigidbody == GameManager.I.currentCar.GetComponent<Rigidbody>()))
                blocked += Time.deltaTime;
        }
        else blocked = 0f;
        // police with blue lights overtakes whatever blocks it, on the other side of the road
        blockedAny = obstacle && v < 2f && blocked == 0f ? blockedAny + Time.deltaTime : 0f; // not when it is the player in the way
        if (police && blockedAny > 2f && overtake <= 0f) overtake = 5f;
        if ((overtake -= Time.deltaTime) > 0f) laneSign = -1f; else laneSign = 1f;
        float stop = police ? float.MaxValue : SignalStop(pos, fwd); // blue lights: no red for the police
        if (stop < float.MaxValue) want = Mathf.Min(want, Mathf.Sqrt(2f * 4f * Mathf.Max(0f, stop)));
        if (shock > 0f) { shock -= Time.deltaTime; want = 0f; }
        if ((honk -= Time.deltaTime) <= 0f && (blocked > 3f || shock > 3.5f))
        {
            honk = 4f; GameAudio.I?.PlayAt(GameAudio.I.horn, pos, 0.7f);
        }
        car.aiThrottle = want < 0.3f && v < 0.6f ? 0f : Mathf.Clamp((want - v) * 0.6f, -1f, 1f);
        // pushing but not moving (clipped a corner or a wall): back up a bit with the wheel turned the other way
        wedged = car.aiThrottle > 0.5f && Mathf.Abs(v) < 0.4f ? wedged + Time.deltaTime : 0f;
        if (wedged > 1.5f || (police && blockedAny > 3f)) { reverse = 1.6f; wedged = 0f; blockedAny = 0f; } // police backs up and tries again
        if (reverse > 0f) { reverse -= Time.deltaTime; car.aiThrottle = -0.8f; car.aiSteer = -car.aiSteer; }
        stuck = v < 0.5f && stop == float.MaxValue && shock <= 0f ? stuck + Time.deltaTime : 0f; // incl. gridlock behind others
    }

    // Distance to the stop line of the nearest red light ahead; none once inside the junction.
    float SignalStop(Vector3 pos, Vector3 fwd)
    {
        if (Traffic.Green(fwd)) return float.MaxValue;
        var right = new Vector3(fwd.z, 0, -fwd.x); float best = float.MaxValue;
        foreach (var g in Traffic.I.signals)
        {
            var d = g - pos; float along = d.x * fwd.x + d.z * fwd.z;
            if (along < 35f && along > -12f && Mathf.Abs(d.x * right.x + d.z * right.z) < 9f)
            {
                if (along < 7f) return float.MaxValue; // already at / in the junction: clear it
                best = Mathf.Min(best, along - 7f);
            }
        }
        return best;
    }
}
