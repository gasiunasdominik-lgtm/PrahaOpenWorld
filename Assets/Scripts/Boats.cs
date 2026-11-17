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

    void Add(GameObject vis, Vector3 at, float yaw, float speed, float look, Vector2 home = default)
    {
        var root = new GameObject(vis.name);
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
        vis.transform.SetParent(root.transform, false);
        // hull box from the model, starting just under the waterline so it meets banks and bridge piers, not the river bed
        var bounds = new Bounds(); bool first = true;
        foreach (var f in vis.GetComponentsInChildren<MeshFilter>()) { var mb = f.sharedMesh.bounds; if (first) { bounds = mb; first = false; } else bounds.Encapsulate(mb); }
        var box = root.AddComponent<BoxCollider>();
        float bottom = -0.35f, top = bounds.max.y;
        box.center = new Vector3(bounds.center.x, (bottom + top) / 2f, bounds.center.z);
        box.size = new Vector3(bounds.size.x, top - bottom, bounds.size.z);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.mass = vis.name == "Slapadlo" ? 250f : 60000f; rb.linearDamping = 0.6f; rb.angularDamping = 2.5f;
        rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var b = root.AddComponent<Boat>(); b.speed = speed; b.look = look; b.home = home; b.phase = Random.value * 10f;
        b.vis = vis.transform; b.rb = rb; b.maxSpeed = vis.name == "Slapadlo" ? 2.6f : 6f; b.camRadius = vis.name == "Slapadlo" ? 7f : 20f;
        b.label = vis.name == "Slapadlo" ? "ŠLAPADLO" : "LOĎ";
        b.camTarget = new GameObject("CamTarget").transform; b.camTarget.SetParent(root.transform, false);
        b.camTarget.localPosition = new Vector3(0, top + 0.8f, 0);
        boats.Add(b);
    }

    // ---- the player at the helm ----------------------------------------------------------------------------------
    public Boat driving;
    Boat Nearest(Vector3 p, float r)
    {
        Boat best = null; float bd = float.MaxValue;
        foreach (var b in boats)
        {
            var c = b.GetComponent<BoxCollider>().ClosestPoint(p); float d = (c - p).sqrMagnitude;
            if (d < r * r && d < bd) { bd = d; best = b; }
        }
        return best;
    }

    // dry, walkable ground near the boat to step off onto
    static Vector3? Shore(Vector3 p)
    {
        for (float r = 3f; r <= 22f; r += 1.5f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f; float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                float h = MapBuilder.Height(x, z);
                if (h > MapBuilder.WaterY + 0.3f && h < MapBuilder.WaterY + 6f) return new Vector3(x, h + 0.1f, z);
            }
        return null;
    }

    public bool Interact(UnityEngine.InputSystem.Keyboard kb, Hud hud, GameManager gm)
    {
        bool e = kb != null && kb.eKey.wasPressedThisFrame;
        var player = gm.player;
        if (driving)
        {
            player.transform.position = driving.transform.position + Vector3.up * 1.5f;
            var shore = Shore(driving.transform.position);
            hud.promptText.text = (shore != null ? "[E] VYSTOUPIT NA BŘEH    " : "DOPLUJ KE BŘEHU    ") + "W/S PLYN  ·  A/D KORMIDLO";
            hud.SetSpeed(true, driving.rb.linearVelocity.magnitude * 3.6f);
            if (e && shore is Vector3 at) Leave(gm, at);
            return true;
        }
        if (gm.currentCar) return false;
        var near = Nearest(player.transform.position, 6f);
        if (!near) return false;
        hud.promptText.text = "[E] ŘÍDIT " + near.label;
        if (e) Board(gm, near);
        return true;
    }

    public void Board(GameManager gm, Boat b)
    {
        driving = b; b.SetDriven(true);
        gm.player.controlEnabled = false; gm.player.gameObject.SetActive(false);
        gm.rig.FollowTarget(b.camTarget, b.camRadius, b.transform.eulerAngles.y);
        GameAudio.I?.PlayAt(GameAudio.I.door, b.transform.position, 0.6f);
    }

    public void Leave(GameManager gm, Vector3 at)
    {
        driving.SetDriven(false); driving = null;
        gm.player.gameObject.SetActive(true); gm.player.Teleport(at, gm.player.transform.eulerAngles.y); gm.player.controlEnabled = true;
        gm.rig.FollowPlayer(gm.player.camTarget);
    }

    public Boat NearestTo(Vector3 p, float r) => Nearest(p, r);

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
        Vehicles.Shell(mb, white, new Vector3(0, 1.3f + 0.35f, z), new Vector3(cw, 0.7f, cl));
        Vehicles.GlassBand(mb, new Vector3(0, 1.3f + 0.7f + 0.65f, z), new Vector3(cw - 0.1f, 1.3f, cl - 0.1f));
        // inside: tables with white cloths between benches, as on the Prague river restaurant boats
        var cloth = Gfx.Mat(new Color(0.95f, 0.95f, 0.93f), 0.2f); var bench = Gfx.Mat(new Color(0.42f, 0.12f, 0.1f), 0.2f);
        mb.Box(deck, new Vector3(0, 1.33f, z), new Vector3(cw - 0.15f, 0.06f, cl - 0.15f));
        mb.Box(Gfx.Mat(new Color(0.9f, 0.9f, 0.88f), 0.3f), new Vector3(0, 3.28f, z), new Vector3(cw - 0.15f, 0.04f, cl - 0.15f), Quaternion.identity, 0f, false, true);
        for (float tz = -cl / 2 + 1.2f; tz < cl / 2 - 1f; tz += 1.9f)
            foreach (float sx in new[] { -1f, 1f })
            {
                var tc = new Vector3(sx * (cw / 2 - 0.9f), 1.36f, z + tz);
                mb.Box(cloth, tc + new Vector3(0, 0.74f, 0), new Vector3(1.1f, 0.04f, 0.8f));
                mb.Box(white, tc + new Vector3(0, 0.36f, 0), new Vector3(0.08f, 0.72f, 0.08f));
                foreach (float bz in new[] { -0.7f, 0.7f }) mb.Box(bench, tc + new Vector3(0, 0.25f, bz), new Vector3(1.1f, 0.5f, 0.42f));
            }
        mb.Box(white, new Vector3(0, 1.3f + 2.0f + 0.12f, z), new Vector3(cw + 0.3f, 0.24f, cl + 0.4f));
        // open sun deck: railing posts and a rail, a small wheelhouse at the front
        float ry = 1.3f + 2.24f;
        foreach (float sx in new[] { -1f, 1f })
        {
            mb.Box(white, new Vector3(sx * cw / 2, ry + 0.9f, z), new Vector3(0.06f, 0.06f, cl));
            for (float pz = -cl / 2; pz <= cl / 2; pz += 1.6f) mb.Box(white, new Vector3(sx * cw / 2, ry + 0.45f, z + pz), new Vector3(0.05f, 0.9f, 0.05f));
        }
        // the wheelhouse: a glazed box on the sun deck, the skipper at the wheel
        var wc = new Vector3(0, ry, z + cl / 2 - 1.2f);
        Vehicles.Shell(mb, white, wc + new Vector3(0, 0.4f, 0), new Vector3(2.2f, 0.8f, 1.8f));
        Vehicles.GlassBand(mb, wc + new Vector3(0, 1.1f, 0), new Vector3(2.2f, 0.6f, 1.8f));
        mb.Box(white, wc + new Vector3(0, 1.45f, 0), new Vector3(2.4f, 0.1f, 2.0f), Quaternion.identity, 0f, true, true);
        mb.Box(deck, wc + new Vector3(0, 0.02f, 0), new Vector3(2.1f, 0.04f, 1.7f));
        mb.Box(Gfx.Mat(new Color(0.15f, 0.15f, 0.16f), 0.4f), wc + new Vector3(0, 0.5f, 0.6f), new Vector3(1.4f, 1.0f, 0.4f));
        var go = mb.Build("Lod", null, false);
        Vehicles.Driver(go.transform, wc + new Vector3(0, 0.55f, -0.1f), rng.Next());
        return go;
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
    public float speed, look, phase, maxSpeed = 6f, camRadius = 20f; public Vector2 home;
    public Transform vis, camTarget; public Rigidbody rb; public string label;
    public bool Driven { get; private set; }
    float heading, turn, throttle;
    public float debugThrottle, debugRudder; // automated tests drive without a keyboard

    void Start() { heading = transform.eulerAngles.y; }

    static bool Wet(Vector3 p) => MapBuilder.Height(p.x, p.z) < -1f;

    public void SetDriven(bool on)
    {
        Driven = on; rb.isKinematic = !on;
        gameObject.tag = on ? "Player" : "Untagged"; // the camera's deoccluder ignores what the player drives
        if (!on) { heading = transform.eulerAngles.y; speed = 0f; } // left where the player moored it
    }

    void Update()
    {
        float t = Time.time + phase;
        // bobbing and rolling on the ripples: only the model moves, the hull body stays level on the water
        float roll = Mathf.Sin(t * 0.63f) * 1.2f + (Driven ? -turn * 0.08f : 0f);
        vis.localPosition = Vector3.up * (Mathf.Sin(t * 1.1f) * 0.05f);
        vis.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.8f) * 0.8f - throttle * 1.5f, 0, roll);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (Driven)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            float th = (kb == null ? 0 : (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 0.5f : 0)) + debugThrottle;
            float rd = (kb == null ? 0 : (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0)) + debugRudder;
            throttle = Mathf.MoveTowards(throttle, th, dt * 0.8f);
            var v = rb.linearVelocity; var fwd = transform.forward;
            float along = Vector3.Dot(v, fwd);
            // thrust up to hull speed, water resistance sideways (a hull doesn't slide), rudder works with flow
            if (along < maxSpeed) rb.AddForce(fwd * throttle * maxSpeed * 0.45f, ForceMode.Acceleration);
            var side = v - fwd * along; rb.AddForce(-side * 1.8f, ForceMode.Acceleration);
            turn = Mathf.Lerp(turn, rd * 22f * Mathf.Clamp01(0.25f + Mathf.Abs(along) / maxSpeed), dt * 2f);
            rb.MoveRotation(Quaternion.Euler(0, rb.rotation.eulerAngles.y + turn * dt * Mathf.Sign(along + 0.2f), 0));
            var p = rb.position; p.y = MapBuilder.WaterY; rb.position = p;
            return;
        }
        throttle = 0f;
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
            else turn = Mathf.MoveTowards(turn, 0f, 10f * dt);
            // pedal boats stay near their rental
            if (home != default && Vector2.Distance(new Vector2(transform.position.x, transform.position.z), home) > 110f)
            {
                var to = new Vector3(home.x - transform.position.x, 0, home.y - transform.position.z);
                turn = Mathf.DeltaAngle(heading, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg) > 0 ? 20f : -20f;
            }
            heading += turn * dt;
            var np = rb.position + Quaternion.Euler(0, heading, 0) * Vector3.forward * speed * dt; np.y = MapBuilder.WaterY;
            rb.MovePosition(np); rb.MoveRotation(Quaternion.Euler(0, heading, 0));
        }
    }
}
