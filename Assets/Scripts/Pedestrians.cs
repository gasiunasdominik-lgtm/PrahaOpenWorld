using System.Collections.Generic;
using UnityEngine;

// Busy streets as an illusion: only people near the player exist. How many, and where they head,
// follows the hour (Rozvrh) and how many real places (Místa) around are open.
public class Pedestrians : MonoBehaviour
{
    public static Pedestrians I { get; private set; }
    public int maxPeds = 80;
    public float spawnMin = 25f, spawnMax = 140f, despawn = 200f;
    public readonly List<Walker> live = new();
    readonly Stack<Walker> pool = new();
    readonly Dictionary<Vector2Int, List<(int street, int vtx)>> grid = new(); // street vertices by 25 m cell
    readonly System.Random rng = new(7);
    float spawnTimer, localTimer, local = 1f;
    const float Cell = 25f;

    void Awake()
    {
        I = this;
        Physics.IgnoreLayerCollision(Traffic.CarLayer, Traffic.BodyLayer); // cars pass through bodies; the trigger decides what a hit does
        var st = MapBuilder.D.streets;
        for (int s = 0; s < st.Length; s++)
            for (int v = 0; v < st[s].p.Length / 2; v++)
            {
                var c = CellOf(st[s].p[2 * v], st[s].p[2 * v + 1]);
                if (!grid.TryGetValue(c, out var l)) grid[c] = l = new();
                l.Add((s, v));
            }
    }

    static Vector2Int CellOf(float x, float z) => new(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));

    // People out per hour: quiet nights, morning and evening peaks.
    public static float Activity(float h) =>
        h < 5f ? 0.08f : h < 7f ? Mathf.Lerp(0.08f, 0.9f, (h - 5f) / 2f) : h < 9.5f ? 0.9f : h < 16f ? 0.6f :
        h < 19f ? 0.85f : h < 22f ? 0.6f : Mathf.Lerp(0.6f, 0.15f, (h - 22f) / 2f);

    public int Target => Mathf.RoundToInt(maxPeds * Activity(GameClock.I.hour) * local);

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        Vector3 focus = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;
        float hour = GameClock.I.hour;

        if ((localTimer -= Time.deltaTime) <= 0f)
        {
            localTimer = 2f;
            local = Mathf.Clamp(Places.OpenNear(focus, hour) / 12f, 0.25f, 1.4f);
        }
        for (int i = live.Count - 1; i >= 0; i--)
            if ((live[i].transform.position - focus).sqrMagnitude > despawn * despawn && !live[i].waiting) Despawn(live[i]);

        if ((spawnTimer -= Time.deltaTime) <= 0f && live.Count < Target)
        {
            spawnTimer = 0.15f;
            TrySpawn(focus, hour, spawnMin);
        }
    }

    void TrySpawn(Vector3 focus, float hour, float minDist)
    {
        bool fromDoor = rng.Next(2) == 0;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Lerp(minDist, spawnMax, (float)rng.NextDouble());
            var probe = new Vector3(focus.x + Mathf.Cos(a) * d, 0, focus.z + Mathf.Sin(a) * d);
            Vector3? door = null;
            if (fromDoor)
            {
                var place = Places.PickOpenNear(probe, hour, rng, 40f);
                if (place == null) continue;
                door = new Vector3(place.p[0], MapBuilder.Height(place.p[0], place.p[1]), place.p[1]);
                probe = door.Value;
            }
            if (!NearestVertex(probe, out int s, out int v)) continue;
            if (MapBuilder.StreetY[s][v] < 0.5f) continue; // not mid-river
            var at = (door ?? new Vector3(MapBuilder.D.streets[s].p[2 * v], MapBuilder.StreetY[s][v], MapBuilder.D.streets[s].p[2 * v + 1])) + Vector3.up;
            if ((at - focus).sqrMagnitude < 70f * 70f && InView(at)) continue; // never pop into sight up close
            string purpose = Places.Purpose(hour);
            var start = new Vector3(MapBuilder.D.streets[s].p[2 * v], 0, MapBuilder.D.streets[s].p[2 * v + 1]);
            var dest = Places.PickDestination(start, purpose, hour, rng);
            Vector3 target; string name;
            if (dest != null) { target = new Vector3(dest.p[0], 0, dest.p[1]); name = string.IsNullOrEmpty(dest.n) ? Places.KindName(dest.k) : dest.n; }
            else { purpose = "domů"; target = start + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 800f; name = "domov"; }
            var w = pool.Count > 0 ? pool.Pop() : Walker.Create(transform, rng);
            w.gameObject.SetActive(true);
            w.Begin(s, v, target, purpose, name, door, rng);
            live.Add(w);
            // ~half of city-centre pedestrians walk in groups of 2–4, abreast at low density (Moussaïd et al. 2010)
            if (rng.NextDouble() < 0.4 && live.Count + 3 < maxPeds)
            {
                int n = rng.NextDouble() < 0.75 ? 1 : rng.NextDouble() < 0.7 ? 2 : 3;
                w.GroupSpeed(n + 1);
                for (int k = 0; k < n; k++)
                {
                    var c = pool.Count > 0 ? pool.Pop() : Walker.Create(transform, rng);
                    c.gameObject.SetActive(true);
                    c.Begin(s, v, target, purpose, name, door, rng);
                    c.leader = w; c.offset = (k / 2 + 1) * 0.75f * (k % 2 == 0 ? 1f : -1f);
                    c.transform.position = w.transform.position + w.transform.right * c.offset;
                    live.Add(c);
                }
            }
            return;
        }
    }

    bool NearestVertex(Vector3 p, out int street, out int vtx)
    {
        street = vtx = -1; float best = 40f * 40f; var c = CellOf(p.x, p.z);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var (s, v) in l)
                {
                    var q = MapBuilder.D.streets[s].p; float ex = q[2 * v] - p.x, ez = q[2 * v + 1] - p.z;
                    if (ex * ex + ez * ez < best && q.Length >= 4) { best = ex * ex + ez * ez; street = s; vtx = v; }
                }
        return street >= 0;
    }

    static bool InView(Vector3 p)
    {
        var cam = Camera.main; if (!cam) return false;
        var v = cam.WorldToViewportPoint(p);
        return v.z > 0 && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }


    public void Despawn(Walker w) { live.Remove(w); w.gameObject.SetActive(false); pool.Push(w); }

    // Street ends that touch the given point (within 4 m), for walking on to the next street.
    public void Junction(Vector3 p, List<(int street, int vtx)> into)
    {
        into.Clear();
        var c = CellOf(p.x, p.z);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l))
                foreach (var e in l)
                {
                    var q = MapBuilder.D.streets[e.street].p;
                    float ex = q[2 * e.vtx] - p.x, ez = q[2 * e.vtx + 1] - p.z;
                    if (ex * ex + ez * ez < 16f) into.Add(e);
                }
    }
}

// One pedestrian: steps out of a door or off a street corner, walks the streets junction by junction,
// and goes in through the destination's door. Solid to the player, knocked over by fast cars.
public class Walker : MonoBehaviour
{
    public string purpose, destName;
    public Vector3 dest;
    public bool Down => down > 0f;
    public bool waiting; // stands still: waiting for a taxi or on the phone to the police
    public Walker leader; public float offset; // walking in a group: keep abreast of the leader
    public void GroupSpeed(int n) => speed = Mathf.Max(0.8f, speed - 0.04f * n); // groups walk slower (Moussaïd et al. 2010)
    CharacterMotion cm; Rigidbody rb; CapsuleCollider solid;
    int street, vtx, dir; float u, side, speed, down; Vector3 dodge, lastPos;
    enum Mode { Street, Exit, Enter }
    Mode mode; Vector3 freeTarget;
    static readonly List<(int, int)> scratch = new();
    static readonly Color[] Coats = { Palette.Jacket, Palette.Terracotta, Palette.Slate, Palette.Ochre, Palette.PaleSage, Palette.DustyPink, Palette.Copper, Palette.Plinth };
    static readonly Color[] Hairs = { Palette.Hair, Palette.Trunk, Palette.Butter, Palette.Slate, Palette.Cornice };

    public static Walker Create(Transform parent, System.Random rng)
    {
        var go = new GameObject("Chodec"); go.transform.SetParent(parent, false);
        var cm = CharacterMotion.BuildHero(go.transform);
        cm.maxSpeed = 3f;
        var coat = Gfx.Mat(Coats[rng.Next(Coats.Length)], 0.15f);
        var hair = Gfx.Mat(Hairs[rng.Next(Hairs.Length)], 0.15f);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            string n = r.name;
            if (n.StartsWith("Torso") || n.StartsWith("Sleeve") || n.StartsWith("JacketHem")) r.sharedMaterial = coat;
            else if (n.StartsWith("Hair") || n.StartsWith("Fringe")) r.sharedMaterial = hair;
            else if (n.Contains("Scarf")) r.enabled = false; // the red scarf is the hero's signature
        }
        var w = go.AddComponent<Walker>(); w.cm = cm;
        // solid body (own layer): blocks the player; cars pass through it and are sensed by the trigger instead (a kinematic body would stop a car dead)
        var body = new GameObject("Telo") { layer = Traffic.BodyLayer }; body.transform.SetParent(go.transform, false);
        w.solid = body.AddComponent<CapsuleCollider>(); w.solid.center = Vector3.up * 0.9f; w.solid.height = 1.8f; w.solid.radius = 0.28f;
        var sense = go.AddComponent<CapsuleCollider>(); sense.isTrigger = true; sense.center = Vector3.up * 0.9f; sense.height = 1.8f; sense.radius = 0.7f;
        w.rb = go.AddComponent<Rigidbody>(); w.rb.isKinematic = true; w.rb.mass = 75f;
        return w;
    }

    public void Begin(int s, int v, Vector3 target, string why, string where, Vector3? door, System.Random rng)
    {
        waiting = false;
        street = s; vtx = v; dest = target; purpose = why; destName = where;
        var p = MapBuilder.D.streets[s].p; int n = p.Length / 2;
        dir = v == n - 1 ? -1 : v == 0 ? 1 : (rng.Next(2) == 0 ? 1 : -1);
        u = 0f; dodge = Vector3.zero; down = 0f;
        // free walking speed ~ N(1.34, 0.2) m/s, 15th percentile ~1.11 (Tarawneh 2001)
        float g = Mathf.Sqrt(-2f * Mathf.Log(1f - (float)rng.NextDouble())) * Mathf.Cos(2f * Mathf.PI * (float)rng.NextDouble());
        speed = Mathf.Clamp(1.34f + 0.2f * g, 0.8f, 1.9f); leader = null;
        float hw = MapBuilder.D.streets[s].w / 2f;
        side = MapBuilder.D.streets[s].foot ? ((float)rng.NextDouble() * 2f - 1f) * hw * 0.7f : (rng.Next(2) == 0 ? 1 : -1) * (hw - 0.4f);
        Stand();
        if (door.HasValue) { mode = Mode.Exit; freeTarget = Point(street, vtx, vtx + dir, 0f); transform.position = lastPos = door.Value + Vector3.up * 0.05f; }
        else { mode = Mode.Street; transform.position = lastPos = Point(street, vtx, vtx + dir, 0f); }
    }

    Vector3 Point(int s, int a, int b, float t)
    {
        var p = MapBuilder.D.streets[s].p; var y = MapBuilder.StreetY[s];
        var A = new Vector3(p[2 * a], y[a], p[2 * a + 1]); var B = new Vector3(p[2 * b], y[b], p[2 * b + 1]);
        var side3 = Vector3.Cross(Vector3.up, new Vector3(B.x - A.x, 0, B.z - A.z)).normalized * side;
        return Vector3.Lerp(A, B, t) + side3 + Vector3.up * 0.12f;
    }

    void Update()
    {
        if (down > 0f)
        {
            if ((down -= Time.deltaTime) <= 0f) { Stand(); mode = Mode.Exit; freeTarget = Point(street, vtx, vtx + dir, u); lastPos = transform.position; }
            return;
        }
        if (waiting) { cm.Tick(Vector3.zero, Time.deltaTime); lastPos = transform.position; return; }
        if (leader)
        {
            if (!leader.gameObject.activeSelf) { leader = null; Pedestrians.I.Despawn(this); return; } // went in / left together
            var goal = leader.transform.position + leader.transform.right * offset;
            var stepv = Vector3.ClampMagnitude(goal - transform.position, 2.5f * Time.deltaTime);
            var np = transform.position + stepv; np.y = Mathf.Max(MapBuilder.Height(np.x, np.z) + 0.12f, goal.y);
            transform.rotation = leader.transform.rotation;
            rb.MovePosition(np); transform.position = lastPos = np;
            cm.Tick(stepv / Mathf.Max(Time.deltaTime, 1e-4f), Time.deltaTime);
            return;
        }
        Vector3 pos;
        if (mode != Mode.Street)
        {
            // off the street: straight to the street (stepping out) or to the door (going in)
            var to = freeTarget - transform.position; to.y = 0;
            if (to.magnitude < 0.4f)
            {
                if (mode == Mode.Enter) { Pedestrians.I.Despawn(this); return; } // went inside
                mode = Mode.Street;
            }
            pos = transform.position + to.normalized * Mathf.Min(speed * Time.deltaTime, to.magnitude);
            pos.y = MapBuilder.Height(pos.x, pos.z) + 0.12f;
            if (mode == Mode.Exit) pos.y = Mathf.Max(pos.y, freeTarget.y);
        }
        else
        {
            var p = MapBuilder.D.streets[street].p; int n = p.Length / 2, next = vtx + dir;
            float segLen = new Vector2(p[2 * next] - p[2 * vtx], p[2 * next + 1] - p[2 * vtx + 1]).magnitude;
            u += speed * Time.deltaTime / Mathf.Max(segLen, 0.1f);
            if (u >= 1f)
            {
                u = 0f; vtx = next;
                var here = new Vector3(p[2 * vtx], 0, p[2 * vtx + 1]);
                if (new Vector2(here.x - dest.x, here.z - dest.z).sqrMagnitude < 40f * 40f && destName != "domov")
                { mode = Mode.Enter; freeTarget = new Vector3(dest.x, MapBuilder.Height(dest.x, dest.z), dest.z); return; }
                if (vtx + dir < 0 || vtx + dir >= n) ChooseNextStreet(here);
            }
            pos = Point(street, vtx, vtx + dir, u);
        }
        // step aside when a fast car comes close
        var car = GameManager.I.currentCar;
        if (car && car.SpeedKmh > 10f)
        {
            var away = transform.position - car.transform.position; away.y = 0;
            if (away.sqrMagnitude < 64f) dodge = Vector3.MoveTowards(dodge, away.normalized * 3.5f, 9f * Time.deltaTime);
        }
        dodge = Vector3.MoveTowards(dodge, Vector3.zero, 0.8f * Time.deltaTime);
        pos += dodge;

        var vel = (pos - lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        if (new Vector3(vel.x, 0, vel.z).sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z));
        rb.MovePosition(pos); transform.position = lastPos = pos;
        cm.Tick(vel, Time.deltaTime);
    }

    // A car that hits at speed knocks the person over; they get up after a while and return to the pavement.
    void OnTriggerEnter(Collider other)
    {
        if (down > 0f) return;
        var car = other.GetComponentInParent<CarController>();
        if (car == null || car.SpeedKmh < 8f) return;
        if (car.ai && car.SpeedKmh < 25f) // a braking city car only nudges: the person jumps aside
        { var away = transform.position - car.transform.position; away.y = 0; dodge = away.normalized * 2.5f; return; }
        var carRb = car.GetComponent<Rigidbody>();
        down = 4f; // before the report, so the victim on the ground is not picked as the witness
        if (car == GameManager.I.currentCar) Law.I.Crime("sražený chodec", transform.position, car);
        rb.isKinematic = false;
        rb.AddForce(carRb.linearVelocity * 0.9f + Vector3.up * 3.5f, ForceMode.VelocityChange);
        rb.AddTorque(Random.onUnitSphere * 6f, ForceMode.VelocityChange);
        carRb.linearVelocity *= 0.92f; // the hit costs the car some speed
        car.OnCrash?.Invoke(4f);
    }

    void Stand()
    {
        rb.isKinematic = true;
        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        var p = transform.position; p.y = MapBuilder.Height(p.x, p.z) + 0.12f; transform.position = p;
    }

    // At a street end pick the touching street that leads closest to the destination; turn back at dead ends.
    void ChooseNextStreet(Vector3 here)
    {
        Pedestrians.I.Junction(here, scratch);
        float best = float.MaxValue; int bs = -1, bv = 0, bd = 0;
        foreach (var (s, v) in scratch)
        {
            if (s == street) continue;
            var q = MapBuilder.D.streets[s].p; int m = q.Length / 2;
            int d = v == 0 ? 1 : v == m - 1 ? -1 : 0;
            if (d == 0 || q.Length < 4) continue;
            int far = d == 1 ? m - 1 : 0;
            float dist = new Vector2(q[2 * far] - dest.x, q[2 * far + 1] - dest.z).magnitude + Random.value * 40f;
            if (dist < best) { best = dist; bs = s; bv = v; bd = d; }
        }
        if (bs < 0) { dir = -dir; return; }
        street = bs; vtx = bv; dir = bd;
    }
}
