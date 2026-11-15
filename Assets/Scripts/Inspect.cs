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

    public static string Describe(Transform cam)
    {
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
            return $"<b>{name}</b> · {Places.KindName(best.k)}\nMajitel: {Names.Person(bi * 7919)} · {(open ? "otevřeno" : "zavřeno")} ({Places.Hours(best.k)})\n" +
                   $"Posílá {Places.Item(best.k)} jiným podnikům: zakázky pro kurýra v mobilu [TAB].";
        }
        foreach (var en in MapBuilder.D.metro.entrances)
        {
            float dx = en.p[0] - p.x, dz = en.p[1] - p.z;
            if (dx * dx + dz * dz < 100f) return "<b>Vstup do metra</b>\n[E] u vstupu: vybereš stanici. Jízda stojí čas, ne peníze.";
        }
        string district = CityBuilder.District(p);
        return hit.collider.name.StartsWith("Domy") ? $"<b>Obytný dům</b> · {district}\nBydlí tu lidé, kteří ráno vyrážejí do práce."
             : hit.collider.name.StartsWith("Ulice") ? $"<b>Ulice</b> · {district}\nPo silnici jezdí auta, chodci chodí po krajích."
             : $"<b>{district}</b>\nNic zvláštního, aspoň zatím.";
    }
}
