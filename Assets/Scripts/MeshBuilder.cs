using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Accumulates flat-shaded geometry grouped by material, then emits one GameObject
// with one mesh (one submesh per material). Face winding is fixed automatically
// from an "outward" hint, so callers only need to list polygon corners in order.
public class MeshBuilder
{
    class Part
    {
        public readonly List<Vector3> v = new();
        public readonly List<Vector3> n = new();
        public readonly List<Vector2> uv = new();
        public readonly List<int> t = new();
    }

    readonly Dictionary<Material, Part> parts = new();
    readonly List<Material> order = new();

    public int VertexCount { get { int c = 0; foreach (var p in parts.Values) c += p.v.Count; return c; } }

    Part P(Material m)
    {
        if (!parts.TryGetValue(m, out var p)) { p = new Part(); parts[m] = p; order.Add(m); }
        return p;
    }

    public void Poly(Material m, Vector3[] pts, Vector2[] uvs, Vector3 outward)
    {
        Vector3 nrm = Vector3.zero;
        for (int i = 1; i < pts.Length - 1 && nrm.sqrMagnitude < 1e-10f; i++)
            nrm = Vector3.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
        if (nrm.sqrMagnitude < 1e-12f) return;
        nrm.Normalize();
        bool flip = Vector3.Dot(nrm, outward) < 0f;
        if (flip) nrm = -nrm;

        var p = P(m);
        int b = p.v.Count;
        for (int i = 0; i < pts.Length; i++)
        {
            p.v.Add(pts[i]);
            p.n.Add(nrm);
            p.uv.Add(uvs != null ? uvs[i] : Vector2.zero);
        }
        for (int i = 1; i < pts.Length - 1; i++)
        {
            if (!flip) { p.t.Add(b); p.t.Add(b + i); p.t.Add(b + i + 1); }
            else { p.t.Add(b); p.t.Add(b + i + 1); p.t.Add(b + i); }
        }
    }

    // Axis-aligned or rotated box. uvTile = meters per UV unit (0 = 0..1 per face).
    public void Box(Material m, Vector3 center, Vector3 size, Quaternion rot, float uvTile = 0f,
                    bool top = true, bool bottom = false)
    {
        Vector3 h = size * 0.5f;
        Vector3 C(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
        void F(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nLocal, float su, float sv)
        {
            Vector2[] uv = null;
            if (uvTile > 0f)
            {
                float u = su / uvTile, v = sv / uvTile;
                uv = new[] { new Vector2(0, 0), new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0) };
            }
            else uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Poly(m, new[] { a, b, c, d }, uv, rot * nLocal);
        }
        F(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), Vector3.right, size.z, size.y);
        F(C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), C(-1, -1, -1), Vector3.left, size.z, size.y);
        F(C(-1, -1, 1), C(-1, 1, 1), C(1, 1, 1), C(1, -1, 1), Vector3.forward, size.x, size.y);
        F(C(1, -1, -1), C(1, 1, -1), C(-1, 1, -1), C(-1, -1, -1), Vector3.back, size.x, size.y);
        if (top) F(C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), Vector3.up, size.x, size.z);
        if (bottom) F(C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), Vector3.down, size.z, size.x);
    }

    public void Box(Material m, Vector3 center, Vector3 size, float uvTile = 0f, bool top = true)
        => Box(m, center, size, Quaternion.identity, uvTile, top);

    // A wall quad from base point a to base point b, between heights y0..y1.
    // UVs: u = whole number of window cells across, v = floors.
    public void Wall(Material m, Vector3 a, Vector3 b, float y0, float y1, Vector3 outward,
                     float cellW = 3.2f, float cellH = 3.4f)
    {
        float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
        float u = Mathf.Max(1f, Mathf.Round(len / cellW));
        float v = (y1 - y0) / cellH;
        Poly(m, new[]
        {
            new Vector3(a.x, y0, a.z), new Vector3(a.x, y1, a.z),
            new Vector3(b.x, y1, b.z), new Vector3(b.x, y0, b.z)
        }, new[] { new Vector2(0, 0), new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0) }, outward);
    }

    // Gable roof. Ridge runs along the local Z axis (length), slopes across X (width).
    public void Prism(Material roof, Material gable, Vector3 baseCenter, float width, float length,
                      float height, float yaw)
    {
        var r = Quaternion.Euler(0, yaw, 0);
        float w = width * 0.5f, l = length * 0.5f;
        Vector3 P(float x, float y, float z) => baseCenter + r * new Vector3(x, y, z);
        Poly(roof, new[] { P(w, 0, -l), P(w, 0, l), P(0, height, l), P(0, height, -l) }, null, r * new Vector3(height, w, 0));
        Poly(roof, new[] { P(-w, 0, l), P(-w, 0, -l), P(0, height, -l), P(0, height, l) }, null, r * new Vector3(-height, w, 0));
        Poly(gable, new[] { P(w, 0, l), P(-w, 0, l), P(0, height, l) }, null, r * Vector3.forward);
        Poly(gable, new[] { P(-w, 0, -l), P(w, 0, -l), P(0, height, -l) }, null, r * Vector3.back);
    }

    // N-sided cone / spire / pyramid (segments = 4 gives a square pyramid).
    public void Cone(Material m, Vector3 baseCenter, float radius, float height, int segments, float yawDeg = 45f)
    {
        Vector3 apex = baseCenter + Vector3.up * height;
        for (int i = 0; i < segments; i++)
        {
            float a0 = (yawDeg + i * 360f / segments) * Mathf.Deg2Rad;
            float a1 = (yawDeg + (i + 1) * 360f / segments) * Mathf.Deg2Rad;
            Vector3 p0 = baseCenter + new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * radius;
            Vector3 p1 = baseCenter + new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * radius;
            Vector3 mid = (p0 + p1) * 0.5f - baseCenter;
            Poly(m, new[] { p0, p1, apex }, null, mid.normalized + Vector3.up * 0.3f);
        }
    }

    public void Cylinder(Material m, Vector3 baseCenter, float radius, float height, int segments,
                         bool capTop = true, bool capBottom = false, float topRadius = -1f)
    {
        if (topRadius < 0f) topRadius = radius;
        var topPts = new Vector3[segments];
        var botPts = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            botPts[i] = baseCenter + dir * radius;
            topPts[i] = baseCenter + dir * topRadius + Vector3.up * height;
        }
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            Vector3 mid = (botPts[i] + botPts[j]) * 0.5f - baseCenter;
            mid.y = 0;
            Poly(m, new[] { botPts[i], topPts[i], topPts[j], botPts[j] }, null, mid);
        }
        if (capTop) Poly(m, topPts, null, Vector3.up);
        if (capBottom) Poly(m, botPts, null, Vector3.down);
    }

    // Upper hemisphere, optionally squashed (ySquash < 1) or onion-stretched (> 1).
    public void Dome(Material m, Vector3 center, float radius, float ySquash, int segments = 12, int rings = 5)
    {
        for (int r = 0; r < rings; r++)
        {
            float t0 = r / (float)rings * Mathf.PI * 0.5f;
            float t1 = (r + 1) / (float)rings * Mathf.PI * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 Pt(float t, float a) => center + new Vector3(
                    Mathf.Sin(a) * Mathf.Cos(t) * radius, Mathf.Sin(t) * radius * ySquash, Mathf.Cos(a) * Mathf.Cos(t) * radius);
                var q0 = Pt(t0, a0); var q1 = Pt(t0, a1); var q2 = Pt(t1, a1); var q3 = Pt(t1, a0);
                Poly(m, new[] { q0, q1, q2, q3 }, null, ((q0 + q2) * 0.5f - center));
            }
        }
    }

    // Flat horizontal quad with world-meter UVs (uvTile meters per UV unit). uAlongZ swaps axes.
    public void Ground(Material m, float x0, float z0, float x1, float z1, float y, float uvTileU, float uvTileV, bool uAlongZ = false)
    {
        Vector2 U(float x, float z) => uAlongZ
            ? new Vector2((z - z0) / uvTileV, (x - x0) / uvTileU)
            : new Vector2((x - x0) / uvTileU, (z - z0) / uvTileV);
        Poly(m, new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) },
            new[] { U(x0, z0), U(x0, z1), U(x1, z1), U(x1, z0) }, Vector3.up);
    }

    public GameObject Build(string name, Transform parent, bool collider, bool castShadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.isStatic = true;
        if (order.Count == 0) return go;

        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
        var subs = new List<int[]>();
        foreach (var m in order)
        {
            var p = parts[m];
            int b = verts.Count;
            verts.AddRange(p.v); norms.AddRange(p.n); uvs.AddRange(p.uv);
            var tri = new int[p.t.Count];
            for (int i = 0; i < tri.Length; i++) tri[i] = p.t[i] + b;
            subs.Add(tri);
        }
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subs.Count;
        for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i, false);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = order.ToArray();
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
}
