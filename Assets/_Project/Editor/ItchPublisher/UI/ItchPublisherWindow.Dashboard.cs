using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    public partial class ItchPublisherWindow
    {
        Vector2 logScroll;
        int lastLogCount;

        void DrawDashboard()
        {
            InitStyles();
            DrawGameHeader();
            DrawStatusStrip();
            DrawIssues();
            DrawTargets();
            DrawActions();
            DrawRunState();
            DrawLog();
        }

        // ---------------------------------------------------------------- header

        void DrawGameHeader()
        {
            var settings = ItchProjectSettings.instance;
            if (!settings.HasGame)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon"), GUILayout.Width(36), GUILayout.Height(32));
                    GUILayout.Label("Connect this project to an itch.io game page to start publishing.", wrapMini);
                    if (GUILayout.Button("Set up", GUILayout.Width(80), GUILayout.Height(24))) tab = Tab.Itch;
                }
                return;
            }

            var game = ItchAccount.CurrentGame;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(game?.title ?? settings.gameSlug, new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
                GUILayout.Label(settings.GameTarget, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                if (LinkButton("Open page")) Application.OpenURL(settings.GameUrl);
                if (LinkButton("Edit page")) Application.OpenURL(settings.EditUrl);
                using (new EditorGUI.DisabledScope(ItchRemote.IsRefreshing))
                {
                    if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image,
                            "Refresh account and channel status from itch.io"), EditorStyles.iconButton))
                    {
                        ItchAccount.Refresh();
                        ItchRemote.Refresh();
                    }
                }
            }
            if (game != null)
                GUILayout.Label($"{(game.published ? "Published" : "Draft (only you can see it)")}  ·  " +
                                $"{game.views_count:N0} views  ·  {game.downloads_count:N0} downloads", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- status strip

        void DrawStatusStrip()
        {
            var settings = ItchProjectSettings.instance;
            using (new EditorGUILayout.HorizontalScope())
            {
                var butler = Butler.Locate(out _);
                if (butler == null) StatusCell("butler", "Not installed", Bad, Tab.Settings);
                else StatusCell("butler", ItchRemote.ButlerVersion != null ? "v" + ItchRemote.ButlerVersion : "Found", Good, Tab.Settings);

                var accountColor = ItchAccount.State switch
                {
                    AccountState.Ok or AccountState.Unverified => Good,
                    AccountState.Checking or AccountState.Unknown => Muted,
                    AccountState.Offline => Warn,
                    _ => Bad,
                };
                StatusCell("Account", ItchAccount.StateText, accountColor, Tab.Itch);

                var game = ItchAccount.CurrentGame;
                if (!settings.HasGame) StatusCell("Game", "Not set", Bad, Tab.Itch);
                else if (game is { published: false }) StatusCell("Game", "Draft", Warn, Tab.Itch);
                else StatusCell("Game", settings.gameSlug, Good, Tab.Itch);

                var version = Versioning.Current;
                var next = settings.autoBump != VersionBump.None ? $"  → {Versioning.Bump(version, settings.autoBump)}" : "";
                StatusCell("Version" + next, string.IsNullOrEmpty(version) ? "Empty" : version,
                    string.IsNullOrEmpty(version) ? Bad : Good, Tab.Builds);
            }
        }

        void StatusCell(string title, string value, Color color, Tab target)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(90)))
            {
                GUILayout.Label(title, cellTitleStyle);
                ColoredLabel("● " + value, color, cellValueStyle);
            }
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                tab = target;
                Event.current.Use();
            }
        }

        // ---------------------------------------------------------------- issues

        void DrawIssues()
        {
            var list = Issues;
            if (list.Count == 0) return;
            EditorGUILayout.Space(2);
            foreach (var issue in list.OrderByDescending(i => i.Severity))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    var icon = issue.Severity switch
                    {
                        IssueSeverity.Error => "console.erroricon.sml",
                        IssueSeverity.Warning => "console.warnicon.sml",
                        _ => "console.infoicon.sml",
                    };
                    GUILayout.Label(EditorGUIUtility.IconContent(icon), GUILayout.Width(18), GUILayout.Height(18));
                    GUILayout.Label(issue.Message, wrapMini);
                    if (issue.Fix != null && GUILayout.Button(issue.FixLabel, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        issue.Fix();
                        issuesCheckedAt = -100;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- targets

        void DrawTargets()
        {
            var settings = ItchProjectSettings.instance;
            using (new EditorGUILayout.HorizontalScope())
            {
                Section("Targets");
                GUILayout.FlexibleSpace();
                if (LinkButton("Configure…")) tab = Tab.Builds;
            }

            var enabled = settings.EnabledTargets.ToList();
            foreach (var t in enabled) DrawTargetRow(t);

            var disabled = settings.targets.Count - enabled.Count;
            if (enabled.Count == 0)
                EditorGUILayout.HelpBox("No targets are enabled. Turn some on in the Builds tab.", MessageType.Info);
            else if (disabled > 0)
                GUILayout.Label($"{disabled} more target(s) disabled.", EditorStyles.miniLabel);

            var orphans = ItchState.RemoteOnly(settings.targets.Select(t => t.channel)).ToList();
            if (orphans.Count > 0)
                GUILayout.Label("Also on itch.io, not built by this project: " +
                                string.Join(", ", orphans.Select(o => $"{o.channel} ({o.remoteVersion})")), wrapMini);
        }

        void DrawTargetRow(ItchTarget t)
        {
            var record = ItchState.Find(t.channel);
            var dir = TargetInfo.OutputDir(t);
            var hasBuild = record is { lastBuildOk: true } && TargetInfo.HasFiles(dir);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(TargetInfo.Icon(t.target), GUILayout.Width(20), GUILayout.Height(20));

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(TargetInfo.DisplayName(t.target), EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        GUILayout.Label(":" + t.channel + (t.development ? "  · dev" : ""), EditorStyles.miniLabel);
                    }

                    // Local build
                    if (record == null || string.IsNullOrEmpty(record.lastBuildUtc))
                        ColoredLabel("Local: not built yet", Muted, EditorStyles.miniLabel);
                    else if (!record.lastBuildOk)
                        ColoredLabel($"Local: ✗ build failed {ItchState.Ago(record.lastBuildUtc)}: {record.lastBuildError}", Bad, wrapMini);
                    else if (!hasBuild)
                        ColoredLabel("Local: build folder is missing or empty", Warn, EditorStyles.miniLabel);
                    else
                        ColoredLabel($"Local: ✓ v{record.lastBuildVersion} · {ItchState.Size(record.lastBuildBytes)} · " +
                                     ItchState.Ago(record.lastBuildUtc), Good, EditorStyles.miniLabel);

                    DrawRemoteLine(record, hasBuild);
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(Running))
                {
                    if (GUILayout.Button(new GUIContent("Build", "Build only this target"), EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                        PublishPipeline.BuildOnly(new[] { t });
                    using (new EditorGUI.DisabledScope(!hasBuild))
                    {
                        if (GUILayout.Button(new GUIContent("Upload", "Upload the last local build of this target"),
                                EditorStyles.miniButtonMid, GUILayout.Width(56)))
                            PublishPipeline.PublishLastBuilds(new[] { t });
                    }
                }
                if (GUILayout.Button(EditorGUIUtility.IconContent("_Menu"), EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    ShowTargetMenu(t, hasBuild);
            }
        }

        void DrawRemoteLine(ChannelRecord record, bool hasBuild)
        {
            if (record is { lastPushOk: false } && !string.IsNullOrEmpty(record.lastPushUtc))
            {
                ColoredLabel($"itch.io: ✗ last upload failed {ItchState.Ago(record.lastPushUtc)}: {record.lastPushError}", Bad, wrapMini);
                return;
            }

            var live = string.IsNullOrEmpty(record?.remoteUpload) ? null : record.remoteVersion;
            var uploaded = record?.lastUploadedVersion;
            var uploadedAt = ItchState.ParseUtc(record?.lastUploadedUtc);

            // Uploaded, but "butler status" was read before itch.io finished processing it.
            if (!string.IsNullOrEmpty(uploaded) && uploaded != live && uploadedAt > DateTime.UtcNow.AddMinutes(-15))
            {
                ColoredLabel($"itch.io: v{uploaded} uploaded {ItchState.Ago(uploadedAt)}, processing…" +
                             (live != null ? $" (live: v{live})" : ""), Good, EditorStyles.miniLabel);
                return;
            }

            if (live == null)
            {
                ColoredLabel(string.IsNullOrEmpty(uploaded) ? "itch.io: not uploaded yet" : $"itch.io: v{uploaded} uploaded {ItchState.Ago(uploadedAt)}",
                    string.IsNullOrEmpty(uploaded) ? Muted : Good, EditorStyles.miniLabel);
                return;
            }

            var synced = live == record.lastBuildVersion;
            var skipped = record.lastPushSkipped && record.lastPushOk ? $" · checked {ItchState.Ago(record.lastPushUtc)}: unchanged" : "";
            ColoredLabel($"itch.io: v{live} · build {record.remoteBuild}{skipped}" +
                         (hasBuild && !synced ? $"  (local v{record.lastBuildVersion} not uploaded)" : ""),
                synced ? Good : Warn, EditorStyles.miniLabel);
        }

        static void ShowTargetMenu(ItchTarget t, bool hasBuild)
        {
            var menu = new GenericMenu();
            var dir = TargetInfo.OutputDir(t);

            void Item(string label, bool enabled, GenericMenu.MenuFunction action)
            {
                if (enabled) menu.AddItem(new GUIContent(label), false, action);
                else menu.AddDisabledItem(new GUIContent(label));
            }

            Item("Build and publish", !Running, () => PublishPipeline.BuildAndPublish(new[] { t }));
            Item("Upload last build (force, even if unchanged)", !Running && hasBuild, () =>
                PublishPipeline.Start(new PublishRequest
                {
                    Targets = new() { t.Clone() }, Build = false, Push = true, DryRun = ItchUserPrefs.DryRun, Force = true,
                }));
            Item("Upload last build (dry run)", !Running && hasBuild, () =>
                PublishPipeline.Start(new PublishRequest { Targets = new() { t.Clone() }, Build = false, Push = true, DryRun = true }));
            menu.AddSeparator("");

            var runnable = TargetInfo.RunnablePath(t);
            Item("Run local build", runnable != null, () =>
            {
                try { Process.Start(new ProcessStartInfo(runnable) { UseShellExecute = true, WorkingDirectory = dir }); }
                catch (Exception e) { EditorUtility.DisplayDialog("Run build", e.Message, "OK"); }
            });
            Item("Open build folder", System.IO.Directory.Exists(dir), () => RevealFolder(dir));
            Item("Copy build folder path", true, () => EditorGUIUtility.systemCopyBuffer = dir);
            menu.AddSeparator("");
            var url = ItchProjectSettings.instance.GameUrl;
            Item("Open game page", url != null, () => Application.OpenURL(url));
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- actions

        void DrawActions()
        {
            EditorGUILayout.Space(6);
            var settings = ItchProjectSettings.instance;
            var count = settings.EnabledTargets.Count();

            using (new EditorGUI.DisabledScope(Running || count == 0))
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.98f, 0.36f, 0.36f); // itch.io red
                var label = ItchUserPrefs.DryRun ? "Build & Publish (dry run)" : "Build & Publish";
                if (GUILayout.Button(new GUIContent(label, "Build every enabled target, then upload them to itch.io"), bigButton))
                    PublishPipeline.BuildAndPublish();
                GUI.backgroundColor = old;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Build only", "Build every enabled target without uploading")))
                        PublishPipeline.BuildOnly();
                    if (GUILayout.Button(new GUIContent("Publish last builds", "Upload the existing local builds without rebuilding")))
                        PublishPipeline.PublishLastBuilds();
                }
            }

            ItchUserPrefs.DryRun = EditorGUILayout.ToggleLeft(
                new GUIContent("Dry run (check what would be uploaded, don't upload)"), ItchUserPrefs.DryRun);
        }

        // ---------------------------------------------------------------- run state

        void DrawRunState()
        {
            if (Running)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var rect = GUILayoutUtility.GetRect(18, 20, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(rect, PublishPipeline.PhaseProgress,
                        $"{PublishPipeline.Status}  {PublishPipeline.PhaseProgress * 100:0}%");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(PublishPipeline.Phase == PipelinePhase.Building
                            ? "Unity shows its own progress window while building."
                            : "You can keep working; script reloads wait until the upload is done.", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(!PublishPipeline.CanCancel))
                            if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(60)))
                                PublishPipeline.Cancel();
                    }
                }
                return;
            }

            var last = PublishPipeline.LastRun;
            if (last == null) return;
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                ColoredLabel((last.success ? "✓ " : "✗ ") + last.summary, last.success ? Good : Bad, wrapMini);
                GUILayout.FlexibleSpace();
                GUILayout.Label(ItchState.Ago(last.utc), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                if (LinkButton("History")) tab = Tab.History;
            }
        }

        // ---------------------------------------------------------------- log

        void DrawLog()
        {
            const string key = "GameJam.ItchPublisher.LogOpen";
            var open = SessionState.GetBool(key, false);
            var count = PublishPipeline.LogCount;

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var newOpen = EditorGUILayout.Foldout(open, $"Log ({count})", true);
                if (newOpen != open) SessionState.SetBool(key, open = newOpen);
                GUILayout.FlexibleSpace();
                if (open)
                {
                    if (GUILayout.Button("Copy", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                        EditorGUIUtility.systemCopyBuffer = string.Join("\n", PublishPipeline.LogSnapshot());
                    if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                        PublishPipeline.ClearLog();
                }
            }
            if (!open) return;

            var lines = PublishPipeline.LogSnapshot();
            if (lines.Length > 400) lines = lines.Skip(lines.Length - 400).ToArray();
            var text = lines.Length == 0 ? "Nothing yet." : string.Join("\n", lines);

            if (count != lastLogCount)
            {
                lastLogCount = count;
                logScroll.y = float.MaxValue; // follow new output
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(180));
                var height = logStyle.CalcHeight(new GUIContent(text), position.width - 50);
                EditorGUILayout.SelectableLabel(text, logStyle, GUILayout.Height(height));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
