using System.Collections.Generic;
using UnityEngine;

// Pigeon flocks on real squares (MPP). Behaviour after urban-bird studies: the first reaction to an approaching
// person is to walk away; they fly only when it keeps coming or is fast; flight-initiation distance is shorter
// where there are many pedestrians (Ardea 102/1, 2014; Animal Behaviour 178, 2021). Flock size 10–50 (odhad).
public class Pigeons : MonoBehaviour
{
    class Bird { public Transform t; public Vector3 home, vel; public float fly, peck; }
    readonly List<Bird> birds = new();
    readonly System.Random rng = new(9);
    Vector3 flockAt = new(1e9f, 0, 0);
    Mesh body; Material grey;

    void Awake()
    {
        var mb = new MeshBuilder(); grey = Gfx.Mat(new Color(0.48f, 0.5f, 0.56f), 0.2f);
        mb.Box(grey, new Vector3(0, 0.12f, 0), new Vector3(0.14f, 0.13f, 0.26f));
        mb.Box(grey, new Vector3(0, 0.22f, 0.1f), new Vector3(0.09f, 0.09f, 0.09f));
        var go = mb.Build("Holub", transform, false, false); body = go.GetComponent<MeshFilter>().sharedMesh; Destroy(go);
    }

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || gm.State != GameState.Playing) return;
        var me = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;
        float h = GameClock.I.hour; bool day = h > 6f && h < 20f;
        if ((flockAt - me).sqrMagnitude > 180f * 180f) Respawn(me, day);

        // threats: the player (fast when running or driving) and nearby pedestrians
        float crowd = 0; foreach (var w in Pedestrians.I.live) if ((w.transform.position - flockAt).sqrMagnitude < 40f * 40f) crowd++;
        float fid = Mathf.Lerp(6f, 2.5f, Mathf.Clamp01(crowd / 15f)); // shorter flight distance in crowds
        float speed = gm.currentCar ? Mathf.Abs(gm.currentCar.SpeedKmh) / 3.6f : gm.player.Velocity.magnitude;
        foreach (var b in birds)
        {
            var p = b.t.position; var away = p - me; away.y = 0; float d = away.magnitude;
            if (b.fly > 0f)
            {
                b.fly -= Time.deltaTime; b.vel.y = b.fly > 1.2f ? 3f : -2.5f;
                p += b.vel * Time.deltaTime; float g = MapBuilder.Height(p.x, p.z);
                if (p.y < g) { p.y = g; b.fly = 0f; b.home = p; }
            }
            else if (d < fid * (speed > 3f ? 2.2f : 1f) && (speed > 3f || d < fid * 0.5f)) // fast or very close: take off
            {
                b.fly = 2.5f + (float)rng.NextDouble(); b.vel = (away.normalized + Random.insideUnitSphere * 0.4f) * 9f;
                if (birds.IndexOf(b) % 6 == 0) GameAudio.I?.PlayAt(GameAudio.I.click, p, 0.25f);
            }
            else
            {
                Vector3 step = Vector3.zero;
                if (d < fid) step = away.normalized * 1.2f * Time.deltaTime; // walk away first
                else if ((b.peck -= Time.deltaTime) < 0f) { b.peck = 0.5f + (float)rng.NextDouble() * 2f; step = Random.insideUnitSphere * 0.25f; }
                p += new Vector3(step.x, 0, step.z); p.y = MapBuilder.Height(p.x, p.z) + Mathf.Abs(Mathf.Sin(Time.time * 9f + b.home.x)) * 0.02f;
                if (step.sqrMagnitude > 1e-6f) b.t.rotation = Quaternion.LookRotation(new Vector3(step.x, 0, step.z));
            }
            b.t.position = p;
        }
    }

    // A flock on the nearest real square within reach; none at night (they roost).
    void Respawn(Vector3 me, bool day)
    {
        foreach (var b in birds) Destroy(b.t.gameObject);
        birds.Clear(); flockAt = me;
        if (!day) return;
        MapBuilder.Poly best = null; float bd = 160f * 160f; Vector3 c = default;
        foreach (var sq in MapBuilder.D.squares)
        {
            float x = 0, z = 0; int n = sq.p.Length / 2; for (int k = 0; k < n; k++) { x += sq.p[2 * k]; z += sq.p[2 * k + 1]; }
            var cc = new Vector3(x / n, 0, z / n); float d = (cc - me).sqrMagnitude;
            if (d < bd) { bd = d; best = sq; c = cc; }
        }
        if (best == null) return;
        flockAt = c; int count = 12 + rng.Next(28);
        for (int k = 0; k < count; k++)
        {
            var p = c + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 14f;
            p.y = MapBuilder.Height(p.x, p.z);
            var go = new GameObject("Holub"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = body; go.AddComponent<MeshRenderer>().sharedMaterial = grey;
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, rng.Next(360), 0));
            birds.Add(new Bird { t = go.transform, home = p });
        }
    }
}
