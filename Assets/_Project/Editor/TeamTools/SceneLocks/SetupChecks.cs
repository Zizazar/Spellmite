using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    /// <summary>Checks that git / LFS / .gitattributes are set up for locking.</summary>
    static class SetupChecks
    {
        const string LockableLine = "*.unity lockable";

        static double lastCheck = -100;
        static bool gitOk, repoOk, lfsOk, hooksOk, attributesOk, remoteOk;
        static string remoteText, repoRoot;

        static void Recheck()
        {
            lastCheck = EditorApplication.timeSinceStartup;

            var top = Git.Run("rev-parse --show-toplevel", 5000);
            gitOk = !top.GitMissing;
            repoOk = top.Ok;
            repoRoot = repoOk ? top.Output.Trim() : Git.ProjectRoot;

            lfsOk = gitOk && Git.Run("lfs version", 5000).Ok;

            var remote = repoOk ? Git.Run("remote get-url origin", 5000) : default;
            remoteOk = repoOk && remote.Ok;
            remoteText = remoteOk ? remote.Output.Trim() : null;

            hooksOk = false;
            if (repoOk)
            {
                var hooks = Git.Run("rev-parse --git-path hooks", 5000);
                if (hooks.Ok)
                {
                    var hookFile = Path.Combine(Git.ProjectRoot, hooks.Output.Trim(), "post-checkout");
                    hooksOk = File.Exists(hookFile) && File.ReadAllText(hookFile).Contains("git lfs");
                }
            }

            attributesOk = HasLockableAttribute();
        }

        static string AttributesPath => Path.Combine(repoRoot ?? Git.ProjectRoot, ".gitattributes");

        static bool HasLockableAttribute()
        {
            if (!File.Exists(AttributesPath)) return false;
            foreach (var raw in File.ReadAllLines(AttributesPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("*.unity") && line.EndsWith("lockable")) return true;
            }
            return false;
        }

        public static void Draw()
        {
            if (EditorApplication.timeSinceStartup - lastCheck > 10) Recheck();

            Row(gitOk, "git is installed", "Install Git: https://git-scm.com", null, null);
            Row(lfsOk, "Git LFS is installed", "Install Git LFS: https://git-lfs.com", null, null);
            Row(repoOk, "Project is in a git repository",
                "Create a repository on GitHub/GitLab and clone it, or run git init in the project folder.",
                "git init", () => Git.Run("init -b main"));
            Row(remoteOk, remoteOk ? $"Remote: {remoteText}" : "Remote repository (origin) is set",
                "Add a remote: git remote add origin <url>. The server must support Git LFS File Locking " +
                "(GitHub, GitLab, Gitea, Azure DevOps).", null, null);
            Row(hooksOk, "LFS hooks are installed (other people's scenes are read-only)",
                "Run git lfs install once on each computer.",
                "git lfs install", () => Git.Run("lfs install"));
            Row(attributesOk, $".gitattributes contains \"{LockableLine}\"",
                "This attribute keeps scenes read-only until you lock them.",
                "Add", AddLockableAttribute);

            var serverOk = SceneLockService.IsReady && SceneLockService.HasData && SceneLockService.LastError == null;
            Row(serverOk, "Server responds to lock requests",
                SceneLockService.LastError ?? "Click \"Check again\" after setting up the items above.", null, null);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Check again"))
            {
                Recheck();
                SceneLockService.Initialize();
            }
        }

        static void Row(bool ok, string title, string help, string fixLabel, Action fix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(EditorGUIUtility.IconContent(ok ? "TestPassed" : "console.warnicon.sml"),
                    GUILayout.Width(20), GUILayout.Height(18));
                GUILayout.Label(title, EditorStyles.wordWrappedLabel);
                if (!ok && fix != null && GUILayout.Button(fixLabel, GUILayout.Width(120)))
                {
                    fix();
                    Recheck();
                    SceneLockService.Initialize();
                }
            }
            if (!ok)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(24);
                    GUILayout.Label(help, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        static void AddLockableAttribute()
        {
            var text = File.Exists(AttributesPath) ? File.ReadAllText(AttributesPath) : "";
            if (text.Length > 0 && !text.EndsWith("\n")) text += "\n";
            text += "\n# Scenes are locked via Git LFS: other people's scenes are read-only\n" + LockableLine + "\n";
            File.WriteAllText(AttributesPath, text);
            Debug.Log($"[Team] Added \"{LockableLine}\" to {AttributesPath}. Commit .gitattributes.");
        }
    }
}
