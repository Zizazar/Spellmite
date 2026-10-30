using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>Version shown on itch.io (butler --userversion) = Player Settings → Version.</summary>
    public static class Versioning
    {
        public static string Current => PlayerSettings.bundleVersion;

        /// <summary>
        /// Patch: increments the last number ("0.1.9" → "0.1.10", "1.0-beta3" → "1.0-beta4").
        /// Minor/Major: "1.4.2-rc1" → "1.5.0" / "2.0.0".
        /// </summary>
        public static string Bump(string version, VersionBump kind)
        {
            version = (version ?? "").Trim();
            if (kind == VersionBump.None) return version;
            if (version.Length == 0) return "0.1.0";

            if (kind != VersionBump.Patch)
            {
                var m = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
                if (m.Success)
                {
                    var major = int.Parse(m.Groups[1].Value);
                    var minor = int.Parse(m.Groups[2].Value);
                    return kind == VersionBump.Major ? $"{major + 1}.0.0" : $"{major}.{minor + 1}.0";
                }
            }

            var last = Regex.Match(version, @"(\d+)(?!.*\d)");
            if (!last.Success) return version + ".1";
            var next = (long.Parse(last.Value) + 1).ToString().PadLeft(last.Length, '0');
            return version.Substring(0, last.Index) + next + version.Substring(last.Index + last.Length);
        }

        /// <summary>Writes the new version to Player Settings (and bumps Android's versionCode if enabled).</summary>
        public static string Apply(VersionBump kind)
        {
            if (kind == VersionBump.None) return Current;
            var next = Bump(Current, kind);
            Set(next);

            var settings = ItchProjectSettings.instance;
            if (settings.bumpAndroidVersionCode && settings.EnabledTargets.Any(t => t.target == BuildTarget.Android))
                PlayerSettings.Android.bundleVersionCode++;

            return next;
        }

        public static void Set(string version)
        {
            if (version == PlayerSettings.bundleVersion) return;
            Debug.Log($"[Itch] Version {PlayerSettings.bundleVersion} → {version}");
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
        }
    }
}
