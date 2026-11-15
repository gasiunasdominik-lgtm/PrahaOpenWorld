using System.Collections.Generic;
using UnityEngine;

// Procedural motion for the primitive-built hero: gait bob + roll, limb swing, lean,
// landing squash, secondary scarf lag, idle breathing. Driven by normalized speed.
public class CharacterMotion : MonoBehaviour
{
    public Transform body, legL, legR, armL, armR, head, scarfTail;
    public float maxSpeed = 8f;
    public float stepFreq = 2.4f;   // Hz at full speed
    public float bobAmp = 0.06f;
    public float swingDeg = 38f;
    public float leanDeg = 8f;
    public float bankDeg = 5f;

    public float Speed { get; set; }
    public bool Grounded { get; set; } = true;
    public System.Action OnFootstep;

    float phase, amp, lean, bank, squash = 1f, lastYaw, scarfAngle, lastStepSign = 1;
    Vector3 bodyBase;
    Vector3 lastVel;

    void Start()
    {
        bodyBase = body.localPosition;
        lastYaw = transform.eulerAngles.y;
    }

    public void Land(float impact)
    {
        squash = Mathf.Lerp(1f, 0.85f, Mathf.Clamp01(impact / 8f));
    }

    public void Tick(Vector3 velocity, float dt)
    {
        var flat = new Vector3(velocity.x, 0, velocity.z);
        float sn = Mathf.Clamp01(flat.magnitude / maxSpeed);
        // amplitude blends in/out over ~0.3 s so a stop settles instead of freezing
        amp = Mathf.MoveTowards(amp, Grounded ? sn : 0.15f, dt / 0.3f);
        float freq = Mathf.Lerp(1.2f, stepFreq, sn);
        phase += dt * freq * Mathf.PI * 2f * (amp > 0.02f ? 1 : 0);

        float s = Mathf.Sin(phase);
        if (Mathf.Sign(s) != lastStepSign && amp > 0.25f && Grounded) OnFootstep?.Invoke();
        lastStepSign = Mathf.Sign(s);

        // acceleration lean and turn bank
        var accel = (flat - lastVel) / Mathf.Max(dt, 1e-4f);
        lastVel = flat;
        float fwdAccel = Vector3.Dot(accel, transform.forward);
        lean = Mathf.Lerp(lean, Mathf.Clamp(fwdAccel * 0.8f, -leanDeg, leanDeg) + sn * 4f, dt * 6f);
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(dt, 1e-4f);
        lastYaw = yaw;
        bank = Mathf.Lerp(bank, Mathf.Clamp(-yawRate * 0.03f, -bankDeg, bankDeg), dt * 6f);

        squash = Mathf.MoveTowards(squash, 1f, dt / 0.12f);
        float breathe = Mathf.Sin(Time.time * Mathf.PI) * 0.012f * (1f - amp);

        body.localPosition = bodyBase + Vector3.up * (Mathf.Abs(s) * bobAmp * amp + breathe);
        body.localRotation = Quaternion.Euler(lean, 0, Mathf.Sin(phase * 0.5f) * 2f * amp + bank);
        body.localScale = new Vector3(1f + (1f - squash) * 0.6f, squash, 1f + (1f - squash) * 0.6f);

        float swing = s * swingDeg * amp;
        if (!Grounded) swing = 25f;
        legL.localRotation = Quaternion.Euler(swing, 0, 0);
        legR.localRotation = Quaternion.Euler(Grounded ? -swing : -10f, 0, 0);
        armL.localRotation = Quaternion.Euler(-swing * 0.8f, 0, 6f);
        armR.localRotation = Quaternion.Euler(swing * 0.8f, 0, -6f);
        head.localRotation = Quaternion.Euler(-lean * 0.4f, Mathf.Clamp(bank * 4f, -25f, 25f), 0);

        // scarf lags behind: swings back with speed, wobbles with the gait
        float target = 10f + sn * 55f + Mathf.Sin(phase * 2f + 1f) * 6f * amp + Mathf.Sin(Time.time * 1.7f) * 3f;
        scarfAngle = Mathf.Lerp(scarfAngle, target, 1f - Mathf.Exp(-dt / 0.08f));
        scarfTail.localRotation = Quaternion.Euler(scarfAngle, 0, Mathf.Sin(Time.time * 2.3f) * 4f * (0.3f + sn));
    }

    // Coarse stand-ins for Unity's sphere (768 tris) and capsule (832): a walker drops from ~7 400 to ~800 triangles.
    static Mesh lowSphere, lowCapsule;
    public static void LowPoly(Transform root)
    {
        if (!lowSphere)
        {
            float c = Mathf.Sqrt(0.5f) * 0.5f;
            lowSphere = Lathe("LowSphere", new[] { new Vector2(0, -0.5f), new Vector2(c, -c), new Vector2(0.5f, 0), new Vector2(c, c), new Vector2(0, 0.5f) });
            lowCapsule = Lathe("LowCapsule", new[] { new Vector2(0, -1f), new Vector2(c, -0.5f - c), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(c, 0.5f + c), new Vector2(0, 1f) });
        }
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh && f.sharedMesh.name.StartsWith("Sphere")) f.sharedMesh = lowSphere;
            else if (f.sharedMesh && f.sharedMesh.name.StartsWith("Capsule")) f.sharedMesh = lowCapsule;
    }

    // Surface of revolution: profile (radius, y) from bottom pole to top pole, 8 sides, smooth normals.
    static Mesh Lathe(string name, Vector2[] prof)
    {
        const int Seg = 8;
        var v = new List<Vector3>(); var t = new List<int>();
        foreach (var p in prof)
            for (int i = 0; i <= Seg; i++) { float a = i * Mathf.PI * 2f / Seg; v.Add(new Vector3(Mathf.Sin(a) * p.x, p.y, Mathf.Cos(a) * p.x)); }
        for (int r = 0; r + 1 < prof.Length; r++)
            for (int i = 0; i < Seg; i++)
            {
                int a = r * (Seg + 1) + i, b = a + Seg + 1;
                if (prof[r].x > 0f) { t.Add(a); t.Add(a + 1); t.Add(b); }          // skip the zero-area triangle at a pole
                if (prof[r + 1].x > 0f) { t.Add(a + 1); t.Add(b + 1); t.Add(b); }
            }
        var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    public static CharacterMotion BuildHero(Transform root)
    {
        Material M(Color c) => Gfx.Mat(c, 0.15f);
        GameObject Part(string name, PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.GetComponent<Renderer>().sharedMaterial = M(c);
            return g;
        }
        Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            return g;
        }

        var model = Pivot("Model", root, Vector3.zero);
        var body = Pivot("Body", model, new Vector3(0, 0.95f, 0));
        // torso (jacket), slight taper via a smaller chest-top
        Part("Torso", PrimitiveType.Capsule, body, new Vector3(0, 0.33f, 0), new Vector3(0.46f, 0.36f, 0.3f), Palette.Jacket);
        Part("JacketHem", PrimitiveType.Cylinder, body, new Vector3(0, 0.08f, 0), new Vector3(0.48f, 0.06f, 0.32f), Color.Lerp(Palette.Jacket, Palette.Shadow, 0.4f));
        Part("Scarf", PrimitiveType.Cylinder, body, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.06f, 0.26f), Palette.Accent);
        var scarfPivot = Pivot("ScarfPivot", body, new Vector3(0.07f, 0.6f, -0.12f));
        Part("ScarfTail", PrimitiveType.Cube, scarfPivot, new Vector3(0, -0.2f, -0.02f), new Vector3(0.11f, 0.4f, 0.04f), Palette.Accent);
        var head = Pivot("HeadPivot", body, new Vector3(0, 0.66f, 0));
        Part("Head", PrimitiveType.Sphere, head, new Vector3(0, 0.15f, 0), new Vector3(0.3f, 0.33f, 0.3f), Palette.Skin);
        Part("Hair", PrimitiveType.Sphere, head, new Vector3(0, 0.23f, -0.025f), new Vector3(0.32f, 0.22f, 0.33f), Palette.Hair);
        Part("Fringe", PrimitiveType.Cube, head, new Vector3(0.03f, 0.27f, 0.12f), new Vector3(0.22f, 0.07f, 0.08f), Palette.Hair, new Vector3(-20, 0, -8));
        Part("Nose", PrimitiveType.Cube, head, new Vector3(0, 0.13f, 0.15f), new Vector3(0.05f, 0.07f, 0.06f), Color.Lerp(Palette.Skin, Palette.Terracotta, 0.2f));
        Part("EyeL", PrimitiveType.Cube, head, new Vector3(-0.065f, 0.18f, 0.14f), new Vector3(0.04f, 0.05f, 0.02f), Palette.UIDark);
        Part("EyeR", PrimitiveType.Cube, head, new Vector3(0.065f, 0.18f, 0.14f), new Vector3(0.04f, 0.05f, 0.02f), Palette.UIDark);
        // arms (pivot at shoulder), tapered: upper wider than hand
        var armL = Pivot("ArmL", body, new Vector3(-0.27f, 0.52f, 0));
        Part("SleeveL", PrimitiveType.Capsule, armL, new Vector3(-0.02f, -0.26f, 0), new Vector3(0.13f, 0.28f, 0.13f), Palette.Jacket);
        Part("HandL", PrimitiveType.Sphere, armL, new Vector3(-0.03f, -0.55f, 0.01f), new Vector3(0.1f, 0.11f, 0.1f), Palette.Skin);
        var armR = Pivot("ArmR", body, new Vector3(0.27f, 0.52f, 0));
        Part("SleeveR", PrimitiveType.Capsule, armR, new Vector3(0.02f, -0.26f, 0), new Vector3(0.13f, 0.28f, 0.13f), Palette.Jacket);
        Part("HandR", PrimitiveType.Sphere, armR, new Vector3(0.03f, -0.55f, 0.01f), new Vector3(0.1f, 0.11f, 0.1f), Palette.Skin);
        // legs (pivot at hip), shoes as the darker 0.8x taper
        var legL = Pivot("LegL", model, new Vector3(-0.11f, 0.9f, 0));
        Part("ThighL", PrimitiveType.Capsule, legL, new Vector3(0, -0.43f, 0), new Vector3(0.17f, 0.43f, 0.17f), Palette.Jeans);
        Part("ShoeL", PrimitiveType.Cube, legL, new Vector3(0, -0.86f, 0.05f), new Vector3(0.15f, 0.09f, 0.27f), Palette.UIDark);
        var legR = Pivot("LegR", model, new Vector3(0.11f, 0.9f, 0));
        Part("ThighR", PrimitiveType.Capsule, legR, new Vector3(0, -0.43f, 0), new Vector3(0.17f, 0.43f, 0.17f), Palette.Jeans);
        Part("ShoeR", PrimitiveType.Cube, legR, new Vector3(0, -0.86f, 0.05f), new Vector3(0.15f, 0.09f, 0.27f), Palette.UIDark);

        var cm = root.gameObject.AddComponent<CharacterMotion>();
        cm.body = body; cm.legL = legL; cm.legR = legR; cm.armL = armL; cm.armR = armR; cm.head = head; cm.scarfTail = scarfPivot;
        return cm;
    }
}
