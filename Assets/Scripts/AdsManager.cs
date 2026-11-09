using UnityEngine;
using GoogleMobileAds.Api;

/// <summary>
/// Full-screen (interstitial) ads from Google AdMob, shown only at natural breaks:
///  - after every 2nd death (when the player respawns)
///  - when the player taps MENU to go back to the lobby
/// Needs the Google Mobile Ads plugin (Packages/manifest.json). No csc.rsp / define symbol is needed any more.
/// While AdsConfig.ShowDebugStatus is true, a yellow status line at the bottom-left tells what the ads are doing.
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
    public static void DebugShowNow() { if (Instance != null) Instance.ForceShow(); }

    private volatile string status = "starting";
    private int deaths;
    private GUIStyle style;

    private void OnGUI()
    {
        if (!AdsConfig.ShowDebugStatus) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = Mathf.Max(14, Screen.height / 32);
            style.normal.textColor = Color.yellow;
        }
        GUI.Label(new Rect(12f, Screen.height * 0.92f, Screen.width - 24f, Screen.height * 0.08f), "ADS: " + status + "  |  deaths " + deaths, style);
    }

    private InterstitialAd interstitial;
    private volatile bool sdkReady;       // set from plugin callbacks (background thread)
    private volatile bool adClosed;
    private volatile bool loadFailed;
    private bool needLoad;
    private bool adIsShowing;
    private float lastShown = -9999f;
    private float retryAt = -1f;
    private bool adDueAtRespawn;

    private void Awake()
    {
        Instance = this;
        status = "initializing SDK";
        MobileAds.Initialize(initStatus => { sdkReady = true; status = "SDK ready, loading ad"; });
    }

    private void Update()
    {
        if (sdkReady) { sdkReady = false; needLoad = true; }

        if (adClosed)
        {
            adClosed = false;
            EndAdPause();
            status = "ad closed, loading next";
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
        status = "loading ad...";

        InterstitialAd.Load(AdsConfig.InterstitialUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                status = "LOAD FAILED: " + error + " (retry in 30s)";
                Debug.LogWarning("AdsManager: interstitial failed to load: " + error);
                loadFailed = true;
                return;
            }
            interstitial = ad;
            status = "ad READY";
            ad.OnAdFullScreenContentClosed += () => { adClosed = true; };
            ad.OnAdFullScreenContentFailed += (AdError e) => { status = "SHOW FAILED: " + e; adClosed = true; };
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
        BeginShow();
        return true;
    }

    private void ForceShow()
    {
        if (adIsShowing) return;
        if (interstitial == null || !interstitial.CanShowAd()) { status = "TEST AD: not ready yet"; return; }
        BeginShow();
    }

    private void BeginShow()
    {
        lastShown = Time.realtimeSinceStartup;
        adIsShowing = true;
        status = "showing ad";
        Time.timeScale = 0f;          // pause the game while the ad is on screen
        AudioListener.pause = true;
        interstitial.Show();
    }

    private void EndAdPause()
    {
        adIsShowing = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }
}
