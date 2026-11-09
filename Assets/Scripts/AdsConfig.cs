/// <summary>
/// One place for all AdMob settings.
/// While developing keep UseTestAds = true (Google's official test ids, safe, never gets your account banned).
/// For the Play Store release: paste your real ids below and set UseTestAds = false.
/// </summary>
public static class AdsConfig
{
    public const bool UseTestAds = false;

    // Shows a small yellow status line at the bottom-left + a TEST AD button in the lobby. Turn off for the release.
    public const bool ShowDebugStatus = false;

    // ---- Your real ids from admob.google.com ----
    public const string RealAndroidAppId = "ca-app-pub-4388507441365820~3338865698";
    public const string RealInterstitialUnitId = "ca-app-pub-4388507441365820/9257231778";

    // ---- Google's official test ids ----
    public const string TestAndroidAppId = "ca-app-pub-3940256099942544~3347511713";
    public const string TestInterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";

    public static string AndroidAppId
    {
        get { return (UseTestAds || RealAndroidAppId.Length == 0) ? TestAndroidAppId : RealAndroidAppId; }
    }

    public static string InterstitialUnitId
    {
        get { return (UseTestAds || RealInterstitialUnitId.Length == 0) ? TestInterstitialUnitId : RealInterstitialUnitId; }
    }

    // Keep ads rare: players hate too many of them (and it is better for your store rating)
    public const float MinSecondsBetweenAds = 150f;   // at least 2.5 minutes between two ads
    public const int ShowAdEveryNthDeath = 2;         // ad after every 2nd death
}
