using UnityEngine;
using UnityEngine.InputSystem;

// Key Q "Prohlédnout": aim at anything and the game says what it is, what it is for and whom it belongs to.
public class Inspect : MonoBehaviour
{
    float show;

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var kb = Keyboard.current;
        if ((show -= Time.deltaTime) <= 0f) gm.hud.cardPanel.SetActive(false);
        if (kb == null || !kb.qKey.wasPressedThisFrame || Transit.I.InMetro) return;
        string card = Describe(Camera.main.transform);
        gm.hud.cardText.text = card;
        gm.hud.cardPanel.SetActive(true);
        show = 6f;
    }

    // what Q last pointed at, so [B] can buy it
    public static float lastTime = -99f; public static MapBuilder.IprBuilding? lastBuilding; public static int lastPlace = -1;

    public static string Describe(Transform cam)
    {
        lastTime = Time.time; lastBuilding = null; lastPlace = -1;
        float h = GameClock.I.hour;
        // trigger colliders included: a pedestrian's sense capsule is the easiest thing to aim at
        if (!Physics.SphereCast(cam.position, 0.6f, cam.forward, out var hit, 90f, ~0, QueryTriggerInteraction.Collide))
            return "Nebe nad Prahou.\nNa nic nemíříš.";
        var w = hit.collider.GetComponentInParent<Walker>();
        if (w)
            return $"<b>{Names.Person(w.GetHashCode())}</b> · chodec\n" +
                   (w.waiting ? "Stojí a telefonuje." : w.Down ? "Leží na zemi, srazilo ho auto." : $"Jde {Places.Why(w.purpose)}: {w.destName}") +
                   (Places.Why(w.purpose) != "domů" && Vector3.Distance(w.transform.position, w.dest) > 300f ? "\nMá to daleko, možná by si vzal taxi." : "");
        var car = hit.collider.GetComponentInParent<CarController>();
        if (car)
        {
            var d = car.GetComponent<Driver>(); string owner = Law.I.Owner(car);
            string state = car == GameManager.I.currentCar ? (owner == "ty" ? "Tvoje auto, řídíš ho." : "Řídíš ho, ale není tvoje. Je kradené.")
                : d && d.police ? "Hlídka. Policie ví jen to, co jí nahlásí svědci."
                : d && d.enabled ? "Řídí ho majitel. Když ho vytáhneš, je to přepadení."
                : owner == "ty" ? "Tvoje auto. Můžeš si ho vzít." : "Zaparkované. Vzít ho je krádež, pokud tě někdo uvidí.";
            if (Law.I.wantedCar == car) state += "\nPolicie po tomhle autě pátrá.";
            return $"<b>Auto</b> · majitel: {owner}\n{state}";
        }
        var tram = hit.collider.GetComponentInParent<Tram>();
        if (tram) return "<b>Tramvaj</b> · Dopravní podnik\n" + (tram.AtStop ? $"Stojí na zastávce {tram.StopName}. [E] nastoupíš." : "Jede na další zastávku. Nastoupit jde jen na zastávce.");

        var p = hit.point; MapBuilder.Poly best = null; float bd = 25f * 25f; int bi = -1;
        var ps = MapBuilder.D.places;
        for (int k = 0; k < ps.Length; k++)
        {
            float dx = ps[k].p[0] - p.x, dz = ps[k].p[1] - p.z;
            if (dx * dx + dz * dz < bd) { bd = dx * dx + dz * dz; best = ps[k]; bi = k; }
        }
        if (best != null)
        {
            bool open = Places.IsOpen(best.k, h);
            string name = string.IsNullOrEmpty(best.n) ? Places.KindName(best.k) : best.n;
            lastPlace = bi; bool mine = Property.I && Property.I.shops.Contains(bi);
            return $"<b>{name}</b> · {Places.KindName(best.k)}\nMajitel: {(mine ? "ty" : Names.Person(bi * 7919))} · {(open ? "otevřeno" : "zavřeno")} ({Places.Hours(best.k)})\n" +
                   $"Posílá {Places.Item(best.k)} jiným podnikům: zakázky pro kurýra v mobilu [TAB]." + Property.I?.Offer(null, bi);
        }
        foreach (var en in MapBuilder.D.metro.entrances)
        {
            float dx = en.p[0] - p.x, dz = en.p[1] - p.z;
            if (dx * dx + dz * dz < 100f) return "<b>Vstup do metra</b>\n[E] u vstupu: vybereš stanici. Jízda stojí čas, ne peníze.";
        }
        string district = CityBuilder.District(p);
        if (hit.collider.name.StartsWith("Domy") && Building(p, out var b)) // what the city's own register says (IPR Praha)
        {
            lastBuilding = b;
            return $"<b>{(b.use.Length > 0 ? char.ToUpper(b.use[0]) + b.use.Substring(1) : "Budova")}</b> · {district}\n" +
                   (b.floors > 0 ? $"{b.floors} podlaží · " : "") + Access(b.access) + Property.I?.Offer(b, -1);
        }
        return hit.collider.name.StartsWith("Domy") ? $"<b>Obytný dům</b> · {district}\nBydlí tu lidé, kteří ráno vyrážejí do práce."
             : hit.collider.name.StartsWith("Ulice") ? $"<b>Ulice</b> · {district}\nPo silnici jezdí auta, chodci chodí po krajích."
             : $"<b>{district}</b>\nNic zvláštního, aspoň zatím.";
    }

    static bool Building(Vector3 p, out MapBuilder.IprBuilding best)
    {
        best = default; float bd = 30f * 30f; bool found = false;
        foreach (var b in MapBuilder.Ipr)
        {
            float dx = b.at.x - p.x, dz = b.at.z - p.z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = b; found = true; }
        }
        return found;
    }

    // VEREJ_PRISTUP codes of the IPR building register
    static string Access(int a) => a switch
    {
        1 => "Veřejně přístupná – můžeš dovnitř.",
        2 => "Přístupná v otevírací době.",
        3 => "Neveřejná – bydlí nebo pracují tu lidé.",
        4 => "Jen pro ty, kdo tu mají co dělat.",
        _ => "Kdo sem smí, není jasné.",
    };
}
