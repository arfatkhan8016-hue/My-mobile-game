using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Put in Assets/Editor/. Runs at the START of every Android build, BEFORE the Google Mobile Ads plugin checks its settings:
///  - writes the AdMob App ID (from AdsConfig.cs) into GoogleMobileAdsSettings.asset
///    (without it the app crashes the moment it opens)
///  - sets Android min API 24 / target API 35 (needed by the plugin)
/// Does nothing about the App ID if the plugin is not installed.
/// </summary>
public class AdMobBuildSetup : IPreprocessBuildWithReport
{
    // Lower than the plugin's own pre-processor, so we run first.
    public int callbackOrder { get { return -10000; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;

        try { PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24; }
        catch (Exception e) { Debug.LogWarning("AdMobBuildSetup: min SDK: " + e.Message); }
        try { PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35; }
        catch (Exception e) { Debug.LogWarning("AdMobBuildSetup: target SDK: " + e.Message); }

        Type t = FindType("GoogleMobileAds.Editor.GoogleMobileAdsSettings");
        if (t == null)
        {
            Debug.Log("AdMobBuildSetup: Google Mobile Ads plugin not found, skipping App ID.");
            return;
        }

        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object settings = null;
        MethodInfo load = t.GetMethod("LoadInstance", statics);
        if (load != null) settings = load.Invoke(null, null);
        if (settings == null) throw new BuildFailedException("AdMobBuildSetup: could not load GoogleMobileAdsSettings.");

        string id = AdsConfig.AndroidAppId;
        bool done = false;

        PropertyInfo prop = t.GetProperty("GoogleMobileAdsAndroidAppId", members);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(settings, id, null);
            done = true;
        }
        else
        {
            foreach (FieldInfo f in t.GetFields(members))
            {
                string n = f.Name.ToLowerInvariant();
                if (f.FieldType == typeof(string) && n.Contains("android") && n.Contains("appid"))
                {
                    f.SetValue(settings, id);
                    done = true;
                    break;
                }
            }
        }

        if (!done) throw new BuildFailedException("AdMobBuildSetup: could not find the Android App ID setting in the plugin.");

        EditorUtility.SetDirty((UnityEngine.Object)settings);
        AssetDatabase.SaveAssets();
        Debug.Log("AdMobBuildSetup: Android App ID = " + id);
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type x = a.GetType(fullName, false);
            if (x != null) return x;
        }
        return null;
    }
}
