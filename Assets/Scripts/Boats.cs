using System.Collections.Generic;
using UnityEngine;

// Life on the Vltava: excursion boats cruising up and down (Prague has ~30 of them on the central stretch), pedal boats
// around Slovanský ostrov (the rental by Žofín) and boats moored along the embankments. Built from code like the rest
// of the city. Boats steer by the river itself: water is where the carved bed is (MapBuilder.Height < -1); when the
// bank comes up ahead they turn towards the deeper side. No colliders: they pass under the bridge arches.
public class Boats : MonoBehaviour
{
    public static Boats I;
    readonly List<Boat> boats = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { I = null; }

    static bool Wet(float x, float z) => MapBuilder.Height(x, z) < -1f;
    static bool WideWater(float x, float z, float r)
    {
        for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4f; if (!Wet(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r)) return false; }
        return Wet(x, z);
    }

    void Start()
    {
        I = this;
        var rng = new System.Random(5);
        var t = MapBuilder.D.terrain;
        float X0 = t.x0, Z0 = t.z0, X1 = t.x0 + (t.w - 1) * t.cell, Z1 = t.z0 + (t.h - 1) * t.cell;
        Vector3? Find(float r, Vector2 near, float within)
        {
            for (int i = 0; i < 4000; i++)
            {
                float x = within > 0 ? near.x + ((float)rng.NextDouble() * 2 - 1) * within : Mathf.Lerp(X0, X1, (float)rng.NextDouble());
                float z = within > 0 ? near.y + ((float)rng.NextDouble() * 2 - 1) * within : Mathf.Lerp(Z0, Z1, (float)rng.NextDouble());
                if (WideWater(x, z, r)) return new Vector3(x, MapBuilder.WaterY, z);
            }
            return null;
        }
        for (int i = 0; i < 7; i++) // cruising excursion boats
            if (Find(28f, default, 0) is Vector3 p) Add(Excursion(rng), p, (float)rng.NextDouble() * 360f, 2.6f + (float)rng.NextDouble(), 26f);
        for (int i = 0; i < 9; i++) // pedal boats by Slovanský ostrov
            if (Find(6f, new Vector2(-40, -650), 90f) is Vector3 p) Add(Pedalo(rng), p, (float)rng.NextDouble() * 360f, 0.9f, 5f, new Vector2(-40, -650));
        // moored along the banks: water right next to land, bow along the bank
        int moored = 0;
        for (int i = 0; i < 6000 && moored < 8; i++)
        {
            float x = Mathf.Lerp(X0, X1, (float)rng.NextDouble()), z = Mathf.Lerp(Z0, Z1, (float)rng.NextDouble());
            if (!WideWater(x, z, 5f)) continue;
            // land within 9 m on exactly one side
            Vector2 land = Vector2.zero; int sides = 0;
            for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); if (!Wet(x + d.x * 9f, z + d.y * 9f)) { land += d; sides++; } }
            if (sides == 0 || sides > 3) continue;
            float yaw = Mathf.Atan2(land.x, land.y) * Mathf.Rad2Deg + 90f; // parallel to the bank
            Add(Excursion(rng), new Vector3(x, MapBuilder.WaterY, z), yaw, 0f, 0f);
            moored++;
        }
        Debug.Log($"[Lodě] {boats.Count} (z toho u břehu {moored})");
    }

    void Add(GameObject go, Vector3 at, float yaw, float speed, float look, Vector2 home = default)
    {
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
        var b = go.AddComponent<Boat>(); b.speed = speed; b.look = look; b.home = home; b.phase = Random.value * 10f;
        boats.Add(b);
    }

    // ---- models -------------------------------------------------------------------------------------------------

    // A hull with a pointed bow: deck outline at the waterline + freeboard, sides tapering in towards the keel.
    static void Hull(MeshBuilder mb, float L, float W, float free, float draft, Material top, Material low, Material deck)
    {
        float hl = L / 2, hw = W / 2;
        Vector3[] Outline(float y, float s) => new[] {
            new Vector3(0, y, hl * s), new Vector3(hw * 0.75f * s, y, hl * 0.72f * s), new Vector3(hw * s, y, hl * 0.35f), new Vector3(hw * s, y, -hl * 0.85f),
            new Vector3(hw * 0.85f * s, y, -hl), new Vector3(-hw * 0.85f * s, y, -hl), new Vector3(-hw * s, y, -hl * 0.85f), new Vector3(-hw * s, y, hl * 0.35f),
            new Vector3(-hw * 0.75f * s, y, hl * 0.72f * s) };
        var a = Outline(free, 1f); var m = Outline(0.15f, 0.97f); var b = Outline(-draft, 0.8f);
        for (int i = 0; i < a.Length; i++)
        {
            int j = (i + 1) % a.Length; var o = (a[i] + a[j]) * 0.5f; o.y = 0;
            mb.Poly(top, new[] { m[i], m[j], a[j], a[i] }, null, o);
            mb.Poly(low, new[] { b[i], b[j], m[j], m[i] }, null, o);
        }
        mb.Poly(deck, a, null, Vector3.up);
    }

    static GameObject Excursion(System.Random rng)
    {
        var mb = new MeshBuilder();
        var white = Gfx.Mat(new Color(0.93f, 0.93f, 0.91f), 0.55f);
        var accent = Gfx.Mat(rng.NextDouble() < 0.5 ? new Color(0.12f, 0.2f, 0.38f) : new Color(0.45f, 0.1f, 0.1f), 0.45f);
        var deck = Gfx.Mat(new Color(0.45f, 0.33f, 0.22f), 0.2f);
        var glass = Gfx.Mat(new Color(0.12f, 0.17f, 0.22f), 0.92f);
        float L = 26f + (float)rng.NextDouble() * 12f, W = 5.6f + (float)rng.NextDouble() * 1.2f;
        Hull(mb, L, W, 1.3f, 1.1f, white, accent, deck);
        // saloon: glass band between a white skirt and a white roof
        float cl = L * 0.62f, cw = W * 0.86f, z = -L * 0.06f;
        mb.Box(white, new Vector3(0, 1.3f + 0.35f, z), new Vector3(cw, 0.7f, cl));
        mb.Box(glass, new Vector3(0, 1.3f + 0.7f + 0.65f, z), new Vector3(cw - 0.1f, 1.3f, cl - 0.1f));
        mb.Box(white, new Vector3(0, 1.3f + 2.0f + 0.12f, z), new Vector3(cw + 0.3f, 0.24f, cl + 0.4f));
        // open sun deck: railing posts and a rail, a small wheelhouse at the front
        float ry = 1.3f + 2.24f;
        foreach (float sx in new[] { -1f, 1f })
        {
            mb.Box(white, new Vector3(sx * cw / 2, ry + 0.9f, z), new Vector3(0.06f, 0.06f, cl));
            for (float pz = -cl / 2; pz <= cl / 2; pz += 1.6f) mb.Box(white, new Vector3(sx * cw / 2, ry + 0.45f, z + pz), new Vector3(0.05f, 0.9f, 0.05f));
        }
        mb.Box(white, new Vector3(0, ry + 0.75f, z + cl / 2 - 1.2f), new Vector3(2.2f, 1.5f, 1.8f));
        mb.Box(glass, new Vector3(0, ry + 1.05f, z + cl / 2 - 0.29f), new Vector3(2.0f, 0.7f, 0.05f));
        return mb.Build("Lod", null, false);
    }

    static GameObject Pedalo(System.Random rng)
    {
        var mb = new MeshBuilder();
        var cols = new[] { new Color(0.9f, 0.85f, 0.2f), new Color(0.2f, 0.55f, 0.85f), new Color(0.9f, 0.35f, 0.25f), new Color(0.95f, 0.95f, 0.95f) };
        var body = Gfx.Mat(cols[rng.Next(cols.Length)], 0.5f); var seat = Gfx.Mat(new Color(0.85f, 0.85f, 0.82f), 0.4f);
        Hull(mb, 3.2f, 1.8f, 0.35f, 0.25f, body, body, seat);
        mb.Box(seat, new Vector3(0, 0.6f, -0.3f), new Vector3(1.4f, 0.5f, 0.15f)); // backrest
        return mb.Build("Slapadlo", null, false);
    }
}

public class Boat : MonoBehaviour
{
    public float speed, look, phase; public Vector2 home;
    float heading, turn;

    void Start() { heading = transform.eulerAngles.y; }

    static bool Wet(Vector3 p) => MapBuilder.Height(p.x, p.z) < -1f;

    void Update()
    {
        float t = Time.time + phase;
        if (speed > 0f)
        {
            var fwd = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            // the bank ahead: turn to whichever side has water further out (or back, in a dead end)
            if (!Wet(transform.position + fwd * look) || !Wet(transform.position + fwd * look * 0.5f))
            {
                float best = 180f, bestScore = -1;
                foreach (float a in new[] { 25f, -25f, 55f, -55f, 90f, -90f, 135f, -135f, 180f })
                {
                    var d = Quaternion.Euler(0, heading + a, 0) * Vector3.forward; float score = 0;
                    for (float r = 6f; r <= look * 2f; r += 6f) { if (!Wet(transform.position + d * r)) break; score = r; }
                    if (score > bestScore + 0.1f) { bestScore = score; best = a; }
                }
                turn = Mathf.Sign(best) * Mathf.Min(Mathf.Abs(best), 40f);
            }
            else turn = Mathf.MoveTowards(turn, 0f, 10f * Time.deltaTime);
            // pedal boats stay near their rental
            if (home != default && Vector2.Distance(new Vector2(transform.position.x, transform.position.z), home) > 110f)
            {
                var to = new Vector3(home.x - transform.position.x, 0, home.y - transform.position.z);
                turn = Mathf.DeltaAngle(heading, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg) > 0 ? 20f : -20f;
            }
            heading += turn * Time.deltaTime;
            transform.position += Quaternion.Euler(0, heading, 0) * Vector3.forward * speed * Time.deltaTime;
        }
        // bobbing on the ripples
        var p = transform.position; p.y = MapBuilder.WaterY + Mathf.Sin(t * 1.1f) * 0.05f; transform.position = p;
        transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.8f) * 0.8f, heading, Mathf.Sin(t * 0.63f) * 1.2f);
    }
}
