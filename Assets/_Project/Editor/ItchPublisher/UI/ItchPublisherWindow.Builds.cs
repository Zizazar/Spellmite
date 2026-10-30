using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    public partial class ItchPublisherWindow
    {
        static string[] targetNames;

        void DrawBuilds()
        {
            InitStyles();
            var s = ItchProjectSettings.instance;
            EditorGUIUtility.labelWidth = 200;

            EditorGUI.BeginChangeCheck();
            DrawOutputSettings(s);
            DrawTargetEditor(s);
            if (EditorGUI.EndChangeCheck()) s.SaveNow();

            DrawScenes();
            DrawVersion(s);
            if (s.targets.Any(t => t.target == BuildTarget.WebGL)) DrawWebGL();
        }

        void DrawOutputSettings(ItchProjectSettings s)
        {
            Section("Output");
            using (new EditorGUILayout.HorizontalScope())
            {
                s.buildRoot = EditorGUILayout.DelayedTextField(
                    new GUIContent("Build folder", "Relative to the project folder, or an absolute path. Each channel gets a sub-folder."),
                    s.buildRoot);
                if (GUILayout.Button("…", EditorStyles.miniButtonLeft, GUILayout.Width(26)))
                {
                    var picked = EditorUtility.OpenFolderPanel("Build folder", s.BuildRootFull, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        var root = ItchProjectSettings.ProjectRoot + "/";
                        s.buildRoot = picked.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? picked.Substring(root.Length) : picked;
                        GUI.changed = true;
                    }
                }
                if (GUILayout.Button("Open", EditorStyles.miniButtonRight, GUILayout.Width(44))) RevealFolder(s.BuildRootFull);
            }
            GUILayout.Label(s.BuildRootFull + "/<channel>", EditorStyles.miniLabel);

            var problem = TargetInfo.BuildRootProblem(s.BuildRootFull);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Error);

            s.cleanBeforeBuild = EditorGUILayout.Toggle(
                new GUIContent("Clean folder before building", "Delete the channel folder first so no stale files get uploaded."),
                s.cleanBeforeBuild);
            s.restoreActivePlatform = EditorGUILayout.Toggle(
                new GUIContent("Switch back to active platform",
                    "Building another platform switches the editor to it. Switch back afterwards (costs a reimport)."),
                s.restoreActivePlatform);
        }

        void DrawTargetEditor(ItchProjectSettings s)
        {
            Section("Targets");
            GUILayout.Label("Each target is built into its own folder and uploaded to its own itch.io channel. " +
                            "itch.io detects the platform from the channel name (win / linux / mac / android).", wrapMini);

            targetNames ??= TargetInfo.Supported.Select(TargetInfo.DisplayName).ToArray();
            int moveFrom = -1, moveTo = -1, remove = -1;

            for (var i = 0; i < s.targets.Count; i++)
            {
                var t = s.targets[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    t.enabled = EditorGUILayout.Toggle(t.enabled, GUILayout.Width(16));
                    GUILayout.Label(TargetInfo.Icon(t.target), GUILayout.Width(18), GUILayout.Height(18));

                    using (new EditorGUI.DisabledScope(!t.enabled))
                    {
                        var index = Array.IndexOf(TargetInfo.Supported, t.target);
                        var names = index >= 0 ? targetNames : targetNames.Append(TargetInfo.DisplayName(t.target)).ToArray();
                        if (index < 0) index = names.Length - 1;
                        var newIndex = EditorGUILayout.Popup(index, names, GUILayout.Width(150));
                        if (newIndex != index && newIndex < TargetInfo.Supported.Length)
                        {
                            var newTarget = TargetInfo.Supported[newIndex];
                            if (t.channel == TargetInfo.DefaultChannel(t.target)) t.channel = UniqueChannel(s, TargetInfo.DefaultChannel(newTarget), t);
                            t.target = newTarget;
                        }

                        GUILayout.Label("channel", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                        var channel = EditorGUILayout.DelayedTextField(t.channel, GUILayout.Width(100));
                        if (channel != t.channel) t.channel = channel.Trim().ToLowerInvariant().Replace(' ', '-');

                        t.development = GUILayout.Toggle(t.development, new GUIContent("Dev", "Development build"),
                            EditorStyles.miniButton, GUILayout.Width(36));
                    }

                    if (!TargetInfo.IsInstalled(t.target))
                        ColoredLabel("module not installed", Bad, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22))) { moveFrom = i; moveTo = i - 1; }
                    using (new EditorGUI.DisabledScope(i == s.targets.Count - 1))
                        if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22))) { moveFrom = i; moveTo = i + 1; }
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22))) remove = i;
                }
            }

            if (moveFrom >= 0)
            {
                (s.targets[moveFrom], s.targets[moveTo]) = (s.targets[moveTo], s.targets[moveFrom]);
                GUI.changed = true;
            }
            if (remove >= 0)
            {
                s.targets.RemoveAt(remove);
                GUI.changed = true;
            }

            if (GUILayout.Button("Add target", GUILayout.Width(120)))
            {
                var menu = new GenericMenu();
                foreach (var target in TargetInfo.Supported)
                {
                    var label = TargetInfo.DisplayName(target) + (TargetInfo.IsInstalled(target) ? "" : "  (module not installed)");
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        s.targets.Add(new ItchTarget { target = target, channel = UniqueChannel(s, TargetInfo.DefaultChannel(target), null) });
                        s.SaveNow();
                    });
                }
                menu.ShowAsContext();
            }
        }

        static string UniqueChannel(ItchProjectSettings s, string channel, ItchTarget except)
        {
            var result = channel;
            for (var n = 2; s.targets.Any(t => t != except && t.channel == result); n++) result = channel + "-" + n;
            return result;
        }

        void DrawScenes()
        {
            Section("Scenes");
            var scenes = EditorBuildSettings.scenes.Where(sc => sc.enabled).ToList();
            if (scenes.Count == 0)
                EditorGUILayout.HelpBox("No scenes are enabled in the build. Add them in Build Profiles → Scene List.", MessageType.Error);
            for (var i = 0; i < scenes.Count; i++)
                GUILayout.Label($"{i}.  {scenes[i].path}", EditorStyles.miniLabel);
            if (GUILayout.Button("Edit scene list…", GUILayout.Width(120))) BuildPlayerWindow.ShowBuildPlayerWindow();
        }

        void DrawVersion(ItchProjectSettings s)
        {
            Section("Version");
            GUILayout.Label("Shown on itch.io next to each upload (butler --userversion). " +
                            "It is Player Settings → Version, so the game can display it too (Application.version).", wrapMini);

            using (new EditorGUILayout.HorizontalScope())
            {
                var version = EditorGUILayout.DelayedTextField("Version", Versioning.Current);
                if (version != Versioning.Current) Versioning.Set(version.Trim());
                if (GUILayout.Button("+patch", EditorStyles.miniButtonLeft, GUILayout.Width(50))) Versioning.Set(Versioning.Bump(Versioning.Current, VersionBump.Patch));
                if (GUILayout.Button("+minor", EditorStyles.miniButtonMid, GUILayout.Width(50))) Versioning.Set(Versioning.Bump(Versioning.Current, VersionBump.Minor));
                if (GUILayout.Button("+major", EditorStyles.miniButtonRight, GUILayout.Width(50))) Versioning.Set(Versioning.Bump(Versioning.Current, VersionBump.Major));
            }

            EditorGUI.BeginChangeCheck();
            s.autoBump = (VersionBump)EditorGUILayout.EnumPopup(
                new GUIContent("Bump on every publish", "Applied right before \"Build & Publish\" (not for build-only)."), s.autoBump);
            if (s.targets.Any(t => t.target == BuildTarget.Android))
            {
                s.bumpAndroidVersionCode = EditorGUILayout.Toggle("Also bump Android versionCode", s.bumpAndroidVersionCode);
                var code = EditorGUILayout.IntField("Android versionCode", PlayerSettings.Android.bundleVersionCode);
                if (code != PlayerSettings.Android.bundleVersionCode) PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, code);
            }
            if (EditorGUI.EndChangeCheck()) s.SaveNow();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Product name", PlayerSettings.productName);
                if (GUILayout.Button("Player Settings…", EditorStyles.miniButton, GUILayout.Width(110)))
                    SettingsService.OpenProjectSettings("Project/Player");
            }
        }

        void DrawWebGL()
        {
            Section("Web (HTML5)");
            var format = (WebGLCompressionFormat)EditorGUILayout.EnumPopup("Compression", PlayerSettings.WebGL.compressionFormat);
            if (format != PlayerSettings.WebGL.compressionFormat) PlayerSettings.WebGL.compressionFormat = format;
            using (new EditorGUI.DisabledScope(format == WebGLCompressionFormat.Disabled))
            {
                var fallback = EditorGUILayout.Toggle(
                    new GUIContent("Decompression fallback",
                        "Lets the game load even when the server doesn't send compression headers. Recommended for itch.io."),
                    PlayerSettings.WebGL.decompressionFallback);
                if (fallback != PlayerSettings.WebGL.decompressionFallback) PlayerSettings.WebGL.decompressionFallback = fallback;
            }

            var width = PlayerSettings.defaultWebScreenWidth;
            var height = PlayerSettings.defaultWebScreenHeight;
            EditorGUILayout.LabelField("Canvas size", $"{width} × {height}");
            GUILayout.Label($"On the itch.io Edit game page set <b>Embed options → Viewport dimensions</b> to {width} × {height} " +
                            "and consider enabling the fullscreen button.", wrapMini);

            if (GUILayout.Button(new GUIContent("Apply recommended for itch.io", "Gzip compression with decompression fallback"),
                    GUILayout.Width(220)))
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
