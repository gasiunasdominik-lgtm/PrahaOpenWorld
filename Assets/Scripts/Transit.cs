using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

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
        BuildStops();
    }

    // Every stop gets what Prague stops have (after photos): a glazed steel shelter with a bench, the DPP stop pole with its
    // name board, and a raised kerb - on the side of its track away from houses, the other track and the car lanes.
    void BuildStops()
    {
        var D = MapBuilder.D; var mb = new MeshBuilder(); var root = new GameObject("Zastavky").transform; root.SetParent(transform, false);
        var glass = Gfx.Mat(new Color(0.16f, 0.21f, 0.24f), 0.97f); var steel = Gfx.Mat(new Color(0.32f, 0.34f, 0.36f), 0.6f);
        var roof = Gfx.Mat(new Color(0.22f, 0.23f, 0.24f), 0.4f); var kerb = Gfx.Pbr("Setts", 0.12f, 1.5f, new Color(0.8f, 0.78f, 0.75f));
        var white = Gfx.Mat(new Color(0.95f, 0.95f, 0.93f), 0.4f); var redM = Gfx.Mat(new Color(0.75f, 0.08f, 0.1f), 0.5f);
        var wood = Gfx.Mat(new Color(0.42f, 0.28f, 0.16f), 0.3f);
        for (int k = 0; k < stops.Length; k++)
        {
            var p = stops[k]; if (p.y < 0.5f) continue; // (on a bridge deck)
            // track direction: the nearest tram segment
            Vector3 dir = Vector3.forward; float bd = float.MaxValue; int own = -1;
            for (int w = 0; w < D.trams.Length; w++)
            {
                var q = D.trams[w].p;
                for (int i = 0; i + 3 < q.Length; i += 2)
                {
                    var a = new Vector3(q[i], 0, q[i + 1]); var b = new Vector3(q[i + 2], 0, q[i + 3]); var ab = b - a;
                    float tt = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x, 0, p.z) - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float d = (a + ab * tt - new Vector3(p.x, 0, p.z)).sqrMagnitude;
                    if (d < bd) { bd = d; dir = ab.normalized; own = w; }
                }
            }
            var side = Vector3.Cross(Vector3.up, dir); Vector3 best = side * 3.4f; float bs = float.MaxValue;
            foreach (float off in new[] { 3.4f, -3.4f, 5.5f, -5.5f })
            {
                var c = p + side * off; float sc = Mathf.Abs(off) * 0.2f;
                foreach (var col in Physics.OverlapBox(c + Vector3.up * 1.5f, new Vector3(2.5f, 1.2f, 1f), Quaternion.LookRotation(dir)))
                    if (col.name.StartsWith("Domy") || col.name.StartsWith("IprDomy")) { sc += 10f; break; }
                for (int w = 0; w < D.trams.Length; w++)
                {
                    if (w == own) continue; var q = D.trams[w].p;
                    for (int i = 0; i + 1 < q.Length; i += 2) { float dx = q[i] - c.x, dz = q[i + 1] - c.z; if (dx * dx + dz * dz < 2.2f * 2.2f) { sc += 4f; i = q.Length; } }
                }
                if (sc < bs) { bs = sc; best = side * off; }
            }
            var o = p + best; o.y = MapBuilder.Height(o.x, o.z) + 0.12f; var away = best.normalized; var rot = Quaternion.LookRotation(dir);
            Vector3 L(float across, float y, float along) => o + away * across + Vector3.up * y + dir * along;
            mb.Box(kerb, L(-0.6f, 0.09f, 0), new Vector3(2.6f, 0.18f, 18f), rot, 1.5f); // raised boarding kerb
            mb.Box(glass, L(0.75f, 1.3f, 0), new Vector3(0.04f, 2.0f, 4.2f), Quaternion.LookRotation(dir)); // the back wall
            foreach (float e in new[] { -2.1f, 2.1f })
            {
                mb.Box(glass, L(0.15f, 1.3f, e), new Vector3(1.2f, 2.0f, 0.04f), Quaternion.LookRotation(away));
                foreach (float a in new[] { -0.45f, 0.78f }) mb.Box(steel, L(a, 1.25f, e), new Vector3(0.08f, 2.5f, 0.08f), rot);
            }
            mb.Box(roof, L(0.1f, 2.55f, 0), new Vector3(1.8f, 0.1f, 4.6f), Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 0, 3f), 0, true, true);
            mb.Box(wood, L(0.45f, 0.48f, 0), new Vector3(0.45f, 0.06f, 2.6f), rot); // bench
            foreach (float bz in new[] { -1.1f, 1.1f }) mb.Box(steel, L(0.45f, 0.24f, bz), new Vector3(0.06f, 0.48f, 0.06f), rot);
            // the stop pole: grey mast, a white board with the red tram band, timetable box
            var pole = L(-0.9f, 0, 4.2f);
            mb.Box(steel, pole + Vector3.up * 1.6f, new Vector3(0.09f, 3.2f, 0.09f), rot);
            mb.Box(white, pole + Vector3.up * 2.75f, new Vector3(0.05f, 0.5f, 0.9f), rot);
            mb.Box(redM, pole + Vector3.up * 3.08f, new Vector3(0.06f, 0.16f, 0.9f), rot);
            mb.Box(white, pole + Vector3.up * 1.5f + away * 0.1f, new Vector3(0.08f, 0.6f, 0.45f), rot);
            foreach (float sgn in new[] { 1f, -1f })
            {
                var t = new GameObject("Nazev").AddComponent<TextMeshPro>(); t.transform.SetParent(root, false);
                t.transform.SetPositionAndRotation(pole + Vector3.up * 2.75f + away * (0.035f * -sgn), Quaternion.LookRotation(away * sgn));
                t.text = stopNames[k]; t.fontSize = 1.1f; t.color = new Color(0.1f, 0.1f, 0.12f); t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center; t.rectTransform.sizeDelta = new Vector2(0.86f, 0.45f); t.enableAutoSizing = true; t.fontSizeMin = 0.5f; t.fontSizeMax = 1.1f;
            }
        }
        mb.Build("ZastavkyMesh", root, false);
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
        if (Metro.I != null && Metro.Halls.Count > 0)
        {
            if (Metro.I.AtTrain(pp, out var hall))
            {
                hud.promptText.text = $"[E] NASTOUPIT · LINKA {hall.lineRef}";
                if (e) OpenMetro(hall.station, gm, hall.line);
                return true;
            }
            return false;
        }
        foreach (var en in MapBuilder.D.metro.entrances) // (no walk-down metro built: the old shortcut)
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

    public void OpenMetro(int st, GameManager gm, int onlyLine = -1)
    {
        lines.Clear();
        var order = MapBuilder.D.metro.order;
        for (int l = 0; l < order.Length; l++) if (System.Array.IndexOf(order[l].s, st) >= 0 && (onlyLine < 0 || l == onlyLine)) lines.Add(l);
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
                              $"[A/D] CÍL   {(lines.Count > 1 ? "[W] PŘESTUP NA JINOU LINKU   " : "")}[E] JET   [Q] ZPĚT";
        if ((kb != null && kb.eKey.wasPressedThisFrame) || debugE) StartCoroutine(Ride(L.s[dest], minutes, L.@ref, gm, lines[line]));
    }

    IEnumerator Ride(int to, int minutes, string lineRef, GameManager gm, int lineIdx = -1)
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
        float yaw = gm.player.transform.eulerAngles.y;
        if (Metro.I != null && Metro.I.Platform(to, lineIdx, out var plat, out var py)) { at = plat; yaw = py; } // off the train on that platform
        gm.player.Teleport(at, yaw);
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
    public bool dead; public Transform camTarget; public int route;
    static readonly int[] Routes = { 2, 3, 5, 6, 9, 12, 14, 15, 17, 18, 20, 22, 23, 24 }; // lines through the centre
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
        // Prague liveries after photos: red below, cream band, black glazing; 15T ForCity (low floor, long sloped nose) or
        // the boxy T3 (high floor, cream band under the windows); grey roof gear, a pantograph on the middle section
        bool t3 = rng.NextDouble() < 0.3; t.route = Routes[rng.Next(Routes.Length)];
        var red = Gfx.Mat(new Color(0.72f, 0.07f, 0.09f), 0.6f); var cream = Gfx.Mat(new Color(0.94f, 0.9f, 0.78f), 0.5f);
        var glass = Gfx.Mat(new Color(0.06f, 0.07f, 0.08f), 0.92f); var grey = Gfx.Mat(Palette.Tire, 0.3f);
        var roofGrey = Gfx.Mat(new Color(0.55f, 0.56f, 0.57f), 0.4f); var doorM = Gfx.Mat(new Color(0.18f, 0.18f, 0.19f), 0.7f);
        var head = Gfx.Emissive(Palette.HeadLight, 1.8f); var tail = Gfx.Emissive(new Color(0.9f, 0.08f, 0.05f), 1.4f);
        var amber = Gfx.Emissive(new Color(1f, 0.62f, 0.12f), 1.6f);
        for (int k = 0; k < 3; k++)
        {
            var sec = new GameObject("Clanek" + k); sec.transform.SetParent(go.transform, false); sec.layer = Traffic.CarLayer; sec.tag = "Player"; // camera deoccluder ignores it, as with cars
            var mb = new MeshBuilder();
            float fl = t3 ? 0.9f : 0.35f, L = 9.3f, wy = t3 ? 2.35f : 2.05f; // floor, length, window band centre
            mb.Box(grey, new Vector3(0, fl * 0.5f + 0.1f, 0), new Vector3(2.2f, fl + 0.2f, L - 0.6f)); // bogies / skirt
            mb.Box(red, new Vector3(0, (fl + wy - 0.5f) / 2f, 0), new Vector3(2.46f, wy - 0.5f - fl, L));
            mb.Box(glass, new Vector3(0, wy, 0), new Vector3(2.48f, 1.0f, L - 0.2f));
            mb.Box(t3 ? cream : red, new Vector3(0, wy + 0.65f, 0), new Vector3(2.46f, 0.3f, L));
            mb.Box(cream, new Vector3(0, t3 ? wy - 0.62f : wy + 0.88f, 0), new Vector3(2.47f, 0.22f, L)); // the cream band
            mb.Box(roofGrey, new Vector3(0, wy + 1.0f, 0), new Vector3(2.3f, 0.12f, L - 0.3f));
            mb.Box(roofGrey, new Vector3(0, wy + 1.25f, k == 1 ? -2.5f : 0), new Vector3(1.7f, 0.38f, 4.5f)); // roof gear covers
            foreach (float sx in new[] { -1f, 1f }) // double doors, glazed
                foreach (float dz in new[] { -2.6f, 2.6f })
                {
                    mb.Box(doorM, new Vector3(sx * 1.24f, (fl + wy + 0.45f) / 2f, dz), new Vector3(0.04f, wy + 0.45f - fl, 1.3f));
                    mb.Box(glass, new Vector3(sx * 1.25f, wy - 0.1f, dz), new Vector3(0.04f, 1.1f, 1.1f));
                }
            if (k == 1) // pantograph
            {
                var pz = new Vector3(0, wy + 1.45f, 2.2f);
                mb.Box(grey, pz + new Vector3(0, 0.45f, -0.4f), new Vector3(0.06f, 0.06f, 1.3f), Quaternion.Euler(-40, 0, 0));
                mb.Box(grey, pz + new Vector3(0, 0.95f, 0.05f), new Vector3(0.06f, 0.06f, 1.2f), Quaternion.Euler(45, 0, 0));
                mb.Box(grey, pz + new Vector3(0, 1.38f, -0.2f), new Vector3(1.5f, 0.06f, 0.12f));
            }
            if (k != 1) // the ends: 15T a long sloped nose with a big dark windscreen, T3 a rounded box; lights, route display
            {
                float e = k == 0 ? 1f : -1f, z = e * L / 2f;
                if (!t3)
                {
                    mb.Box(red, new Vector3(0, (fl + 1.6f) / 2f, z + e * 0.35f), new Vector3(2.3f, 1.6f - fl, 0.7f));
                    mb.Box(glass, new Vector3(0, 2.35f, z + e * 0.22f), new Vector3(2.2f, 1.5f, 0.5f), Quaternion.Euler(e * -14f, 0, 0));
                    mb.Box(red, new Vector3(0, 3.1f, z + e * 0.05f), new Vector3(2.3f, 0.35f, 0.3f));
                }
                else
                {
                    mb.Box(red, new Vector3(0, (fl + 2.0f) / 2f, z + e * 0.25f), new Vector3(2.3f, 2.0f - fl, 0.5f));
                    mb.Box(glass, new Vector3(0, 2.5f, z + e * 0.2f), new Vector3(2.1f, 1.0f, 0.42f), Quaternion.Euler(e * -6f, 0, 0));
                }
                float lz = z + e * (t3 ? 0.51f : 0.71f);
                foreach (float hx in new[] { -0.8f, 0.8f }) mb.Box(k == 0 ? head : tail, new Vector3(hx, fl + 0.55f, lz), new Vector3(0.28f, 0.16f, 0.05f));
                mb.Box(amber, new Vector3(0, t3 ? 3.15f : 3.0f, z + e * (t3 ? 0.46f : 0.21f)), new Vector3(1.2f, 0.26f, 0.05f)); // route display
            }
            mb.Build("Mesh", sec.transform, false).isStatic = false;
            if (k == 0)
            {
                var num = new GameObject("Linka").AddComponent<TextMeshPro>(); num.transform.SetParent(sec.transform, false);
                num.transform.localPosition = new Vector3(0, t3 ? 3.15f : 3.0f, 9.3f / 2f + (t3 ? 0.49f : 0.24f)); num.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                num.text = t.route.ToString(); num.fontSize = 2.2f; num.color = Color.black; num.fontStyle = FontStyles.Bold;
                num.alignment = TextAlignmentOptions.Center; num.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f); num.textWrappingMode = TextWrappingModes.NoWrap;
            }
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
