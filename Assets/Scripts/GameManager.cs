using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public enum GameState { Title, Playing, Paused, Ended }

// Boots the whole game from one scene object: city, lighting, player, cars, camera, HUD, shell.
public class GameManager : MonoBehaviour
{
    public static GameManager I { get; private set; }
    public GameState State { get; private set; } = GameState.Title;

    public PlayerController player;
    public CarController currentCar;
    public readonly List<CarController> cars = new();
    public CameraRig rig;
    public Hud hud;
    public readonly HashSet<string> found = new();
    public float playTime;

    public static Vector3 SpawnPos => new(686f, MapBuilder.Height(686f, 80f) + 0.3f, 80f); // Old Town Square
    const float SpawnYaw = 270f;
    float dustTimer;
    bool transitioning;

    void Awake()
    {
        I = this;
        Time.timeScale = 1f;
        Gfx.Init();
        ApplyLighting();

        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        CityBuilder.Build();
        gameObject.AddComponent<GameClock>();
        new GameObject("Chodci").AddComponent<Pedestrians>();
        new GameObject("Audio").AddComponent<GameAudio>();
        new GameObject("Vfx").AddComponent<Vfx>();

        // player
        var p = new GameObject("Hrac") { tag = "Player" };
        p.transform.SetPositionAndRotation(SpawnPos, Quaternion.Euler(0, SpawnYaw, 0));
        var cc = p.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0, 0.92f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50f;
        CharacterMotion.BuildHero(p.transform);
        player = p.AddComponent<PlayerController>();
        player.camTarget = new GameObject("CamTarget").transform;
        player.camTarget.SetParent(p.transform, false);
        player.camTarget.localPosition = new Vector3(0, 1.55f, 0);
        player.OnToast = m => hud.Toast(m.ToUpperInvariant(), Palette.UILight, 1.6f);

        // cars: a hero car by the bridge, the rest parked around the city
        cars.Add(SpawnCar(new Vector3(78.2f, 0.25f, 34f), 0f, Palette.Accent, 0));
        int i = 0;
        foreach (var (pos, yaw) in CityBuilder.ParkingSpots)
        {
            if (i >= 34) break;
            cars.Add(SpawnCar(pos + Vector3.up * 0.05f, yaw, Palette.CarPaints[i % Palette.CarPaints.Length], i % 3));
            i++;
        }

        new GameObject("Doprava").AddComponent<Traffic>();
        new GameObject("MHD").AddComponent<Transit>();
        new GameObject("Zakon").AddComponent<Law>();
        new GameObject("Zakazky").AddComponent<Jobs>();
        new GameObject("Majetek").AddComponent<Property>();
        gameObject.AddComponent<Inspect>();
        new GameObject("Holubi").AddComponent<Pigeons>();

        rig = new GameObject("CameraRig").AddComponent<CameraRig>();
        rig.Setup();
        rig.FollowPlayer(player.camTarget);
        rig.orbit.HorizontalAxis.Value = SpawnYaw;

        hud = new GameObject("Hud").AddComponent<Hud>();
        hud.Build(rig.minimapRT);
        hud.SetLandmarks(0, CityBuilder.Landmarks.Length);
        hud.resumeBtn.onClick.AddListener(Resume);
        hud.restartBtn.onClick.AddListener(Restart);
        hud.quitBtn.onClick.AddListener(Quit);
        hud.endContinueBtn.onClick.AddListener(ContinueAfterEnd);
        hud.musicSlider.value = GameAudio.MusicGain;
        hud.sfxSlider.value = GameAudio.SfxGain;
        hud.musicSlider.onValueChanged.AddListener(v => GameAudio.MusicGain = v);
        hud.sfxSlider.onValueChanged.AddListener(v => GameAudio.SfxGain = v);

        live = new GameObject("LiveServices").AddComponent<LiveServices>();
        LoadProgress();

        EnterTitle();
    }

    LiveServices live;

    async void LoadProgress()
    {
        var p = await live.InitAndLoadAsync();
        if (this == null) return;
        loaded = true; // from now on saving cannot overwrite a save we have not read yet
        if (p == null) return;
        foreach (var n in p.found) found.Add(n);
        playTime = Mathf.Max(playTime, p.playTime);
        if (p.money >= 0) Jobs.I.money = p.money;
        if (p.repDistrict != null) for (int i = 0; i < p.repDistrict.Count && i < p.repScore.Count; i++) Rep.score[p.repDistrict[i]] = p.repScore[i];
        Property.I.Load(p.flat, p.garage, p.shops);
        if (State == GameState.Title && Property.I.Home(out var home)) player.transform.position = home; // you wake up at home
        hud.SetLandmarks(found.Count, CityBuilder.Landmarks.Length);
    }

    bool loaded;
    public void Save() => SaveProgress();
    void SaveProgress()
    {
        if (live == null || !loaded) return;
        live.Save(new LiveServices.Progress { found = new List<string>(found), playTime = playTime, money = Jobs.I ? Jobs.I.money : -1,
            repDistrict = new List<string>(Rep.score.Keys), repScore = new List<int>(Rep.score.Values),
            flat = Property.I ? Property.I.flat : -1, garage = Property.I ? Property.I.garage : -1,
            shops = Property.I ? new List<int>(Property.I.shops) : new List<int>() });
    }

    void OnApplicationQuit() => SaveProgress();

    CarController SpawnCar(Vector3 pos, float yaw, Color paint, int style)
    {
        var car = CarController.Build(pos, yaw, paint, style);
        car.OnCrash = imp =>
        {
            if (car != currentCar) return;
            rig.Shake(Mathf.Clamp(imp * 0.08f, 0.2f, 1.2f));
            Vfx.I.Emit(Vfx.I.sparks, car.transform.position + car.transform.forward * 2f + Vector3.up * 0.6f, 14, car.transform.forward);
        };
        return car;
    }

    void ApplyLighting()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) sun = new GameObject("Slunce").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Palette.SunKey;
        sun.intensity = 1.25f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(34f, 40f, 0f);
        RenderSettings.sun = sun;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(Palette.SkyTop, Palette.Shadow, 0.25f);
        RenderSettings.ambientEquatorColor = Color.Lerp(Palette.Horizon, Palette.Cobble, 0.45f);
        RenderSettings.ambientGroundColor = Palette.GroundBounce;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(Palette.Fog, Palette.SkyTop, 0.4f);
        RenderSettings.fogStartDistance = 300f;
        RenderSettings.fogEndDistance = 2300f;
    }

    // ---------------------------------------------------------------- shell
    void EnterTitle()
    {
        State = GameState.Title;
        rig.SetTitle(true);
        rig.InputEnabled = false;
        player.controlEnabled = false;
        hud.hudRoot.gameObject.SetActive(false);
        hud.titleRoot.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        StartCoroutine(hud.FadeTo(0f, 0.8f));
    }

    public void StartGame()
    {
        if (State != GameState.Title || transitioning) return;
        StartCoroutine(StartGameCo());
    }

    IEnumerator StartGameCo()
    {
        transitioning = true;
        GameAudio.I.PlayUI(GameAudio.I.click);
        yield return hud.FadeTo(1f, 0.3f);
        hud.titleRoot.gameObject.SetActive(false);
        hud.hudRoot.gameObject.SetActive(true);
        rig.SetTitle(false);
        yield return null;
        State = GameState.Playing;
        rig.InputEnabled = true;
        player.controlEnabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        yield return hud.FadeTo(0f, 0.3f);
        transitioning = false;
        StartCoroutine(HideHintLater());
        hud.Toast("VÍTEJ V PRAZE · [TAB] MOBIL · [Q] PROHLÉDNOUT", Palette.UILight, 3f);
    }

    IEnumerator HideHintLater()
    {
        yield return new WaitForSeconds(14f);
        yield return Ease.Tween(1f, t => hud.hintText.alpha = 0.75f * (1f - t), Ease.OutCubic, false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Time.timeScale = 0f;
        GameAudio.I.SetDuck(true);
        hud.pauseRoot.gameObject.SetActive(true);
        rig.InputEnabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        hud.resumeBtn.Select();
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Time.timeScale = 1f;
        GameAudio.I.SetDuck(false);
        hud.pauseRoot.gameObject.SetActive(false);
        rig.InputEnabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    public void Restart() => StartCoroutine(RestartCo());

    IEnumerator RestartCo()
    {
        // "Začít znovu" is a clean run: discoveries and the clock reset
        found.Clear(); playTime = 0f; Jobs.I.money = 500; Rep.score.Clear(); Property.I.Clear();
        SaveProgress();
        yield return hud.FadeTo(1f, 0.3f);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ContinueAfterEnd()
    {
        if (State != GameState.Ended) return;
        hud.endRoot.gameObject.SetActive(false);
        State = GameState.Playing;
        Time.timeScale = 1f;
        rig.InputEnabled = true;
        player.controlEnabled = currentCar == null;
        if (currentCar) currentCar.SetDriven(true);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    // ---------------------------------------------------------------- loop
    void Update()
    {
        var kb = Keyboard.current;
        switch (State)
        {
            case GameState.Title:
                if ((kb != null && kb.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
                    StartGame();
                return;
            case GameState.Paused:
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Resume();
                return;
            case GameState.Ended:
                return;
        }

        playTime += Time.deltaTime;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { Pause(); return; }

        Vector3 focus = currentCar ? currentCar.transform.position : player.transform.position;
        hud.SetDistrict(CityBuilder.District(focus));
        hud.clockText.text = GameClock.I.Clock;
        hud.SetMinimap(currentCar ? currentCar.transform.eulerAngles.y : player.transform.eulerAngles.y);
        hud.SetSpeed(currentCar != null, currentCar ? currentCar.SpeedKmh : 0f);

        if (Transit.I.Interact(kb, hud, this)) { CheckLandmarks(focus); UpdateDust(); return; }
        var near = currentCar ? null : NearestCar(4f);
        hud.promptText.text = currentCar ? "[E] VYSTOUPIT    [H] TROUBIT    [R] OTOČIT AUTO" : near ? "[E] NASTOUPIT" : "";
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            if (currentCar) ExitCar();
            else if (near) EnterCar(near);
        }

        CheckLandmarks(focus);
        UpdateDust();
    }

    CarController NearestCar(float maxDist)
    {
        CarController best = null; float bd = maxDist * maxDist;
        foreach (var c in cars)
        {
            if (!c.gameObject.activeInHierarchy) continue; // pooled traffic car
            float d = (c.transform.position - player.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    public void EnterCar(CarController car)
    {
        Law.I.OnEnterCar(car, car.ai);
        currentCar = car;
        car.SetDriven(true);
        player.controlEnabled = false;
        player.gameObject.SetActive(false);
        rig.FollowCar(car);
        GameAudio.I.PlayAt(GameAudio.I.door, car.transform.position, 0.9f);
        Vfx.I.Emit(Vfx.I.puff, car.transform.position + Vector3.up * 0.3f, 6);
    }

    public void ExitCar()
    {
        var car = currentCar;
        currentCar = null;
        car.SetDriven(false);
        var exit = car.seat.position;
        exit.y = Mathf.Max(car.transform.position.y, 0f) + 0.1f;
        player.gameObject.SetActive(true);
        player.Teleport(exit, car.transform.eulerAngles.y);
        player.controlEnabled = true;
        rig.FollowPlayer(player.camTarget);
        GameAudio.I.PlayAt(GameAudio.I.door, car.transform.position, 0.9f);
    }

    void UpdateDust()
    {
        if (!currentCar) return;
        dustTimer -= Time.deltaTime;
        if (dustTimer > 0) return;
        bool drifting = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        float n = currentCar.NormSpeed;
        if (n < 0.35f && !(drifting && n > 0.1f)) return;
        dustTimer = drifting ? 0.03f : 0.08f;
        var t = currentCar.transform;
        foreach (float s in new[] { -0.8f, 0.8f })
            Vfx.I.Emit(Vfx.I.dust, t.position + t.right * s - t.forward * 1.4f + Vector3.up * 0.15f, drifting ? 3 : 1, -t.forward + Vector3.up * 0.4f);
    }

    void CheckLandmarks(Vector3 p)
    {
        foreach (var l in CityBuilder.Landmarks)
        {
            if (found.Contains(l.name)) continue;
            var d = p - l.pos; d.y = 0;
            if (d.magnitude < l.radius && p.y >= l.minY) Discover(l.name, p);
        }
    }

    public void Discover(string name, Vector3 at)
    {
        if (!found.Add(name)) return;
        StartCoroutine(hud.Flash());
        GameAudio.I.PlayUI(GameAudio.I.sting, 0.9f);
        Vfx.I.Emit(Vfx.I.confetti, at + Vector3.up * 1.5f, 45);
        hud.Toast("OBJEVENO: " + name.ToUpperInvariant(), Palette.Gold);
        hud.SetLandmarks(found.Count, CityBuilder.Landmarks.Length);
        Debug.Log($"[Praha] Discovered {name} ({found.Count}/{CityBuilder.Landmarks.Length})");
        SaveProgress();
        if (found.Count == CityBuilder.Landmarks.Length) StartCoroutine(EndCo());
    }

    IEnumerator EndCo()
    {
        yield return new WaitForSeconds(2.8f);
        State = GameState.Ended;
        if (currentCar) currentCar.SetDriven(false);
        player.controlEnabled = false;
        rig.InputEnabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        int min = Mathf.FloorToInt(playTime / 60f), sec = Mathf.FloorToInt(playTime % 60f);
        hud.endStats.text = $"VŠECH {CityBuilder.Landmarks.Length} PAMÁTEK OBJEVENO ZA {min}:{sec:00}";
        hud.endRoot.gameObject.SetActive(true);
        StartCoroutine(hud.Flash());
        GameAudio.I.PlayUI(GameAudio.I.sting, 1f);
        yield return Ease.Tween(0.35f, t => hud.endTitle.rectTransform.localScale = Vector3.one * t, Ease.OutBack);
    }

    // ---------------------------------------------------------------- debug hooks (for automated verification)
    public void DebugTeleport(Vector3 p, float yaw)
    {
        if (currentCar) ExitCar();
        player.Teleport(p, yaw);
    }
}
