using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    public enum VersionBump { None, Patch, Minor, Major }

    /// <summary>One platform build that is uploaded to its own itch.io channel.</summary>
    [Serializable]
    public class ItchTarget
    {
        public bool enabled = true;
        public BuildTarget target = BuildTarget.StandaloneWindows64;
        public string channel = "windows";
        public bool development;

        public ItchTarget Clone() => (ItchTarget)MemberwiseClone();
    }

    /// <summary>
    /// Shared project settings, committed to git: ProjectSettings/ItchPublisher.asset.
    /// Nothing secret goes here; the API key lives in <see cref="ItchUserPrefs"/>.
    /// </summary>
    [FilePath("ProjectSettings/ItchPublisher.asset", FilePathAttribute.Location.ProjectFolder)]
    public class ItchProjectSettings : ScriptableSingleton<ItchProjectSettings>
    {
        public static event Action Changed;

        [Header("itch.io")]
        public string itchUser = "";
        public string gameSlug = "";
        public long gameId;

        [Header("Builds")]
        public List<ItchTarget> targets = new();
        public string buildRoot = "Builds";
        public bool cleanBeforeBuild = true;
        public bool restoreActivePlatform = true;

        [Header("Versioning")]
        public VersionBump autoBump = VersionBump.Patch;
        public bool bumpAndroidVersionCode = true;

        [Header("Upload")]
        public bool ifChanged = true;
        public bool excludeDoNotShip = true;
        public string extraIgnore = "";

        [SerializeField] bool initialized;

        public static readonly string ProjectRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');

        void OnEnable()
        {
            if (initialized) return;
            initialized = true;
            targets = new List<ItchTarget>
            {
                new() { target = BuildTarget.StandaloneWindows64, channel = "windows", enabled = true },
                new() { target = BuildTarget.WebGL, channel = "html5", enabled = TargetInfo.IsInstalled(BuildTarget.WebGL) },
                new() { target = BuildTarget.StandaloneLinux64, channel = "linux", enabled = false },
                new() { target = BuildTarget.StandaloneOSX, channel = "mac", enabled = false },
            };
        }

        public void SaveNow()
        {
            Save(true);
            Changed?.Invoke();
        }

        public IEnumerable<ItchTarget> EnabledTargets => targets.Where(t => t.enabled);

        public bool HasGame => !string.IsNullOrWhiteSpace(itchUser) && !string.IsNullOrWhiteSpace(gameSlug);

        /// <summary>butler target, e.g. "user/game".</summary>
        public string GameTarget => $"{itchUser.Trim()}/{gameSlug.Trim()}";

        public string GameUrl => HasGame ? $"https://{itchUser.Trim()}.itch.io/{gameSlug.Trim()}" : null;

        public string EditUrl => gameId > 0 ? $"https://itch.io/game/edit/{gameId}" : "https://itch.io/dashboard";

        public string BuildRootFull
        {
            get
            {
                var root = string.IsNullOrWhiteSpace(buildRoot) ? "Builds" : buildRoot.Trim();
                return Path.GetFullPath(Path.Combine(ProjectRoot, root)).Replace('\\', '/').TrimEnd('/');
            }
        }

        /// <summary>
        /// Accepts "user/game", "https://user.itch.io/game" or "user.itch.io/game".
        /// Returns false if the text doesn't look like any of these.
        /// </summary>
        public static bool TryParseGame(string text, out string user, out string slug)
        {
            user = slug = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0) text = text.Substring(schemeEnd + 3);
            text = text.Split('?', '#')[0].TrimEnd('/');

            var parts = text.Split('/');
            if (parts.Length < 2) return false;

            const string host = ".itch.io";
            if (parts[0].EndsWith(host, StringComparison.OrdinalIgnoreCase))
                user = parts[0].Substring(0, parts[0].Length - host.Length);
            else if (parts.Length == 2 && !parts[0].Contains('.'))
                user = parts[0];
            else
                return false;

            slug = parts[1];
            user = user.ToLowerInvariant();
            return user.Length > 0 && slug.Length > 0;
        }
    }
}
