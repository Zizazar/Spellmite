using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GameJam.ItchPublisher
{
    [Serializable]
    public class ItchUser
    {
        public long id;
        public string username;
        public string display_name;
        public string url;
    }

    [Serializable]
    public class ItchGame
    {
        public long id;
        public string title;
        public string url;
        public string short_text;
        public string classification;
        public string type;
        public bool published;
        public long views_count;
        public long downloads_count;
        public long purchases_count;
    }

    public enum AccountState { Unknown, Checking, NotLoggedIn, Ok, Unverified, KeyRejected, Offline }

    /// <summary>
    /// itch.io server-side API (https://itch.io/docs/api/serverside): who is logged in, and their games.
    /// Callbacks run on the main thread.
    /// </summary>
    public static class ItchAccount
    {
        public static event Action Changed;

        public static AccountState State { get; private set; } = AccountState.Unknown;
        public static ItchUser User { get; private set; }
        public static ItchGame[] Games { get; private set; }
        public static string Error { get; private set; }
        public static string KeySource { get; private set; }

        public static bool HasCredentials => !string.IsNullOrEmpty(ItchUserPrefs.ResolveApiKey(out _));

        public static string StateText => State switch
        {
            AccountState.Unknown => "Not checked",
            AccountState.Checking => "Checking…",
            AccountState.NotLoggedIn => "Not logged in",
            AccountState.Ok => User?.username ?? "Logged in",
            AccountState.Unverified => "Logged in (butler)",
            AccountState.KeyRejected => "API key rejected",
            AccountState.Offline => "Offline",
            _ => State.ToString(),
        };

        /// <summary>The game from <see cref="Games"/> that matches the project settings, if loaded.</summary>
        public static ItchGame CurrentGame
        {
            get
            {
                var s = ItchProjectSettings.instance;
                if (Games == null || !s.HasGame) return null;
                return Games.FirstOrDefault(g => g.id == s.gameId)
                       ?? Games.FirstOrDefault(g => SameGame(g, s.itchUser, s.gameSlug));
            }
        }

        public static bool SameGame(ItchGame game, string user, string slug) =>
            ItchProjectSettings.TryParseGame(game.url, out var u, out var s)
            && string.Equals(u, user?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(s, slug?.Trim(), StringComparison.OrdinalIgnoreCase);

        public static void Refresh()
        {
            if (State == AccountState.Checking) return;

            var key = ItchUserPrefs.ResolveApiKey(out var source);
            KeySource = source;
            User = null;
            Games = null;
            Error = null;

            if (string.IsNullOrEmpty(key))
            {
                Set(AccountState.NotLoggedIn);
                return;
            }

            Set(AccountState.Checking);
            Get<ProfileResponse>(key, "https://api.itch.io/profile", (profile, error, offline) =>
            {
                if (profile?.user == null)
                {
                    Error = error;
                    // A key saved by "butler login" may be limited to uploads; butler still works with it.
                    Set(offline ? AccountState.Offline
                        : source == "butler login" ? AccountState.Unverified
                        : AccountState.KeyRejected);
                    return;
                }

                User = profile.user;
                Set(AccountState.Ok);
                FillProjectDefaults();

                Get<GamesResponse>(key, "https://api.itch.io/profile/games", (games, gamesError, _) =>
                {
                    Games = games?.games ?? Array.Empty<ItchGame>();
                    if (games == null) Error = gamesError;
                    FillProjectDefaults();
                    Changed?.Invoke();
                });
            });
        }

        /// <summary>Fills in the itch user name and game id where the project settings leave them empty.</summary>
        static void FillProjectDefaults()
        {
            var s = ItchProjectSettings.instance;
            var dirty = false;
            if (string.IsNullOrWhiteSpace(s.itchUser) && User != null)
            {
                s.itchUser = User.username;
                dirty = true;
            }
            var game = CurrentGame;
            if (game != null && s.gameId != game.id)
            {
                s.gameId = game.id;
                dirty = true;
            }
            if (dirty) s.SaveNow();
        }

        static void Set(AccountState state)
        {
            State = state;
            Changed?.Invoke();
        }

        [Serializable] class ProfileResponse { public ItchUser user; }
        [Serializable] class GamesResponse { public ItchGame[] games; }
        [Serializable] class ErrorResponse { public string[] errors; }

        static void Get<T>(string key, string url, Action<T, string, bool> done) where T : class
        {
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", "Bearer " + key);
            request.timeout = 20;
            request.SendWebRequest().completed += _ =>
            {
                T value = null;
                string error = null;
                var offline = request.result == UnityWebRequest.Result.ConnectionError;
                var text = request.downloadHandler?.text;
                try
                {
                    var errors = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<ErrorResponse>(text)?.errors;
                    if (errors is { Length: > 0 }) error = string.Join(", ", errors);
                    else if (request.result != UnityWebRequest.Result.Success) error = request.error;
                    else value = JsonUtility.FromJson<T>(text);
                }
                catch (Exception e)
                {
                    error = "Unexpected response: " + e.Message;
                }
                finally
                {
                    request.Dispose();
                }
                done(value, error, offline);
            };
        }
    }
}
