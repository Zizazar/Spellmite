using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>itch.io Publisher: state, build setup, itch.io account/game, settings and history.</summary>
    public partial class ItchPublisherWindow : EditorWindow
    {
        public enum Tab { Dashboard, Builds, Itch, Settings, History }

        static readonly GUIContent[] TabLabels =
        {
            new("Dashboard"), new("Builds"), new("itch.io"), new("Settings"), new("History"),
        };

        [SerializeField] Tab tab;
        Vector2 scroll;

        List<PreflightIssue> issues = new();
        double issuesCheckedAt = -100;

        // ---------------------------------------------------------------- menu

        [MenuItem("Tools/Itch.io/Publisher", priority = 0)]
        public static void OpenMenu() => Open(Tab.Dashboard);

        [MenuItem("Tools/Itch.io/Build and Publish All", priority = 20)]
        static void BuildAndPublishMenu() => PublishPipeline.BuildAndPublish();

        [MenuItem("Tools/Itch.io/Build All (no upload)", priority = 21)]
        static void BuildOnlyMenu() => PublishPipeline.BuildOnly();

        [MenuItem("Tools/Itch.io/Publish Last Builds", priority = 22)]
        static void PublishLastMenu() => PublishPipeline.PublishLastBuilds();

        [MenuItem("Tools/Itch.io/Build and Publish All", true)]
        [MenuItem("Tools/Itch.io/Build All (no upload)", true)]
        [MenuItem("Tools/Itch.io/Publish Last Builds", true)]
        static bool CanStart() => !PublishPipeline.IsRunning;

        [MenuItem("Tools/Itch.io/Open Game Page", priority = 40)]
        static void OpenPageMenu() => Application.OpenURL(ItchProjectSettings.instance.GameUrl);

        [MenuItem("Tools/Itch.io/Open Game Page", true)]
        static bool CanOpenPage() => ItchProjectSettings.instance.HasGame;

        [MenuItem("Tools/Itch.io/Open Builds Folder", priority = 41)]
        static void OpenBuildsMenu() => RevealFolder(ItchProjectSettings.instance.BuildRootFull);

        public static ItchPublisherWindow Open(Tab tab)
        {
            var window = GetWindow<ItchPublisherWindow>();
            window.titleContent = new GUIContent("itch.io Publisher", EditorGUIUtility.IconContent("CloudConnect").image);
            window.minSize = new Vector2(460, 320);
            window.tab = tab;
            window.Show();
            return window;
        }

        /// <summary>Shows a toast in the window if it's open.</summary>
        public static void Notify(string message)
        {
            if (!HasOpenInstances<ItchPublisherWindow>()) return;
            var window = GetWindow<ItchPublisherWindow>(false, null, false);
            window.ShowNotification(new GUIContent(message), 4);
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            PublishPipeline.Changed += OnStateChanged;
            ItchAccount.Changed += OnStateChanged;
            ItchRemote.Changed += OnStateChanged;
            ItchState.Changed += OnStateChanged;
            ItchProjectSettings.Changed += OnStateChanged;
            ItchUserPrefs.Changed += OnStateChanged;

            ItchRemote.CheckButlerVersion();
            if (ItchUserPrefs.CheckOnOpen && ItchAccount.State == AccountState.Unknown)
            {
                ItchAccount.Refresh();
                if (ItchState.RemoteCheckedUtc < DateTime.UtcNow.AddMinutes(-10)) ItchRemote.Refresh();
            }
        }

        void OnDisable()
        {
            PublishPipeline.Changed -= OnStateChanged;
            ItchAccount.Changed -= OnStateChanged;
            ItchRemote.Changed -= OnStateChanged;
            ItchState.Changed -= OnStateChanged;
            ItchProjectSettings.Changed -= OnStateChanged;
            ItchUserPrefs.Changed -= OnStateChanged;
        }

        void OnFocus()
        {
            // The user may have finished "butler login" or changed Player Settings elsewhere.
            issuesCheckedAt = -100;
            if (ItchAccount.State == AccountState.NotLoggedIn && ItchAccount.HasCredentials) ItchAccount.Refresh();
            ItchRemote.CheckButlerVersion();
        }

        void OnStateChanged()
        {
            issuesCheckedAt = -100;
            Repaint();
        }

        List<PreflightIssue> Issues
        {
            get
            {
                // Checks touch the file system: re-run at most every 2 seconds.
                if (EditorApplication.timeSinceStartup - issuesCheckedAt > 2)
                {
                    issuesCheckedAt = EditorApplication.timeSinceStartup;
                    var targets = new List<ItchTarget>(ItchProjectSettings.instance.EnabledTargets);
                    issues = Preflight.Check(targets, build: true, push: true);
                }
                return issues;
            }
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                tab = (Tab)GUILayout.Toolbar((int)tab, TabLabels, EditorStyles.toolbarButton, GUI.ToolbarButtonSize.FitToContents);
                GUILayout.FlexibleSpace();
                if (PublishPipeline.IsRunning)
                    GUILayout.Label(PublishPipeline.Status, EditorStyles.miniLabel);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(4);
            switch (tab)
            {
                case Tab.Dashboard: DrawDashboard(); break;
                case Tab.Builds: DrawBuilds(); break;
                case Tab.Itch: DrawItch(); break;
                case Tab.Settings: DrawSettings(); break;
                case Tab.History: DrawHistory(); break;
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- shared helpers

        static GUIStyle sectionStyle, cellTitleStyle, cellValueStyle, wrapMini, bigButton, logStyle;

        static void InitStyles()
        {
            if (sectionStyle != null) return;
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(4, 4, 8, 2) };
            cellTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft };
            cellValueStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = false, clipping = TextClipping.Clip };
            wrapMini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            bigButton = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 30 };
            logStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = false,
                fontSize = 11,
            };
        }

        static readonly Color Good = new(0.36f, 0.78f, 0.38f);
        static readonly Color Warn = new(0.98f, 0.75f, 0.2f);
        static readonly Color Bad = new(0.95f, 0.35f, 0.32f);
        static readonly Color Muted = new(0.6f, 0.6f, 0.6f);

        static void Section(string title)
        {
            InitStyles();
            GUILayout.Label(title, sectionStyle);
        }

        static void ColoredLabel(string text, Color color, GUIStyle style = null, params GUILayoutOption[] options)
        {
            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, style ?? EditorStyles.label, options);
            GUI.contentColor = old;
        }

        static bool LinkButton(string text, string tooltip = null)
        {
            var content = new GUIContent(text, tooltip);
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.linkLabel, GUILayout.ExpandWidth(false));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, content, EditorStyles.linkLabel);
        }

        public static void RevealFolder(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                // Show the nearest existing parent instead of failing silently.
                var parent = System.IO.Path.GetDirectoryName(path);
                if (parent != null && System.IO.Directory.Exists(parent)) path = parent;
                else return;
            }
            EditorUtility.OpenWithDefaultApp(path);
        }

        static bool Running => PublishPipeline.IsRunning;
    }
}
