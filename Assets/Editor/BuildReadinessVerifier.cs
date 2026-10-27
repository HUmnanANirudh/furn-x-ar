using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildReadinessVerifier
{
    public static void VerifyAll()
    {
        Debug.Log("=== [VERIFY] Starting Full Build Readiness Verification ===");

        var vuforiaConfig = Resources.Load("VuforiaConfiguration");
        Debug.Log("=== [VERIFY] VuforiaConfiguration loaded: " + (vuforiaConfig != null ? vuforiaConfig.GetType().FullName : "NULL"));
        if (vuforiaConfig == null)
        {
            Debug.LogError("=== [VERIFY] FAILED: VuforiaConfiguration missing! ===");
            EditorApplication.Exit(1);
            return;
        }

        int enabledCount = 0;
        int totalMissingScripts = 0;

        foreach (var sceneSetting in EditorBuildSettings.scenes)
        {
            if (!sceneSetting.enabled)
                continue;

            enabledCount++;
            if (!File.Exists(sceneSetting.path))
            {
                Debug.LogError("=== [VERIFY] Missing build scene file: " + sceneSetting.path);
                EditorApplication.Exit(1);
                return;
            }

            var scene = EditorSceneManager.OpenScene(sceneSetting.path, OpenSceneMode.Single);
            int sceneMissing = 0;
            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
                {
                    int cnt = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (cnt > 0)
                    {
                        sceneMissing += cnt;
                        Debug.LogWarning($"=== [VERIFY] Missing script ({cnt}) in {sceneSetting.path} -> GameObject '{t.gameObject.name}'");
                    }
                }
            }
            totalMissingScripts += sceneMissing;
        }

        // Leave 1-Menu.unity open as the active editor scene
        const string menuScene = "Assets/Scenes/App/marker less/1-Menu.unity";
        if (File.Exists(menuScene))
            EditorSceneManager.OpenScene(menuScene, OpenSceneMode.Single);

        Debug.Log($"=== [VERIFY] Checked {enabledCount} enabled build scenes. Total missing scripts: {totalMissingScripts} ===");
    }
}
