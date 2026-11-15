using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Atom "Majetek": everything you buy does something. Flat = save + hideout, garage = repaint (the police stop
// looking for that car), business = money every open hour and more people in its street.
// Buy: aim with Q at a real building or place, then [B]. Use: [F] at your flat (on foot) or garage (in a car).
public class Property : MonoBehaviour
{
    public static Property I { get; private set; }
    public int flat = -1, garage = -1;                 // IPR building ids
    public readonly HashSet<int> shops = new();        // indices into MapBuilder.D.places
    public const int FlatPrice = 3000, GaragePrice = 2000, RepaintPrice = 200;
    readonly Dictionary<int, GameObject> signs = new();
    int lastHour = -1;

    static readonly HashSet<string> Homes = new() { "bytové domy", "polyfunkční rodinné a bytové domy", "činžovní vily", "rodinné domy" };
    static readonly HashSet<string> Garages = new() { "garáže", "čerpací stanice pohonných hmot", "servisní a provozní dopravní plochy a zařízení" };

    void Awake() { I = this; }

    public static int ShopPrice(string kind) => kind is "restaurant" or "pub" or "bar" or "biergarten" ? 6000 : 4000;
    public static int Income(string kind) => ShopPrice(kind) / 100; // Kč per open game hour: pays back in ~100 hours

    // The line the Q card adds: what the thing costs, or what it does for you.
    public string Offer(MapBuilder.IprBuilding? b, int place)
    {
        if (place >= 0)
        {
            var p = MapBuilder.D.places[place];
            return shops.Contains(place) ? $"\n<color=#E8C15A>Tvůj podnik: vydělává {Income(p.k)} Kč za každou otevřenou hodinu.</color>"
                 : string.IsNullOrEmpty(p.n) ? "" : $"\n<color=#E8C15A>[B] koupit podnik za {ShopPrice(p.k)} Kč · vydělá {Income(p.k)} Kč/h, přiláká lidi.</color>";
        }
        if (b == null) return "";
        var v = b.Value;
        if (v.id == flat) return "\n<color=#E8C15A>Tvůj byt: [F] u domu = uložit hru a schovat se před policií.</color>";
        if (v.id == garage) return $"\n<color=#E8C15A>Tvoje garáž: přijeď autem a [F] přelakuje ({RepaintPrice} Kč), policie ho přestane hledat.</color>";
        if (Homes.Contains(v.use)) return $"\n<color=#E8C15A>[B] koupit byt za {FlatPrice} Kč · uložení hry a úkryt.</color>";
        if (Garages.Contains(v.use)) return $"\n<color=#E8C15A>[B] koupit garáž za {GaragePrice} Kč · přelakování auta.</color>";
        return "";
    }

    void Buy(MapBuilder.IprBuilding? b, int place)
    {
        var hud = GameManager.I.hud; var jobs = Jobs.I;
        int price; string what;
        if (place >= 0 && !shops.Contains(place) && !string.IsNullOrEmpty(MapBuilder.D.places[place].n)) { price = ShopPrice(MapBuilder.D.places[place].k); what = MapBuilder.D.places[place].n; }
        else if (place < 0 && b != null && Homes.Contains(b.Value.use) && b.Value.id != flat) { price = FlatPrice; what = "byt"; }
        else if (place < 0 && b != null && Garages.Contains(b.Value.use) && b.Value.id != garage) { price = GaragePrice; what = "garáž"; }
        else return;
        if (jobs.money < price) { hud.Toast($"NA TO NEMÁŠ: CHYBÍ {price - jobs.money} KČ", Palette.Accent); return; }
        jobs.money -= price;
        if (place >= 0) { shops.Add(place); Sign(place); } else if (what == "byt") flat = b.Value.id; else garage = b.Value.id;
        Rep.Add(place >= 0 ? At(place) : b.Value.at, 1); // a local owner: the street knows you
        hud.Toast($"KOUPENO: {what.ToUpperInvariant()} · −{price} KČ", Palette.Gold, 2.4f);
        GameAudio.I?.PlayAt(GameAudio.I.sting, GameManager.I.player.transform.position, 0.6f);
        GameManager.I.Save();
    }

    static Vector3 At(int place) { var p = MapBuilder.D.places[place].p; return new Vector3(p[0], MapBuilder.Height(p[0], p[1]), p[1]); }

    // a gold sign over an owned business: you can see your street from afar
    void Sign(int place)
    {
        if (signs.ContainsKey(place)) return;
        var mb = new MeshBuilder();
        mb.Box(Gfx.Emissive(Palette.Gold, 2.5f), Vector3.zero, new Vector3(3.2f, 1f, 0.15f));
        var go = mb.Build("VyvesniStit", transform, false, false);
        go.transform.position = At(place) + Vector3.up * 3.5f; // the place point is its street door: a shop sign over it
        signs[place] = go;
    }

    static bool Near(MapBuilder.IprBuilding b, Vector3 me, float extra) =>
        new Vector2(b.at.x - me.x, b.at.z - me.z).magnitude < b.r + extra;

    static bool Find(int id, out MapBuilder.IprBuilding b)
    {
        foreach (var x in MapBuilder.Ipr) if (x.id == id) { b = x; return true; }
        b = default; return false;
    }

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var kb = Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame && Time.time - Inspect.lastTime < 6f) Buy(Inspect.lastBuilding, Inspect.lastPlace);

        int h = Mathf.FloorToInt(GameClock.I.hour);
        if (h != lastHour)
        {
            if (lastHour >= 0) Pay(h);
            lastHour = h;
        }
    }

    public void Pay(int hour)
    {
        int sum = 0;
        foreach (int s in shops) { var p = MapBuilder.D.places[s]; if (Places.IsOpen(p.k, hour)) sum += Income(p.k); }
        if (sum > 0) { Jobs.I.money += sum; GameManager.I.hud.Toast($"PODNIKY VYDĚLALY +{sum} KČ", Palette.Gold, 1.6f); }
    }

    // After everyone else wrote their prompt: offer what your property can do here.
    void LateUpdate()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var cam = Camera.main.transform;
        foreach (var go in signs.Values) go.transform.rotation = Quaternion.Euler(0, cam.eulerAngles.y, 0); // always readable
        var kb = Keyboard.current; var car = gm.currentCar;
        var me = car ? car.transform.position : gm.player.transform.position;
        if (!car && flat >= 0 && Find(flat, out var fb) && Near(fb, me, 5f))
        {
            gm.hud.promptText.text = "[F] DOMŮ: ULOŽIT HRU" + (Law.I.wanted > 0 ? " A SCHOVAT SE" : "");
            if (kb != null && kb.fKey.wasPressedThisFrame) GoHome();
        }
        else if (car && garage >= 0 && Find(garage, out var gb) && Near(gb, me, 8f) && Mathf.Abs(car.SpeedKmh) < 6f)
        {
            gm.hud.promptText.text = $"[F] PŘELAKOVAT AUTO ({RepaintPrice} KČ)";
            if (kb != null && kb.fKey.wasPressedThisFrame) RepaintHere(car);
        }
    }

    public void GoHome()
    {
        var gm = GameManager.I;
        bool wanted = Law.I.wanted > 0, hid = Law.I.Hide();
        gm.Save();
        gm.hud.Toast(hid ? "DOMA · POLICIE TĚ ZTRATILA · ULOŽENO" : wanted ? "DOMA · ULOŽENO, ALE POLICIE TĚ VIDĚLA JÍT DOVNITŘ" : "DOMA · ULOŽENO", Palette.Gold, 2.4f);
    }

    public void RepaintHere(CarController car)
    {
        var gm = GameManager.I;
        if (Jobs.I.money < RepaintPrice) { gm.hud.Toast("NA LAK NEMÁŠ", Palette.Accent); return; }
        Jobs.I.money -= RepaintPrice;
        Repaint(car, Palette.CarPaints[Random.Range(0, Palette.CarPaints.Length)]);
        Law.I.Claim(car);
        gm.hud.Toast("PŘELAKOVÁNO · AUTO JE TEĎ TVOJE A POLICIE HO NEPOZNÁ", Palette.Gold, 2.4f);
    }

    public static void Repaint(CarController car, Color c)
    {
        var mr = car.transform.Find("Karoserie")?.GetComponent<MeshRenderer>();
        if (!mr) return;
        var mats = mr.sharedMaterials; mats[0] = Gfx.Mat(c, 0.65f); mr.sharedMaterials = mats; // paint is the body's first material
    }

    // Respawn at home after loading a save.
    public bool Home(out Vector3 at)
    {
        at = default;
        if (flat < 0 || !Find(flat, out var b)) return false;
        if (!Traffic.I.net.Nearest(b.at, out int s, out int v)) return false;
        at = Traffic.I.net.V(s, v) + Vector3.up * 0.3f; return true;
    }

    public void Load(int f, int g, List<int> s)
    {
        flat = f; garage = g;
        if (s != null) foreach (int i in s) if (i >= 0 && i < MapBuilder.D.places.Length) { shops.Add(i); Sign(i); }
    }

    public void Clear()
    {
        flat = garage = -1; shops.Clear();
        foreach (var go in signs.Values) Destroy(go);
        signs.Clear();
    }

    public bool OwnsPlace(MapBuilder.Poly p) => shops.Count > 0 && shops.Contains(System.Array.IndexOf(MapBuilder.D.places, p));
}
