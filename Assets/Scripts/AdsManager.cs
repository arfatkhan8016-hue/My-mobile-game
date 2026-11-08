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
