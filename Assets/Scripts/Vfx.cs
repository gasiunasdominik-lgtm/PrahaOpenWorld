using UnityEngine;

// Pooled, palette-tinted particle effects. One system per effect, reused via Emit().
public class Vfx : MonoBehaviour
{
    public static Vfx I { get; private set; }
    public ParticleSystem confetti, dust, puff, sparks;
    Material mat;

    void Awake()
    {
        I = this;
        mat = new Material(Resources.Load<Material>("SpriteMat")) { name = "M_Particles" }; // Shader.Find can miss a stripped shader in a build
        confetti = Make("Confetti", 1.4f, 0.35f, 9f, 1.2f, new[] { Palette.Accent, Palette.Gold, Palette.Copper });
        var shp = confetti.shape; shp.shapeType = ParticleSystemShapeType.Cone; shp.angle = 35; shp.radius = 0.5f;
        dust = Make("Dust", 0.9f, 0.9f, 1.2f, -0.05f, new[] { Color.Lerp(Palette.Cobble, Palette.Fog, 0.5f), Palette.Cobble });
        var dsz = dust.sizeOverLifetime; dsz.enabled = true; dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.4f, 1, 1.6f));
        puff = Make("Puff", 0.35f, 0.25f, 0.8f, 0f, new[] { Color.Lerp(Palette.Cobble, Palette.Fog, 0.4f) });
        sparks = Make("Sparks", 0.4f, 0.12f, 7f, 1.5f, new[] { Palette.Gold, Palette.HeadLight });
    }

    ParticleSystem Make(string n, float life, float size, float speed, float gravity, Color[] colors)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var g = new Gradient();
        var ck = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i], colors.Length == 1 ? 0 : i / (float)(colors.Length - 1));
        g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission; em.rateOverTime = 0;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var fadeG = new Gradient();
        fadeG.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = fadeG;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    public void Emit(ParticleSystem ps, Vector3 pos, int count, Vector3? dir = null)
    {
        ps.transform.position = pos;
        ps.transform.rotation = Quaternion.LookRotation(dir ?? Vector3.up);
        ps.Emit(count);
    }
}
