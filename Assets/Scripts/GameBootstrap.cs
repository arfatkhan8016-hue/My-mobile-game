using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Builds a complete playable test scene at runtime (no manual scene editing needed):
/// ground, walls, targets, capsule player, TPS camera, HUD (joystick, look pad, buttons), weapon.
/// Put in Assets/Scripts/. Skips itself if a TPSCharacterController already exists in the scene.
/// Player layer = 8 (unnamed user layer, so no TagManager edits are needed).
/// </summary>
public static class GameBootstrap
{
    private const int PlayerLayer = 8;
    private static Material baseMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Object.FindObjectOfType<TPSCharacterController>() != null) return;

        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        BuildWorld();
        WeaponSystem weapon = BuildPlayerAndWeapon();
        BuildHud(weapon);
    }

    // ------------------------------------------------------------------ World
    private static void BuildWorld()
