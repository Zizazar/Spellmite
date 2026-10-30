using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    public enum LockState
    {
        /// <summary>Status not received from the server yet (or the system is disabled).</summary>
        Unknown,
        Free,
        Mine,
        Theirs,
    }

    public enum LockAvailability
    {
        Checking,
        Ready,
        Disabled,
        NoGit,
        NoRepo,
        NoLfs,
        NoRemote,
    }

    [Serializable]
    public class LockInfo
    {
        public string id;
        public string path;   // path relative to the git repository root
        public string owner;
        public string lockedAt;
        public bool mine;
        public string assetPath; // path in the Unity project (Assets/...)

        public DateTime LockedAtLocal =>
            DateTime.TryParse(lockedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime()
                : DateTime.MinValue;

        public string Since
        {
            get
            {
                var t = LockedAtLocal;
                if (t == DateTime.MinValue) return "";
                var span = DateTime.Now - t;
                if (span.TotalMinutes < 1) return "just now";
                if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min";
                if (span.TotalDays < 1) return $"{(int)span.TotalHours} h {span.Minutes} min";
                return t.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Scene locks via Git LFS File Locking.
    /// The server (GitHub / GitLab / Gitea…) guarantees that a file can have only one owner.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneLockService
    {
        public const string LockableExtension = ".unity";
        const string CacheKey = "GameJam.TeamTools.LockCache";

        public static event Action Changed;

        public static LockAvailability Availability { get; private set; } = LockAvailability.Checking;
        public static bool IsReady => Availability == LockAvailability.Ready;
        public static bool HasData { get; private set; }
        public static bool IsRefreshing { get; private set; }
        public static DateTime LastSyncUtc { get; private set; } = DateTime.MinValue;
        public static string LastError { get; private set; }
        public static string RepoRoot { get; private set; }

        /// <summary>Path prefix of the project inside the repository ("" if the project is at the root).</summary>
        static string repoPrefix = "";
        static readonly Dictionary<string, LockInfo> Locks = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
        static double nextAutoRefresh;
        static bool initStarted;

        static SceneLockService()
        {
            RestoreCache();
            EditorApplication.update += Tick;
            EditorApplication.focusChanged += focused =>
            {
                if (focused && (DateTime.UtcNow - LastSyncUtc).TotalSeconds > 10) Refresh();
            };
            TeamSettings.Changed += () =>
            {
                if (!TeamSettings.LocksEnabled) Availability = LockAvailability.Disabled;
                else if (Availability == LockAvailability.Disabled) Initialize();
            };
        }

        #region State queries

        public static IEnumerable<LockInfo> AllLocks => Locks.Values;
        public static IEnumerable<LockInfo> MyLocks => Locks.Values.Where(l => l.mine);

        public static bool IsLockable(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) &&
            assetPath.EndsWith(LockableExtension, StringComparison.OrdinalIgnoreCase);

        public static bool IsPending(string assetPath) => Pending.Contains(assetPath);

        public static LockState GetState(string assetPath)
        {
            if (!IsReady || !HasData || !IsLockable(assetPath)) return LockState.Unknown;
            if (!Locks.TryGetValue(assetPath, out var info)) return LockState.Free;
            return info.mine ? LockState.Mine : LockState.Theirs;
        }

        public static LockInfo GetLock(string assetPath) =>
            assetPath != null && Locks.TryGetValue(assetPath, out var info) ? info : null;

        /// <summary>Asset path (Assets/...) from a repository path; null if the file is outside the project.</summary>
        public static string ToAssetPath(string repoPath)
        {
            repoPath = repoPath.Replace('\\', '/');
            if (repoPrefix.Length == 0) return repoPath;
            return repoPath.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase)
                ? repoPath.Substring(repoPrefix.Length)
                : null;
        }

        public static string AvailabilityText => Availability switch
        {
            LockAvailability.Checking => "Checking git…",
            LockAvailability.Ready => "Working",
            LockAvailability.Disabled => "Disabled in preferences",
            LockAvailability.NoGit => "git is not installed",
            LockAvailability.NoRepo => "Project is not in a git repository",
            LockAvailability.NoLfs => "Git LFS is not installed",
            LockAvailability.NoRemote => "No remote repository",
            _ => Availability.ToString(),
        };

        #endregion

        #region Initialization and refresh

        public static void Initialize()
        {
            initStarted = true;
            if (!TeamSettings.LocksEnabled)
            {
                Availability = LockAvailability.Disabled;
                NotifyChanged();
                return;
            }

            Availability = LockAvailability.Checking;
            var projectRoot = Git.ProjectRoot;
            Task.Run(() =>
            {
                var top = Git.Run("rev-parse --show-toplevel", 10000);
                if (top.GitMissing) return (LockAvailability.NoGit, (string)null);
                if (!top.Ok) return (LockAvailability.NoRepo, null);

                var root = top.Output.Trim().Replace('\\', '/');
                if (!Git.Run("lfs version", 10000).Ok) return (LockAvailability.NoLfs, root);

                var remote = Git.Run("remote", 10000);
                if (!remote.Ok || string.IsNullOrWhiteSpace(remote.Output)) return (LockAvailability.NoRemote, root);

                return (LockAvailability.Ready, root);
            }).ContinueWith(t => MainThread.Post(() =>
            {
                var (availability, root) = t.Result;
                RepoRoot = root;
                repoPrefix = ComputePrefix(root, projectRoot);
                Availability = TeamSettings.LocksEnabled ? availability : LockAvailability.Disabled;
                NotifyChanged();
                if (IsReady) Refresh();
            }));
        }

        static string ComputePrefix(string repoRoot, string projectRoot)
        {
            if (string.IsNullOrEmpty(repoRoot)) return "";
            var repo = Path.GetFullPath(repoRoot).Replace('\\', '/').TrimEnd('/');
            var project = projectRoot.TrimEnd('/');
            if (!project.StartsWith(repo, StringComparison.OrdinalIgnoreCase) || project.Length == repo.Length) return "";
            return project.Substring(repo.Length).Trim('/') + "/";
        }

        static void Tick()
        {
            // Not via delayCall: it doesn't fire while the editor is in the background.
            if (!initStarted) Initialize();
            if (!IsReady || IsRefreshing) return;
            if (EditorApplication.timeSinceStartup < nextAutoRefresh) return;
            if (!InternalEditorUtility.isApplicationActive) return;
            Refresh();
        }

        public static void Refresh()
        {
            if (!IsReady)
            {
                if (Availability != LockAvailability.Checking && Availability != LockAvailability.Disabled) Initialize();
                return;
            }
            if (IsRefreshing) return;

            IsRefreshing = true;
            nextAutoRefresh = EditorApplication.timeSinceStartup + TeamSettings.RefreshIntervalSeconds;
            NotifyChanged();

            Task.Run(FetchLocks).ContinueWith(t => MainThread.Post(() =>
            {
                IsRefreshing = false;
                var (ok, list, error) = t.Result;
                if (ok) ApplyLocks(list);
                else LastError = error;
                nextAutoRefresh = EditorApplication.timeSinceStartup + TeamSettings.RefreshIntervalSeconds;
                NotifyChanged();
            }));
        }

        /// <summary>Synchronous check against the server (used by the save guard).</summary>
        public static bool VerifyBlocking(out string error)
        {
            EditorUtility.DisplayProgressBar("Scene Locks", "Checking scene status on the server…", 0.5f);
            try
            {
                var (ok, list, err) = FetchLocks();
                error = err;
                if (ok) ApplyLocks(list);
                NotifyChanged();
                return ok;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static (bool ok, List<LockInfo> locks, string error) FetchLocks()
        {
            var r = Git.Run("lfs locks --verify --json", 30000);
            if (r.Ok)
            {
                var parsed = JsonUtility.FromJson<VerifyResponse>(r.Output);
                var list = new List<LockInfo>();
                foreach (var l in parsed.ours ?? Array.Empty<LfsLock>()) list.Add(l.ToInfo(true));
                foreach (var l in parsed.theirs ?? Array.Empty<LfsLock>()) list.Add(l.ToInfo(false));
                return (true, list, null);
            }

            // Some servers don't support --verify: tell our own locks apart using the local cache.
            var all = Git.Run("lfs locks --json", 30000);
            if (!all.Ok) return (false, null, r.Message);

            var local = Git.Run("lfs locks --local --json", 10000);
            var mineIds = new HashSet<string>();
            if (local.Ok)
                foreach (var l in ParseArray(local.Output)) mineIds.Add(l.id);

            return (true, ParseArray(all.Output).Select(l => l.ToInfo(mineIds.Contains(l.id))).ToList(), null);
        }

        static void ApplyLocks(List<LockInfo> list)
        {
            var before = new Dictionary<string, LockInfo>(Locks, StringComparer.OrdinalIgnoreCase);

            Locks.Clear();
            foreach (var info in list)
            {
                var assetPath = ToAssetPath(info.path);
                if (assetPath == null) continue;
                info.assetPath = assetPath;
                Locks[assetPath] = info;
            }

            HasData = true;
            LastError = null;
            LastSyncUtc = DateTime.UtcNow;
            SaveCache();
            ReportChangesForOpenScenes(before);
        }

        /// <summary>Reports changes that affect currently open scenes.</summary>
        static void ReportChangesForOpenScenes(Dictionary<string, LockInfo> before)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.path)) continue;

                before.TryGetValue(scene.path, out var was);
                Locks.TryGetValue(scene.path, out var now);

                if (was != null && !was.mine && now == null)
                    TeamNotify.Info($"Scene \"{scene.name}\" is now free and can be edited.");
                else if (was != null && was.mine && (now == null || !now.mine))
                    TeamNotify.Warning(now == null
                        ? $"Your lock on scene \"{scene.name}\" was removed."
                        : $"{now.owner} force-took scene \"{scene.name}\"!");
                else if ((was == null || was.mine) && now != null && !now.mine && scene.isDirty)
                    TeamNotify.Warning($"{now.owner} locked scene \"{scene.name}\". You won't be able to save your changes.");
            }
        }

        #endregion

        #region Lock and unlock

        /// <summary>Lock a scene (in the background). onDone(success, message) runs on the main thread.</summary>
        public static void Lock(string assetPath, Action<bool, string> onDone = null)
        {
            if (!IsReady || !IsLockable(assetPath) || Pending.Contains(assetPath)) return;
            Pending.Add(assetPath);
            NotifyChanged();

            Task.Run<(bool ok, bool untracked, LockInfo info, string error, int behind)>(() =>
            {
                if (!IsTracked(assetPath))
                    return (true, true, null, null, 0);

                var r = Git.Run($"lfs lock --json {Git.Quote(assetPath)}", 60000);
                if (!r.Ok) return (false, false, null, r.Message, 0);

                var info = JsonUtility.FromJson<LfsLock>(r.Output).ToInfo(true);
                return (true, false, info, null, CommitsBehind(assetPath, fetch: true));
            }).ContinueWith(t => MainThread.Post(() =>
            {
                Pending.Remove(assetPath);
                var res = t.Result;
                string message = null;

                if (res.ok && res.info != null)
                {
                    res.info.assetPath = assetPath;
                    Locks[assetPath] = res.info;
                    SaveCache();
                    if (res.behind > 0)
                        message = $"The server has a newer version of this scene ({res.behind} commit(s)). " +
                                  "Pull before editing, otherwise you'll get a conflict.";
                }
                else if (!res.ok)
                {
                    message = FriendlyLockError(res.error);
                    Refresh();
                }

                NotifyChanged();
                onDone?.Invoke(res.ok, message);
            }));
        }

        /// <summary>Synchronous lock (used when saving).</summary>
        public static bool LockBlocking(string assetPath, out string error)
        {
            error = null;
            EditorUtility.DisplayProgressBar("Scene Locks", $"Locking {Path.GetFileName(assetPath)}…", 0.5f);
            try
            {
                var r = Git.Run($"lfs lock --json {Git.Quote(assetPath)}", 20000);
                if (!r.Ok)
                {
                    error = FriendlyLockError(r.Message);
                    return false;
                }

                var info = JsonUtility.FromJson<LfsLock>(r.Output).ToInfo(true);
                info.assetPath = assetPath;
                Locks[assetPath] = info;
                SaveCache();
                NotifyChanged();
                return true;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>
        /// Unlock your own scene. Checks that changes are saved, committed and pushed.
        /// Returns false if the user cancelled or unlocking isn't possible.
        /// </summary>
        public static bool Unlock(string assetPath, bool interactive = true, Action<bool, string> onDone = null)
        {
            var info = GetLock(assetPath);
            if (info == null || !info.mine || Pending.Contains(assetPath)) return false;

            var problem = CheckCanRelease(assetPath, out var canForce);
            if (problem != null)
            {
                if (!interactive || !canForce) { if (interactive) EditorUtility.DisplayDialog("Can't unlock scene", problem, "OK"); return false; }
                if (!EditorUtility.DisplayDialog("Unlock scene?", problem + "\n\nUnlock anyway?",
                        "Unlock", "Cancel")) return false;
            }

            Pending.Add(assetPath);
            NotifyChanged();
            Git.RunAsync($"lfs unlock --id={info.id}", r =>
            {
                Pending.Remove(assetPath);
                if (r.Ok)
                {
                    Locks.Remove(assetPath);
                    SaveCache();
                }
                else
                {
                    Refresh();
                }
                NotifyChanged();
                onDone?.Invoke(r.Ok, r.Ok ? null : r.Message);
                if (!r.Ok && interactive) EditorUtility.DisplayDialog("Failed to unlock scene", r.Message, "OK");
            }, 60000);
            return true;
        }

        /// <summary>Remove someone else's lock (if they left and forgot to unlock the scene).</summary>
        public static void ForceUnlock(string assetPath)
        {
            var info = GetLock(assetPath);
            if (info == null || info.mine) return;

            if (!EditorUtility.DisplayDialog("Remove someone else's lock?",
                    $"Scene \"{Path.GetFileName(assetPath)}\" is locked by {info.owner} ({info.Since}).\n\n" +
                    "If they have unsaved or unpushed changes, those will conflict with yours. " +
                    "Only do this if you've agreed on it or the person is unavailable.",
                    "Remove lock", "Cancel")) return;

            Pending.Add(assetPath);
            NotifyChanged();
            Git.RunAsync($"lfs unlock --force --id={info.id}", r =>
            {
                Pending.Remove(assetPath);
                if (r.Ok) { Locks.Remove(assetPath); SaveCache(); }
                else EditorUtility.DisplayDialog("Failed to remove lock", r.Message, "OK");
                NotifyChanged();
                Refresh();
            }, 60000);
        }

        /// <summary>
        /// Reason why unlocking the scene is unsafe (null if it's fine).
        /// canForce = false if unlocking is not possible at all.
        /// </summary>
        public static string CheckCanRelease(string assetPath, out bool canForce)
        {
            canForce = false;
            var scene = SceneManager.GetSceneByPath(assetPath);
            if (scene.IsValid() && scene.isDirty)
                return $"Scene \"{scene.name}\" is open and has unsaved changes. Save it, commit and push.";

            var status = Git.Run($"status --porcelain -- {Git.Quote(assetPath)}", 10000);
            if (status.Ok && !string.IsNullOrWhiteSpace(status.Output))
                return "The scene has uncommitted changes. Commit and push first, " +
                       "or discard the changes.";

            canForce = true;
            var unpushed = Git.Run($"rev-list --count @{{u}}..HEAD -- {Git.Quote(assetPath)}", 10000);
            if (unpushed.Ok && int.TryParse(unpushed.Output.Trim(), out var n) && n > 0)
                return $"Scene changes are committed but not pushed ({n} commit(s)). " +
                       "If you unlock now, the next person will start from an old version. Push first.";

            return null;
        }

        static bool IsTracked(string assetPath)
        {
            var r = Git.Run($"ls-files -- {Git.Quote(assetPath)}", 10000);
            return !r.Ok || !string.IsNullOrWhiteSpace(r.Output);
        }

        public static bool IsTrackedBlocking(string assetPath) => IsTracked(assetPath);

        /// <summary>How many commits the local version of the file is behind the server.</summary>
        static int CommitsBehind(string assetPath, bool fetch)
        {
            if (fetch) Git.Run("fetch --quiet", 60000);
            var r = Git.Run($"rev-list --count HEAD..@{{u}} -- {Git.Quote(assetPath)}", 10000);
            return r.Ok && int.TryParse(r.Output.Trim(), out var n) ? n : 0;
        }

        static string FriendlyLockError(string error)
        {
            if (string.IsNullOrEmpty(error)) return "Unknown error";
            if (error.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Someone else has already locked this scene.";
            if (error.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("404", StringComparison.Ordinal) >= 0)
                return "The server doesn't support Git LFS File Locking. GitHub, GitLab and Gitea do.\n\n" + error;
            if (error.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("403", StringComparison.Ordinal) >= 0)
                return "Server authentication failed. Run any git push/pull in a terminal to sign in.\n\n" + error;
            return error;
        }

        #endregion

        #region Cache across domain reloads

        [Serializable] class Cache { public List<LockInfo> locks; public string syncUtc; }

        static void SaveCache()
        {
            var cache = new Cache { locks = Locks.Values.ToList(), syncUtc = LastSyncUtc.ToString("o") };
            SessionState.SetString(CacheKey, JsonUtility.ToJson(cache));
        }

        static void RestoreCache()
        {
            var json = SessionState.GetString(CacheKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var cache = JsonUtility.FromJson<Cache>(json);
            if (cache?.locks == null) return;

            // The repository prefix isn't known yet, so use the saved asset paths.
            foreach (var l in cache.locks)
                if (!string.IsNullOrEmpty(l.assetPath)) Locks[l.assetPath] = l;
            DateTime.TryParse(cache.syncUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t);
            LastSyncUtc = t;
            HasData = true;
        }

        #endregion

        static void NotifyChanged()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        #region git-lfs JSON

        [Serializable] class LfsOwner { public string name; }

        [Serializable]
        class LfsLock
        {
            public string id;
            public string path;
            public LfsOwner owner;
            public string locked_at;

            public LockInfo ToInfo(bool mine) => new()
            {
                id = id,
                path = path,
                owner = owner?.name ?? "?",
                lockedAt = locked_at,
                mine = mine,
            };
        }

        [Serializable] class VerifyResponse { public LfsLock[] ours; public LfsLock[] theirs; }
        [Serializable] class LockArray { public LfsLock[] items; }

        static LfsLock[] ParseArray(string json)
        {
            json = json.Trim();
            if (string.IsNullOrEmpty(json) || json == "null") return Array.Empty<LfsLock>();
            return JsonUtility.FromJson<LockArray>("{\"items\":" + json + "}").items ?? Array.Empty<LfsLock>();
        }

        #endregion
    }
}
