using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    public partial class ItchPublisherWindow
    {
        bool showKey;

        void DrawItch()
        {
            InitStyles();
            EditorGUIUtility.labelWidth = 140;
            DrawAccount();
            DrawGamePage();
            DrawRemoteChannels();
            DrawUploadOptions();
        }

        // ---------------------------------------------------------------- account

        void DrawAccount()
        {
            Section("Account");

            using (new EditorGUILayout.HorizontalScope())
            {
                var color = ItchAccount.State switch
                {
                    AccountState.Ok or AccountState.Unverified => Good,
                    AccountState.Checking or AccountState.Unknown => Muted,
                    AccountState.Offline => Warn,
                    _ => Bad,
                };
                var user = ItchAccount.User;
                ColoredLabel("● " + (user != null
                    ? $"Logged in as {user.display_name ?? user.username} (@{user.username})"
                    : ItchAccount.StateText), color, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (user?.url != null && LinkButton("Profile")) Application.OpenURL(user.url);
                using (new EditorGUI.DisabledScope(ItchAccount.State == AccountState.Checking))
                    if (GUILayout.Button("Check", EditorStyles.miniButton, GUILayout.Width(50))) ItchAccount.Refresh();
            }
            if (ItchAccount.KeySource != null)
                GUILayout.Label("Credentials from: " + ItchAccount.KeySource, EditorStyles.miniLabel);
            if (ItchAccount.State == AccountState.Unverified)
                GUILayout.Label("butler's login key can't read your profile, so game info isn't shown. Uploads still work; " +
                                "add an API key below to see your games.", wrapMini);
            if (!string.IsNullOrEmpty(ItchAccount.Error) && ItchAccount.State != AccountState.Unverified)
                EditorGUILayout.HelpBox(ItchAccount.Error, MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var key = ItchUserPrefs.ApiKey;
                var label = new GUIContent("API key", "itch.io → Settings → API keys → Generate new API key");
                var newKey = showKey ? EditorGUILayout.TextField(label, key) : EditorGUILayout.PasswordField(label, key);
                if (newKey != key) ItchUserPrefs.ApiKey = newKey;
                showKey = GUILayout.Toggle(showKey, new GUIContent(EditorGUIUtility.IconContent("animationvisibilitytoggleon").image, "Show key"),
                    EditorStyles.miniButton, GUILayout.Width(26));
                if (GUILayout.Button("Verify", EditorStyles.miniButton, GUILayout.Width(50))) ItchAccount.Refresh();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Saved in this computer's editor preferences, never in the project.", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (LinkButton("Get an API key")) Application.OpenURL("https://itch.io/user/settings/api-keys");
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Or log butler in with the browser:", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                var butler = Butler.Locate(out _);
                using (new EditorGUI.DisabledScope(butler == null))
                {
                    if (GUILayout.Button("butler login", EditorStyles.miniButtonLeft, GUILayout.Width(90)))
                    {
                        if (Butler.StartLogin(butler, out var error))
                            ShowNotification(new GUIContent("Finish the login in your browser"), 4);
                        else
                            EditorUtility.DisplayDialog("butler login", error, "OK");
                    }
                }
                var creds = Butler.CredentialsPath;
                using (new EditorGUI.DisabledScope(creds == null))
                {
                    if (GUILayout.Button("Log out", EditorStyles.miniButtonRight, GUILayout.Width(60)) &&
                        EditorUtility.DisplayDialog("Log out of butler", "Delete butler's saved login (" + creds + ")?", "Log out", "Cancel"))
                    {
                        try { File.Delete(creds); }
                        catch (IOException e) { EditorUtility.DisplayDialog("Log out", e.Message, "OK"); }
                        ItchAccount.Refresh();
                    }
                }
            }
            GUILayout.Label("CI / build servers: set the " + ItchUserPrefs.EnvVar + " environment variable instead.", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- game page

        void DrawGamePage()
        {
            var s = ItchProjectSettings.instance;
            Section("Game page");

            var current = s.HasGame ? s.GameUrl : "";
            var entered = EditorGUILayout.DelayedTextField(
                new GUIContent("Page URL", "https://user.itch.io/game or user/game. Saved in ProjectSettings (shared with the team)."),
                current);
            if (entered != current)
            {
                if (string.IsNullOrWhiteSpace(entered))
                {
                    s.itchUser = s.gameSlug = "";
                    s.gameId = 0;
                    s.SaveNow();
                }
                else if (ItchProjectSettings.TryParseGame(entered, out var user, out var slug))
                {
                    s.itchUser = user;
                    s.gameSlug = slug;
                    s.gameId = ItchAccount.Games?.FirstOrDefault(g => ItchAccount.SameGame(g, user, slug))?.id ?? 0;
                    s.SaveNow();
                    ItchRemote.Refresh();
                }
                else
                {
                    ShowNotification(new GUIContent("Use https://user.itch.io/game or user/game"), 3);
                }
            }
            if (s.HasGame) EditorGUILayout.LabelField("butler target", s.GameTarget + ":<channel>");

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                var games = ItchAccount.Games;
                using (new EditorGUI.DisabledScope(games == null || games.Length == 0))
                {
                    if (EditorGUILayout.DropdownButton(new GUIContent(games == null ? "My games (log in with an API key)" : "Pick from my games"),
                            FocusType.Passive, GUILayout.Width(220)))
                        ShowGamesMenu(games);
                }
                if (LinkButton("Create a new project on itch.io")) Application.OpenURL("https://itch.io/game/new");
            }

            var game = ItchAccount.CurrentGame;
            if (game != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(game.title, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (LinkButton("Open")) Application.OpenURL(game.url);
                        if (LinkButton("Edit")) Application.OpenURL(s.EditUrl);
                    }
                    if (!string.IsNullOrEmpty(game.short_text)) GUILayout.Label(game.short_text, wrapMini);
                    ColoredLabel(game.published ? "Published" : "Draft: only you can see the page. Publish it from the Edit page.",
                        game.published ? Good : Warn, EditorStyles.miniLabel);
                    GUILayout.Label($"{game.views_count:N0} views · {game.downloads_count:N0} downloads · " +
                                    $"{game.purchases_count:N0} purchases", EditorStyles.miniLabel);
                }
            }
            else if (s.HasGame && ItchAccount.Games != null)
            {
                EditorGUILayout.HelpBox($"\"{s.GameTarget}\" isn't one of your account's games. Check the spelling, " +
                                        "or make sure you're logged in with the account that owns it.", MessageType.Warning);
            }
        }

        static void ShowGamesMenu(ItchGame[] games)
        {
            var s = ItchProjectSettings.instance;
            var menu = new GenericMenu();
            foreach (var g in games.OrderBy(g => g.title))
            {
                var selected = g.id == s.gameId;
                menu.AddItem(new GUIContent((g.title ?? g.url).Replace("/", "∕") + (g.published ? "" : "  (draft)")), selected, () =>
                {
                    if (!ItchProjectSettings.TryParseGame(g.url, out var user, out var slug)) return;
                    s.itchUser = user;
                    s.gameSlug = slug;
                    s.gameId = g.id;
                    s.SaveNow();
                    ItchRemote.Refresh();
                });
            }
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- remote channels

        void DrawRemoteChannels()
        {
            var s = ItchProjectSettings.instance;
            using (new EditorGUILayout.HorizontalScope())
            {
                Section("Channels on itch.io");
                GUILayout.FlexibleSpace();
                var checkedAt = ItchState.RemoteCheckedUtc;
                GUILayout.Label(ItchRemote.IsRefreshing ? "checking…" : checkedAt == System.DateTime.MinValue ? "" : "checked " + ItchState.Ago(checkedAt),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                using (new EditorGUI.DisabledScope(ItchRemote.IsRefreshing || !s.HasGame))
                    if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60))) ItchRemote.Refresh();
            }

            if (!string.IsNullOrEmpty(ItchState.RemoteError))
                EditorGUILayout.HelpBox("butler status: " + ItchState.RemoteError, MessageType.Warning);

            var configured = s.targets.Select(t => t.channel).ToList();
            var rows = configured.Select(ItchState.Find).Where(r => r != null && !string.IsNullOrEmpty(r.remoteUpload))
                .Concat(ItchState.RemoteOnly(configured)).ToList();
            if (rows.Count == 0)
            {
                GUILayout.Label(s.HasGame ? "No uploads yet (or not checked)." : "Set the game page first.", EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Channel", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                GUILayout.Label("Version", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.Label("Build", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                GUILayout.Label("Upload", EditorStyles.miniBoldLabel);
            }
            foreach (var r in rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(r.channel + (configured.Contains(r.channel) ? "" : " *"), GUILayout.Width(120));
                    GUILayout.Label(r.remoteVersion, GUILayout.Width(90));
                    GUILayout.Label(r.remoteBuild, GUILayout.Width(110));
                    GUILayout.Label(r.remoteUpload);
                }
            }
            if (rows.Any(r => !configured.Contains(r.channel)))
                GUILayout.Label("* not built by this project", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- upload options

        void DrawUploadOptions()
        {
            var s = ItchProjectSettings.instance;
            Section("Upload options");
            EditorGUIUtility.labelWidth = 200;
            EditorGUI.BeginChangeCheck();

            s.ifChanged = EditorGUILayout.Toggle(
                new GUIContent("Skip unchanged builds",
                    "Don't create a new itch.io build when both the files and the version equal what's on itch.io " +
                    "(butler --if-changed). A new version is always uploaded."),
                s.ifChanged);
            s.excludeDoNotShip = EditorGUILayout.Toggle(
                new GUIContent("Exclude \"DoNotShip\" folders",
                    "Unity writes *_BurstDebugInformation_DoNotShip and *_BackUpThisFolder_ButDontShipItWithYourGame next to the " +
                    "player. They contain debug data and must not be uploaded."),
                s.excludeDoNotShip);

            EditorGUILayout.LabelField(new GUIContent("Extra ignore patterns",
                "One per line, e.g. *.pdb. Matched like Go's filepath.Match (* and ? wildcards)."));
            s.extraIgnore = EditorGUILayout.TextArea(s.extraIgnore, GUILayout.MinHeight(36));

            if (EditorGUI.EndChangeCheck()) s.SaveNow();

            GUILayout.Label("butler uploads only what changed since the last build (delta patches), so updates are quick. " +
                            "Players with the itch app get updates automatically.", wrapMini);

            if (s.targets.Any(t => t.enabled && t.target == BuildTarget.WebGL))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "Web builds, once after the first upload: Edit game → Kind of project = HTML, tick " +
                    "\"This file will be played in the browser\" on the html5 upload, set the viewport size, then Save.",
                    MessageType.Info);
            }
        }
    }
}
