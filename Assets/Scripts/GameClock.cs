using UnityEngine;

// Atom "Čas": one game day = 24 real minutes. Drives the sun, ambient light, sky and fog.
public class GameClock : MonoBehaviour
{
    public static GameClock I { get; private set; }
    public float hour = 7.5f, minutesPerSecond = 1f;
    public string Clock => $"{(int)hour:00}:{(int)(hour % 1f * 60f):00}";
    /// 0 at night, 1 in full day.
    public float Daylight => Mathf.InverseLerp(5f, 8f, hour) * (1f - Mathf.InverseLerp(19f, 21.5f, hour));

    Light sun; Material sky; Color ambSky, ambEq, ambGround, fog, sunColor; float sunIntensity, skyExposure;
    static readonly Color Moon = new(0.55f, 0.65f, 1f);

    void Awake() => I = this;

    void Start()
    {
        sun = RenderSettings.sun; sunColor = sun.color; sunIntensity = sun.intensity;
        ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor;
        fog = RenderSettings.fogColor;
        if (RenderSettings.skybox) { sky = RenderSettings.skybox = new Material(RenderSettings.skybox); skyExposure = sky.GetFloat("_Exposure"); } // copy: never edit the asset
    }

    void Update()
    {
        hour = (hour + Time.deltaTime * minutesPerSecond / 60f) % 24f;
        float day = Daylight;
        // the sun arcs east → south → west between 6:00 and 18:00; at night a dim bluish moon key light stays up for shadows
        float arc = (hour - 6f) / 12f;
        float elev = Mathf.Max(Mathf.Sin(arc * Mathf.PI) * 55f, 12f);
        sun.transform.rotation = Quaternion.Euler(elev, 90f + arc * 180f, 0f);
        sun.color = Color.Lerp(Moon, sunColor, day);
        sun.intensity = Mathf.Lerp(0.3f, sunIntensity, day);
        float amb = Mathf.Lerp(0.45f, 1f, day); // night stays playable, not pitch black
        RenderSettings.ambientSkyColor = ambSky * amb; RenderSettings.ambientEquatorColor = ambEq * amb; RenderSettings.ambientGroundColor = ambGround * amb;
        RenderSettings.fogColor = fog * Mathf.Lerp(0.3f, 1f, day);
        if (sky) sky.SetFloat("_Exposure", skyExposure * Mathf.Lerp(0.2f, 1f, day));
        Gfx.SetNightWindows((1f - day) * 1.6f);
    }
}
