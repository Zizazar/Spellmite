using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace GameJam.ItchPublisher
{
    public enum IssueSeverity { Info, Warning, Error }

    public class PreflightIssue
    {
        public IssueSeverity Severity;
        public string Message;
        public string FixLabel;
        public Action Fix;
    }

    /// <summary>Problems that would make a build or an upload fail (or land badly on itch.io).</summary>
    public static class Preflight
    {
        public static List<PreflightIssue> Check(IReadOnlyCollection<ItchTarget> targets, bool build, bool push)
        {
            var issues = new List<PreflightIssue>();
            var settings = ItchProjectSettings.instance;

            void Add(IssueSeverity severity, string message, string fixLabel = null, Action fix = null) =>
                issues.Add(new PreflightIssue { Severity = severity, Message = message, FixLabel = fixLabel, Fix = fix });

            if (targets.Count == 0)
                Add(IssueSeverity.Error, "No build targets are enabled.");

            // ---- channels
            foreach (var group in targets.GroupBy(t => t.channel).Where(g => g.Count() > 1))
                Add(IssueSeverity.Error, $"Channel \"{group.Key}\" is used by more than one target.");

            foreach (var t in targets)
            {
                if (!TargetInfo.IsValidChannel(t.channel))
                {
                    Add(IssueSeverity.Error,
                        $"Channel \"{t.channel}\" is invalid: use lower-case letters, digits, '-', '_' or '.'.",
                        "Use default", () => SetChannel(t, TargetInfo.DefaultChannel(t.target)));
                    continue;
                }

                var tag = TargetInfo.MissingPlatformTag(t);
                if (tag != null)
                    Add(IssueSeverity.Warning,
                        $"Channel \"{t.channel}\" ({TargetInfo.DisplayName(t.target)}) doesn't contain \"{tag}\", so itch.io " +
                        "won't tag its platform automatically.",
                        "Rename to " + TargetInfo.DefaultChannel(t.target),
                        () => SetChannel(t, TargetInfo.DefaultChannel(t.target)));
            }

            // ---- building
            if (build)
            {
                if (!EditorBuildSettings.scenes.Any(s => s.enabled))
                    Add(IssueSeverity.Error, "No scenes are enabled in the build scene list.",
                        "Open Build Profiles", BuildPlayerWindow.ShowBuildPlayerWindow);

                var rootProblem = TargetInfo.BuildRootProblem(settings.BuildRootFull);
                if (rootProblem != null)
                    Add(IssueSeverity.Error, rootProblem + " Change it in the Builds tab.");

                foreach (var t in targets.Where(t => !TargetInfo.IsInstalled(t.target)))
                    Add(IssueSeverity.Error,
                        $"{TargetInfo.DisplayName(t.target)} build support isn't installed. Add the module in Unity Hub " +
                        "(Installs → ⚙ → Add modules).");

                if (targets.Any(t => t.target == BuildTarget.WebGL) && WebGLNeedsFallback)
                    Add(IssueSeverity.Warning,
                        $"WebGL uses {PlayerSettings.WebGL.compressionFormat} compression without Decompression Fallback. " +
                        "If the browser can't decompress the files the game stops at load; the fallback makes it work everywhere.",
                        "Enable fallback", () => PlayerSettings.WebGL.decompressionFallback = true);

                if (targets.Any(t => t.development))
                    Add(IssueSeverity.Info, "Some targets are Development builds (slower, shows a debug overlay).");
            }

            // ---- uploading
            if (push)
            {
                if (Butler.Locate(out _) == null)
                    Add(IssueSeverity.Error, "butler (itch.io's upload tool) isn't installed.", "Install butler",
                        () => Butler.Install((ok, msg) =>
                        {
                            if (!ok) EditorUtility.DisplayDialog("itch.io Publisher", msg, "OK");
                        }));

                if (!ItchAccount.HasCredentials)
                    Add(IssueSeverity.Error, "Not logged in to itch.io. Add an API key or log in on the itch.io tab.",
                        "Open itch.io tab", () => ItchPublisherWindow.Open(ItchPublisherWindow.Tab.Itch));
                else if (ItchAccount.State == AccountState.KeyRejected)
                    Add(IssueSeverity.Error, "itch.io rejected the API key: " + ItchAccount.Error);

                if (!settings.HasGame)
                    Add(IssueSeverity.Error, "No itch.io game page is set.",
                        "Open itch.io tab", () => ItchPublisherWindow.Open(ItchPublisherWindow.Tab.Itch));

                if (string.IsNullOrWhiteSpace(Versioning.Current))
                    Add(IssueSeverity.Error, "Version is empty (Player Settings → Version).");

                if (!build)
                {
                    foreach (var t in targets)
                    {
                        var record = ItchState.Find(t.channel);
                        if (record == null || !record.lastBuildOk || !TargetInfo.HasFiles(TargetInfo.OutputDir(t)))
                            Add(IssueSeverity.Error, $"There is no successful local build for \"{t.channel}\" to upload.");
                    }
                }

                foreach (var t in targets.Where(t => t.target == BuildTarget.WebGL))
                {
                    var record = ItchState.Find(t.channel);
                    if (record == null || string.IsNullOrEmpty(record.remoteUpload))
                        Add(IssueSeverity.Info,
                            $"After the first upload of \"{t.channel}\": on the itch.io Edit game page set Kind of project to HTML " +
                            "and tick \"This file will be played in the browser\" for that file.",
                            "Open edit page", () => UnityEngine.Application.OpenURL(settings.EditUrl));
                }
            }

            return issues;
        }

        static bool WebGLNeedsFallback =>
            PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Disabled &&
            !PlayerSettings.WebGL.decompressionFallback;

        static void SetChannel(ItchTarget t, string channel)
        {
            var settings = ItchProjectSettings.instance;
            var target = settings.targets.FirstOrDefault(x => x.channel == t.channel && x.target == t.target);
            if (target == null || settings.targets.Any(x => x != target && x.channel == channel)) return;
            target.channel = channel;
            settings.SaveNow();
        }
    }
}
