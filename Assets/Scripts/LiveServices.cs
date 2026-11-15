using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using Unity.Services.RemoteConfig;
using UnityEngine;

// Live floor: anonymous identity + one Cloud Save slot ("progress") + one Remote Config kill switch
// ("liveops_enabled", default true). Client-authoritative on purpose; never blocks gameplay.
// Progress persists on this device while the anonymous sign-in cache survives.
public class LiveServices : MonoBehaviour
{
    public static LiveServices I { get; private set; }
    public bool SignedIn { get; private set; }
    public string PlayerId { get; private set; } = "";
    public bool LiveOpsEnabled { get; private set; } = true;

    const string SaveKey = "progress";
    const float RefetchSeconds = 300f;
    float refetchTimer;

    struct UserAttributes { }
    struct AppAttributes { }

    [Serializable]
    public class Progress
    {
        public List<string> found = new();
        public float playTime;
        public int money = -1; // -1 = old save without money
        public List<string> repDistrict = new(); public List<int> repScore = new(); // lists, not a dictionary: the local JsonUtility fallback can't store one
    }

    void Awake() { I = this; }

    public async Task<Progress> InitAndLoadAsync()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            SignedIn = AuthenticationService.Instance.IsSignedIn;
            PlayerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"[Live] Signed in anonymously, PlayerId={PlayerId}");
            await FetchFlagAsync();
            if (!LiveOpsEnabled) return LoadLocal();

            var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SaveKey });
            if (data.TryGetValue(SaveKey, out var item))
            {
                var p = item.Value.GetAs<Progress>();
                Debug.Log($"[Live] Loaded progress: {p.found.Count} landmarks");
                return p;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Live] Services unavailable, playing offline: {e.Message}");
        }
        return LoadLocal();
    }

    async Task FetchFlagAsync()
    {
        try
        {
            await RemoteConfigService.Instance.FetchConfigsAsync(new UserAttributes(), new AppAttributes());
            LiveOpsEnabled = RemoteConfigService.Instance.appConfig.GetBool("liveops_enabled", true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Live] Remote Config fetch failed, keeping liveops_enabled={LiveOpsEnabled}: {e.Message}");
        }
    }

    public async void Save(Progress p)
    {
        SaveLocal(p);
        if (!SignedIn || !LiveOpsEnabled) return;
        try
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { SaveKey, p } });
            Debug.Log($"[Live] Saved progress ({p.found.Count} landmarks)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Live] Cloud save failed (kept locally): {e.Message}");
        }
    }

    // offline cache
    static void SaveLocal(Progress p) { PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(p)); PlayerPrefs.Save(); }
    static Progress LoadLocal()
    {
        var s = PlayerPrefs.GetString(SaveKey, "");
        return string.IsNullOrEmpty(s) ? new Progress() : JsonUtility.FromJson<Progress>(s);
    }

    void Update()
    {
        if (!SignedIn) return;
        refetchTimer += Time.unscaledDeltaTime;
        if (refetchTimer > RefetchSeconds) { refetchTimer = 0; _ = FetchFlagAsync(); }
    }

    void OnApplicationFocus(bool focus)
    {
        if (focus && SignedIn) _ = FetchFlagAsync();
    }
}
