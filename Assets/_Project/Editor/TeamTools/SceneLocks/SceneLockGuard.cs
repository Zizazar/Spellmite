using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    /// <summary>
    /// Protection: prevents saving a scene locked by someone else,
    /// automatically locks a free scene on the first change,
    /// and reminds you to unlock scenes when quitting Unity.
    /// </summary>
    [InitializeOnLoad]
    static class SceneLockGuard
    {
        /// <summary>Scenes whose dirty state was already handled (until saved/reopened).</summary>
        static readonly HashSet<string> HandledDirty = new();

        static SceneLockGuard()
        {
            EditorSceneManager.sceneDirtied += OnSceneDirtied;
            EditorSceneManager.sceneSaved += s => HandledDirty.Remove(s.path);
            EditorSceneManager.sceneClosed += s => HandledDirty.Remove(s.path);
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.wantsToQuit += OnWantsToQuit;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            HandledDirty.Remove(scene.path);
            if (!SceneLockService.IsReady) return;

            if (SceneLockService.GetState(scene.path) == LockState.Theirs)
            {
                var info = SceneLockService.GetLock(scene.path);
                TeamNotify.Warning($"{info.owner} is editing scene \"{scene.name}\". It's open read-only.");
            }
            SceneLockService.Refresh();
        }

        static void OnSceneDirtied(Scene scene)
        {
            if (Application.isPlaying || string.IsNullOrEmpty(scene.path) || !SceneLockService.IsReady) return;
            if (!HandledDirty.Add(scene.path)) return;

            switch (SceneLockService.GetState(scene.path))
            {
                case LockState.Theirs:
                    var info = SceneLockService.GetLock(scene.path);
                    TeamNotify.Warning($"{info.owner} is editing scene \"{scene.name}\"! You won't be able to save your changes.");
                    break;

                case LockState.Free:
                case LockState.Unknown:
                    if (!TeamSettings.AutoLockOnEdit) break;
                    var sceneName = scene.name;
                    SceneLockService.Lock(scene.path, (ok, message) =>
                    {
                        if (!ok)
                            TeamNotify.Warning($"Couldn't lock \"{sceneName}\": {message}");
                        else if (message != null)
                            EditorUtility.DisplayDialog("Scene is out of date", message, "Got it");
                        else if (SceneLockService.GetState(scene.path) == LockState.Mine)
                            TeamNotify.Info($"Scene \"{sceneName}\" is now locked by you");
                    });
                    break;
            }
        }

        static bool OnWantsToQuit()
        {
            if (!SceneLockService.IsReady || !TeamSettings.AskUnlockOnQuit) return true;
            var mine = SceneLockService.MyLocks.ToList();
            if (mine.Count == 0) return true;

            var names = string.Join("\n", mine.Select(l => "• " + Path.GetFileName(l.path)));
            var choice = EditorUtility.DisplayDialogComplex("You have scenes locked",
                $"These scenes are locked by you:\n{names}\n\nUnlock them so others can work?\n" +
                "(Only scenes whose changes are already pushed will be unlocked.)",
                "Unlock", "Cancel", "Quit and keep locks");

            if (choice == 1) return false; // cancel quitting
            if (choice == 2) return true;

            var notReleased = new List<string>();
            for (var i = 0; i < mine.Count; i++)
            {
                var info = mine[i];
                EditorUtility.DisplayProgressBar("Unlocking scenes", Path.GetFileName(info.path), (float)i / mine.Count);
                var problem = SceneLockService.CheckCanRelease(info.assetPath, out _);
                if (problem != null) { notReleased.Add($"• {Path.GetFileName(info.path)}: {problem}"); continue; }

                var r = Git.Run($"lfs unlock --id={info.id}", 20000);
                if (!r.Ok) notReleased.Add($"• {Path.GetFileName(info.path)}: {r.Message}");
            }
            EditorUtility.ClearProgressBar();

            if (notReleased.Count == 0) return true;
            return EditorUtility.DisplayDialog("Not all scenes were unlocked",
                string.Join("\n\n", notReleased) + "\n\nQuit anyway?", "Quit", "Stay");
        }
    }

    /// <summary>Intercepts scene saving.</summary>
    class SceneLockSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            if (Application.isBatchMode || !SceneLockService.IsReady) return paths;

            var allowed = new List<string>(paths.Length);
            foreach (var path in paths)
            {
                if (!SceneLockService.IsLockable(path) || CanSave(path)) allowed.Add(path);
            }
            return allowed.ToArray();
        }

        static bool CanSave(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var state = SceneLockService.GetState(path);
            if (state == LockState.Mine) return true;

            if (state == LockState.Theirs)
            {
                // The cache may be stale, so re-check on the server before blocking the save.
                SceneLockService.VerifyBlocking(out _);
                state = SceneLockService.GetState(path);
                if (state == LockState.Mine) return true;
                if (state == LockState.Theirs) return Blocked(path, name);
            }

            // Free or unknown. New scenes (not in git yet) are saved without a lock.
            if (!SceneLockService.IsTrackedBlocking(path)) return true;
            if (SceneLockService.LockBlocking(path, out var error)) return true;

            // Locking failed: either someone else just locked the scene, or the server is unreachable.
            if (SceneLockService.VerifyBlocking(out _) && SceneLockService.GetState(path) == LockState.Theirs)
                return Blocked(path, name);

            var saveAnyway = EditorUtility.DisplayDialog("Couldn't lock scene",
                $"The lock server is unavailable:\n{error}\n\n" +
                $"Save \"{name}\" without a lock? This may conflict if someone else is changing it too.",
                "Save without lock", "Don't save");
            if (saveAnyway) MakeWritable(path);
            return saveAnyway;
        }

        /// <summary>Git LFS makes unlocked scenes read-only; clear the flag when saving deliberately.</summary>
        static void MakeWritable(string path)
        {
            if (!File.Exists(path)) return;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        static bool Blocked(string path, string name)
        {
            var info = SceneLockService.GetLock(path);
            ShowBlockedDialog(path, name, info?.owner ?? "another team member", info?.Since);
            return false;
        }

        static void ShowBlockedDialog(string path, string name, string owner, string since)
        {
            var sinceText = string.IsNullOrEmpty(since) ? "" : $" ({since})";
            var choice = EditorUtility.DisplayDialogComplex("Scene is locked",
                $"{owner} is editing scene \"{name}\"{sinceText}.\n\n" +
                "Your changes were NOT saved, so their work isn't overwritten.\n\n" +
                "You can save a copy of the scene and move your changes over later, " +
                "or ask them to unlock the scene.",
                "Save copy…", "Close", "Open lock list");

            if (choice == 0) EditorApplication.delayCall += () => SaveCopy(path);
            if (choice == 2) EditorApplication.delayCall += SceneLocksWindow.Open;
        }

        static void SaveCopy(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid()) return;

            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var copyName = $"{scene.name}_{TeamSettings.UserName}_copy";
            var target = EditorUtility.SaveFilePanelInProject("Save scene copy", copyName, "unity",
                "The copy won't be linked to the original", dir);
            if (string.IsNullOrEmpty(target)) return;

            if (EditorSceneManager.SaveScene(scene, target, true))
                TeamNotify.Info($"Copy saved: {target}");
        }
    }
}
