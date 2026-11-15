using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Atom "Zakázka": who wants what, from where to where, by when and for how much. Offers come from the city itself:
// a pedestrian with a long way to go wants a taxi, an open place wants something delivered to another place.
public class Job
{
    public string kind, giver, reason;
    public Vector3 from, to; public string fromName, toName;
    public int reward; public float limit, started, posted; public int stage; // 0 = go to pickup, 1 = deliver
    public Walker passenger;
    public float Km => Vector3.Distance(from, to) / 1000f;
}

public class Jobs : MonoBehaviour
{
    public static Jobs I { get; private set; }
    public int money = 500;
    public readonly List<Job> offers = new();
    public Job active;
    public static readonly List<string> feed = new(); // what happens in the city without you
    public static void News(string s) { feed.Insert(0, $"{GameClock.I.Clock}  {s}"); if (feed.Count > 4) feed.RemoveAt(4); }
    public bool phoneOpen;
    readonly System.Random rng = new(21);
    float offerTimer;
    Transform beacon;

    void Awake()
    {
        I = this; feed.Clear(); Rep.score.Clear(); // statics survive play-mode restarts (no domain reload)
        Debug.Assert(Pay(100, 10f, 100f) == 110 && Pay(100, 80f, 100f) == 100 && Pay(100, 110f, 100f) == 50 && Pay(100, 201f, 100f) == 0, "Jobs.Pay");
        var mb = new MeshBuilder();
        mb.Cylinder(Gfx.Emissive(Palette.Gold, 2f), Vector3.zero, 0.6f, 60f, 10, true, true);
        beacon = mb.Build("Cil", transform, false, false).transform;
        beacon.gameObject.SetActive(false);
    }

    public static bool Rush(float h) => (h >= 7f && h < 9f) || (h >= 16f && h < 19f);

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var kb = Keyboard.current;
        var me = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;

        if ((offerTimer -= Time.deltaTime) <= 0f)
        {
            offerTimer = 3f;
            offers.RemoveAll(o => (o.from - me).sqrMagnitude > 500f * 500f || (o.passenger && !o.passenger.gameObject.activeSelf));
            for (int k = offers.Count - 1; k >= 0; k--) // the city does not wait for you: someone else takes it
                if (Time.time - offers[k].posted > 70f + rng.Next(60))
                {
                    var o = offers[k]; offers.RemoveAt(k);
                    News(o.kind == "taxi" ? $"{o.giver} si vzal jiné taxi." : $"{o.giver}: zásilku odvezl jiný kurýr.");
                }
            for (int k = 0; k < 3 && offers.Count < 4; k++) { int r = rng.Next(3); var j = r == 0 ? Taxi(me) : Courier(me, r == 2 ? Local.For(CityBuilder.District(me)) : null); if (j != null) { j.posted = Time.time; offers.Add(j); } }
        }
        if (kb != null)
        {
            if (kb.tabKey.wasPressedThisFrame) phoneOpen = !phoneOpen;
            if (phoneOpen && active == null)
                for (int k = 0; k < offers.Count; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame) { Accept(offers[k]); break; }
            if (phoneOpen && active != null && kb.xKey.wasPressedThisFrame) { Release(); active = null; gm.hud.Toast("ZAKÁZKA ZRUŠENA", Palette.UILight); }
        }
        if (active != null) Progress(gm, me);

        int rep = Rep.Of(CityBuilder.District(me));
        gm.hud.moneyText.text = $"{money:N0} Kč".Replace(',', ' ') + $"\n<size=22>POVĚST ZDE: {rep:+0;-0;0}</size>";
        gm.hud.jobText.text = active == null ? "" :
            $"{(active.stage == 0 ? "VYZVEDNI" : "DORUČ")}: {(active.stage == 0 ? active.fromName : active.toName).ToUpperInvariant()}\n" +
            $"{active.reward} KČ · ZBÝVÁ {Mmss(active.limit - (Time.time - active.started))} · {Vector3.Distance(me, Target):0} M";
        gm.hud.phonePanel.SetActive(phoneOpen);
        if (phoneOpen) gm.hud.phoneText.text = PhoneText(me);
        beacon.gameObject.SetActive(active != null);
        if (active != null) beacon.position = new Vector3(Target.x, MapBuilder.Height(Target.x, Target.z), Target.z);
    }

    Vector3 Target => active.stage == 0 ? (active.passenger ? active.passenger.transform.position : active.from) : active.to;

    static string Mmss(float s) => s < 0 ? "-" + Mmss(-s) : $"{(int)(s / 60)}:{(int)(s % 60):00}";

    string PhoneText(Vector3 me)
    {
        var sb = new System.Text.StringBuilder("<size=26>MOBIL · ZAKÁZKY KOLEM TEBE</size>\n\n");
        if (active != null) sb.Append($"Právě děláš: {active.reason}\n{active.reward} Kč\n\n[X] zrušit zakázku   [TAB] zavřít");
        else if (offers.Count == 0) sb.Append("Teď tu nikdo nic nepotřebuje.\nZkus jinou čtvrť nebo jinou hodinu.");
        else
            for (int k = 0; k < offers.Count; k++)
            {
                var o = offers[k];
                sb.Append($"[{k + 1}] {o.kind.ToUpperInvariant()} · {o.reward} Kč · {Mmss(o.limit)} · {Vector3.Distance(me, o.from):0} m\n");
                sb.Append($"<size=21>{o.giver}: „{o.reason}“</size>\n\n");
            }
        if (Rush(GameClock.I.hour)) sb.Append("\n<size=21>Špička: zakázky platí o polovinu víc.</size>");
        if (feed.Count > 0) { sb.Append("\n<size=23>CO SE DĚJE VE MĚSTĚ</size>\n<size=20>"); foreach (var f in feed) sb.Append(f).Append('\n'); sb.Append("</size>"); }
        return sb.ToString();
    }

    // Taxi: someone already walking the streets whose destination is far.
    Job Taxi(Vector3 me)
    {
        foreach (var w in Pedestrians.I.live)
        {
            if (w.Down || w.waiting || w.destName == "domov") continue;
            float d = (w.transform.position - me).magnitude, trip = Vector3.Distance(w.transform.position, w.dest);
            if (d < 40f || d > 350f || trip < 300f || offers.Exists(o => o.passenger == w)) continue;
            float mult = Rush(GameClock.I.hour) ? 1.5f : 1f;
            var j = new Job { kind = "taxi", giver = Names.Person(w.GetHashCode()), passenger = w, from = w.transform.position, to = w.dest,
                fromName = "zákazník", toName = w.destName, reason = $"Potřebuju {Places.Why(w.purpose)} – {w.destName}, je to daleko." };
            mult *= Rep.PayMult(j.from);
            j.reward = Mathf.RoundToInt((50f + 35f * j.Km) * mult / 10f) * 10; j.limit = trip / 8f + 45f;
            return j;
        }
        return null;
    }

    // Courier: an open place sends what it makes to another open place.
    // With a local template the run is the district's own trade: from a place of its kind in this district.
    Job Courier(Vector3 me, Local loc = null)
    {
        float h = GameClock.I.hour; var ps = MapBuilder.D.places;
        for (int tries = 0; tries < (loc == null ? 30 : 1500); tries++)
        {
            var a = ps[rng.Next(ps.Length)];
            var ap = new Vector3(a.p[0], 0, a.p[1]); ap.y = MapBuilder.Height(ap.x, ap.z);
            float d = Vector3.Distance(new Vector3(me.x, ap.y, me.z), ap);
            if (d < 60f || d > 400f || !Places.IsOpen(a.k, h) || string.IsNullOrEmpty(a.n)) continue;
            if (loc != null && (System.Array.IndexOf(loc.from, a.k) < 0 || !loc.Has(CityBuilder.District(ap)))) continue;
            var b = ps[rng.Next(ps.Length)];
            if (loc != null && System.Array.IndexOf(loc.to, b.k) < 0) continue;
            var bp = new Vector3(b.p[0], 0, b.p[1]); bp.y = MapBuilder.Height(bp.x, bp.z);
            float trip = Vector3.Distance(ap, bp);
            if (b == a || trip < 300f || trip > 1600f || !Places.IsOpen(b.k, h) || string.IsNullOrEmpty(b.n)) continue;
            float mult = Rush(h) ? 1.5f : 1f;
            var j = new Job { kind = loc == null ? "kurýr" : "místní", giver = a.n, from = ap, to = bp, fromName = a.n, toName = b.n,
                reason = loc == null ? $"Pošleme {Places.Item(a.k)} do {b.n}, potřebují to hned." : string.Format(loc.why, b.n) };
            if (loc != null) mult *= 1.3f; // local trade pays a little better: they need someone who knows the streets
            mult *= Rep.PayMult(j.from);
            j.reward = Mathf.RoundToInt((40f + 30f * j.Km) * mult / 10f) * 10; j.limit = trip / 7f + 40f;
            return j;
        }
        return null;
    }

    void Accept(Job j)
    {
        active = j; offers.Remove(j); j.started = Time.time; j.stage = 0;
        if (j.passenger) j.passenger.waiting = true; // stands at the curb and waves
        phoneOpen = false;
        GameManager.I.hud.Toast($"ZAKÁZKA PŘIJATA: {j.reward} KČ", Palette.Gold);
    }

    void Release() { if (active?.passenger) active.passenger.waiting = false; }
    public void Drop() { Release(); active = null; }

    // Full pay in time (+10 % tip when fast), half when late, nothing past twice the limit.
    public static int Pay(int reward, float t, float limit) =>
        t > limit * 2f ? 0 : t <= limit ? reward + (t < limit * 0.6f ? reward / 10 : 0) : reward / 2;

    void Progress(GameManager gm, Vector3 me)
    {
        var j = active; float t = Time.time - j.started;
        if (t > j.limit * 2f) { Release(); active = null; gm.hud.Toast("ZAKÁZKA PROPADLA: NESTIHL JSI TO", Palette.Accent); return; }
        var car = gm.currentCar; bool slow = !car || car.SpeedKmh < 6f;
        if (j.stage == 0)
        {
            if (j.kind == "taxi")
            {
                if (!j.passenger || !j.passenger.gameObject.activeSelf) { active = null; gm.hud.Toast("ZÁKAZNÍK ODEŠEL", Palette.Accent); return; }
                if (car && slow && (j.passenger.transform.position - me).sqrMagnitude < 12f * 12f)
                {
                    j.passenger.waiting = false; Pedestrians.I.Despawn(j.passenger); j.passenger = null; j.stage = 1;
                    gm.hud.Toast("ZÁKAZNÍK NASTOUPIL", Palette.UILight);
                }
                else if (!car && (j.passenger.transform.position - me).sqrMagnitude < 6f * 6f) gm.hud.promptText.text = "TAXI POTŘEBUJE AUTO";
            }
            else if (slow && Flat(j.from - me) < 9f) { j.stage = 1; gm.hud.Toast("VYZVEDNUTO, VEZ TO DO: " + j.toName.ToUpperInvariant(), Palette.UILight); }
            return;
        }
        if (slow && Flat(j.to - me) < (j.kind == "taxi" ? 20f : 9f))
        {
            int pay = Pay(j.reward, t, j.limit);
            money += pay; if (t <= j.limit) Rep.Add(j.to, 1); active = null;
            gm.hud.Toast(t <= j.limit ? $"HOTOVO: +{pay} KČ" : $"POZDĚ: JEN +{pay} KČ", Palette.Gold, 2.4f);
            GameAudio.I?.PlayAt(GameAudio.I.sting, me, 0.6f);
        }
    }

    static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }
}

// Deterministic Czech names for owners, drivers and passers-by.
public static class Names
{
    static readonly string[] F = { "Jan", "Petr", "Jana", "Eva", "Tomáš", "Lucie", "Karel", "Marie", "Pavel", "Hana", "Jiří", "Tereza", "Martin", "Kateřina", "Lukáš", "Zdeňka" };
    static readonly string[] L = { "Novák", "Svoboda", "Dvořák", "Černý", "Procházka", "Kučera", "Veselý", "Horák", "Němec", "Pokorný", "Marek", "Kohout", "Beneš", "Fiala" };
    public static string Person(int seed)
    {
        uint h = (uint)seed * 2654435761u; string f = F[h % F.Length], l = L[(h >> 8) % L.Length];
        bool woman = f.EndsWith("a") || f == "Marie";
        if (woman) l = l.EndsWith("ý") ? l[..^1] + "á" : l.EndsWith("ek") ? l[..^2] + "ková" : l.EndsWith("ec") ? l[..^2] + "cová" : l.EndsWith("a") ? l[..^1] + "ová" : l + "ová";
        return f + " " + l;
    }
}

// District reputation = the sum of what you did there (on-time jobs +1, crimes -2). Pays more where people trust you; where they don't, they watch you.
public static class Rep
{
    public static readonly Dictionary<string, int> score = new();
    public static int Of(string district) => score.TryGetValue(district, out int v) ? v : 0;
    public static void Add(Vector3 at, int d) { var k = CityBuilder.District(at); score[k] = Of(k) + d; }
    public static float PayMult(Vector3 at) => 1f + Mathf.Clamp(Of(CityBuilder.District(at)), -10, 10) * 0.03f;
    public static float WitnessRange(Vector3 at) => 35f + Mathf.Clamp(-Of(CityBuilder.District(at)), 0, 10) * 2.5f;
}

// What each part of town trades. Matched by district name; reasons are written from the real character of the place.
public class Local
{
    public string[] names, from, to; public string why;
    public bool Has(string district) { foreach (var n in names) if (district.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
    static readonly Local[] all =
    {
        new() { names = new[] { "Hrad", "Hradčany", "Strahov", "Jelení", "Královská" }, from = new[] { "restaurant", "pub", "bar" }, to = new[] { "restaurant", "pub", "cafe" },
                why = "Klášterní pivovar na Strahově vaří víc, než vypijí. Sud piva do {0}, ať to stihneš." },
        new() { names = new[] { "Malá Strana", "Klárov", "Petřín", "Vojtěšská" }, from = new[] { "office", "cafe" }, to = new[] { "office", "restaurant" },
                why = "Z velvyslanectví pod Hradem jdou dokumenty, osobně do {0}. Nikomu je nedávej." },
        new() { names = new[] { "Staré Město", "Josefov", "Haštalská", "Anenská" }, from = new[] { "shop", "cafe" }, to = new[] { "shop", "restaurant" },
                why = "Turisti vykoupili stánek. Nové zboží do {0}, než přijde další zájezd." },
        new() { names = new[] { "Nové Město", "Karlov", "Podskalí", "Petrská", "nádraží" }, from = new[] { "office", "shop" }, to = new[] { "office", "cafe" },
                why = "Smlouva se podepisuje dnes. Vezmi ji do {0}, čekají jen na tebe." },
        new() { names = new[] { "Vinohrady", "Riegrovy" }, from = new[] { "bar", "restaurant", "shop" }, to = new[] { "bar", "restaurant", "cafe" },
                why = "Vinohrady jsou po vinicích: bedna vína do {0}, večer mají degustaci." },
        new() { names = new[] { "Žižkov", "Vítkov" }, from = new[] { "pub", "bar" }, to = new[] { "pub", "bar", "restaurant" },
                why = "Došel sud a štamgasti čekají. Vezmi plný do {0}." },
        new() { names = new[] { "Karlín", "Smíchov", "Rohanský" }, from = new[] { "office", "cafe" }, to = new[] { "office", "cafe" },
                why = "Kancelář v nové budově potřebuje notebook pro nováčka. Do {0}, prosím." },
    };
    public static Local For(string district) { foreach (var l in all) if (l.Has(district)) return l; return null; }
}
