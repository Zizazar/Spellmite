using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameJam.ItchPublisher
{
    public enum PipelinePhase { Idle, Building, Uploading, Finished }

    public class PublishRequest
    {
        public List<ItchTarget> Targets;
        public bool Build = true;
        public bool Push = true;
        public bool DryRun;
        /// <summary>Upload even if itch.io already has identical files.</summary>
        public bool Force;

        public string Label => Build && Push ? "Build & publish" : Build ? "Build" : "Publish";
    }

    public class BuildOutcome
    {
        public ItchTarget Target;
        public bool Ok;
        public bool Cancelled;
        public string Error;
        public long Bytes;
        public float Seconds;
    }

    public class PushJob
    {
        public ItchTarget Target;
        public string Dir;
        public string Version;
        /// <summary>Pass --if-changed: butler skips the push when the files equal the latest itch.io build.</summary>
        public bool AllowSkip;

        /// <summary>
        /// --if-changed only compares files, not the version. Allow skipping only when itch.io already has
        /// this version too; otherwise a byte-identical rebuild would leave the old version number on itch.io.
        /// Main thread only.
        /// </summary>
        public static PushJob Create(ItchTarget target, string version, bool force)
        {
            var record = ItchState.Find(target.channel);
            var sameVersion = record != null &&
                              (record.remoteVersion == version || record.lastUploadedVersion == version);
            return new PushJob
            {
                Target = target,
                Dir = TargetInfo.OutputDir(target),
                Version = version,
                AllowSkip = !force && ItchProjectSettings.instance.ifChanged && sameVersion,
            };
        }
    }

    public class PushOutcome
    {
        public PushJob Job;
        public bool Ok;
        public bool Skipped;
        public bool Cancelled;
        public string Error;
    }

    /// <summary>Everything an upload needs, captured on the main thread.</summary>
    public class PushContext
    {
        public string Butler;
        public string ApiKey;
        public string Game;
        public bool DryRun;
        public List<string> Ignore = new();

        public static PushContext Create(bool dryRun)
        {
            var s = ItchProjectSettings.instance;
            var ctx = new PushContext
            {
                Butler = ItchPublisher.Butler.Locate(out _),
                ApiKey = ItchUserPrefs.ExplicitApiKey(),
                Game = s.GameTarget,
                DryRun = dryRun,
            };
            if (s.excludeDoNotShip)
            {
                // Unity puts these next to Windows/Linux players; they are for crash symbolication, not players.
                ctx.Ignore.Add("*_BurstDebugInformation_DoNotShip");
                ctx.Ignore.Add("*_BackUpThisFolder_ButDontShipItWithYourGame");
            }
            ctx.Ignore.AddRange((s.extraIgnore ?? "")
                .Split('\n', ',', ';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));
            return ctx;
        }
    }

    /// <summary>
    /// Build → upload pipeline. Builds run on the main thread (Unity requires it), uploads run
    /// butler on a background thread; script reloads are locked meanwhile so they aren't cut off.
    /// </summary>
    public static class PublishPipeline
    {
        public static event Action Changed;

        public static bool IsRunning { get; private set; }
        public static PipelinePhase Phase { get; private set; }
        public static string Status { get; private set; } = "";
        public static HistoryEntry LastRun { get; private set; }

        /// <summary>Progress of the current phase, 0..1.</summary>
        public static float PhaseProgress => phaseProgress;

        public static bool CanCancel => IsRunning && Phase == PipelinePhase.Uploading && cts is { IsCancellationRequested: false };

        static volatile float phaseProgress;
        static volatile bool dirty;
        static double lastTick;

        static PublishRequest active;
        static CancellationTokenSource cts;
        static int progressId = -1;
        static bool reloadLocked;
        static DateTime startedUtc;
        static List<string> details = new();
        static string runVersion;

        const string InterruptedKey = "GameJam.ItchPublisher.Interrupted";

        // ---------------------------------------------------------------- log

        const int MaxLog = 1500;
        static readonly List<string> LogLines = new();

        public static string[] LogSnapshot()
        {
            lock (LogLines) return LogLines.ToArray();
        }

        public static int LogCount
        {
            get { lock (LogLines) return LogLines.Count; }
        }

        public static void ClearLog()
        {
            lock (LogLines) LogLines.Clear();
            Changed?.Invoke();
        }

        static bool echoToConsole;

        /// <summary>Thread-safe.</summary>
        public static void AppendLog(string line)
        {
            if (echoToConsole) Debug.Log("[Itch] " + line); // batch mode: the log file is all there is
            lock (LogLines)
            {
                LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
                if (LogLines.Count > MaxLog) LogLines.RemoveRange(0, LogLines.Count - MaxLog);
            }
            dirty = true;
        }

        // ---------------------------------------------------------------- lifecycle

        [InitializeOnLoadMethod]
        static void Init()
        {
            echoToConsole = Application.isBatchMode;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            LastRun = ItchState.History.FirstOrDefault();

            var interrupted = SessionState.GetString(InterruptedKey, "");
            if (!string.IsNullOrEmpty(interrupted))
            {
                SessionState.EraseString(InterruptedKey);
                AppendLog(interrupted);
                Debug.LogWarning("[Itch] " + interrupted);
            }
        }

        static void Tick()
        {
            if (!dirty) return;
            // Butler reports progress many times per second; 10 repaints a second are plenty.
            if (EditorApplication.timeSinceStartup - lastTick < 0.1) return;
            lastTick = EditorApplication.timeSinceStartup;
            dirty = false;
            if (progressId >= 0 && Progress.Exists(progressId))
                Progress.Report(progressId, phaseProgress, Status);
            Changed?.Invoke();
        }

        static void OnBeforeReload()
        {
            if (!IsRunning || Phase != PipelinePhase.Uploading) return;
            // Normally impossible (reloads are locked), but e.g. entering Play Mode can force one.
            Butler.KillCurrent();
            const string message = "The upload was interrupted by a script reload. Run it again.";
            SessionState.SetString(InterruptedKey, message);
            if (progressId >= 0 && Progress.Exists(progressId)) Progress.Finish(progressId, Progress.Status.Failed);
            ItchState.AddHistory(NewEntry(false, message));
        }

        // ---------------------------------------------------------------- start

        public static void BuildAndPublish(IEnumerable<ItchTarget> targets = null) =>
            Start(new PublishRequest { Targets = List(targets), Build = true, Push = true, DryRun = ItchUserPrefs.DryRun });

        public static void BuildOnly(IEnumerable<ItchTarget> targets = null) =>
            Start(new PublishRequest { Targets = List(targets), Build = true, Push = false });

        public static void PublishLastBuilds(IEnumerable<ItchTarget> targets = null) =>
            Start(new PublishRequest { Targets = List(targets), Build = false, Push = true, DryRun = ItchUserPrefs.DryRun });

        static List<ItchTarget> List(IEnumerable<ItchTarget> targets) =>
            (targets ?? ItchProjectSettings.instance.EnabledTargets).Select(t => t.Clone()).ToList();

        /// <summary>Interactive start: checks, asks for confirmation, then runs on the next editor tick.</summary>
        public static bool Start(PublishRequest request)
        {
            if (IsRunning)
            {
                EditorUtility.DisplayDialog("itch.io Publisher", "Something is already running.", "OK");
                return false;
            }

            var issues = Preflight.Check(request.Targets, request.Build, request.Push);
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Can't start " + request.Label.ToLowerInvariant(),
                    string.Join("\n\n", errors.Select(e => "• " + e.Message)), "OK");
                ItchPublisherWindow.Open(ItchPublisherWindow.Tab.Dashboard);
                return false;
            }

            if (request.Push && !request.DryRun && ItchUserPrefs.ConfirmBeforePublish)
            {
                var settings = ItchProjectSettings.instance;
                var version = request.Build
                    ? Versioning.Bump(Versioning.Current, settings.autoBump)
                    : string.Join(", ", request.Targets.Select(t => ItchState.Find(t.channel)?.lastBuildVersion).Distinct());
                var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => "⚠ " + i.Message).ToList();
                var text = new StringBuilder()
                    .AppendLine($"Upload version {version} to {settings.GameTarget}?")
                    .AppendLine()
                    .AppendLine("Channels: " + string.Join(", ", request.Targets.Select(t => t.channel)));
                if (request.Build) text.AppendLine("Everything is rebuilt first.");
                if (warnings.Count > 0) text.AppendLine().AppendLine(string.Join("\n", warnings));
                if (!EditorUtility.DisplayDialog("Publish to itch.io", text.ToString(), "Publish", "Cancel")) return false;
            }

            if (request.Build && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            IsRunning = true;
            active = request;
            startedUtc = DateTime.UtcNow;
            details = new List<string>();
            runVersion = null;
            SetPhase(request.Build ? PipelinePhase.Building : PipelinePhase.Uploading, "Starting…", 0);
            AppendLog($"=== {request.Label}{(request.DryRun ? " (dry run)" : "")}: " +
                      string.Join(", ", request.Targets.Select(t => t.channel)));

            // BuildPlayer must not run inside OnGUI or a menu callback: start on the next editor tick.
            EditorApplication.CallbackFunction once = null;
            once = () =>
            {
                EditorApplication.update -= once;
                Run(request);
            };
            EditorApplication.update += once;
            return true;
        }

        static void Run(PublishRequest request)
        {
            try
            {
                List<PushJob> jobs;
                if (request.Build)
                {
                    var version = request.Push ? Versioning.Apply(ItchProjectSettings.instance.autoBump) : Versioning.Current;
                    runVersion = version;
                    var builds = BuildAll(request.Targets, version, (i, n, t) =>
                    {
                        SetPhase(PipelinePhase.Building, $"Building {TargetInfo.DisplayName(t.target)} ({i + 1}/{n})", (float)i / n);
                        Tick();
                    });
                    foreach (var b in builds)
                        details.Add(b.Ok
                            ? $"✓ built {b.Target.channel} ({ItchState.Size(b.Bytes)}, {b.Seconds:0}s)"
                            : $"✗ build {b.Target.channel}: {b.Error}");

                    if (builds.Any(b => b.Cancelled))
                    {
                        Finish(false, "Build cancelled", cancelled: true);
                        return;
                    }
                    jobs = builds.Where(b => b.Ok).Select(b => PushJob.Create(b.Target, version, request.Force)).ToList();

                    if (!request.Push)
                    {
                        var failed = builds.Count(b => !b.Ok);
                        Finish(failed == 0, failed == 0
                            ? $"Built {builds.Count} target(s), version {version}"
                            : $"{failed} of {builds.Count} build(s) failed");
                        return;
                    }
                    if (jobs.Count == 0)
                    {
                        Finish(false, "All builds failed, nothing was uploaded");
                        return;
                    }
                }
                else
                {
                    jobs = request.Targets
                        .Select(t => PushJob.Create(t, ItchState.Find(t.channel)?.lastBuildVersion ?? Versioning.Current, request.Force))
                        .ToList();
                    runVersion = string.Join(", ", jobs.Select(j => j.Version).Distinct());
                }

                StartUploads(jobs, request);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(false, "Error: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- build (main thread)

        /// <summary>Builds the targets one after another; the active platform goes first to avoid a switch.</summary>
        public static List<BuildOutcome> BuildAll(IReadOnlyList<ItchTarget> targets, string version,
            Action<int, int, ItchTarget> onStep = null)
        {
            var originalTarget = EditorUserBuildSettings.activeBuildTarget;
            var ordered = targets.OrderBy(t => t.target == originalTarget ? 0 : 1).ToList();
            var results = new List<BuildOutcome>();

            for (var i = 0; i < ordered.Count; i++)
            {
                onStep?.Invoke(i, ordered.Count, ordered[i]);
                var outcome = Build(ordered[i], version);
                results.Add(outcome);
                if (outcome.Cancelled) break;
            }

            if (ItchProjectSettings.instance.restoreActivePlatform && !Application.isBatchMode &&
                EditorUserBuildSettings.activeBuildTarget != originalTarget)
            {
                AppendLog("Switching the editor back to " + TargetInfo.DisplayName(originalTarget));
                EditorUserBuildSettings.SwitchActiveBuildTarget(TargetInfo.Group(originalTarget), originalTarget);
            }
            return results;
        }

        static BuildOutcome Build(ItchTarget t, string version)
        {
            var outcome = new BuildOutcome { Target = t };
            var dir = TargetInfo.OutputDir(t);
            var watch = Stopwatch.StartNew();
            AppendLog($"Building {TargetInfo.DisplayName(t.target)} → {dir}");

            try
            {
                if (ItchProjectSettings.instance.cleanBeforeBuild && Directory.Exists(dir) &&
                    TargetInfo.BuildRootProblem(ItchProjectSettings.instance.BuildRootFull) == null)
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception e) { throw new IOException($"Couldn't clean {dir} (is the game still running?): {e.Message}"); }
                }
                Directory.CreateDirectory(dir);

                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = TargetInfo.LocationPath(t),
                    target = t.target,
                    targetGroup = TargetInfo.Group(t.target),
                    options = t.development ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var result = report.summary.result;

                outcome.Ok = result == BuildResult.Succeeded;
                outcome.Cancelled = result == BuildResult.Cancelled;
                if (!outcome.Ok)
                {
                    outcome.Error = report.steps
                        .SelectMany(s => s.messages)
                        .Where(m => m.type is LogType.Error or LogType.Exception)
                        .Select(m => m.content.Split('\n')[0])
                        .FirstOrDefault() ?? result.ToString();
                }
            }
            catch (Exception e)
            {
                outcome.Error = e.Message;
            }

            outcome.Seconds = (float)watch.Elapsed.TotalSeconds;
            outcome.Bytes = outcome.Ok ? TargetInfo.DirectorySize(dir) : 0;
            AppendLog(outcome.Ok
                ? $"✓ {t.channel}: built in {outcome.Seconds:0}s, {ItchState.Size(outcome.Bytes)}"
                : $"✗ {t.channel}: {outcome.Error}");

            var record = ItchState.Get(t.channel);
            record.lastBuildUtc = ItchState.FormatUtc(DateTime.UtcNow);
            record.lastBuildOk = outcome.Ok;
            record.lastBuildBytes = outcome.Bytes;
            record.lastBuildSeconds = outcome.Seconds;
            record.lastBuildVersion = version;
            record.lastBuildError = outcome.Error;
            record.lastBuildDevelopment = t.development;
            ItchState.Save();
            return outcome;
        }

        // ---------------------------------------------------------------- upload (background thread)

        static void StartUploads(List<PushJob> jobs, PublishRequest request)
        {
            var ctx = PushContext.Create(request.DryRun);
            SetPhase(PipelinePhase.Uploading, "Uploading…", 0);

            EditorApplication.LockReloadAssemblies();
            reloadLocked = true;
            cts = new CancellationTokenSource();
            var token = cts.Token;

            progressId = Progress.Start("itch.io upload", ctx.Game + (ctx.DryRun ? " (dry run)" : ""));
            Progress.RegisterCancelCallback(progressId, () =>
            {
                Cancel();
                return true;
            });

            Task.Run(() => PushAll(jobs, ctx, token,
                    (i, n, job) =>
                    {
                        Status = $"Uploading {job.Target.channel} ({i + 1}/{n})";
                        phaseProgress = 0;
                        dirty = true;
                    },
                    p =>
                    {
                        phaseProgress = p;
                        dirty = true;
                    },
                    AppendLog))
                .ContinueWith(t => EditorDispatcher.Post(() =>
                    FinishUploads(t.IsFaulted ? null : t.Result, t.Exception?.GetBaseException(), request.DryRun)));
        }

        /// <summary>Pushes the builds one by one. Safe on any thread; used by the CLI too.</summary>
        public static List<PushOutcome> PushAll(List<PushJob> jobs, PushContext ctx, CancellationToken token,
            Action<int, int, PushJob> onStep, Action<float> onProgress, Action<string> log)
        {
            var results = new List<PushOutcome>();
            for (var i = 0; i < jobs.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    results.AddRange(jobs.Skip(i).Select(j => new PushOutcome { Job = j, Cancelled = true, Error = "cancelled" }));
                    break;
                }
                onStep?.Invoke(i, jobs.Count, jobs[i]);
                results.Add(Push(jobs[i], ctx, token, onProgress, log));
            }
            return results;
        }

        [Serializable]
        class ButlerMessage
        {
            public string type;
            public string level;
            public string message;
            public float progress;
        }

        static PushOutcome Push(PushJob job, PushContext ctx, CancellationToken token, Action<float> onProgress, Action<string> log)
        {
            var target = $"{ctx.Game}:{job.Target.channel}";
            var args = new StringBuilder("--json push ")
                .Append(Butler.Quote(job.Dir)).Append(' ')
                .Append(Butler.Quote(target))
                .Append(" --userversion ").Append(Butler.Quote(job.Version));
            if (job.AllowSkip) args.Append(" --if-changed");
            if (ctx.DryRun) args.Append(" --dry-run");
            foreach (var pattern in ctx.Ignore) args.Append(" --ignore ").Append(Butler.Quote(pattern));

            log?.Invoke($"→ {target} v{job.Version}{(ctx.DryRun ? " (dry run)" : "")}");

            string lastError = null;
            var skipped = false;
            var result = Butler.Run(ctx.Butler, args.ToString(), ctx.ApiKey, line =>
            {
                ButlerMessage msg = null;
                if (line.StartsWith("{"))
                {
                    try { msg = JsonUtility.FromJson<ButlerMessage>(line); }
                    catch { /* not JSON after all */ }
                }

                if (msg?.type == "progress")
                {
                    onProgress?.Invoke(Mathf.Clamp01(msg.progress));
                    return;
                }

                var text = msg != null ? msg.message : line;
                if (string.IsNullOrWhiteSpace(text)) return;
                if (msg != null && (msg.type == "error" || msg.level == "error")) lastError = text;
                if (text.IndexOf("not pushing", StringComparison.OrdinalIgnoreCase) >= 0) skipped = true;
                log?.Invoke("   " + text);
            }, token);

            var outcome = new PushOutcome
            {
                Job = job,
                Ok = result.Ok,
                Cancelled = result.Cancelled,
                Skipped = result.Ok && skipped,
            };
            if (!result.Ok) outcome.Error = lastError ?? result.Message;

            log?.Invoke(outcome.Ok
                ? outcome.Skipped ? $"= {job.Target.channel}: unchanged, skipped" : $"✓ {job.Target.channel}: uploaded"
                : $"✗ {job.Target.channel}: {outcome.Error}");
            return outcome;
        }

        public static void Cancel()
        {
            if (!CanCancel) return;
            cts.Cancel();
            Status = "Cancelling…";
            AppendLog("Cancelling…");
        }

        // ---------------------------------------------------------------- finish (main thread)

        static void FinishUploads(List<PushOutcome> results, Exception error, bool dryRun)
        {
            if (reloadLocked)
            {
                EditorApplication.UnlockReloadAssemblies();
                reloadLocked = false;
            }
            cts?.Dispose();
            cts = null;

            if (results == null)
            {
                if (error != null) Debug.LogException(error);
                FinishProgress(Progress.Status.Failed);
                Finish(false, "Upload error: " + error?.Message);
                return;
            }

            if (!dryRun)
            {
                foreach (var r in results.Where(r => !r.Cancelled))
                {
                    var record = ItchState.Get(r.Job.Target.channel);
                    record.lastPushUtc = ItchState.FormatUtc(DateTime.UtcNow);
                    record.lastPushOk = r.Ok;
                    record.lastPushSkipped = r.Skipped;
                    record.lastPushVersion = r.Job.Version;
                    record.lastPushError = r.Error;
                    if (r.Ok && !r.Skipped)
                    {
                        record.lastUploadedUtc = record.lastPushUtc;
                        record.lastUploadedVersion = r.Job.Version;
                    }
                }
                ItchState.Save();
            }

            foreach (var r in results)
                details.Add(r.Ok
                    ? r.Skipped ? $"= {r.Job.Target.channel}: unchanged" : $"✓ uploaded {r.Job.Target.channel}"
                    : $"✗ upload {r.Job.Target.channel}: {r.Error}");

            var cancelled = results.Any(r => r.Cancelled);
            var failed = results.Count(r => !r.Ok && !r.Cancelled);
            var ok = failed == 0 && !cancelled && details.All(d => !d.StartsWith("✗"));
            var uploaded = results.Count(r => r.Ok && !r.Skipped);

            string summary;
            if (cancelled) summary = "Upload cancelled";
            else if (failed > 0) summary = $"{failed} of {results.Count} upload(s) failed";
            else if (!ok) summary = $"Uploaded {uploaded}, but some builds failed";
            else if (dryRun) summary = $"Dry run OK for {results.Count} channel(s), nothing was uploaded";
            else if (uploaded == 0) summary = "Nothing changed, nothing was uploaded";
            else summary = $"Published v{runVersion} to {uploaded} channel(s)";

            FinishProgress(ok ? Progress.Status.Succeeded : cancelled ? Progress.Status.Canceled : Progress.Status.Failed);
            Finish(ok, summary, cancelled);

            if (!dryRun && uploaded > 0)
            {
                // itch.io processes a new build for a while; until then "butler status" shows the old one.
                ItchRemote.Refresh();
                ItchRemote.RefreshLater(30);
                ItchRemote.RefreshLater(90);
                if (ok && ItchUserPrefs.OpenPageAfterPublish && ItchProjectSettings.instance.GameUrl != null)
                    Application.OpenURL(ItchProjectSettings.instance.GameUrl);
            }
        }

        static void FinishProgress(Progress.Status status)
        {
            if (progressId >= 0 && Progress.Exists(progressId)) Progress.Finish(progressId, status);
            progressId = -1;
        }

        static HistoryEntry NewEntry(bool success, string summary) => new()
        {
            utc = ItchState.FormatUtc(DateTime.UtcNow),
            action = active?.Label ?? "?",
            version = runVersion,
            success = success,
            dryRun = active?.DryRun ?? false,
            seconds = (float)(DateTime.UtcNow - startedUtc).TotalSeconds,
            summary = summary,
            details = new List<string>(details),
        };

        static void Finish(bool success, string summary, bool cancelled = false)
        {
            var entry = NewEntry(success, summary);
            ItchState.AddHistory(entry);
            LastRun = entry;

            AppendLog((success ? "=== ✓ " : "=== ✗ ") + summary + $" ({entry.seconds:0}s)");
            if (success) Debug.Log("[Itch] " + summary);
            else if (!cancelled) Debug.LogWarning("[Itch] " + summary + "\n" + string.Join("\n", details));

            IsRunning = false;
            active = null;
            SetPhase(PipelinePhase.Finished, summary, success ? 1 : phaseProgress);

            if (ItchUserPrefs.BeepOnFinish) EditorApplication.Beep();
            ItchPublisherWindow.Notify((success ? "✓ " : "✗ ") + summary);
            Changed?.Invoke();
        }

        static void SetPhase(PipelinePhase phase, string status, float progress)
        {
            Phase = phase;
            Status = status;
            phaseProgress = progress;
            dirty = true;
        }
    }
}
