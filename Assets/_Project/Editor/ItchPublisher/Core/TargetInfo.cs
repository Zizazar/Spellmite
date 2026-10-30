using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>Per-platform knowledge: names, default channels, output layout, icons.</summary>
    public static class TargetInfo
    {
        /// <summary>Platforms itch.io can host.</summary>
        public static readonly BuildTarget[] Supported =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneWindows,
            BuildTarget.StandaloneLinux64,
            BuildTarget.StandaloneOSX,
            BuildTarget.WebGL,
            BuildTarget.Android,
        };

        public static string DisplayName(BuildTarget t) => t switch
        {
            BuildTarget.StandaloneWindows64 => "Windows (64-bit)",
            BuildTarget.StandaloneWindows => "Windows (32-bit)",
            BuildTarget.StandaloneLinux64 => "Linux",
            BuildTarget.StandaloneOSX => "macOS",
            BuildTarget.WebGL => "Web (HTML5)",
            BuildTarget.Android => "Android",
            _ => ObjectNames.NicifyVariableName(t.ToString()),
        };

        public static string DefaultChannel(BuildTarget t) => t switch
        {
            BuildTarget.StandaloneWindows64 => "windows",
            BuildTarget.StandaloneWindows => "windows-32",
            BuildTarget.StandaloneLinux64 => "linux",
            BuildTarget.StandaloneOSX => "mac",
            BuildTarget.WebGL => "html5",
            BuildTarget.Android => "android",
            _ => t.ToString().ToLowerInvariant(),
        };

        public static GUIContent Icon(BuildTarget t)
        {
            var name = t switch
            {
                BuildTarget.WebGL => "BuildSettings.WebGL.Small",
                BuildTarget.Android => "BuildSettings.Android.Small",
                _ => "BuildSettings.Standalone.Small",
            };
            return EditorGUIUtility.IconContent(name);
        }

        public static BuildTargetGroup Group(BuildTarget t) => BuildPipeline.GetBuildTargetGroup(t);

        public static bool IsInstalled(BuildTarget t) => BuildPipeline.IsBuildTargetSupported(Group(t), t);

        /// <summary>
        /// itch.io tags a channel's platform from its name (win / linux / mac or osx / android).
        /// Returns null if the name already tags the right platform (or the platform has no tag).
        /// </summary>
        public static string MissingPlatformTag(ItchTarget t)
        {
            var c = (t.channel ?? "").ToLowerInvariant();
            return t.target switch
            {
                BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => c.Contains("win") ? null : "win",
                BuildTarget.StandaloneLinux64 => c.Contains("linux") ? null : "linux",
                BuildTarget.StandaloneOSX => c.Contains("mac") || c.Contains("osx") ? null : "mac",
                BuildTarget.Android => c.Contains("android") ? null : "android",
                _ => null,
            };
        }

        static readonly Regex ChannelPattern = new("^[a-z0-9][a-z0-9._-]*$");

        public static bool IsValidChannel(string channel) => !string.IsNullOrEmpty(channel) && ChannelPattern.IsMatch(channel);

        /// <summary>Folder that is built into and then pushed as a whole.</summary>
        public static string OutputDir(ItchTarget t) =>
            ItchProjectSettings.instance.BuildRootFull + "/" + t.channel;

        /// <summary>BuildPlayerOptions.locationPathName for this target.</summary>
        public static string LocationPath(ItchTarget t)
        {
            var dir = OutputDir(t);
            var name = ProductFileName;
            return t.target switch
            {
                BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => $"{dir}/{name}.exe",
                BuildTarget.StandaloneLinux64 => $"{dir}/{name}.x86_64",
                BuildTarget.StandaloneOSX => $"{dir}/{name}.app",
                BuildTarget.Android => $"{dir}/{name}.apk",
                BuildTarget.WebGL => dir,
                _ => $"{dir}/{name}",
            };
        }

        /// <summary>Executable to run a local build, or null when it can't be started directly.</summary>
        public static string RunnablePath(ItchTarget t)
        {
            var currentOs = Application.platform;
            var path = LocationPath(t);
            var runnable = t.target switch
            {
                BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => currentOs == RuntimePlatform.WindowsEditor,
                BuildTarget.StandaloneLinux64 => currentOs == RuntimePlatform.LinuxEditor,
                BuildTarget.StandaloneOSX => currentOs == RuntimePlatform.OSXEditor,
                _ => false,
            };
            return runnable && (File.Exists(path) || Directory.Exists(path)) ? path : null;
        }

        public static string ProductFileName
        {
            get
            {
                var invalid = Path.GetInvalidFileNameChars();
                var name = new string((PlayerSettings.productName ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
                return name.Length > 0 ? name : "Game";
            }
        }

        /// <summary>Unity's debug-data folders next to a player; they are not uploaded by default.</summary>
        public static bool IsDoNotShipFolder(string name) =>
            name.EndsWith("_DoNotShip", StringComparison.Ordinal) ||
            name.EndsWith("_ButDontShipItWithYourGame", StringComparison.Ordinal);

        /// <summary>Size of what will be uploaded (without the DoNotShip folders when they are excluded).</summary>
        public static long DirectorySize(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                var skip = ItchProjectSettings.instance.excludeDoNotShip;
                var info = new DirectoryInfo(dir);
                return info.EnumerateFiles().Sum(f => f.Length) +
                       info.EnumerateDirectories()
                           .Where(d => !skip || !IsDoNotShipFolder(d.Name))
                           .SelectMany(d => d.EnumerateFiles("*", SearchOption.AllDirectories))
                           .Sum(f => f.Length);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static bool HasFiles(string dir) =>
            Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();

        /// <summary>
        /// The build root must not be (or contain) the project or any of its Unity folders:
        /// "Clean before build" deletes channel folders inside it.
        /// </summary>
        public static string BuildRootProblem(string root)
        {
            var project = ItchProjectSettings.ProjectRoot.TrimEnd('/');
            var r = root.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(r, project, StringComparison.OrdinalIgnoreCase))
                return "The build folder can't be the project folder itself.";
            if (project.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
                return "The build folder can't contain the project.";
            foreach (var reserved in new[] { "Assets", "Library", "Packages", "ProjectSettings", "UserSettings", "Temp", "Logs" })
            {
                var path = project + "/" + reserved;
                if (string.Equals(r, path, StringComparison.OrdinalIgnoreCase) ||
                    r.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                    return $"The build folder can't be inside {reserved}/.";
            }
            return null;
        }
    }
}
