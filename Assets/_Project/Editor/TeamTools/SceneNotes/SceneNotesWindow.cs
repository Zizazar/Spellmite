using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    /// <summary>List of all notes in all scenes, with filters and an editor.</summary>
    public class SceneNotesWindow : EditorWindow
    {
        string search = "";
        bool showResolved;
        bool onlyForMe;
        readonly bool[] kindFilter = { true, true, true, true };
        string selectedId;
        Vector2 listScroll;
        double lastClickTime;

        [MenuItem("Tools/Team/Scene Notes", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<SceneNotesWindow>();
            window.titleContent = new GUIContent("Scene Notes", EditorGUIUtility.IconContent("TextAsset Icon").image);
            window.minSize = new Vector2(360, 300);
        }

        void OnEnable() => SceneNoteStore.Changed += Repaint;
        void OnDisable() => SceneNoteStore.Changed -= Repaint;

        void OnGUI()
        {
            DrawToolbar();

            var notes = SceneNoteStore.All
                .Where(Matches)
                .OrderBy(n => n.ScenePath)
                .ThenBy(n => n.resolved)
                .ThenByDescending(n => n.createdUtc)
                .ToList();

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            if (notes.Count == 0)
                EditorGUILayout.HelpBox("No notes yet. Hover over the Scene view and press Alt+N.", MessageType.Info);

            string currentScene = null;
            foreach (var note in notes)
            {
                if (note.ScenePath != currentScene)
                {
                    currentScene = note.ScenePath;
                    DrawSceneHeader(currentScene);
                }
                DrawRow(note);
            }
            EditorGUILayout.EndScrollView();

            var selected = SceneNoteStore.Get(selectedId);
            if (selected != null) DrawDetails(selected);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(80));

                for (var i = 0; i < kindFilter.Length; i++)
                {
                    var old = GUI.contentColor;
                    GUI.contentColor = kindFilter[i] ? NoteEditorGUI.KindColor((NoteKind)i) : Color.gray;
                    kindFilter[i] = GUILayout.Toggle(kindFilter[i], NoteEditorGUI.KindLabel((NoteKind)i), EditorStyles.toolbarButton);
                    GUI.contentColor = old;
                }

                onlyForMe = GUILayout.Toggle(onlyForMe, new GUIContent("Mine", $"Notes assigned to {TeamSettings.UserName}"),
                    EditorStyles.toolbarButton);
                showResolved = GUILayout.Toggle(showResolved, "Resolved", EditorStyles.toolbarButton);

                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Reload from disk"),
                        EditorStyles.toolbarButton, GUILayout.Width(28)))
                    SceneNoteStore.Reload();
            }
        }

        bool Matches(SceneNote n)
        {
            if (n.resolved && !showResolved) return false;
            if (!kindFilter[(int)n.kind]) return false;
            if (onlyForMe && !string.Equals(n.assignee, TeamSettings.UserName, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrEmpty(search)) return true;
            return Contains(n.text, search) || Contains(n.author, search) || Contains(n.assignee, search) ||
                   Contains(n.anchorName, search);
        }

        static bool Contains(string s, string q) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        static void DrawSceneHeader(string scenePath)
        {
            EditorGUILayout.Space(4);
            var name = string.IsNullOrEmpty(scenePath) ? "(scene deleted)" : Path.GetFileNameWithoutExtension(scenePath);
            var loaded = !string.IsNullOrEmpty(scenePath) && SceneManager.GetSceneByPath(scenePath).isLoaded;
            EditorGUILayout.LabelField(loaded ? name : name + "  (not open)", EditorStyles.boldLabel);
        }

        void DrawRow(SceneNote note)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20);
            var e = Event.current;

            if (e.type == EventType.Repaint)
            {
                if (note.id == selectedId) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                var color = NoteEditorGUI.KindColor(note.kind);
                if (note.resolved) color.a = 0.4f;
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, 4, rect.height - 4), color);
            }

            var meta = string.IsNullOrEmpty(note.assignee) ? note.author : $"{note.author} → {note.assignee}";
            meta += "  " + note.CreatedLocal;
            var metaWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(meta)).x;

            var titleRect = new Rect(rect.x + 8, rect.y, rect.width - metaWidth - 12, rect.height);
            var label = (note.resolved ? "✓ " : "") + $"[{NoteEditorGUI.KindLabel(note.kind)}] {note.Title}";
            GUI.Label(titleRect, label);
            GUI.Label(new Rect(rect.xMax - metaWidth, rect.y, metaWidth, rect.height), meta, EditorStyles.miniLabel);

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                var doubleClick = selectedId == note.id && EditorApplication.timeSinceStartup - lastClickTime < 0.35;
                selectedId = note.id;
                lastClickTime = EditorApplication.timeSinceStartup;
                GUI.FocusControl(null);
                if (doubleClick || e.clickCount == 2) ShowInScene(note);
                e.Use();
            }
        }

        void DrawDetails(SceneNote note)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(210)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Path.GetFileNameWithoutExtension(note.ScenePath), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Show in scene", EditorStyles.miniButton)) ShowInScene(note);
                }

                if (NoteEditorGUI.Draw(note, false, false) == NoteAction.Delete)
                {
                    SceneNoteStore.Delete(note);
                    selectedId = null;
                }
            }
        }

        public static void ShowInScene(SceneNote note)
        {
            var path = note.ScenePath;
            if (string.IsNullOrEmpty(path)) return;

            if (!SceneManager.GetSceneByPath(path).isLoaded)
            {
                if (!EditorUtility.DisplayDialog("Open scene?",
                        $"The note is in scene \"{Path.GetFileNameWithoutExtension(path)}\".", "Open", "Cancel")) return;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(path);
            }

            TeamSettings.NotesVisible = true;
            var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            SceneNotesSceneGUI.ActiveNoteId = note.id;
            view.LookAt(SceneNotesSceneGUI.WorldPosition(note), view.rotation, Mathf.Min(view.size, 8f));
            view.Repaint();
        }
    }
}
