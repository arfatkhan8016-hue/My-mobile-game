using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Put in Assets/Editor/. Runs automatically in the cloud build:
///  - Google Mobile Ads needs Android min API 24 and target API 35
///  - writes the AdMob App ID (from AdsConfig.cs) into the plugin's settings, so no Unity menu is needed
/// Does nothing about the App ID if the plugin is not installed.
/// </summary>
[InitializeOnLoad]
public static class AdMobBuildSetup
{
    static AdMobBuildSetup()
    {
        try { Apply(); }
        catch (Exception) { EditorApplication.delayCall += SafeApply; }
    }

    private static void SafeApply()
    {
        try { Apply(); }
        catch (Exception e) { Debug.LogWarning("AdMobBuildSetup: " + e.Message); }
    }

    private static void Apply()
    {
        try { PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24; }
        catch (Exception e) { Debug.LogWarning("AdMobBuildSetup: min SDK: " + e.Message); }
        try { PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35; }
        catch (Exception e) { Debug.LogWarning("AdMobBuildSetup: target SDK: " + e.Message); }

        Type t = FindType("GoogleMobileAds.Editor.GoogleMobileAdsSettings");
        if (t == null) return;   // plugin not installed (yet)

        const BindingFlags all = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object settings = null;
        MethodInfo load = t.GetMethod("LoadInstance", all);
        if (load != null) settings = load.Invoke(null, null);
        if (settings == null)
        {
            PropertyInfo p = t.GetProperty("Instance", all);
            if (p != null) settings = p.GetValue(null, null);
        }
        if (settings == null) { Debug.LogWarning("AdMobBuildSetup: could not load GoogleMobileAdsSettings"); return; }

        bool changed = false;
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            string n = f.Name.ToLowerInvariant();
            if (f.FieldType == typeof(string) && n.Contains("android") && n.Contains("appid"))
            {
                if ((string)f.GetValue(settings) != AdsConfig.AndroidAppId)
                {
                    f.SetValue(settings, AdsConfig.AndroidAppId);
                    changed = true;
                }
            }
            else if (f.FieldType == typeof(bool) && n.Contains("admob") && n.Contains("enabled"))
            {
                if (!(bool)f.GetValue(settings)) { f.SetValue(settings, true); changed = true; }
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty((UnityEngine.Object)settings);
            AssetDatabase.SaveAssets();
            Debug.Log("AdMobBuildSetup: AdMob App ID written to the plugin settings.");
        }
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
