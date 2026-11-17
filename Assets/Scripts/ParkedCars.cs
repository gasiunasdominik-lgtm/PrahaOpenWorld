using System.Collections.Generic;
using UnityEngine;

// Cars parked along the kerbs (MapBuilder.ParkingSlots): thousands of them, so each 200 m tile is one merged mesh plus
// a box collider per car, and tiles far from the camera are hidden. Walk up to one and press E: it becomes a real,
// drivable car (and leaves the merged row).
public class ParkedCars : MonoBehaviour
{
    public static ParkedCars I;
    const float Tile = 200f, ShowDist = 260f;
    class Slot { public Vector3 pos; public float yaw; public int paint, style; public bool gone, locked; }
    readonly Dictionary<Vector2Int, List<Slot>> tiles = new();
    readonly Dictionary<Vector2Int, GameObject> built = new();
    Material[] paints; Material glass, trim, chrome, head, tail;
    float timer;

    // real-world colour mix of European cars (grey/black/white/silver dominate)
    static readonly (Color c, float w)[] Mix = {
        (new Color(0.42f, 0.43f, 0.45f), 0.24f), (new Color(0.05f, 0.05f, 0.06f), 0.2f), (new Color(0.92f, 0.92f, 0.9f), 0.2f),
        (new Color(0.7f, 0.71f, 0.72f), 0.13f), (new Color(0.1f, 0.18f, 0.38f), 0.09f), (new Color(0.55f, 0.06f, 0.06f), 0.06f),
        (new Color(0.32f, 0.22f, 0.15f), 0.03f), (new Color(0.12f, 0.28f, 0.2f), 0.02f), (new Color(0.85f, 0.6f, 0.1f), 0.015f), (new Color(0.2f, 0.45f, 0.7f), 0.015f) };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { I = null; }

    void Start()
    {
        I = this;
        paints = new Material[Mix.Length];
        for (int i = 0; i < Mix.Length; i++) paints[i] = Gfx.Mat(Mix[i].c, 0.7f);
        glass = Gfx.Mat(Palette.Glass, 0.92f); trim = Gfx.Mat(Palette.Tire, 0.3f); chrome = Gfx.Mat(Palette.Chrome, 0.7f);
        head = Gfx.Mat(new Color(0.85f, 0.85f, 0.8f), 0.9f); tail = Gfx.Mat(new Color(0.5f, 0.05f, 0.05f), 0.7f);
        var rng = new System.Random(23);
        // occupancy: kerbs in the centre and around shops, pubs and sights are full, quiet residential streets half empty
        var busy = new Dictionary<Vector2Int, float>(); int skipped = 0;
        foreach (var (pos, yaw) in MapBuilder.ParkingSlots)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(pos.x / 100f), Mathf.FloorToInt(pos.z / 100f));
            if (!busy.TryGetValue(cell, out float occ))
            {
                var c = new Vector3((cell.x + 0.5f) * 100f, 0, (cell.y + 0.5f) * 100f);
                float centre = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(c.x, c.z), new Vector2(694f, 114f)) / 2200f);
                float shops = Mathf.Clamp01(Places.OpenNear(c, 13f, 150f) / 10f);
                float hot = 0f; foreach (var h in Pedestrians.Hotspots) hot = Mathf.Max(hot, Mathf.Clamp01(1f - (Vector2.Distance(new Vector2(c.x, c.z), (h.a + h.b) / 2f) - h.r) / 400f));
                busy[cell] = occ = Mathf.Clamp(0.25f + 0.3f * centre + 0.3f * shops + 0.2f * hot, 0.25f, 0.95f);
            }
            if (rng.NextDouble() > occ) { skipped++; continue; }
            double r = rng.NextDouble(); int pi = 0;
            for (float acc = 0; pi < Mix.Length - 1; pi++) { acc += Mix[pi].w; if (r < acc) break; }
            var s = new Slot { pos = pos, yaw = yaw, paint = pi, style = new[] { 0, 1, 0, 3, 1, 2, 0, 1, 3, 1 }[rng.Next(10)], locked = rng.NextDouble() < 0.55 };
            var key = Key(pos);
            if (!tiles.TryGetValue(key, out var l)) tiles[key] = l = new List<Slot>();
            l.Add(s);
        }
        foreach (var k in tiles.Keys) Rebuild(k);
        Debug.Log($"[Parkování] {MapBuilder.ParkingSlots.Count - skipped} aut ({skipped} míst volných) v {tiles.Count} dlaždicích");
    }

    static Vector2Int Key(Vector3 p) => new(Mathf.FloorToInt(p.x / Tile), Mathf.FloorToInt(p.z / Tile));

    void Rebuild(Vector2Int k)
    {
        if (built.TryGetValue(k, out var old)) Destroy(old);
        var mb = new MeshBuilder();
        var root = new GameObject($"Parkovani_{k.x}_{k.y}") { isStatic = true }; root.transform.SetParent(transform, false);
        var groups = new Dictionary<Material, List<CombineInstance>>();
        foreach (var s in tiles[k])
        {
            if (s.gone) continue;
            if (Vehicles.Low(CarController.ModelOf(s.style), out var mesh, out var mats, out var local))
            {
                var m = Matrix4x4.TRS(s.pos, Quaternion.Euler(0, s.yaw, 0), Vector3.one) * local;
                for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                {
                    var mat = Vehicles.For(mats[i], paints[s.paint]);
                    if (!groups.TryGetValue(mat, out var l)) groups[mat] = l = new List<CombineInstance>();
                    l.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = m });
                }
            }
            else Body(mb, s);
            var c = new GameObject("P").AddComponent<BoxCollider>(); c.transform.SetParent(root.transform, false);
            c.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0, s.yaw, 0));
            bool van = s.style == 2; c.center = new Vector3(0, van ? 1.05f : 0.8f, 0); c.size = new Vector3(1.8f, van ? 1.9f : 1.4f, van ? 4.6f : 4.2f);
        }
        mb.Build("Auta", root.transform, false);
        foreach (var kv in groups)
        {
            var me = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            me.CombineMeshes(kv.Value.ToArray(), true, true); me.RecalculateBounds();
            var go = new GameObject("Auta_" + kv.Key.name) { isStatic = true }; go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = me;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key;
            r.shadowCastingMode = kv.Key.renderQueue >= 3000 ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        }
        built[k] = root;
    }

    // The same silhouette as the drivable cars (CarController.Build), in the tile's merged mesh.
    void Body(MeshBuilder mb, Slot s)
    {
        var rot = Quaternion.Euler(0, s.yaw, 0); var o = s.pos;
        void B(Material m, Vector3 c, Vector3 size, Quaternion? r = null) => mb.Box(m, o + rot * c, size, rot * (r ?? Quaternion.identity));
        var paint = paints[s.paint]; bool van = s.style == 2; float L = van ? 4.4f : s.style == 1 ? 3.9f : 4.3f;
        B(paint, new Vector3(0, 0.72f, 0), new Vector3(1.8f, 0.62f, L));
        B(trim, new Vector3(0, 0.45f, L / 2 + 0.03f), new Vector3(1.84f, 0.22f, 0.12f));
        B(trim, new Vector3(0, 0.45f, -L / 2 - 0.03f), new Vector3(1.84f, 0.22f, 0.12f));
        if (van)
        {
            B(paint, new Vector3(0, 1.45f, -0.35f), new Vector3(1.76f, 0.95f, L - 1.1f));
            B(glass, new Vector3(0, 1.38f, L / 2 - 1.0f), new Vector3(1.66f, 0.6f, 0.6f), Quaternion.Euler(-20, 0, 0));
        }
        else
        {
            float cabZ = s.style == 1 ? -0.3f : -0.15f, cabL = s.style == 1 ? 2.0f : 2.1f;
            B(glass, new Vector3(0, 1.28f, cabZ), new Vector3(1.6f, 0.52f, cabL));
            B(paint, new Vector3(0, 1.56f, cabZ - 0.05f), new Vector3(1.64f, 0.08f, cabL - 0.25f));
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            B(head, new Vector3(sx * 0.62f, 0.8f, L / 2 + 0.01f), new Vector3(0.36f, 0.16f, 0.06f));
            B(tail, new Vector3(sx * 0.66f, 0.82f, -L / 2 - 0.01f), new Vector3(0.3f, 0.14f, 0.06f));
            foreach (float sz in new[] { 1.3f, -1.25f }) // wheels: a rotated box reads as round under the body
                B(trim, new Vector3(sx * 0.82f, 0.34f, sz), new Vector3(0.24f, 0.6f, 0.6f), Quaternion.Euler(45, 0, 0));
        }
    }

    void Update()
    {
        if ((timer -= Time.deltaTime) > 0f) return;
        timer = 0.5f;
        var cam = Camera.main; if (!cam) return;
        var p = cam.transform.position;
        foreach (var kv in built)
        {
            var c = new Vector3((kv.Key.x + 0.5f) * Tile, p.y, (kv.Key.y + 0.5f) * Tile);
            bool on = (c - p).sqrMagnitude < (ShowDist + Tile * 0.7f) * (ShowDist + Tile * 0.7f);
            if (kv.Value.activeSelf != on) kv.Value.SetActive(on);
        }
    }

    // whether the nearest parked car within r is locked (with an alarm: no way in without a tool)
    public bool IsLocked(Vector3 at, float r)
    {
        Slot best = null; float bd = r * r; var k0 = Key(at);
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            if (tiles.TryGetValue(new Vector2Int(k0.x + dx, k0.y + dz), out var l))
                foreach (var s in l) { if (s.gone) continue; float d = (s.pos - at).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
        return best != null && best.locked;
    }

    // The nearest parked car within r, taken out of the row: its pose, paint and body style for a drivable replacement.
    public bool Take(Vector3 at, float r, bool remove, out Vector3 pos, out float yaw, out Color paint, out int style)
    {
        pos = default; yaw = 0; paint = default; style = 0;
        Slot best = null; float bd = r * r; Vector2Int bk = default;
        var k0 = Key(at);
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
        {
            var k = new Vector2Int(k0.x + dx, k0.y + dz);
            if (!tiles.TryGetValue(k, out var l)) continue;
            foreach (var s in l) { if (s.gone) continue; float d = (s.pos - at).sqrMagnitude; if (d < bd) { bd = d; best = s; bk = k; } }
        }
        if (best == null) return false;
        pos = best.pos; yaw = best.yaw; paint = Mix[best.paint].c; style = best.style;
        if (remove) { best.gone = true; Rebuild(bk); }
        return true;
    }
}
