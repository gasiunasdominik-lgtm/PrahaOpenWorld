using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Menu: Praha > Nastavit scénu. Creates Assets/Scenes/Praha.unity with the lighting story,
// palette sky, global Volume and the GameManager bootstrap (the city is generated at runtime).
public static class PrahaSetup
{
    const string ScenePath = "Assets/Scenes/Praha.unity";
    const string SkyPath = "Assets/Settings/PrahaSky.mat";
    const string ProfilePath = "Assets/Settings/PrahaVolume.asset";

    [MenuItem("Praha/Nastavit scénu")]
    public static string Setup()
    {
        // URP asset: shadow distance sized to an open city, HDR on so bloom thresholds work
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.shadowDistance = 220f;
            urp.supportsHDR = true;
            urp.shadowCascadeCount = 4;
            EditorUtility.SetDirty(urp);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunGo = new GameObject("Slunce");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Palette.SunKey; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(34f, 40f, 0f);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        cam.allowHDR = true;
        camGo.transform.position = new Vector3(120, 90, -200);

        // palette-derived procedural sky
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, SkyPath);
        }
        sky.SetColor("_SkyTint", Palette.SkyTop);
        sky.SetColor("_GroundColor", Color.Lerp(Palette.Fog, Palette.GroundBounce, 0.4f));
        sky.SetFloat("_AtmosphereThickness", 0.75f);
        sky.SetFloat("_Exposure", 1.0f);
        sky.SetFloat("_SunSize", 0.05f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(Palette.SkyTop, Palette.Shadow, 0.25f);
        RenderSettings.ambientEquatorColor = Color.Lerp(Palette.Horizon, Palette.Cobble, 0.45f);
        RenderSettings.ambientGroundColor = Palette.GroundBounce;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(Palette.Fog, Palette.SkyTop, 0.4f);
        RenderSettings.fogStartDistance = 300f;
        RenderSettings.fogEndDistance = 2300f;

        // global volume: golden-hour grade
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        foreach (var c in profile.components.ToArray()) { profile.Remove(c.GetType()); Object.DestroyImmediate(c, true); }
        var tm = Add<Tonemapping>(profile); tm.mode.Override(TonemappingMode.ACES);
        var bloom = Add<Bloom>(profile); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.6f);
        var vig = Add<Vignette>(profile); vig.intensity.Override(0.22f); vig.smoothness.Override(0.45f);
        var ca = Add<ColorAdjustments>(profile); ca.saturation.Override(10f); ca.contrast.Override(6f); ca.postExposure.Override(0.15f);
        var wb = Add<WhiteBalance>(profile); wb.temperature.Override(10f);
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("Global Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = profile;

        new GameObject("GameManager").AddComponent<GameManager>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        return "Praha scene ready: " + ScenePath;
    }

    static T Add<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = p.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }
}
