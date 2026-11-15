using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Full-screen city map on M: a top-down orthographic view of the real city (its own camera, rendering only while
// open), with the player's position and heading, the landmarks and the district names. Wheel zooms, drag or WASD pans.
// The game holds still while the map is open (time scale 0), as in most open-world games.
public class BigMap : MonoBehaviour
{
    const int Res = 1024;
    Camera cam; RenderTexture rt; GameObject root; RectTransform view, player;
    readonly List<(RectTransform rt, Vector3 world)> labels = new();
    Vector3 center; float size = 900f; bool open;
    public static bool IsOpen;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { IsOpen = false; }

    void Start()
    {
        rt = new RenderTexture(Res, Res, 24) { name = "BigMapRT" };
        var cgo = new GameObject("MapaCam"); cgo.transform.SetParent(transform, false);
        cam = cgo.AddComponent<Camera>();
        cam.orthographic = true; cam.nearClipPlane = 1f; cam.farClipPlane = 1500f; cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Palette.UIDark; cam.enabled = false;
        cgo.transform.rotation = Quaternion.Euler(90, 0, 0);

        var canvas = new GameObject("Mapa", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
        var sc = canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
        root = canvas.gameObject; root.transform.SetParent(transform, false);
        var bg = new GameObject("Pozadi", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(root.transform, false); bg.color = new Color(0, 0, 0, 0.82f); Stretch(bg.rectTransform);
        var img = new GameObject("Pohled", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        img.transform.SetParent(root.transform, false); img.texture = rt;
        view = img.rectTransform; view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f); view.sizeDelta = new Vector2(1000, 1000);

        foreach (var l in CityBuilder.Landmarks) AddLabel("★ " + l.name, l.pos, 22, Palette.Butter);
        if (MapBuilder.D?.districts != null)
            foreach (var d in MapBuilder.D.districts)
            {
                if (d.p == null || d.p.Length < 6 || string.IsNullOrEmpty(d.n)) continue;
                float x = 0, z = 0; int n = d.p.Length / 2; for (int i = 0; i < n; i++) { x += d.p[2 * i]; z += d.p[2 * i + 1]; }
                AddLabel(d.n.ToUpperInvariant(), new Vector3(x / n, 0, z / n), 17, new Color(1, 1, 1, 0.75f));
            }
        player = new GameObject("Hrac", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        player.SetParent(view, false); player.sizeDelta = new Vector2(34, 34);
        var pi = player.GetComponent<Image>(); pi.sprite = Gfx.ArrowSprite; pi.color = Palette.Accent;
        var help = Label("M / Esc zavřít  ·  kolečko přiblížit  ·  táhni myší nebo WASD posunout", 18, new Color(1, 1, 1, 0.8f));
        help.rectTransform.SetParent(root.transform, false);
        help.rectTransform.anchorMin = help.rectTransform.anchorMax = new Vector2(0.5f, 0f); help.rectTransform.anchoredPosition = new Vector2(0, 30);
        root.SetActive(false);
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    TextMeshProUGUI Label(string s, float sizePx, Color c)
    {
        var t = new GameObject("Popisek", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset) t.font = TMP_Settings.defaultFontAsset;
        t.text = s; t.fontSize = sizePx; t.color = c; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 200);
        t.rectTransform.sizeDelta = new Vector2(600, 40);
        return t;
    }

    void AddLabel(string s, Vector3 world, float px, Color c)
    {
        var t = Label(s, px, c); t.rectTransform.SetParent(view, false);
        labels.Add((t.rectTransform, world));
    }

    void Toggle(bool on)
    {
        var gm = GameManager.I;
        if (on && (gm == null || gm.State != GameState.Playing)) return;
        open = IsOpen = on; root.SetActive(on); cam.enabled = on;
        Time.timeScale = on ? 0f : 1f;
        if (on)
        {
            var p = gm.currentCar ? gm.currentCar.transform.position : gm.player.transform.position;
            center = new Vector3(p.x, 0, p.z); size = 600f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    void Update()
    {
        var kb = Keyboard.current; var mouse = Mouse.current;
        if (kb == null) return;
        if (kb.mKey.wasPressedThisFrame) Toggle(!open);
        else if (open && kb.escapeKey.wasPressedThisFrame) Toggle(false);
        if (!open) return;

        float dt = Time.unscaledDeltaTime;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) size = Mathf.Clamp(size * (wheel > 0 ? 0.85f : 1.18f), 80f, 2200f);
            if (mouse.leftButton.isPressed)
            {
                var d = mouse.delta.ReadValue(); float k = 2f * size / view.rect.height * (1080f / Screen.height);
                center -= new Vector3(d.x, 0, d.y) * k;
            }
        }
        var pan = new Vector3((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), 0, (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
        center += pan * size * 1.2f * dt;
        // the view square fits the screen's height
        float side = 1080f * 0.92f; view.sizeDelta = new Vector2(side, side);
        cam.orthographicSize = size;
        cam.transform.position = new Vector3(center.x, 700f, center.z);

        Vector2 ToMap(Vector3 w) => new Vector2(w.x - center.x, w.z - center.z) / size * (side / 2f);
        foreach (var (r, w) in labels)
        {
            var m = ToMap(w); bool inside = Mathf.Abs(m.x) < side / 2 && Mathf.Abs(m.y) < side / 2;
            r.gameObject.SetActive(inside); r.anchoredPosition = m;
        }
        var gm = GameManager.I;
        var me = gm.currentCar ? gm.currentCar.transform : gm.player.transform;
        player.anchoredPosition = ToMap(me.position); player.localRotation = Quaternion.Euler(0, 0, -me.eulerAngles.y);
    }
}
