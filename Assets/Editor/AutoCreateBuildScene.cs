using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AutoCreateBuildScene
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    static AutoCreateBuildScene()
    {
        try { Ensure(); }
        catch (Exception) { EditorApplication.delayCall += Ensure; }
    }

    private static void Ensure()
    {
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            if (s.enabled && File.Exists(s.path)) return;

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TestCube";
            cube.transform.position = new Vector3(0f, 0.5f, 5f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("AutoCreateBuildScene: added " + ScenePath);
    }
}
