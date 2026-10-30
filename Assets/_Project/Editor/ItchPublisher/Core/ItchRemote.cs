using System;
using System.Threading.Tasks;
using UnityEditor;

namespace GameJam.ItchPublisher
{
    /// <summary>Background butler queries: "butler status" for the game and "butler version".</summary>
    public static class ItchRemote
    {
        public static event Action Changed;

        public static bool IsRefreshing { get; private set; }

        public static string ButlerVersion { get; private set; }
        static string butlerVersionFor;
        static bool checkingVersion;

        /// <summary>Re-reads channel versions from itch.io into <see cref="ItchState"/>.</summary>
        public static void Refresh()
        {
            var settings = ItchProjectSettings.instance;
            var exe = Butler.Locate(out _);
            if (IsRefreshing || !settings.HasGame || exe == null || !ItchAccount.HasCredentials) return;

            IsRefreshing = true;
            Changed?.Invoke();

            var key = ItchUserPrefs.ExplicitApiKey();
            var game = settings.GameTarget;
            Task.Run(() => Butler.Run(exe, "status " + Butler.Quote(game), key, timeoutMs: 60000))
                .ContinueWith(t => EditorDispatcher.Post(() =>
                {
                    IsRefreshing = false;
                    var r = t.Result;
                    if (r.Ok) ItchState.SetRemote(Butler.ParseStatus(r.Output), null);
                    else ItchState.SetRemote(null, r.Message);
                    Changed?.Invoke();
                }));
        }

        /// <summary>Refreshes again after a delay (uses update, which also ticks in the background).</summary>
        public static void RefreshLater(double seconds)
        {
            var at = EditorApplication.timeSinceStartup + seconds;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= tick;
                Refresh();
            };
            EditorApplication.update += tick;
        }

        /// <summary>Checks the butler version once per executable path.</summary>
        public static void CheckButlerVersion(bool force = false)
        {
            var exe = Butler.Locate(out _);
            if (exe == null)
            {
                ButlerVersion = null;
                butlerVersionFor = null;
                return;
            }
            if (checkingVersion || (!force && exe == butlerVersionFor)) return;

            checkingVersion = true;
            butlerVersionFor = exe;
            Task.Run(() => Butler.Run(exe, "version", null, timeoutMs: 15000))
                .ContinueWith(t => EditorDispatcher.Post(() =>
                {
                    checkingVersion = false;
                    ButlerVersion = t.Result.Ok ? Butler.ParseVersion(t.Result.Output) ?? "?" : null;
                    Changed?.Invoke();
                }));
        }
    }
}
