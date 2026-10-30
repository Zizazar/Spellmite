using System;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>
    /// Command-line / CI entry points (same settings as the window):
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod GameJam.ItchPublisher.ItchPublisherCli.BuildAndPublish
    ///       [-itchChannels windows,html5] [-itchVersion 1.2.3] [-itchBump none|patch|minor|major]
    ///       [-itchGame user/game] [-itchDryRun] [-itchForce]
    /// </code>
    /// Authentication: BUTLER_API_KEY environment variable (or a prior "butler login").
    /// Exit code: 0 success, 1 a build or upload failed, 2 configuration error.
    /// </summary>
    public static class ItchPublisherCli
    {
        public static void BuildAndPublish() => Run(build: true, push: true);

        public static void BuildOnly() => Run(build: true, push: false);

        public static void PublishLastBuilds() => Run(build: false, push: true);

        static void Run(bool build, bool push)
        {
            try
            {
                var settings = ItchProjectSettings.instance;

                var game = Arg("-itchGame");
                if (game != null)
                {
                    if (!ItchProjectSettings.TryParseGame(game, out settings.itchUser, out settings.gameSlug))
                        Fail(2, "Invalid -itchGame, expected user/game: " + game);
                }

                var targets = settings.EnabledTargets.Select(t => t.Clone()).ToList();
                var channels = Arg("-itchChannels");
                if (channels != null)
                {
                    var wanted = channels.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
                    targets = settings.targets.Where(t => wanted.Contains(t.channel)).Select(t => t.Clone()).ToList();
                    var unknown = wanted.Except(targets.Select(t => t.channel)).ToList();
                    if (unknown.Count > 0) Fail(2, "Unknown channel(s): " + string.Join(", ", unknown));
                }

                var dryRun = Environment.GetCommandLineArgs().Contains("-itchDryRun");
                var errors = Preflight.Check(targets, build, push).Where(i => i.Severity == IssueSeverity.Error).ToList();
                if (errors.Count > 0) Fail(2, "Preflight failed:\n" + string.Join("\n", errors.Select(e => "  • " + e.Message)));

                var explicitVersion = Arg("-itchVersion");
                var bump = Enum.TryParse<VersionBump>(Arg("-itchBump") ?? "", true, out var b) ? b : settings.autoBump;

                var force = Environment.GetCommandLineArgs().Contains("-itchForce");

                if (build)
                {
                    if (explicitVersion != null) Versioning.Set(explicitVersion);
                    else if (push) Versioning.Apply(bump);
                    var version = Versioning.Current;

                    Log($"Building {targets.Count} target(s), version {version}");
                    var builds = PublishPipeline.BuildAll(targets, version);
                    foreach (var r in builds.Where(r => !r.Ok)) Log($"✗ {r.Target.channel}: {r.Error}");
                    if (builds.Any(r => !r.Ok)) Fail(1, "Build failed");
                }

                if (push)
                {
                    var jobs = targets.Select(t => PushJob.Create(t,
                        build ? Versioning.Current : explicitVersion ?? ItchState.Find(t.channel)?.lastBuildVersion ?? Versioning.Current,
                        force)).ToList();
                    var ctx = PushContext.Create(dryRun);
                    var results = PublishPipeline.PushAll(jobs, ctx, CancellationToken.None, null, null, Log);
                    if (results.Any(r => !r.Ok)) Fail(1, "Upload failed");
                }

                Log("Done");
                EditorApplication.Exit(0);
            }
            catch (CliExit e)
            {
                EditorApplication.Exit(e.Code);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        class CliExit : Exception
        {
            public readonly int Code;
            public CliExit(int code) => Code = code;
        }

        static void Fail(int code, string message)
        {
            Debug.LogError("[Itch] " + message);
            throw new CliExit(code);
        }

        static void Log(string message) => Debug.Log("[Itch] " + message);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
