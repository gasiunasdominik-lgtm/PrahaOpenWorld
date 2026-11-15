using System.Collections.Generic;
using UnityEngine;

// Realistic pedestrians: skinned MakeHuman (CC0) characters built in Blender by Tools/make_people.py
// (Resources/People/*.fbx, two LODs each, a 21-bone "mixamorig" skeleton, textures in People/Tex).
// Materials are made here (URP Lit, alpha-clipped hair), clothes get a subtle per-person tint so ten models
// read as a crowd of different people.
public static class People
{
    static GameObject[] models;
    static readonly Dictionary<string, List<GameObject>> byKind = new();
    [System.Serializable] class Person { public string name; public float age, height; public bool female; }
    [System.Serializable] class Manifest { public Person[] people; }
    public static string KindOf(float age) => age < 0.015f ? "baby" : age < 0.07f ? "toddler" : age < 0.2f ? "child" : age < 0.4f ? "teen" : age < 0.8f ? "adult" : "senior";
    static readonly Dictionary<string, Material> mats = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { models = null; mats.Clear(); byKind.Clear(); }

    public static bool Available { get { Load(); return models.Length > 0; } }

    static void Load()
    {
        if (models != null) return;
        models = Resources.LoadAll<GameObject>("People");
        var ages = new Dictionary<string, float>();
        var mf = Resources.Load<TextAsset>("People/manifest");
        if (mf) foreach (var p in JsonUtility.FromJson<Manifest>(mf.text).people) ages[p.name] = p.age;
        foreach (var m in models)
        {
            string k = KindOf(ages.TryGetValue(m.name, out var a) ? a : 0.5f);
            if (!byKind.TryGetValue(k, out var l)) byKind[k] = l = new List<GameObject>();
            l.Add(m);
        }
    }

    static Material Mat(string name)
    {
        name = name.Replace(" (Instance)", "");
        int dot = name.IndexOf('.'); if (dot > 0) name = name.Substring(0, dot);
        if (mats.TryGetValue(name, out var m)) return m;
        var tex = Resources.Load<Texture2D>("People/Tex/" + name);
        bool hair = name.StartsWith("short") || name.StartsWith("long") || name.StartsWith("bob") || name.StartsWith("ponytail") || name.StartsWith("braid") || name.StartsWith("afro") || name.StartsWith("eyebrow");
        bool skin = name.StartsWith("Skin_"), eye = name.StartsWith("Eye_");
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "P_" + name };
        if (tex) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", hair ? 0.35f : skin ? 0.3f : eye ? 0.8f : 0.12f);
        m.SetFloat("_Metallic", 0f);
        if (hair)
        {
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f); m.doubleSidedGI = true;
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        mats[name] = m;
        return m;
    }

    // A pram (Czech "kočárek": deep tub, folding hood, big wheels) with a sleeping baby, pushed ~0.9 m ahead of the walker.
    static Material pramBody, pramDark, pramChrome;
    public static GameObject Pram(Transform parent, System.Random rng)
    {
        var cols = new[] { new Color(0.12f, 0.14f, 0.2f), new Color(0.25f, 0.27f, 0.28f), new Color(0.35f, 0.22f, 0.25f), new Color(0.55f, 0.55f, 0.52f), new Color(0.16f, 0.24f, 0.2f) };
        pramDark ??= Gfx.Mat(new Color(0.05f, 0.05f, 0.05f), 0.3f); pramChrome ??= Gfx.Mat(new Color(0.75f, 0.75f, 0.75f), 0.8f);
        var body = Gfx.Mat(cols[rng.Next(cols.Length)], 0.25f);
        var mb = new MeshBuilder();
        float z = 1.0f;
        // tub: a rounded boat shape (octagonal section), hood folded back over the head end
        void Ring(Material m, float cz, float len, float cy, float rx, float ry, float a0, float a1, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Lerp(a0, a1, (i + 1f) / seg);
                Vector3 P(float t, float dz) => new Vector3(Mathf.Cos(t) * rx, cy + Mathf.Sin(t) * ry, cz + dz);
                var o = new Vector3(Mathf.Cos((t0 + t1) / 2), Mathf.Sin((t0 + t1) / 2), 0);
                mb.Poly(m, new[] { P(t0, -len / 2), P(t1, -len / 2), P(t1, len / 2), P(t0, len / 2) }, null, o);
                mb.Poly(m, new[] { P(t0, -len / 2), P(t1, -len / 2), new Vector3(0, cy, cz - len / 2) }, null, Vector3.back);
                mb.Poly(m, new[] { P(t0, len / 2), P(t1, len / 2), new Vector3(0, cy, cz + len / 2) }, null, Vector3.forward);
            }
        }
        Ring(body, z, 0.74f, 0.72f, 0.22f, 0.2f, Mathf.PI, Mathf.PI * 2f, 8);           // tub (lower half)
        Ring(body, z - 0.2f, 0.34f, 0.72f, 0.23f, 0.3f, 0f, Mathf.PI, 8);                // hood (upper half, over the head at the parent's end)
        mb.Box(pramDark, new Vector3(0, 0.45f, z), new Vector3(0.3f, 0.03f, 0.62f));      // chassis
        foreach (float sz in new[] { -0.24f, 0.24f }) mb.Box(pramChrome, new Vector3(0, 0.5f, z + sz), new Vector3(0.025f, 0.12f, 0.025f));
        // wheels: round, with a dark tyre and a chrome hub
        void Wheel(Vector3 c, float r)
        {
            const int Seg = 14; float hw = 0.022f;
            for (int i = 0; i < Seg; i++)
            {
                float t0 = i * Mathf.PI * 2f / Seg, t1 = (i + 1) * Mathf.PI * 2f / Seg;
                Vector3 P(float t, float x, float rr) => c + new Vector3(x, Mathf.Sin(t) * rr, Mathf.Cos(t) * rr);
                mb.Poly(pramDark, new[] { P(t0, -hw, r), P(t1, -hw, r), P(t1, hw, r), P(t0, hw, r) }, null, new Vector3(0, Mathf.Sin((t0 + t1) / 2), Mathf.Cos((t0 + t1) / 2)));
                foreach (float sx in new[] { -hw, hw })
                {
                    mb.Poly(pramDark, new[] { P(t0, sx, r), P(t1, sx, r), P(t1, sx, r * 0.8f), P(t0, sx, r * 0.8f) }, null, new Vector3(sx, 0, 0));
                    mb.Poly(pramChrome, new[] { P(t0, sx, r * 0.8f), P(t1, sx, r * 0.8f), c + new Vector3(sx, 0, 0) }, null, new Vector3(sx, 0, 0));
                }
            }
        }
        foreach (float sx in new[] { -0.25f, 0.25f }) { Wheel(new Vector3(sx, 0.16f, z - 0.27f), 0.16f); Wheel(new Vector3(sx, 0.14f, z + 0.27f), 0.14f); }
        foreach (float sx in new[] { -0.25f, 0.25f }) mb.Box(pramDark, new Vector3(sx * 0.6f, 0.3f, z), new Vector3(0.02f, 0.02f, 0.62f), Quaternion.Euler(0, 0, 0));
        // handle: two struts up and back from the tub to a bar at the parent's hands
        mb.Box(pramChrome, new Vector3(0, 1.0f, z - 0.62f), new Vector3(0.48f, 0.03f, 0.03f));
        foreach (float sx in new[] { -0.22f, 0.22f })
            mb.Box(pramChrome, new Vector3(sx, 0.83f, z - 0.5f), new Vector3(0.022f, 0.4f, 0.022f), Quaternion.Euler(-35f, 0, 0));
        var go = mb.Build("Kocarek", parent, false);
        // the baby lies in the tub under the hood: head end at the hood, a blanket over the body
        Load();
        if (byKind.TryGetValue("baby", out var babies) && babies.Count > 0)
        {
            var hm = Spawn(go.transform, rng, "baby"); hm.enabled = false;
            // on its back, head under the hood towards the parent: up -> back, face -> up
            var bt = hm.transform; bt.localRotation = Quaternion.Euler(-90f, 0, 0);
            bt.localPosition = new Vector3(0, 0.66f, z + 0.28f);
            foreach (var r in bt.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mb = new MeshBuilder(); mb.Box(Gfx.Mat(new Color(0.9f, 0.88f, 0.8f), 0.1f), new Vector3(0, 0.76f, z + 0.1f), new Vector3(0.38f, 0.05f, 0.5f));
            mb.Build("Deka", go.transform, false);
        }
        return go;
    }

    // A random person under parent, feet at the parent's origin, facing +Z.
    public static HumanMotion Spawn(Transform parent, System.Random rng, string kind = "adult")
    {
        Load();
        var pool = byKind.TryGetValue(kind, out var kl) && kl.Count > 0 ? kl : byKind.TryGetValue("adult", out var a) ? a : new List<GameObject>(models);
        var src = pool[rng.Next(pool.Count)];
        var holder = new GameObject("Clovek").transform; holder.SetParent(parent, false);
        var go = Object.Instantiate(src, holder, false); go.name = src.name;
        // face +Z: the toes point forward
        Transform foot = null, toe = null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.EndsWith("LeftFoot")) foot = t; else if (t.name.EndsWith("LeftToeBase")) toe = t;
        }
        if (foot && toe)
        {
            var f = holder.InverseTransformDirection(toe.position - foot.position); f.y = 0;
            if (f.sqrMagnitude > 1e-6f) go.transform.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * go.transform.localRotation;
        }
        var block = new MaterialPropertyBlock();
        // clothes tint: mostly neutral, sometimes a shifted hue (darker/lighter jacket, other jeans)
        float h = (float)rng.NextDouble(), k = 0.75f + 0.25f * (float)rng.NextDouble();
        var tint = Color.Lerp(Color.white, Color.HSVToRGB(h, 0.35f, 1f), rng.NextDouble() < 0.6 ? 0.55f : 0f) * k; tint.a = 1;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++) shared[i] = Mat(shared[i] ? shared[i].name : "x");
            r.sharedMaterials = shared;
            for (int i = 0; i < shared.Length; i++)
            {
                string n = shared[i].name;
                if (n.Contains("suit")) { block.Clear(); block.SetColor("_BaseColor", tint); r.SetPropertyBlock(block, i); }
            }
            r.updateWhenOffscreen = false;
            bool far = r.name.EndsWith("_LOD1");
            r.shadowCastingMode = far ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            r.skinnedMotionVectors = false;
        }
        var lod = go.GetComponent<LODGroup>();
        if (lod)
        {
            var l = lod.GetLODs();
            if (l.Length >= 2) { l[0].screenRelativeTransitionHeight = 0.22f; l[1].screenRelativeTransitionHeight = 0.012f; lod.SetLODs(l); }
        }
        var hm = holder.gameObject.AddComponent<HumanMotion>();
        hm.kind = kind;
        // shorter legs, quicker steps (cadence ~ 1/sqrt(leg length)); the old walk slower and a little stooped
        if (kind == "child") hm.stepFreq = 2.4f; else if (kind == "toddler") hm.stepFreq = 3.0f; else if (kind == "teen") hm.stepFreq = 2.0f;
        if (kind == "senior") { hm.stepFreq = 1.65f; hm.stoop = 7f; }
        hm.Bind();
        return hm;
    }
}

// Procedural walk on the mixamo skeleton: every rotation is written in the character's own space
// (right/up/forward) and converted into each bone's parent frame, so the bone roll from Blender doesn't matter.
public class HumanMotion : MonoBehaviour
{
    public float maxSpeed = 3f, stepFreq = 1.9f, stoop;
    public bool pushing; // hands on a pram's handle
    public string kind = "adult";
    public bool Grounded = true;
    class B { public Transform t; public Quaternion bind, P, Pinv; public Vector3 dir, pos; }
    readonly Dictionary<string, B> bones = new();
    B hips, spine, spine2, neck, head, upL, upR, legL, legR, footL, footR, armL, armR, foreL, foreR, handL, handR;
    Quaternion armRestL, armRestR;
    float phase, amp, lean;
    Vector3 lastVel;

    B Get(string n)
    {
        if (bones.TryGetValue(n, out var b)) return b;
        return null;
    }

    public void Bind()
    {
        var root = transform;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name; int c = n.IndexOf(':'); if (c >= 0) n = n.Substring(c + 1);
            if (bones.ContainsKey(n)) continue;
            var b = new B { t = t, bind = t.localRotation, pos = t.localPosition };
            b.P = Quaternion.Inverse(root.rotation) * (t.parent ? t.parent.rotation : root.rotation);
            b.Pinv = Quaternion.Inverse(b.P);
            bones[n] = b;
        }
        hips = Get("Hips"); spine = Get("Spine1"); spine2 = Get("Spine2"); neck = Get("Neck"); head = Get("Head");
        upL = Get("LeftUpLeg"); upR = Get("RightUpLeg"); legL = Get("LeftLeg"); legR = Get("RightLeg");
        footL = Get("LeftFoot"); footR = Get("RightFoot"); armL = Get("LeftArm"); armR = Get("RightArm");
        foreL = Get("LeftForeArm"); foreR = Get("RightForeArm"); handL = Get("LeftHand"); handR = Get("RightHand");
        if (hips == null || armL == null || foreL == null) { enabled = false; return; }
        // the models are exported in an A-pose: rest the arms along the body (8 degrees out)
        Vector3 Dir(B a, B b) => transform.InverseTransformDirection(b.t.position - a.t.position).normalized;
        Quaternion Rest(Vector3 d) { float sx = Mathf.Sign(d.x), r = 9f * Mathf.Deg2Rad; return Quaternion.FromToRotation(d, new Vector3(sx * Mathf.Sin(r), -Mathf.Cos(r), 0.05f).normalized); }
        armRestL = Rest(Dir(armL, foreL)); armRestR = Rest(Dir(armR, foreR));
        phase = Random.value * 10f;
        Tick(Vector3.zero, 0.02f);
    }

    // elbow: aim the forearm along the upper arm, folded forward by deg
    void Bend(B fore, B arm, B hand, float deg)
    {
        if (fore == null || hand == null) return;
        fore.t.localRotation = fore.bind;
        var up = fore.t.position - arm.t.position;
        var want = Quaternion.AngleAxis(-deg, transform.right) * up;
        fore.t.rotation = Quaternion.FromToRotation(hand.t.position - fore.t.position, want) * fore.t.rotation;
    }

    static void Set(B b, Quaternion q) { if (b != null) b.t.localRotation = b.Pinv * q * b.P * b.bind; }

    public void Tick(Vector3 velocity, float dt)
    {
        if (!enabled) return;
        var flat = new Vector3(velocity.x, 0, velocity.z);
        float sn = Mathf.Clamp01(flat.magnitude / 1.6f);
        amp = Mathf.MoveTowards(amp, sn, dt / 0.3f);
        float freq = Mathf.Lerp(0.9f, stepFreq, sn) * Mathf.Clamp(flat.magnitude / 1.35f, 0.75f, 1.35f);
        phase += dt * freq * Mathf.PI * (amp > 0.03f ? 1 : 0); // one full sin period = two steps
        float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
        var accel = (flat - lastVel) / Mathf.Max(dt, 1e-4f); lastVel = flat;
        lean = Mathf.Lerp(lean, Mathf.Clamp(Vector3.Dot(accel, transform.forward) * 0.6f, -5f, 5f) + amp * 3f, dt * 5f);

        Vector3 R = Vector3.right, U = Vector3.up, F = Vector3.forward;
        float hip = 24f * amp, knee = 52f * amp;
        // thighs swing opposite; the knee folds while its leg travels forward (swing phase)
        Set(upL, Quaternion.AngleAxis(-s * hip - 3f * amp, R));
        Set(upR, Quaternion.AngleAxis(s * hip - 3f * amp, R));
        Set(legL, Quaternion.AngleAxis(Mathf.Max(0f, c) * knee + 4f * amp, R));
        Set(legR, Quaternion.AngleAxis(Mathf.Max(0f, -c) * knee + 4f * amp, R));
        Set(footL, Quaternion.AngleAxis(-Mathf.Max(0f, c) * 18f * amp + s * 8f * amp, R));
        Set(footR, Quaternion.AngleAxis(-Mathf.Max(0f, -c) * 18f * amp - s * 8f * amp, R));
        // arms hang, swing against the legs, elbows a little bent
        float arm = 20f * amp + 2f;
        float breathe = Mathf.Sin(Time.time * 1.6f) * 1.2f;
        if (pushing)
        {
            // upper arms forward and down to the handle, elbows bent: the hands rest on the bar at hip height
            Set(armL, Quaternion.AngleAxis(-30f, R) * armRestL); Set(armR, Quaternion.AngleAxis(-30f, R) * armRestR);
            Bend(foreL, armL, handL, 40f); Bend(foreR, armR, handR, 40f);
        }
        else
        {
            Set(armL, Quaternion.AngleAxis(s * arm, R) * armRestL);
            Set(armR, Quaternion.AngleAxis(-s * arm, R) * armRestR);
            Bend(foreL, armL, handL, 14f + Mathf.Max(0f, -s) * 20f * amp);
            Bend(foreR, armR, handR, 14f + Mathf.Max(0f, s) * 20f * amp);
        }
        // pelvis turns with the stride, the chest counter-rotates, a soft bob (lowest at heel strike)
        Set(hips, Quaternion.AngleAxis(s * 5f * amp, U) * Quaternion.AngleAxis(c * 2.5f * amp, F));
        if (hips != null) hips.t.localPosition = hips.pos + hips.Pinv * (Vector3.up * ((Mathf.Abs(c) - 0.6f) * 0.035f * amp));
        Set(spine, Quaternion.AngleAxis(lean + stoop + (pushing ? 6f : 0f), R) * Quaternion.AngleAxis(-s * 7f * amp, U));
        Set(spine2, Quaternion.AngleAxis(breathe, R));
        Set(head, Quaternion.AngleAxis(-lean * 0.6f - stoop * 0.7f, R) * Quaternion.AngleAxis(s * 2f * amp, U));
    }
}
