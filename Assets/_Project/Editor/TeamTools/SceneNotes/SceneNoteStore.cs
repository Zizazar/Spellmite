using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    public enum NoteKind
    {
        Todo,
        Bug,
        Idea,
        Info,
    }

    [Serializable]
    public class SceneNote
    {
        public string id;
        public string sceneGuid;
        public NoteKind kind;
        public string text = "";
        public string author;
        public string assignee = "";
        public string createdUtc;
        public bool resolved;
        public string resolvedBy;

        /// <summary>World position (for anchored notes, the last known one).</summary>
        public Vector3 position;
        /// <summary>Offset from the anchor object.</summary>
        public Vector3 offset;
        public string anchorId;
        public string anchorName;

        public bool IsAnchored => !string.IsNullOrEmpty(anchorId);

        public string Title
        {
            get
            {
                var t = (text ?? "").Trim();
                var nl = t.IndexOf('\n');
                if (nl >= 0) t = t.Substring(0, nl).Trim();
                if (t.Length == 0) t = "(empty note)";
                return t.Length > 40 ? t.Substring(0, 40) + "…" : t;
            }
        }

        public string CreatedLocal =>
            DateTime.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture)
                : "";

        public string ScenePath => AssetDatabase.GUIDToAssetPath(sceneGuid);
    }

    /// <summary>
    /// Note storage: each note is a separate JSON file in the TeamNotes/ folder next to Assets/.
    /// Scenes aren't modified, so you can leave a note even in a scene someone else has locked,
    /// and git almost never reports conflicts.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneNoteStore
    {
        public static readonly string Root = Path.Combine(Git.ProjectRoot, "TeamNotes").Replace('\\', '/');

        public static event Action Changed;

        static readonly Dictionary<string, SceneNote> Notes = new();
        static readonly HashSet<string> DirtyIds = new();
        static double saveAt;

        static SceneNoteStore()
        {
            Reload();
            EditorApplication.focusChanged += focused => { if (focused) Reload(); };
            EditorApplication.update += SaveDirty;
            EditorApplication.quitting += FlushDirty;
            AssemblyReloadEvents.beforeAssemblyReload += FlushDirty;
        }

        public static IEnumerable<SceneNote> All => Notes.Values;

        public static SceneNote Get(string id) => id != null && Notes.TryGetValue(id, out var n) ? n : null;

        public static IEnumerable<SceneNote> ForScene(string sceneGuid) =>
            Notes.Values.Where(n => n.sceneGuid == sceneGuid);

        public static IEnumerable<SceneNote> ForLoadedScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var path = SceneManager.GetSceneAt(i).path;
                if (string.IsNullOrEmpty(path)) continue;
                foreach (var note in ForScene(AssetDatabase.AssetPathToGUID(path))) yield return note;
            }
        }

        /// <summary>All names found in notes, used to suggest an assignee.</summary>
        public static IEnumerable<string> KnownPeople =>
            Notes.Values.Select(n => n.author)
                .Concat(Notes.Values.Select(n => n.assignee))
                .Append(TeamSettings.UserName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s);

        public static SceneNote CreateNew(Scene scene, Vector3 worldPosition)
        {
            return new SceneNote
            {
                id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                sceneGuid = AssetDatabase.AssetPathToGUID(scene.path),
                author = TeamSettings.UserName,
                createdUtc = DateTime.UtcNow.ToString("o"),
                position = worldPosition,
            };
        }

        public static void Reload()
        {
            var dirty = DirtyIds.Select(Get).Where(n => n != null).ToList();
            Notes.Clear();

            if (Directory.Exists(Root))
            {
                foreach (var file in Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var note = JsonUtility.FromJson<SceneNote>(File.ReadAllText(file));
                        if (note != null && !string.IsNullOrEmpty(note.id)) Notes[note.id] = note;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Team] Failed to read note {file}: {e.Message}");
                    }
                }
            }

            // Unsaved local edits take priority over the version on disk.
            foreach (var note in dirty) Notes[note.id] = note;
            NotifyChanged();
        }

        public static void Save(SceneNote note)
        {
            Notes[note.id] = note;
            DirtyIds.Remove(note.id);

            var path = FilePath(note);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(note, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            NotifyChanged();
        }

        /// <summary>Deferred save (for frequent edits: dragging, typing).</summary>
        public static void MarkDirty(SceneNote note)
        {
            Notes[note.id] = note;
            DirtyIds.Add(note.id);
            saveAt = EditorApplication.timeSinceStartup + 0.7;
            SceneView.RepaintAll();
        }

        public static void Delete(SceneNote note)
        {
            Notes.Remove(note.id);
            DirtyIds.Remove(note.id);
            var path = FilePath(note);
            if (File.Exists(path)) File.Delete(path);

            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
            NotifyChanged();
        }

        public static bool Exists(SceneNote note) => Notes.ContainsKey(note.id);

        static string FilePath(SceneNote note) => Path.Combine(Root, note.sceneGuid, note.id + ".json");

        static void SaveDirty()
        {
            if (DirtyIds.Count == 0 || EditorApplication.timeSinceStartup < saveAt) return;
            if (GUIUtility.hotControl != 0) return; // handle is still being dragged
            FlushDirty();
        }

        static void FlushDirty()
        {
            foreach (var id in DirtyIds.ToList())
            {
                var note = Get(id);
                if (note != null) Save(note);
            }
            DirtyIds.Clear();
        }

        static void NotifyChanged()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
