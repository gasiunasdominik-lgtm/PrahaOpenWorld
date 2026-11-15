using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Builds every screen in code: gameplay HUD, title, pause/settings, finale banner, fades.
// All colors from Palette; text styled (bold, spaced, outlined) so the stock font reads as chosen.
public class Hud : MonoBehaviour
{
    public Canvas canvas;
    public RectTransform hudRoot, titleRoot, pauseRoot, endRoot;
    public TextMeshProUGUI districtText, landmarkText, speedText, speedUnit, promptText, toastText, hintText;
    public TextMeshProUGUI titleName, titleSub, titlePrompt, endTitle, endStats;
    public RectTransform minimapArrow, speedPanel, toastPanel;
    public RawImage minimap;
    public Image flash, fade;
    public Slider musicSlider, sfxSlider;
    public Button resumeBtn, restartBtn, quitBtn, endContinueBtn;
    public Image landmarkBar;

    string lastDistrict = "";
    Coroutine toastCo;
    float landmarkFill, landmarkTarget;

    TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

    public void Build(RenderTexture minimapRT)
    {
        var cgo = new GameObject("UI");
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)cgo.transform;

        // ---------------- gameplay HUD
        hudRoot = Rect("HUD", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var loc = Panel("Location", hudRoot, new Vector2(0, 1), new Vector2(40, -40), new Vector2(460, 112), Palette.UIDark, 0.78f);
        var bar = Img("Accent", loc, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(0, -16), Palette.Accent, 1f);
        bar.rectTransform.anchoredPosition = new Vector2(16, 0);
        districtText = Text("District", loc, "STARÉ MĚSTO", 38, Palette.UILight, TextAlignmentOptions.TopLeft);
        Place(districtText.rectTransform, new Vector2(0, 1), new Vector2(36, -14), new Vector2(410, 50));
        landmarkText = Text("Landmarks", loc, "PAMÁTKY 0/6", 20, Palette.Gold, TextAlignmentOptions.TopLeft);
        Place(landmarkText.rectTransform, new Vector2(0, 1), new Vector2(36, -64), new Vector2(240, 30));
        var barBg = Img("LandmarkBarBg", loc, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, 8), new Vector2(232, -76), Palette.Shadow, 1f);
        landmarkBar = Img("LandmarkBar", barBg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, Palette.Gold, 1f);
        landmarkBar.type = Image.Type.Filled;
        landmarkBar.fillMethod = Image.FillMethod.Horizontal;
        landmarkBar.fillAmount = 0;

        // minimap (bottom-left)
        var mapFrame = Panel("MinimapFrame", hudRoot, new Vector2(0, 0), new Vector2(40, 40), new Vector2(300, 300), Palette.UIDark, 0.9f);
        mapFrame.pivot = new Vector2(0, 0);
        minimap = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        minimap.transform.SetParent(mapFrame, false);
        minimap.texture = minimapRT;
        minimap.color = Color.Lerp(Color.white, Palette.Sandstone, 0.15f);
        Place(minimap.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 280));
        minimapArrow = Img("Arrow", mapFrame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 30), Vector2.zero, Palette.Accent, 1f, Gfx.ArrowSprite).rectTransform;
        var north = Text("N", mapFrame, "S", 20, Palette.UILight, TextAlignmentOptions.Center);
        Place(north.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(40, 30));

        // speedometer (bottom-right)
        speedPanel = Panel("Speed", hudRoot, new Vector2(1, 0), new Vector2(-40, 40), new Vector2(260, 130), Palette.UIDark, 0.78f);
        speedPanel.pivot = new Vector2(1, 0);
        speedText = Text("SpeedValue", speedPanel, "0", 64, Palette.UILight, TextAlignmentOptions.Right);
        Place(speedText.rectTransform, new Vector2(1, 0.5f), new Vector2(-96, 6), new Vector2(160, 80));
        speedUnit = Text("SpeedUnit", speedPanel, "KM/H", 20, Palette.Accent, TextAlignmentOptions.Right);
        Place(speedUnit.rectTransform, new Vector2(1, 0.5f), new Vector2(-22, -14), new Vector2(68, 30));
        speedPanel.gameObject.SetActive(false);

        // interaction prompt (bottom-center)
        promptText = Text("Prompt", hudRoot, "", 28, Palette.UILight, TextAlignmentOptions.Center);
        Place(promptText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(800, 50));

        // toast (center-top)
        toastPanel = Panel("Toast", hudRoot, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(760, 110), Palette.UIDark, 0.82f);
        toastText = Text("ToastText", toastPanel, "", 34, Palette.Gold, TextAlignmentOptions.Center);
        Place(toastText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 100));
        toastPanel.localScale = Vector3.zero;

        hintText = Text("Hint", hudRoot, "WASD POHYB  ·  SHIFT BĚH  ·  MEZERNÍK SKOK  ·  MYŠ KAMERA  ·  E AUTO  ·  ESC PAUZA", 18, Palette.UILight, TextAlignmentOptions.Center);
        Place(hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(60, 40), new Vector2(1100, 30));
        hintText.alpha = 0.75f;

        // ---------------- title
        titleRoot = Rect("Title", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var scrim = Img("Scrim", titleRoot, new Vector2(0, 0.42f), new Vector2(1, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Palette.UIDark, 0.62f, null);
        titleName = Text("Name", titleRoot, "STOVĚŽATÁ", 150, Palette.Accent, TextAlignmentOptions.Center);
        Place(titleName.rectTransform, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1600, 190));
        titleName.characterSpacing = 8;
        titleName.fontSharedMaterial = ShadowMat(titleName);
        titleSub = Text("Sub", titleRoot, "PRAHA  ·  OTEVŘENÝ SVĚT", 30, Palette.UILight, TextAlignmentOptions.Center);
        Place(titleSub.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 50));
        titleSub.characterSpacing = 14;
        titlePrompt = Text("Prompt", titleRoot, "STISKNI LIBOVOLNOU KLÁVESU", 24, Palette.UILight, TextAlignmentOptions.Center);
        Place(titlePrompt.rectTransform, new Vector2(0.5f, 0.25f), Vector2.zero, new Vector2(1000, 40));
        titlePrompt.characterSpacing = 18;

        // ---------------- pause
        pauseRoot = Rect("Pause", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img("Dim", pauseRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Palette.UIDark, 0.7f, null);
        var pp = Panel("PausePanel", pauseRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 620), Palette.Shadow, 0.95f);
        var pt = Text("PauseTitle", pp, "PAUZA", 56, Palette.Accent, TextAlignmentOptions.Center);
        Place(pt.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(500, 80));
        resumeBtn = MakeButton("Pokracovat", pp, "POKRAČOVAT", new Vector2(0, -160));
        restartBtn = MakeButton("Restart", pp, "ZAČÍT ZNOVU", new Vector2(0, -240));
        musicSlider = MakeSlider("Hudba", pp, "HUDBA", new Vector2(0, -340));
        sfxSlider = MakeSlider("Efekty", pp, "EFEKTY", new Vector2(0, -420));
        quitBtn = MakeButton("Konec", pp, "UKONČIT HRU", new Vector2(0, -520));
        pauseRoot.gameObject.SetActive(false);

        // ---------------- end (all landmarks found)
        endRoot = Rect("End", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img("Dim", endRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Palette.UIDark, 0.55f, null);
        endTitle = Text("EndTitle", endRoot, "PRAHA JE TVOJE", 110, Palette.Gold, TextAlignmentOptions.Center);
        Place(endTitle.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1600, 150));
        endTitle.fontSharedMaterial = ShadowMat(endTitle);
        endStats = Text("EndStats", endRoot, "", 30, Palette.UILight, TextAlignmentOptions.Center);
        Place(endStats.rectTransform, new Vector2(0.5f, 0.47f), Vector2.zero, new Vector2(1200, 100));
        endContinueBtn = MakeButton("Dal", endRoot, "JEZDIT DÁL", Vector2.zero);
        Place((RectTransform)endContinueBtn.transform, new Vector2(0.5f, 0.32f), Vector2.zero, new Vector2(420, 64));
        endRoot.gameObject.SetActive(false);

        // ---------------- overlays
        flash = Img("Flash", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Palette.Accent, 0f, null);
        flash.raycastTarget = false;
        fade = Img("Fade", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Palette.UIDark, 1f, null);
        fade.raycastTarget = false;
    }

    // ---------------------------------------------------------------- runtime updates
    void Update()
    {
        if (titlePrompt && titleRoot.gameObject.activeSelf)
            titlePrompt.alpha = Mathf.Lerp(0.5f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.6f));
        landmarkFill = Mathf.MoveTowards(landmarkFill, landmarkTarget, Time.unscaledDeltaTime / 0.3f * Mathf.Max(0.05f, Mathf.Abs(landmarkTarget - landmarkFill)));
        if (landmarkBar) landmarkBar.fillAmount = Ease.OutCubic(landmarkFill);
    }

    public void SetDistrict(string d)
    {
        if (d == lastDistrict) return;
        lastDistrict = d;
        districtText.text = d.ToUpperInvariant();
        StartCoroutine(Ease.Tween(0.25f, t => districtText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, t), Ease.OutBack));
    }

    public void SetLandmarks(int found, int total)
    {
        landmarkText.text = $"PAMÁTKY {found}/{total}";
        landmarkTarget = total > 0 ? found / (float)total : 0;
    }

    public void SetSpeed(bool show, float kmh)
    {
        if (speedPanel.gameObject.activeSelf != show) speedPanel.gameObject.SetActive(show);
        if (show) speedText.text = Mathf.RoundToInt(Mathf.Abs(kmh)).ToString();
    }

    public void SetMinimap(float playerYaw) => minimapArrow.localRotation = Quaternion.Euler(0, 0, -playerYaw);

    public void Toast(string msg, Color color, float hold = 2.2f)
    {
        if (toastCo != null) StopCoroutine(toastCo);
        toastCo = StartCoroutine(ToastCo(msg, color, hold));
    }

    IEnumerator ToastCo(string msg, Color color, float hold)
    {
        toastText.text = msg; toastText.color = color;
        yield return Ease.Tween(0.35f, t => toastPanel.localScale = Vector3.one * t, Ease.OutBack);
        yield return new WaitForSecondsRealtime(hold);
        yield return Ease.Tween(0.2f, t => toastPanel.localScale = Vector3.one * (1f - t), Ease.OutCubic);
    }

    public IEnumerator Flash()
    {
        yield return Ease.Tween(0.1f, t => flash.color = Palette.WithAlpha(Palette.Accent, 0.6f * (1f - t)), Ease.OutCubic);
    }

    public IEnumerator FadeTo(float a, float dur = 0.3f)
    {
        float from = fade.color.a;
        yield return Ease.Tween(dur, t => fade.color = Palette.WithAlpha(Palette.UIDark, Mathf.Lerp(from, a, t)), Ease.OutCubic);
    }

    // ---------------------------------------------------------------- builders
    RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        return rt;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y);
        if (anchor.x == 0.5f) rt.pivot = new Vector2(0.5f, rt.pivot.y);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    Image Img(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos, Color c, float alpha, Sprite sprite = null)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        var rt = img.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = pos;
        bool fullscreen = name is "Scrim" or "Dim" or "Flash" or "Fade";
        if (sprite != null) img.sprite = sprite;
        else if (!fullscreen) { img.sprite = Gfx.RoundedSprite; img.type = Image.Type.Sliced; }
        img.color = Palette.WithAlpha(c, alpha);
        img.raycastTarget = false;
        return img;
    }

    RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, float alpha)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Gfx.RoundedSprite; img.type = Image.Type.Sliced;
        img.color = Palette.WithAlpha(c, alpha);
        img.raycastTarget = false;
        Place(img.rectTransform, anchor, pos, size);
        return img.rectTransform;
    }

    TextMeshProUGUI Text(string name, Transform parent, string s, float size, Color c, TextAlignmentOptions align)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        if (Font) t.font = Font;
        t.text = s; t.fontSize = size; t.color = c; t.alignment = align;
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 3;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.18f;
        t.outlineColor = Palette.UIDark;
        t.raycastTarget = false;
        return t;
    }

    Material ShadowMat(TextMeshProUGUI t)
    {
        var m = new Material(t.fontSharedMaterial);
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", Palette.WithAlpha(Palette.UIDark, 0.85f));
        m.SetFloat("_UnderlayOffsetX", 0.6f);
        m.SetFloat("_UnderlayOffsetY", -0.6f);
        m.SetFloat("_UnderlaySoftness", 0.4f);
        m.SetFloat("_OutlineWidth", 0.12f);
        m.SetColor("_OutlineColor", Palette.UIDark);
        return m;
    }

    Button MakeButton(string name, Transform parent, string label, Vector2 pos)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Gfx.RoundedSprite; img.type = Image.Type.Sliced;
        img.color = Palette.UIDark;
        Place(img.rectTransform, new Vector2(0.5f, 1), pos, new Vector2(420, 64));
        var b = img.GetComponent<Button>();
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.Lerp(Color.white, Palette.Accent, 0.6f);
        cb.selectedColor = Color.Lerp(Color.white, Palette.Accent, 0.4f);
        cb.pressedColor = Palette.Accent;
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        var t = Text("Label", img.transform, label, 28, Palette.UILight, TextAlignmentOptions.Center);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        b.onClick.AddListener(() => GameAudio.I?.PlayUI(GameAudio.I.click, 0.8f));
        return b;
    }

    Slider MakeSlider(string name, Transform parent, string label, Vector2 pos)
    {
        var rt = Rect(name, parent, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(0.5f, 1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(420, 60);
        var t = Text("Label", rt, label, 22, Palette.UILight, TextAlignmentOptions.Left);
        Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(160, 30));
        var bg = Img("Track", rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(0, 8), Palette.UIDark, 1f);
        var fillArea = Rect("FillArea", rt, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
        fillArea.pivot = new Vector2(0.5f, 0); fillArea.sizeDelta = new Vector2(0, 12); fillArea.anchoredPosition = new Vector2(0, 8);
        var fill = Img("Fill", fillArea, Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, Palette.Accent, 1f);
        var handleArea = Rect("HandleArea", rt, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
        handleArea.pivot = new Vector2(0.5f, 0); handleArea.sizeDelta = new Vector2(-24, 12); handleArea.anchoredPosition = new Vector2(0, 8);
        var handle = Img("Handle", handleArea, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28, 28), Vector2.zero, Palette.UILight, 1f, Gfx.CircleSprite);
        handle.raycastTarget = true;
        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
        s.minValue = 0; s.maxValue = 1;
        bg.raycastTarget = true;
        return s;
    }
}
