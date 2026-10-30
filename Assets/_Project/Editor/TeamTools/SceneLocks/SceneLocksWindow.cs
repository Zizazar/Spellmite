using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    /// <summary>Window listing all project scenes with their locks, plus setup diagnostics.</summary>
    public class SceneLocksWindow : EditorWindow
    {
        Vector2 scroll;
        bool showSetup;
        List<string> scenes = new();

        [MenuItem("Tools/Team/Scene Locks", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<SceneLocksWindow>();
            window.titleContent = new GUIContent("Scene Locks", EditorGUIUtility.IconContent("P4_LockedLocal").image);
            window.minSize = new Vector2(420, 200);
        }

        [MenuItem("Tools/Team/Refresh Lock Status", priority = 1)]
        static void RefreshMenu() => SceneLockService.Refresh();

        void OnEnable()
        {
            SceneLockService.Changed += Repaint;
            EditorApplication.projectChanged += CollectScenes;
            CollectScenes();
            showSetup = !SceneLockService.IsReady;
        }

        void OnDisable()
        {
            SceneLockService.Changed -= Repaint;
            EditorApplication.projectChanged -= CollectScenes;
        }

        void CollectScenes()
        {
            scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();

            if (SceneLockService.LastError != null)
                EditorGUILayout.HelpBox("Server connection error: " + SceneLockService.LastError, MessageType.Error);

            if (!SceneLockService.IsReady && SceneLockService.Availability != LockAvailability.Checking)
                EditorGUILayout.HelpBox($"Locks aren't working: {SceneLockService.AvailabilityText}. " +
                                        "Open \"Setup & Diagnostics\" below.", MessageType.Warning);

            scroll = EditorGUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            DrawSceneList();
            EditorGUILayout.Space(10);
            showSetup = EditorGUILayout.BeginFoldoutHeaderGroup(showSetup, "Setup & Diagnostics");
            if (showSetup) SetupChecks.Draw();
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var status = SceneLockService.IsRefreshing
                    ? "Refreshing…"
                    : SceneLockService.IsReady
                        ? SceneLockService.LastSyncUtc == DateTime.MinValue
                            ? "No data"
                            : $"Updated at {SceneLockService.LastSyncUtc.ToLocalTime():HH:mm:ss}"
                        : SceneLockService.AvailabilityText;
                GUILayout.Label(status, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!SceneLockService.IsReady || !SceneLockService.MyLocks.Any()))
                {
                    if (GUILayout.Button("Unlock all mine", EditorStyles.toolbarButton))
                        foreach (var l in SceneLockService.MyLocks.ToList())
                            SceneLockService.Unlock(l.assetPath);
                }

                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh"),
                        EditorStyles.toolbarButton, GUILayout.Width(28)))
                {
                    CollectScenes();
                    SceneLockService.Refresh();
                }

                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("_Popup").image, "Preferences"),
                        EditorStyles.toolbarButton, GUILayout.Width(28)))
                    SettingsService.OpenUserPreferences("Preferences/Team Tools");
            }
        }

        void DrawSceneList()
        {
            // Project scenes plus locked files you don't have yet (teammates' new scenes).
            var rows = new List<string>(scenes);
            rows.AddRange(SceneLockService.AllLocks
                .Where(l => l.assetPath != null && !scenes.Contains(l.assetPath))
                .Select(l => l.assetPath));

            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("There are no scenes in the project.", MessageType.Info);
                return;
            }

            foreach (var path in rows) DrawRow(path);
        }

        static void DrawRow(string path)
        {
            var state = SceneLockService.GetState(path);
            var info = SceneLockService.GetLock(path);
            var pending = SceneLockService.IsPending(path);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(state is LockState.Mine or LockState.Theirs
                    ? SceneLockIcons.Icon(state)
                    : EditorGUIUtility.IconContent("SceneAsset Icon"), GUILayout.Width(20), GUILayout.Height(18));

                using (new EditorGUILayout.VerticalScope())
                {
                    if (GUILayout.Button(Path.GetFileNameWithoutExtension(path), EditorStyles.boldLabel))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));

                    var old = GUI.contentColor;
                    GUI.contentColor = state == LockState.Mine ? SceneLockIcons.MineColor
                        : state == LockState.Theirs ? SceneLockIcons.TheirsColor : old;
                    var desc = SceneLockIcons.Describe(path);
                    if (info != null) desc += $"  ·  {info.Since}";
                    GUILayout.Label(desc, EditorStyles.miniLabel);
                    GUI.contentColor = old;
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(pending || !SceneLockService.IsReady))
                {
                    switch (state)
                    {
                        case LockState.Free:
                            if (GUILayout.Button("Lock", GUILayout.Width(110))) LockWithFeedback(path);
                            break;
                        case LockState.Mine:
                            if (GUILayout.Button("Unlock", GUILayout.Width(110))) SceneLockService.Unlock(path);
                            break;
                        case LockState.Theirs:
                            if (GUILayout.Button(new GUIContent("Force unlock", "If the person forgot to unlock the scene"),
                                    GUILayout.Width(110))) SceneLockService.ForceUnlock(path);
                            break;
                    }
                }
            }
        }

        public static void LockWithFeedback(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            SceneLockService.Lock(path, (ok, message) =>
            {
                if (!ok) EditorUtility.DisplayDialog("Couldn't lock scene", message, "OK");
                else if (message != null) EditorUtility.DisplayDialog("Scene is out of date", message, "Got it");
                else if (SceneLockService.GetState(path) == LockState.Mine) TeamNotify.Info($"Scene \"{name}\" is now locked by you");
                else TeamNotify.Info($"\"{name}\" isn't in git yet, so no lock is needed");
            });
        }
    }
}
