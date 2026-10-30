using System;
using System.IO;
using UnityEditor;

namespace GameJam.ItchPublisher
{
    /// <summary>
    /// Personal settings stored in EditorPrefs on this machine (never in the project).
    /// Shared between all Unity projects of this user, which suits an itch.io account.
    /// </summary>
    public static class ItchUserPrefs
    {
        const string Prefix = "GameJam.ItchPublisher.";

        public static event Action Changed;

        public static string ApiKey
        {
            get => EditorPrefs.GetString(Prefix + "ApiKey", "");
            set { EditorPrefs.SetString(Prefix + "ApiKey", (value ?? "").Trim()); Changed?.Invoke(); }
        }

        public static string ButlerPathOverride
        {
            get => EditorPrefs.GetString(Prefix + "ButlerPath", "");
            set { EditorPrefs.SetString(Prefix + "ButlerPath", (value ?? "").Trim()); Changed?.Invoke(); }
        }

        public static bool ConfirmBeforePublish
        {
            get => EditorPrefs.GetBool(Prefix + "ConfirmBeforePublish", true);
            set => SetBool("ConfirmBeforePublish", value);
        }

        public static bool OpenPageAfterPublish
        {
            get => EditorPrefs.GetBool(Prefix + "OpenPageAfterPublish", false);
            set => SetBool("OpenPageAfterPublish", value);
        }

        public static bool BeepOnFinish
        {
            get => EditorPrefs.GetBool(Prefix + "BeepOnFinish", true);
            set => SetBool("BeepOnFinish", value);
        }

        public static bool DryRun
        {
            get => EditorPrefs.GetBool(Prefix + "DryRun", false);
            set => SetBool("DryRun", value);
        }

        public static bool CheckOnOpen
        {
            get => EditorPrefs.GetBool(Prefix + "CheckOnOpen", true);
            set => SetBool("CheckOnOpen", value);
        }

        static void SetBool(string key, bool value)
        {
            EditorPrefs.SetBool(Prefix + key, value);
            Changed?.Invoke();
        }

        public const string EnvVar = "BUTLER_API_KEY";

        /// <summary>
        /// API key used for butler and the itch.io API:
        /// preferences → BUTLER_API_KEY environment variable → credentials saved by "butler login".
        /// </summary>
        public static string ResolveApiKey(out string source)
        {
            var key = ApiKey;
            if (!string.IsNullOrEmpty(key)) { source = "API key in Preferences"; return key; }

            key = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrWhiteSpace(key)) { source = EnvVar + " environment variable"; return key.Trim(); }

            var creds = Butler.CredentialsPath;
            if (creds != null)
            {
                try
                {
                    key = File.ReadAllText(creds).Trim();
                    if (key.Length > 0) { source = "butler login"; return key; }
                }
                catch { /* unreadable: treat as not logged in */ }
            }

            source = null;
            return null;
        }

        /// <summary>The key to hand to butler explicitly (butler reads its own login file by itself).</summary>
        public static string ExplicitApiKey()
        {
            var key = ResolveApiKey(out var source);
            return source == "butler login" ? null : key;
        }
    }
}
