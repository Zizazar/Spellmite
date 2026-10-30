using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    public partial class ItchPublisherWindow
    {
        readonly HashSet<string> expandedHistory = new();

        const string CliExample =
            "Unity -batchmode -quit -projectPath . -executeMethod GameJam.ItchPublisher.ItchPublisherCli.BuildAndPublish " +
            "-itchChannels windows,html5 -itchBump patch";

        void DrawSettings()
        {
            InitStyles();
            EditorGUIUtility.labelWidth = 200;

            Section("butler");
            GUILayout.Label("butler is itch.io's official command-line upload tool.", wrapMini);
            var path = Butler.Locate(out var source);
            if (path == null)
            {
                EditorGUILayout.HelpBox("butler wasn't found. Install it here (downloads from itch.io's server, " +
                                        "~25 MB, into your user folder) or set a custom path below.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Using", path);
                EditorGUILayout.LabelField("Found via", source);
                EditorGUILayout.LabelField("Version", ItchRemote.ButlerVersion ?? "checking…");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                using (new EditorGUI.DisabledScope(Butler.IsInstalling || Running))
                {
                    if (GUILayout.Button(Butler.IsInstalling ? "Installing…" : path == null ? "Install butler" : "Install / update butler",
                            GUILayout.Width(160)))
                    {
                        Butler.Install((ok, message) =>
                        {
                            if (ok)
                            {
                                ItchRemote.CheckButlerVersion(true);
                                ShowNotification(new GUIContent("butler installed"), 3);
                            }
                            else EditorUtility.DisplayDialog("Install butler", message, "OK");
                            Repaint();
                        });
                    }
                }
                if (path != null && GUILayout.Button("Show", GUILayout.Width(50))) RevealFolder(Path.GetDirectoryName(path));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var custom = EditorGUILayout.DelayedTextField(
                    new GUIContent("Custom butler path", "Leave empty to find butler automatically."), ItchUserPrefs.ButlerPathOverride);
                if (custom != ItchUserPrefs.ButlerPathOverride)
                {
                    ItchUserPrefs.ButlerPathOverride = custom;
                    ItchRemote.CheckButlerVersion(true);
                }
                if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(26)))
                {
                    var picked = EditorUtility.OpenFilePanel("butler executable", "", Butler.IsWindows ? "exe" : "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        ItchUserPrefs.ButlerPathOverride = picked;
                        ItchRemote.CheckButlerVersion(true);
                    }
                }
            }

            Section("Behaviour (only for you)");
            ItchUserPrefs.ConfirmBeforePublish = EditorGUILayout.Toggle("Confirm before uploading", ItchUserPrefs.ConfirmBeforePublish);
            ItchUserPrefs.OpenPageAfterPublish = EditorGUILayout.Toggle("Open the game page afterwards", ItchUserPrefs.OpenPageAfterPublish);
            ItchUserPrefs.BeepOnFinish = EditorGUILayout.Toggle("Beep when finished", ItchUserPrefs.BeepOnFinish);
            ItchUserPrefs.CheckOnOpen = EditorGUILayout.Toggle(
                new GUIContent("Check itch.io on open", "When the window opens: account, games and channel versions."), ItchUserPrefs.CheckOnOpen);

            GUILayout.Label("Toolbar button: open the ⋮ menu at the right end of Unity's main toolbar and tick " +
                            "<b>Itch.io → Publisher</b>.", wrapMini);

            Section("Command line / CI");
            GUILayout.Label("Same settings, no window. Set " + ItchUserPrefs.EnvVar + " on the build machine. " +
                            "Options: -itchChannels a,b  -itchVersion 1.2.3  -itchBump none|patch|minor|major  -itchGame user/game  -itchDryRun  -itchForce. " +
                            "Also: ItchPublisherCli.BuildOnly and ItchPublisherCli.PublishLastBuilds.", wrapMini);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(CliExample, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(44))) EditorGUIUtility.systemCopyBuffer = CliExample;
            }

            Section("Where things are stored");
            GUILayout.Label("<b>ProjectSettings/ItchPublisher.asset</b>: game page, targets, version and upload options. Commit it.\n" +
                            "<b>UserSettings/ItchPublisher.json</b>: your build/upload history (not committed).\n" +
                            "<b>Editor preferences</b>: API key and the options above (this computer only).", wrapMini);
        }

        void DrawHistory()
        {
            InitStyles();
            var history = ItchState.History;
            using (new EditorGUILayout.HorizontalScope())
            {
                Section($"History ({history.Count})");
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(history.Count == 0))
                {
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)) &&
                        EditorUtility.DisplayDialog("Clear history", "Delete the local build/upload history?", "Clear", "Cancel"))
                        ItchState.ClearHistory();
                }
            }

            if (history.Count == 0)
            {
                GUILayout.Label("Nothing has been built or published yet.", EditorStyles.miniLabel);
                return;
            }

            foreach (var entry in history)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        ColoredLabel(entry.success ? "✓" : "✗", entry.success ? Good : Bad, EditorStyles.boldLabel, GUILayout.Width(14));
                        GUILayout.Label($"{entry.action}{(entry.dryRun ? " (dry run)" : "")}" +
                                        (string.IsNullOrEmpty(entry.version) ? "" : "  ·  v" + entry.version),
                            EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        var time = ItchState.ParseUtc(entry.utc).ToLocalTime();
                        GUILayout.Label($"{time:dd MMM HH:mm}  ·  {entry.seconds:0}s", EditorStyles.miniLabel);
                    }
                    GUILayout.Label(entry.summary, wrapMini);

                    if (entry.details is { Count: > 0 })
                    {
                        var open = expandedHistory.Contains(entry.utc);
                        var newOpen = EditorGUILayout.Foldout(open, "Details", true);
                        if (newOpen != open)
                        {
                            if (newOpen) expandedHistory.Add(entry.utc);
                            else expandedHistory.Remove(entry.utc);
                        }
                        if (newOpen)
                            foreach (var line in entry.details)
                                GUILayout.Label(line, wrapMini);
                    }
                }
            }
        }
    }
}
