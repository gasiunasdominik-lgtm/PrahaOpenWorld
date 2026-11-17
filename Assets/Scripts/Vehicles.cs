using System.Collections.Generic;
using UnityEngine;

// Car models built in Blender (Tools/make_vehicles.py) after photos of Prague traffic: Hatch, Estate, Suv, Van, Taxi.
// The materials come by name and are replaced with game materials: painted body, see-through glass (the interior and
// the driver show through), chrome, lit lamps. Parked rows use the model's merged "Low" mesh.
public static class Vehicles
{
    public static readonly string[] Models = { "Hatch", "Estate", "Suv", "Van", "Taxi" };
    static readonly Dictionary<string, GameObject> src = new();
    static readonly Dictionary<string, Quaternion> fix = new();
    static Material glass, black, chrome, tyre, rim, head, tail, ind, plate, interior, seat, taxi;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { src.Clear(); fix.Clear(); glass = null; }

    public static bool Available => Src("Estate") != null;

    static GameObject Src(string n)
    {
        if (src.TryGetValue(n, out var g)) return g;
        g = Resources.Load<GameObject>("Vehicles/" + n); src[n] = g;
        if (g)
        {
            // the Front marker lies ahead of the bonnet: turn the model so that it points along +Z
            var f = g.transform.Find("Front"); var d = f ? f.position - g.transform.position : Vector3.forward; d.y = 0;
            fix[n] = Quaternion.FromToRotation(d.normalized, Vector3.forward);
        }
        return g;
    }

    static void Init()
    {
        if (glass) return;
        glass = Glass(new Color(0.55f, 0.62f, 0.66f, 0.28f));
        black = Gfx.Mat(new Color(0.03f, 0.03f, 0.035f), 0.35f); chrome = Gfx.Mat(new Color(0.78f, 0.79f, 0.8f), 0.85f);
        chrome.SetFloat("_Metallic", 0.9f);
        tyre = Gfx.Mat(new Color(0.04f, 0.04f, 0.04f), 0.15f); rim = Gfx.Mat(new Color(0.62f, 0.63f, 0.65f), 0.7f); rim.SetFloat("_Metallic", 0.8f);
        head = Gfx.Emissive(new Color(1f, 0.97f, 0.9f), 1.4f); tail = Gfx.Emissive(new Color(0.75f, 0.04f, 0.04f), 1.6f);
        ind = Gfx.Emissive(new Color(1f, 0.55f, 0.1f), 0.8f); plate = Gfx.Mat(new Color(0.95f, 0.95f, 0.93f), 0.4f);
        interior = Gfx.Mat(new Color(0.07f, 0.07f, 0.075f), 0.25f); seat = Gfx.Mat(new Color(0.15f, 0.15f, 0.16f), 0.1f);
        taxi = Gfx.Emissive(new Color(1f, 0.82f, 0.15f), 1.2f);
    }

    // URP Lit, transparent, still reflecting the sky (premultiplied so the highlights stay bright)
    public static Material Glass(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sklo" };
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 1f); m.SetFloat("_AlphaPremultiply", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.96f); m.SetFloat("_Metallic", 0.1f);
        return m;
    }

    public static Material For(string name, Material paint)
    {
        Init();
        int sp = name.IndexOf(' '); if (sp > 0) name = name.Substring(0, sp);
        return name switch
        {
            "Paint" => paint, "Glass" => glass, "Black" => black, "Chrome" => chrome, "Tyre" => tyre, "Rim" => rim, "Head" => head,
            "Tail" => tail, "Indicator" => ind, "Plate" => plate, "Interior" => interior, "Seat" => seat, "Taxi" => taxi, _ => black,
        };
    }

    public class Inst { public Transform root; public Transform[] wheels = new Transform[4]; public Vector3 seat; public float wheelR = 0.32f; }

    // the detailed model under parent (origin on the ground between the axles); wheels re-parented to plain pivots
    public static Inst Build(string model, Transform parent, Material paint)
    {
        var s = Src(model); if (!s) return null;
        var r = new Inst();
        var pivot = new GameObject("Model").transform; pivot.SetParent(parent, false); r.root = pivot;
        var g = Object.Instantiate(s, pivot); g.name = model;
        g.transform.localRotation = fix[model];
        foreach (var mr in g.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.name == "Low") { mr.gameObject.SetActive(false); continue; }
            var ms = mr.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = For(ms[i] ? ms[i].name : "", paint);
            mr.sharedMaterials = ms; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        string[] wn = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        for (int i = 0; i < 4; i++)
        {
            var w = Deep(g.transform, wn[i]); if (!w) continue;
            var p = new GameObject("Wheel" + i).transform; p.SetParent(parent, false);
            p.position = w.GetComponent<Renderer>().bounds.center; p.rotation = parent.rotation;
            r.wheelR = w.GetComponent<Renderer>().bounds.extents.y;
            w.SetParent(p, true); r.wheels[i] = p;
        }
        var st = Deep(g.transform, "Seat_Driver"); r.seat = st ? parent.InverseTransformPoint(st.position) : new Vector3(-0.37f, 0.6f, 0);
        return r;
    }

    static Transform Deep(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { var f = Deep(c, n); if (f) return f; }
        return null;
    }

    // the low mesh, its material names and the matrix from the car's pose to the mesh
    public static bool Low(string model, out Mesh mesh, out string[] mats, out Matrix4x4 local)
    {
        mesh = null; mats = null; local = Matrix4x4.identity;
        var s = Src(model); if (!s) return false;
        var low = Deep(s.transform, "Low"); if (!low) return false;
        mesh = low.GetComponent<MeshFilter>().sharedMesh;
        var ms = low.GetComponent<MeshRenderer>().sharedMaterials; mats = new string[ms.Length];
        for (int i = 0; i < ms.Length; i++) mats[i] = ms[i] ? ms[i].name : "";
        local = Matrix4x4.Rotate(fix[model]) * s.transform.worldToLocalMatrix * low.localToWorldMatrix;
        return true;
    }

    static Material sharedGlass, cabFloor, cabSeat, cabPole, cabLight, cabDesk;
    public static Material SharedGlass { get { if (!sharedGlass) sharedGlass = Glass(new Color(0.5f, 0.58f, 0.62f, 0.3f)); return sharedGlass; } }
    // a window band: glass sides and ends only, so the saloon behind it is seen
    public static void GlassBand(MeshBuilder mb, Vector3 c, Vector3 size, Quaternion? rot = null) => mb.Box(SharedGlass, c, size, rot ?? Quaternion.identity, 0f, false, false);
    // a hollow body shell (no top or bottom face) so the cabin is not filled solid
    public static void Shell(MeshBuilder mb, Material m, Vector3 c, Vector3 size) => mb.Box(m, c, size, Quaternion.identity, 0f, false, false);

    // A public-transport saloon along z: floor, ceiling with light panels, seat pairs along both sides facing the way of
    // travel with an aisle between, and the yellow grab poles of Prague trams and trains. seatCol tints the upholstery.
    public static void Saloon(MeshBuilder mb, float W, float z0, float z1, float floorY, float ceilY, Color seatCol, bool poles, float pitch = 1.7f)
    {
        if (!cabFloor)
        {
            cabFloor = Gfx.Mat(new Color(0.24f, 0.25f, 0.26f), 0.3f); cabPole = Gfx.Mat(new Color(0.95f, 0.75f, 0.05f), 0.6f);
            cabLight = Gfx.Emissive(new Color(1f, 0.97f, 0.9f), 1.2f); cabDesk = Gfx.Mat(new Color(0.2f, 0.2f, 0.22f), 0.4f);
        }
        var seatM = Gfx.Mat(seatCol, 0.15f); var ceil = Gfx.Mat(new Color(0.85f, 0.86f, 0.86f), 0.3f);
        float zc = (z0 + z1) / 2f, L = z1 - z0;
        mb.Box(cabFloor, new Vector3(0, floorY - 0.03f, zc), new Vector3(W - 0.1f, 0.06f, L));
        mb.Box(ceil, new Vector3(0, ceilY + 0.03f, zc), new Vector3(W - 0.1f, 0.06f, L), Quaternion.identity, 0f, false, true);
        for (float z = z0 + 0.8f; z < z1 - 0.6f; z += 2.4f) mb.Box(cabLight, new Vector3(0, ceilY - 0.005f, z), new Vector3(0.5f, 0.02f, 1.3f), Quaternion.identity, 0f, false, true);
        float sx = W / 2f - 0.55f;
        for (float z = z0 + 0.6f; z < z1 - 0.5f; z += pitch)
            foreach (float side in new[] { -1f, 1f })
            {
                var c = new Vector3(side * sx, floorY, z);
                mb.Box(cabDesk, c + new Vector3(0, 0.22f, 0), new Vector3(0.9f, 0.44f, 0.4f));
                mb.Box(seatM, c + new Vector3(0, 0.5f, 0), new Vector3(0.9f, 0.1f, 0.46f));
                mb.Box(seatM, c + new Vector3(0, 0.88f, -0.25f), new Vector3(0.9f, 0.72f, 0.1f), Quaternion.Euler(-8f, 0, 0));
                if (poles) mb.Box(cabPole, c + new Vector3(-side * 0.5f, (ceilY - floorY) / 2f, -0.35f), new Vector3(0.04f, ceilY - floorY, 0.04f));
            }
        if (poles) mb.Box(cabPole, new Vector3(0, ceilY - 0.22f, zc), new Vector3(0.035f, 0.035f, L - 0.4f));
    }

    // a seated driver: hips on the seat, hands on the wheel
    public static HumanMotion Driver(Transform car, Vector3 seat, int seed)
    {
        if (!People.Available) return null;
        var holder = new GameObject("Ridic").transform; holder.SetParent(car, false);
        var h = People.Spawn(holder, new System.Random(seed));
        if (h == null) { Object.Destroy(holder.gameObject); return null; }
        h.seated = true; h.Tick(Vector3.zero, 0.02f);
        float hip = h.HipHeight();
        holder.localPosition = seat + new Vector3(0, -hip + 0.08f, 0.12f);
        foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        car.gameObject.AddComponent<NearOnly>().target = holder.gameObject;
        return h;
    }
}

// shows target only near the camera (the seated drivers are full skinned people, seen through the glass up close)
public class NearOnly : MonoBehaviour
{
    public GameObject target; public float dist = 45f; float t;
    void Update()
    {
        if (!target || (t -= Time.deltaTime) > 0f) return;
        t = 0.4f + Random.value * 0.2f; var cam = Camera.main;
        bool near = cam && (cam.transform.position - transform.position).sqrMagnitude < dist * dist;
        if (target.activeSelf != near) target.SetActive(near);
    }
}
