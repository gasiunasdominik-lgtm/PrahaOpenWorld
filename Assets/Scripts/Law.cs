using System.Collections.Generic;
using UnityEngine;

// Atoms "Vlastnictví" and "Svědek", and the police that only knows what a witness told it.
// Crime → nearest witness with line of sight calls (30 s, visible) → report: a patrol drives to the reported spot →
// a patrol that sees you (or the reported car) chases → caught = fine; out of sight long enough = it fades.
public class Law : MonoBehaviour
{
    public static Law I { get; private set; }
    public int wanted; public string why = ""; public CarController wantedCar; public Vector3 reportAt;
    readonly Dictionary<CarController, string> owners = new();
    class Call { public Walker w; public Driver d; public Vector3 at; public float t; public string what; public CarController car; public bool npc; }
    readonly List<Call> calls = new();
    readonly List<Driver> police = new();
    readonly System.Random rng = new(5);
    Transform icon; Material iconMat;
    float unseen, caughtT, decay, incidentT = 40f, dispatchT; Vector3 dispatchAt;
    static readonly string[] Incidents = { "kapsář okradl turistu", "rvačka před hospodou", "někdo rozbil výlohu", "opilec obtěžuje lidi" };
    const float CallTime = 30f, SeeDist = 60f;

    void Awake()
    {
        I = this;
        iconMat = new Material(Gfx.Emissive(Palette.Accent, 3f));
        var mb = new MeshBuilder(); mb.Box(iconMat, Vector3.zero, new Vector3(0.35f, 0.35f, 0.35f));
        icon = mb.Build("Telefon", transform, false, false).transform; icon.gameObject.SetActive(false);
    }

    void Start()
    {
        for (int k = 0; k < 2; k++)
        {
            var car = CarController.Build(new Vector3(0, -60, 0), 0, new Color(0.92f, 0.93f, 0.95f), 0);
            var mb = new MeshBuilder();
            mb.Box(Gfx.Mat(new Color(0.1f, 0.25f, 0.7f), 0.5f), new Vector3(0, 0.75f, 0), new Vector3(1.82f, 0.18f, 4.12f)); // blue stripe
            mb.Box(Gfx.Emissive(new Color(0.2f, 0.4f, 1f), 3f), new Vector3(0, 1.66f, -0.3f), new Vector3(1.1f, 0.14f, 0.3f)); // light bar
            mb.Build("Policie", car.transform, false, false).isStatic = false;
            var d = car.gameObject.AddComponent<Driver>(); d.car = car; d.police = true;
            owners[car] = "Policie ČR";
            car.gameObject.SetActive(false);
            police.Add(d);
        }
    }

    public string Owner(CarController c)
    {
        if (!owners.TryGetValue(c, out var o))
            owners[c] = o = GameManager.I.cars.Count > 0 && c == GameManager.I.cars[0] ? "ty" : Names.Person(c.GetHashCode());
        return o;
    }

    // Called when the player gets into a car.
    public void OnEnterCar(CarController c, bool hadDriver)
    {
        if (Owner(c) == "ty") return;
        var at = c.transform.position;
        Crime(hadDriver ? "přepadení řidiče" : "krádež auta", at, c, hadDriver ? Pedestrians.I.Victim(at - c.transform.right * 2.2f) : null);
    }

    // Something bad happened at `at`. Who saw it?
    public void Crime(string what, Vector3 at, CarController car, Walker victim = null)
    {
        foreach (var c in calls) if (c.what == what && (c.at - at).sqrMagnitude < 400f) return; // already being reported
        Walker bestW = null; Driver bestD = null; float best = Rep.WitnessRange(at); best *= best;
        foreach (var w in Pedestrians.I.live)
        {
            if (w.Down) continue;
            float d = (w.transform.position - at).sqrMagnitude;
            if (d < best && Sees(w.transform.position + Vector3.up * 1.6f, at)) { best = d; bestW = w; }
        }
        foreach (var d in FindObjectsByType<Driver>())
        {
            if (!d.enabled || d.police || d.car == car) continue;
            float q = (d.transform.position - at).sqrMagnitude;
            if (q < best && Sees(d.transform.position + Vector3.up * 1.4f, at)) { best = q; bestW = null; bestD = d; }
        }
        if (victim) { bestW = victim; bestD = null; } // the driver you threw out saw you best of all
        var hud = GameManager.I.hud;
        if (!bestW && !bestD) { hud.Toast($"{what.ToUpperInvariant()} · NIKDO TĚ NEVIDĚL", Palette.UILight); return; }
        Rep.Add(at, -2); // reputation is what people saw
        if (bestW) bestW.waiting = true; // stops to make the call
        calls.Add(new Call { w = bestW, d = bestD, at = at, t = CallTime, what = what, car = car });
        hud.Toast($"{what.ToUpperInvariant()} · {(victim ? "OKRADENÝ ŘIDIČ VOLÁ POLICII" : bestW ? "CHODEC TĚ VIDĚL" : "ŘIDIČ TĚ VIDĚL")}", Palette.Accent);
    }

    public static bool Sees(Vector3 eye, Vector3 at)
    {
        var to = at + Vector3.up - eye;
        return !Physics.Raycast(eye, to.normalized, out var hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore)
               || hit.collider.CompareTag("Player") || hit.rigidbody != null; // only walls and terrain block the view
    }

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var me = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;
        string status = "";

        // witnesses on the phone
        for (int k = calls.Count - 1; k >= 0; k--)
        {
            var c = calls[k]; c.t -= Time.deltaTime;
            bool gone = c.w ? (!c.w.gameObject.activeSelf || c.w.Down) : (!c.d || !c.d.enabled || !c.d.gameObject.activeSelf);
            if (gone) { if (c.w) c.w.waiting = false; calls.RemoveAt(k); continue; }
            if (c.t <= 0f)
            {
                if (c.w) c.w.waiting = false;
                calls.RemoveAt(k);
                Report(c);
            }
        }
        if (calls.Count > 0)
        {
            var c = calls[0]; var who = c.w ? c.w.transform : c.d.transform;
            icon.gameObject.SetActive(true);
            icon.position = who.position + Vector3.up * (c.w ? 2.4f : 2.6f);
            icon.Rotate(0, 180f * Time.deltaTime, 0);
            iconMat.SetColor("_EmissionColor", Palette.Accent * (Mathf.Repeat(Time.time, 0.6f) < 0.3f ? 4f : 0.5f));
            status = c.npc ? "" : $"SVĚDEK VOLÁ POLICII {Mathf.CeilToInt(c.t)} S · {c.what.ToUpperInvariant()}";
        }
        else icon.gameObject.SetActive(false);

        if (wanted > 0) status = Police(gm, me, status);
        else if (dispatchT > 0f) Patrol();
        if ((incidentT -= Time.deltaTime) <= 0f) { incidentT = 70f + rng.Next(50); Incident(me); }
        gm.hud.wantedText.text = status;
    }

    // The city has its own trouble: one passer-by wrongs another, a third one calls the police.
    void Incident(Vector3 me)
    {
        var live = Pedestrians.I.live; if (live.Count < 3) return;
        var v = live[rng.Next(live.Count)]; var at = v.transform.position;
        float d = Vector3.Distance(at, me); if (d < 30f || d > 180f) return;
        Walker witness = null;
        foreach (var w in live) if (w != v && !w.Down && (w.transform.position - at).sqrMagnitude < 30f * 30f && Sees(w.transform.position + Vector3.up * 1.6f, at)) { witness = w; break; }
        // pickpockets work tourists at busy metro interchanges (Policie ČR: ~half of Prague pickpocketing is in Prague 1–2)
        bool hub = false; foreach (var st in MapBuilder.D.metro.stations) if ((new Vector3(st.p[0], at.y, st.p[1]) - at).sqrMagnitude < 250f * 250f) { hub = true; break; }
        string what = hub && rng.NextDouble() < 0.7 ? Incidents[0] : Incidents[rng.Next(Incidents.Length)];
        if (!witness) { Jobs.News($"{CityBuilder.District(at)}: {what}, nikdo nic neviděl."); return; }
        witness.waiting = true;
        calls.Add(new Call { w = witness, at = at, t = CallTime, what = what, npc = true });
        Jobs.News($"{CityBuilder.District(at)}: {what}. Svědek volá policii.");
    }

    void Patrol()
    {
        dispatchT -= Time.deltaTime;
        foreach (var p in police)
            if (p.gameObject.activeSelf && (p.transform.position - p.goal).sqrMagnitude < 25f * 25f)
                p.goal = dispatchAt + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 150f;
        if (dispatchT <= 0f) Clear();
    }

    void Report(Call c)
    {
        if (c.npc) { dispatchT = 90f; dispatchAt = c.at; Dispatch(c.at); Jobs.News($"Policie jede k hlášení: {c.what}."); return; }
        wanted = Mathf.Max(wanted, 1); why = c.what; reportAt = c.at; wantedCar = c.car; decay = 0f;
        GameManager.I.hud.Toast($"POLICIE PŘIJALA HLÁŠENÍ: {c.what.ToUpperInvariant()}", Palette.Accent, 2.4f);
        Dispatch(reportAt);
    }

    void Dispatch(Vector3 to)
    {
        foreach (var p in police)
        {
            if (p.gameObject.activeSelf) { p.goal = to; continue; }
            var net = Traffic.I.net;
            if (!net.RandomVertex(to, 220f, 400f, rng, out int s, out int v)) continue;
            int dir = v == net.N(s) - 1 ? -1 : v == 0 ? 1 : 1;
            var a = net.V(s, v); var b = net.V(s, v + dir);
            var fwd = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
            p.transform.SetPositionAndRotation(a + Vector3.Cross(Vector3.up, fwd) * Driver.LaneOf(net.W[s]) + Vector3.up * 0.45f, Quaternion.LookRotation(fwd));
            p.gameObject.SetActive(true);
            p.goal = to; p.chase = null;
            p.Begin(net, s, v, dir, rng);
        }
    }

    string Police(GameManager gm, Vector3 me, string status)
    {
        var target = gm.currentCar ? gm.currentCar.transform : gm.player.transform;
        bool recognisable = wantedCar == null || gm.currentCar == wantedCar; // they look for the car, not you, when a car was reported
        bool seen = false; Driver near = null; float nd = float.MaxValue;
        foreach (var p in police)
        {
            if (!p.gameObject.activeSelf) continue;
            float d = Vector3.Distance(p.transform.position, me);
            if (d < nd) { nd = d; near = p; }
            if (recognisable && d < SeeDist && Sees(p.transform.position + Vector3.up * 1.4f, me)) seen = true;
            p.chase = wanted >= 2 ? target : null;
            if (wanted >= 2) p.goal = me; // last known position
            if (wanted < 2 && (p.transform.position - p.goal).sqrMagnitude < 25f * 25f) // nobody at the spot: patrol around it
                p.goal = reportAt + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 120f;
        }
        if (seen) { if (wanted < 2) gm.hud.Toast("POLICIE TĚ VIDÍ!", Palette.Accent); wanted = 2; unseen = 0f; }
        else unseen += Time.deltaTime;

        if (wanted >= 2)
        {
            bool slow = !gm.currentCar || gm.currentCar.SpeedKmh < 8f;
            caughtT = near && nd < 12f && slow ? caughtT + Time.deltaTime : 0f;
            if (caughtT > 2f) { Arrest(gm); return ""; }
            if (unseen > 25f) { wanted = 1; decay = 0f; gm.hud.Toast("POLICIE TĚ ZTRATILA Z OČÍ", Palette.UILight); }
            return $"HLEDANOST 2 · PRONÁSLEDUJE TĚ POLICIE · {why.ToUpperInvariant()}" + (unseen > 3f ? $" · ZTRÁCÍŠ JI ({25f - unseen:0} S)" : "");
        }
        // level 1: they search the reported spot (or the reported car); stay away and it fades
        bool away = Vector3.Distance(me, reportAt) > 120f && (wantedCar == null || gm.currentCar != wantedCar);
        decay = away ? decay + Time.deltaTime : 0f;
        if (decay > 60f) { Clear(); gm.hud.Toast("HLÁŠENÍ ZAPADLO", Palette.UILight); return status; }
        string what = wantedCar ? "HLEDAJÍ TVOJE AUTO" : "HLÍDKA JEDE NA MÍSTO HLÁŠENÍ";
        return (status.Length > 0 ? status + "\n" : "") + $"HLEDANOST 1 · {what}" + (away ? $" · ZAPADNE ZA {60f - decay:0} S" : " · ODEJDI OD MÍSTA");
    }

    void Arrest(GameManager gm)
    {
        int fine = Mathf.Min(Jobs.I.money, 500);
        Jobs.I.money -= fine;
        if (gm.currentCar && Owner(gm.currentCar) != "ty")
        {
            var stolen = gm.currentCar; gm.ExitCar(); stolen.gameObject.SetActive(false); // returned to the owner
        }
        Jobs.I.Drop(); // a waiting taxi customer walks on
        gm.hud.Toast($"ZADRŽEN POLICIÍ · POKUTA {fine} KČ", Palette.Accent, 3f);
        Clear();
    }

    // Going inside your flat: the police lose you, unless they are watching you right now.
    public bool Hide()
    {
        if (wanted == 0 || (wanted >= 2 && unseen < 5f)) return false;
        Clear(); return true;
    }

    public void Claim(CarController c) { owners[c] = "ty"; if (wantedCar == c) Clear(); }

    void Clear()
    {
        wanted = 0; why = ""; wantedCar = null; caughtT = unseen = decay = dispatchT = 0f;
        foreach (var p in police) { p.chase = null; p.goal = p.transform.position + p.transform.forward * 2000f; } // drive off, despawn later
    }

    void LateUpdate()
    {
        if (wanted > 0 || dispatchT > 0f) return;
        foreach (var p in police)
            if (p.gameObject.activeSelf && !Net.InView(p.transform.position)) p.gameObject.SetActive(false);
    }
}
