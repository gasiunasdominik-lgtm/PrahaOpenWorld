using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Public transport: trams on the real tram tracks stopping at real stops (board/alight with E),
// and the metro: go down an entrance, pick a station on line A/B/C, come up there; the ride costs game time.
public class Transit : MonoBehaviour
{
    public static Transit I { get; private set; }
    public readonly Net rails = new();
    public Vector3[] stops; public string[] stopNames;
    readonly List<Tram> trams = new();
    readonly System.Random rng = new(11);
    float spawnTimer;
    Tram riding;
    // metro menu state
    int station = -1, line, dest; readonly List<int> lines = new();
    bool travelling;
    public bool debugE;
    public bool InMetro => station >= 0 || travelling; // tests press E without keyboard focus

    void Awake()
    {
        I = this;
        var D = MapBuilder.D;
        foreach (var t in D.trams)
        {
            var y = new float[t.p.Length / 2];
            for (int v = 0; v < y.Length; v++) y[v] = TrackY(t.p[2 * v], t.p[2 * v + 1]);
            rails.Add(t.p, y, 0f);
        }
        rails.Finish();
        stops = new Vector3[D.tramStops.Length]; stopNames = new string[stops.Length];
        for (int k = 0; k < stops.Length; k++)
        {
            float x = D.tramStops[k].p[0], z = D.tramStops[k].p[1];
            stops[k] = new Vector3(x, TrackY(x, z), z); stopNames[k] = D.tramStops[k].n;
        }
    }

    // Ground, or the bridge deck when the track crosses the river (highest street vertex nearby).
    static float TrackY(float x, float z)
    {
        float h = MapBuilder.Height(x, z);
        if (h > 0.5f) return h;
        float best = h; var st = MapBuilder.D.streets;
        for (int s = 0; s < st.Length; s++)
            for (int v = 0; v < st[s].p.Length / 2; v++)
            {
                float dx = st[s].p[2 * v] - x, dz = st[s].p[2 * v + 1] - z;
                if (dx * dx + dz * dz < 25f * 25f) best = Mathf.Max(best, MapBuilder.StreetY[s][v]);
            }
        return best;
    }

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        Vector3 focus = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;
        for (int k = trams.Count - 1; k >= 0; k--)
        {
            var t = trams[k]; if (t == riding) continue;
            float d = (t.Mid - focus).magnitude;
            if ((d > 700f || t.dead) && (d > 900f || !Net.InView(t.Mid))) { Destroy(t.gameObject); trams.RemoveAt(k); }
        }
        // trams in reach follow PID headways: 8 min at peak, 10–20 off-peak, ~30 at night (pid.cz)
        float hr = GameClock.I.hour;
        int target = hr < 5f ? 1 : (hr >= 6f && hr < 9f) || (hr >= 14f && hr < 18.5f) ? 7 : hr < 20f ? 5 : 3;
        if ((spawnTimer -= Time.deltaTime) <= 0f && trams.Count < target)
        {
            spawnTimer = 1f;
            if (!rails.RandomVertex(focus, 150f, 600f, rng, out int s, out int v)) return;
            var p = rails.V(s, v);
            if ((p - focus).sqrMagnitude < 250f * 250f && Net.InView(p)) return;
            foreach (var o in trams) if ((o.Mid - p).sqrMagnitude < 60f * 60f) return;
            int dir = v == rails.N(s) - 1 ? -1 : v == 0 ? 1 : (rng.Next(2) == 0 ? 1 : -1);
            trams.Add(Tram.Create(transform, rails, s, v, dir, rng));
        }
    }

    // Tram and metro interaction for this frame. True when it owns the prompt / E key.
    public bool Interact(Keyboard kb, Hud hud, GameManager gm)
    {
        var player = gm.player;
        bool e = (kb != null && kb.eKey.wasPressedThisFrame) || (debugE && station < 0); if (station < 0) debugE = false;
        if (travelling) { hud.promptText.text = ""; return true; }
        if (riding)
        {
            player.transform.position = riding.Mid; // keeps the city (pedestrians, traffic) alive around the rider
            hud.promptText.text = riding.AtStop ? $"ZASTÁVKA {riding.StopName.ToUpperInvariant()}    [E] VYSTOUPIT" : "TRAMVAJ JEDE…";
            if (e && riding.AtStop)
            {
                var side = riding.MidRot * Vector3.right * 2.8f;
                player.gameObject.SetActive(true);
                var at = riding.Mid + side; at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                player.Teleport(at, riding.MidRot.eulerAngles.y);
                player.controlEnabled = true;
                gm.rig.FollowPlayer(player.camTarget);
                riding = null;
            }
            return true;
        }
        if (station >= 0) { MetroMenu(kb, hud, gm); debugE = false; return true; }
        if (gm.currentCar) return false;

        var pp = player.transform.position;
        foreach (var t in trams)
            if (t.AtStop && t.Near(pp, 6f))
            {
                hud.promptText.text = "[E] NASTOUPIT DO TRAMVAJE";
                if (e)
                {
                    riding = t; player.controlEnabled = false; player.gameObject.SetActive(false);
                    gm.rig.FollowTarget(t.camTarget, 16f, t.MidRot.eulerAngles.y);
                    GameAudio.I?.PlayAt(GameAudio.I.door, pp, 0.8f);
                }
                return true;
            }
        foreach (var en in MapBuilder.D.metro.entrances)
        {
            float dx = en.p[0] - pp.x, dz = en.p[1] - pp.z;
            if (dx * dx + dz * dz > 16f) continue;
            int st = NearestStation(en.p[0], en.p[1]);
            if (st < 0) return false;
            hud.promptText.text = $"[E] METRO · {MapBuilder.D.metro.stations[st].n.ToUpperInvariant()}";
            if (e) OpenMetro(st, gm);
            return true;
        }
        return false;
    }

    static int NearestStation(float x, float z)
    {
        var ss = MapBuilder.D.metro.stations; int best = -1; float bd = 400f * 400f;
        for (int k = 0; k < ss.Length; k++)
        {
            float dx = ss[k].p[0] - x, dz = ss[k].p[1] - z;
            if (dx * dx + dz * dz < bd) { bd = dx * dx + dz * dz; best = k; }
        }
        return best;
    }

    void OpenMetro(int st, GameManager gm)
    {
        lines.Clear();
        var order = MapBuilder.D.metro.order;
        for (int l = 0; l < order.Length; l++) if (System.Array.IndexOf(order[l].s, st) >= 0) lines.Add(l);
        if (lines.Count == 0) return;
        station = st; line = 0; dest = FirstDest();
        gm.player.controlEnabled = false;
    }

    int FirstDest() { var s = MapBuilder.D.metro.order[lines[line]].s; int at = System.Array.IndexOf(s, station); return at + 1 < s.Length ? at + 1 : at - 1; }

    void MetroMenu(Keyboard kb, Hud hud, GameManager gm)
    {
        var M = MapBuilder.D.metro; var L = M.order[lines[line]]; int at = System.Array.IndexOf(L.s, station);
        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) { dest--; if (dest == at) dest--; }
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) { dest++; if (dest == at) dest++; }
            dest = Mathf.Clamp(dest, 0, L.s.Length - 1); if (dest == at) dest = FirstDest();
            if (kb.wKey.wasPressedThisFrame && lines.Count > 1) { line = (line + 1) % lines.Count; dest = FirstDest(); return; }
            if (kb.qKey.wasPressedThisFrame) { station = -1; gm.player.controlEnabled = true; return; }
        }
        int hops = Mathf.Abs(dest - at), minutes = 3 + hops * 2; // wait for the train + ~2 min per stop
        hud.promptText.text = $"METRO {M.stations[station].n.ToUpperInvariant()} · LINKA {L.@ref} → {M.stations[L.s[dest]].n.ToUpperInvariant()} ({hops} ST., {minutes} MIN)\n" +
                              $"[A/D] CÍL   {(lines.Count > 1 ? "[W] PŘESTUP NA JINOU LINKU   " : "")}[E] JET   [Q] ZPĚT NAHORU";
        if ((kb != null && kb.eKey.wasPressedThisFrame) || debugE) StartCoroutine(Ride(L.s[dest], minutes, L.@ref, gm));
    }

    IEnumerator Ride(int to, int minutes, string lineRef, GameManager gm)
    {
        travelling = true;
        yield return gm.hud.FadeTo(1f, 0.4f);
        GameClock.I.hour = (GameClock.I.hour + minutes / 60f) % 24f;
        var M = MapBuilder.D.metro; var sp = M.stations[to].p;
        // come up at the station's nearest entrance
        Vector2 exit = new(sp[0], sp[1]); float bd = float.MaxValue;
        foreach (var en in M.entrances)
        {
            float d = (new Vector2(en.p[0], en.p[1]) - exit).sqrMagnitude;
            if (d < bd && d < 400f * 400f) { bd = d; exit = new Vector2(en.p[0], en.p[1]); }
        }
        var at = new Vector3(exit.x + 2.5f, 0, exit.y + 2.5f); at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
        gm.player.Teleport(at, gm.player.transform.eulerAngles.y);
        station = -1; gm.player.controlEnabled = true;
        yield return new WaitForSeconds(0.3f);
        gm.hud.Toast($"METRO {lineRef}: {M.stations[to].n.ToUpperInvariant()} (+{minutes} MIN)", Palette.UILight, 2f);
        yield return gm.hud.FadeTo(0f, 0.4f);
        travelling = false;
    }
}

// A three-section articulated tram. Sections follow a breadcrumb trail of the front, so it bends through curves.
public class Tram : MonoBehaviour
{
    public bool dead; public Transform camTarget;
    public bool AtStop => dwell > 0f;
    public string StopName => lastStop >= 0 ? Transit.I.stopNames[lastStop] : "";
    public Vector3 Mid => secs[1].transform.position; public Quaternion MidRot => secs[1].transform.rotation;
    readonly Rigidbody[] secs = new Rigidbody[3];
    readonly List<Vector3> trail = new();
    Net net; System.Random rng; int s, i, dir, lastStop = -1; float speed, dwell, bell; Vector3 front;
    const float Step = 0.5f, SecLen = 9.6f, Cruise = 9f; // ~32 km/h between stops → ~19.5 km/h network average with stops (TSK)

    public static Tram Create(Transform parent, Net net, int s, int v, int dir, System.Random rng)
    {
        var go = new GameObject("Tramvaj"); go.transform.SetParent(parent, false);
        var t = go.AddComponent<Tram>(); t.net = net; t.rng = rng; t.s = s; t.i = v; t.dir = dir;
        var red = Gfx.Mat(new Color(0.75f, 0.09f, 0.1f), 0.5f); var cream = Gfx.Mat(new Color(0.93f, 0.88f, 0.74f), 0.4f);
        var glass = Gfx.Mat(Palette.Glass, 0.9f); var grey = Gfx.Mat(Palette.Tire, 0.3f);
        for (int k = 0; k < 3; k++)
        {
            var sec = new GameObject("Clanek" + k); sec.transform.SetParent(go.transform, false); sec.layer = Traffic.CarLayer; sec.tag = "Player"; // camera deoccluder ignores it, as with cars
            var mb = new MeshBuilder();
            mb.Box(grey, new Vector3(0, 0.35f, 0), new Vector3(2.2f, 0.5f, 8.6f));
            mb.Box(red, new Vector3(0, 1.3f, 0), new Vector3(2.46f, 1.5f, 9.3f));
            mb.Box(glass, new Vector3(0, 2.45f, 0), new Vector3(2.48f, 0.85f, 8.9f));
            mb.Box(cream, new Vector3(0, 3.05f, 0), new Vector3(2.46f, 0.38f, 9.3f));
            mb.Box(grey, new Vector3(0, 3.4f, 0), new Vector3(1.6f, 0.35f, 6f));
            if (k == 0) mb.Box(Gfx.Emissive(Palette.HeadLight, 1.6f), new Vector3(0, 1.0f, 4.66f), new Vector3(1.6f, 0.2f, 0.05f));
            mb.Build("Mesh", sec.transform, false).isStatic = false;
            var col = sec.AddComponent<BoxCollider>(); col.center = new Vector3(0, 1.75f, 0); col.size = new Vector3(2.46f, 3.2f, 9.3f);
            var rb = sec.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            t.secs[k] = rb;
        }
        t.camTarget = new GameObject("CamTarget").transform; t.camTarget.SetParent(t.secs[1].transform, false);
        t.camTarget.localPosition = new Vector3(0, 2.6f, 0);
        t.front = net.V(s, v); t.trail.Add(t.front);
        t.Advance(3 * SecLen + 2f); // lay the body out along the track
        t.Pose(true);
        return t;
    }

    void Advance(float rem)
    {
        for (int guard = 0; rem > 1e-4f && guard < 64; guard++)
        {
            int j = i + dir;
            if (j < 0 || j >= net.N(s))
            {
                var (ns, nd, at) = net.Next(s, dir, rng);
                if (ns < 0) { dead = true; speed = 0f; break; } // end of the mapped track
                s = ns; dir = nd; i = at; continue;
            }
            var b = net.V(s, j); var seg = b - front; float l = seg.magnitude;
            if (l <= rem) { front = b; rem -= l; i = j; } else { front += seg / l * rem; rem = 0f; }
        }
        var last = trail[^1];
        while ((front - last).magnitude >= Step) { last += (front - last).normalized * Step; trail.Add(last); }
        if (trail.Count > 160) trail.RemoveRange(0, trail.Count - 160);
    }

    Vector3 Back(float d) => trail[Mathf.Clamp(trail.Count - 1 - Mathf.RoundToInt(d / Step), 0, trail.Count - 1)];

    void Pose(bool snap)
    {
        for (int k = 0; k < 3; k++)
        {
            float c = SecLen * (k + 0.5f) + k * 0.3f;
            Vector3 a = Back(c - 4.2f), b = Back(c + 4.2f), mid = (a + b) * 0.5f, f = a - b;
            if (f.sqrMagnitude < 1e-4f) f = secs[k].transform.forward;
            var rot = Quaternion.LookRotation(f.normalized);
            if (snap) secs[k].transform.SetPositionAndRotation(mid, rot);
            else { secs[k].MovePosition(mid); secs[k].MoveRotation(rot); }
        }
    }

    public bool Near(Vector3 p, float r)
    {
        foreach (var sc in secs) if ((sc.transform.position - p).sqrMagnitude < r * r) return true;
        return false;
    }

    void FixedUpdate()
    {
        if (dead) return;
        float dt = Time.fixedDeltaTime;
        if (dwell > 0f) { dwell -= dt; if (dwell <= 0f) bell = 0f; return; }
        var head = secs[0].transform; var fwd = head.forward;
        float want = Cruise;
        // brake for anything on the track ahead
        var origin = head.position + Vector3.up * 1.6f + fwd * 4.9f;
        if (Physics.BoxCast(origin, new Vector3(1.1f, 0.9f, 0.2f), fwd, out var hit, head.rotation, 8f + speed * speed / 3f, ~0, QueryTriggerInteraction.Ignore)
            && ((hit.rigidbody != null && System.Array.IndexOf(secs, hit.rigidbody) < 0) || hit.collider is CharacterController))
        {
            want = Mathf.Min(want, Mathf.Sqrt(2f * 1.5f * Mathf.Max(0f, hit.distance - 3f)));
            if ((bell -= dt) <= 0f) { bell = 5f; GameAudio.I?.PlayAt(GameAudio.I.horn, head.position, 0.9f); }
        }
        // halt at the next real stop on the track
        for (int k = 0; k < Transit.I.stops.Length; k++)
        {
            if (k == lastStop || (lastStop >= 0 && Transit.I.stopNames[k] == Transit.I.stopNames[lastStop])) continue; // same stop, other track
            var d = Transit.I.stops[k] - front; float along = Vector3.Dot(d, fwd);
            if (along < -1f || along > 40f || (d - fwd * along).sqrMagnitude > 36f) continue;
            want = Mathf.Min(want, Mathf.Sqrt(2f * 1.2f * Mathf.Max(0f, along)) + 0.2f);
            if (along < 1.5f) { lastStop = k; dwell = 20f; /* 15–30 s (odhad) */ speed = 0f; return; }
        }
        speed = Mathf.MoveTowards(speed, want, (want > speed ? 1.2f : 4f) * dt);
        if (speed > 0.01f) { Advance(speed * dt); Pose(false); }
    }
}
