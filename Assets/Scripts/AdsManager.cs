using UnityEngine;
#if ENABLE_ADMOB
using GoogleMobileAds.Api;
#endif

/// <summary>
/// Full-screen (interstitial) ads from Google AdMob, shown only at natural breaks:
///  - after every 2nd death (when the player respawns)
///  - when the player taps MENU to go back to the lobby
/// Ads are OFF until the file Assets/csc.rsp exists with the line:  -define:ENABLE_ADMOB
/// (that keeps the project compiling even if the AdMob plugin is not installed yet).
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    public static void Create()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("AdsManager");
        DontDestroyOnLoad(go);
        go.AddComponent<AdsManager>();
    }

    public static void OnDeath() { if (Instance != null) Instance.HandleDeath(); }
    public static void OnRespawn() { if (Instance != null) Instance.HandleRespawn(); }
    public static void OnMenu() { if (Instance != null) Instance.TryShow(); }

#if ENABLE_ADMOB
    private InterstitialAd interstitial;
    private volatile bool sdkReady;       // set from plugin callbacks (background thread)
    private volatile bool adClosed;
    private volatile bool loadFailed;
    private bool needLoad;
    private bool adIsShowing;
    private float lastShown = -9999f;
    private float retryAt = -1f;
    private int deaths;
    private bool adDueAtRespawn;

    private void Awake()
    {
        Instance = this;
        MobileAds.Initialize(status => { sdkReady = true; });
    }

    private void Update()
    {
        if (sdkReady) { sdkReady = false; needLoad = true; }

        if (adClosed)
        {
            adClosed = false;
            EndAdPause();
            needLoad = true;
        }
        if (loadFailed)
        {
            loadFailed = false;
            retryAt = Time.realtimeSinceStartup + 30f;   // try again in 30 seconds
        }
        if (retryAt > 0f && Time.realtimeSinceStartup >= retryAt) { retryAt = -1f; needLoad = true; }

        if (needLoad)
        {
            needLoad = false;
            LoadAd();
        }
    }

    private void LoadAd()
    {
        if (interstitial != null)
        {
            interstitial.Destroy();
            interstitial = null;
        }

        InterstitialAd.Load(AdsConfig.InterstitialUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("AdsManager: interstitial failed to load: " + error);
                loadFailed = true;
                return;
            }
            interstitial = ad;
            ad.OnAdFullScreenContentClosed += () => { adClosed = true; };
            ad.OnAdFullScreenContentFailed += (AdError e) => { Debug.LogWarning("AdsManager: ad failed to show: " + e); adClosed = true; };
        });
    }

    private void HandleDeath()
    {
        deaths++;
        if (deaths % AdsConfig.ShowAdEveryNthDeath == 0) adDueAtRespawn = true;
    }

    private void HandleRespawn()
    {
        if (!adDueAtRespawn) return;
        if (TryShow()) adDueAtRespawn = false;
    }

    private bool TryShow()
    {
        if (adIsShowing || interstitial == null || !interstitial.CanShowAd()) return false;
        if (Time.realtimeSinceStartup - lastShown < AdsConfig.MinSecondsBetweenAds) return false;

        lastShown = Time.realtimeSinceStartup;
        adIsShowing = true;
        Time.timeScale = 0f;          // pause the game while the ad is on screen
        AudioListener.pause = true;
        interstitial.Show();
        return true;
    }

    private void EndAdPause()
    {
        adIsShowing = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }
#else
    private void Awake()
    {
        Instance = this;
        Debug.Log("AdsManager: ads are OFF. Add Assets/csc.rsp with the line -define:ENABLE_ADMOB to turn them on.");
    }

    private void HandleDeath() { }
    private void HandleRespawn() { }
    private bool TryShow() { return false; }
#endif
}
