using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Vuforia.EditorClasses
{
    /// <summary>
    /// Workaround for Unity 2022.3 Linux/Wayland IMGUI bug in AndroidBuildWindowExtension.DoTargetListUnsafe:
    /// 1. DoTargetListUnsafe calls SpawnFetchTargetsThread() inside EventType.Layout when m_TargetListState == 0,
    ///    which races with the ADB worker thread setting m_TargetListState = 2 before EventType.Repaint finishes
    ///    in the same frame (1 LabelField control in Layout vs Popup + "Refresh" Button in Repaint).
    /// 2. RepaintTargetList calls BuildPlayerWindow.Repaint() every frame while m_TargetListState == 1 (Fetching).
    /// </summary>
    [InitializeOnLoad]
    public static class AndroidBuildWindowFix
    {
        static object s_AndroidExtension;
        static object s_Guard;
        static FieldInfo s_StateField;
        static MethodInfo s_SpawnMethod;
        static EditorApplication.CallbackFunction s_BuggyRepaintDelegate;
        static MethodInfo s_GetBuildWindowMethod;
        static int s_LastLayoutState = -1;
        static bool s_DeferredPopulatedRestore;
        static bool s_WasFetching;
        static bool s_Initialized;

        static AndroidBuildWindowFix()
        {
            EditorApplication.update += OnEditorUpdate;
            RegisterBeforeEventHook();
            SyncTargetListState(allowShortWait: true);
        }

        static void RegisterBeforeEventHook()
        {
            try
            {
                var beforeEventField = typeof(GUIUtility).GetField(
                    "beforeEventProcessed",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (beforeEventField == null)
                    return;

                var existing = beforeEventField.GetValue(null) as Action<EventType, KeyCode>;
                Action<EventType, KeyCode> handler = OnBeforeGUIEvent;
                beforeEventField.SetValue(null, (Action<EventType, KeyCode>)Delegate.Combine(existing, handler));
            }
            catch
            {
                // Safe fallback to EditorApplication.update if internal GUIUtility signature differs
            }
        }

        static void OnBeforeGUIEvent(EventType eventType, KeyCode keyCode)
        {
            if (!EnsureCached())
                return;

            try
            {
                if (eventType == EventType.Layout)
                {
                    SyncTargetListState(allowShortWait: true);
                    s_LastLayoutState = ReadState();
                }
                else if (eventType == EventType.Repaint && s_LastLayoutState == 1)
                {
                    // Guarantee Repaint sees the exact same control count as Layout even if the
                    // background ADB thread finished in the sub-millisecond gap between Layout and Repaint.
                    lock (s_Guard)
                    {
                        int current = Convert.ToInt32(s_StateField.GetValue(s_AndroidExtension));
                        if (current == 2)
                        {
                            s_StateField.SetValue(s_AndroidExtension, Enum.ToObject(s_StateField.FieldType, 1));
                            s_DeferredPopulatedRestore = true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection discrepancies across Unity versions
            }
        }

        static void OnEditorUpdate()
        {
            if (s_DeferredPopulatedRestore && EnsureCached())
            {
                s_DeferredPopulatedRestore = false;
                lock (s_Guard)
                {
                    s_StateField.SetValue(s_AndroidExtension, Enum.ToObject(s_StateField.FieldType, 2));
                }
                s_WasFetching = true;
            }

            SyncTargetListState(allowShortWait: true);
        }

        static bool EnsureCached()
        {
            if (s_Initialized)
                return s_AndroidExtension != null;

            s_Initialized = true;
            try
            {
                var moduleManager = typeof(Editor).Assembly.GetType("UnityEditor.Modules.ModuleManager");
                var getTargetStr = moduleManager?.GetMethod(
                    "GetTargetStringFromBuildTarget",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var getExt = moduleManager?.GetMethod(
                    "GetBuildWindowExtension",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                var androidStr = getTargetStr?.Invoke(null, new object[] { BuildTarget.Android }) as string;
                if (string.IsNullOrEmpty(androidStr))
                    return false;

                s_AndroidExtension = getExt?.Invoke(null, new object[] { androidStr });
                if (s_AndroidExtension == null)
                    return false;

                var extType = s_AndroidExtension.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                s_StateField = extType.GetField("m_TargetListState", flags);
                var guardField = extType.GetField("k_TargetListGuard", flags);
                s_Guard = guardField?.GetValue(s_AndroidExtension);
                s_SpawnMethod = extType.GetMethod("SpawnFetchTargetsThread", flags);
                s_GetBuildWindowMethod = extType.GetMethod("GetBuildPlayerWindow", flags);

                var repaintMethod = extType.GetMethod("RepaintTargetList", flags);
                if (repaintMethod != null)
                {
                    s_BuggyRepaintDelegate = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                        typeof(EditorApplication.CallbackFunction),
                        s_AndroidExtension,
                        repaintMethod,
                        false);
                }

                return s_StateField != null && s_Guard != null;
            }
            catch
            {
                s_AndroidExtension = null;
                return false;
            }
        }

        static int ReadState()
        {
            lock (s_Guard)
            {
                return Convert.ToInt32(s_StateField.GetValue(s_AndroidExtension));
            }
        }

        static void StripBuggyRepaintCallback()
        {
            if (s_BuggyRepaintDelegate != null)
                EditorApplication.update -= s_BuggyRepaintDelegate;
        }

        static void SyncTargetListState(bool allowShortWait)
        {
            if (!EnsureCached())
                return;

            try
            {
                int state = ReadState();

                // Fast steady-state path once ADB device list is Populated (2)
                if (state == 2 && !s_WasFetching)
                    return;

                // Pre-warm ADB device scan outside BuildPlayerWindow.OnGUI(Layout)
                if (state == 0 && s_SpawnMethod != null)
                {
                    s_SpawnMethod.Invoke(s_AndroidExtension, null);
                    StripBuggyRepaintCallback();
                    state = ReadState();

                    // Local ADB daemon responds in ~0.3-30ms; waiting briefly outside k_TargetListGuard
                    // lets state reach Populated (2) cleanly before Layout starts.
                    if (allowShortWait && state == 1)
                    {
                        var sw = Stopwatch.StartNew();
                        while (state == 1 && sw.ElapsedMilliseconds < 250)
                        {
                            Thread.Sleep(1);
                            state = ReadState();
                        }
                    }
                }

                if (state == 1)
                {
                    s_WasFetching = true;
                    StripBuggyRepaintCallback();
                }
                else if (s_WasFetching)
                {
                    s_WasFetching = false;
                    StripBuggyRepaintCallback();

                    var win = s_GetBuildWindowMethod?.Invoke(null, null) as EditorWindow;
                    if (win != null)
                        win.Repaint();
                }
            }
            catch
            {
                // Ignore reflection discrepancies across Unity versions
            }
        }

        [MenuItem("Build/Build Android APK...", priority = 100)]
        public static void BuildAndroidApk()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && File.Exists(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Build Android APK", "No enabled scenes found in Build Settings.", "OK");
                return;
            }

            var outputPath = EditorUtility.SaveFilePanel("Save Android APK", "", "FurnX-AR.apk", "apk");
            if (string.IsNullOrEmpty(outputPath))
                return;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Android APK built successfully: {outputPath} ({report.summary.totalSize / (1024 * 1024f):F1} MB)");
                EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                Debug.LogError($"Android APK build ended with status: {report.summary.result}");
            }
        }
    }
}
