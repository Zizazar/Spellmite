using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    /// <summary>
    /// Draws notes in the Scene view: sticker labels above points, click to edit,
    /// the active note gets a move handle. Alt+N creates a new note under the cursor.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneNotesSceneGUI
    {
        public static string ActiveNoteId;

        static bool createAtMouse, createAtPivot;
        static string hoveredId;
        static readonly Dictionary<string, GameObject> AnchorCache = new();
        static GUIStyle titleStyle, bodyStyle;

        static SceneNotesSceneGUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += AnchorCache.Clear;
            Selection.selectionChanged += () => ActiveNoteId = null;
        }

        [Shortcut("Team/New Note Under Cursor", typeof(SceneView), KeyCode.N, ShortcutModifiers.Alt)]
        static void CreateUnderMouse()
        {
            createAtMouse = true;
            SceneView.lastActiveSceneView?.Repaint();
        }

        [MenuItem("Tools/Team/New Note at View Center", priority = 20)]
        public static void RequestCreateAtPivot()
        {
            createAtPivot = true;
            SceneView.lastActiveSceneView?.Repaint();
        }

        #region Anchors

        public static GameObject ResolveAnchor(SceneNote note)
        {
            if (!note.IsAnchored) return null;
            if (AnchorCache.TryGetValue(note.anchorId, out var cached) && cached != null) return cached;

            GameObject go = null;
            if (GlobalObjectId.TryParse(note.anchorId, out var gid))
                go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
            AnchorCache[note.anchorId] = go;
            return go;
        }

        public static Vector3 WorldPosition(SceneNote note)
        {
            var go = ResolveAnchor(note);
            return go != null ? go.transform.position + note.offset : note.position;
        }

        public static void Anchor(SceneNote note, GameObject go)
        {
            var world = WorldPosition(note);
            note.anchorId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            note.anchorName = go.name;
            note.offset = world - go.transform.position;
            note.position = world;
        }

        public static void Unanchor(SceneNote note)
        {
            note.position = WorldPosition(note);
            note.anchorId = null;
            note.anchorName = null;
            note.offset = Vector3.zero;
        }

        static void SetWorldPosition(SceneNote note, Vector3 world)
        {
            note.position = world;
            var go = ResolveAnchor(note);
            if (go != null) note.offset = world - go.transform.position;
        }

        #endregion

        static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if ((createAtMouse || createAtPivot) && e.type == EventType.Layout) CreateNote(view, e.mousePosition);

            if (!TeamSettings.NotesVisible) return;

            var camPos = view.camera.transform.position;
            var notes = SceneNoteStore.ForLoadedScenes()
                .Where(n => !n.resolved || TeamSettings.ShowResolvedNotes || n.id == ActiveNoteId)
                .OrderByDescending(n => (WorldPosition(n) - camPos).sqrMagnitude) // nearer notes are drawn on top
                .ToList();

            string newHovered = null;
            foreach (var note in notes)
                if (DrawNote(view, note, e)) newHovered = note.id;

            if (e.type == EventType.MouseMove || e.type == EventType.Layout)
            {
                if (newHovered != hoveredId) { hoveredId = newHovered; view.Repaint(); }
            }

            DrawActiveHandle();

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && ActiveNoteId != null)
            {
                ActiveNoteId = null;
                e.Use();
            }
        }

        /// <summary>Draws a note and handles clicks. Returns true if the cursor is over it.</summary>
        static bool DrawNote(SceneView view, SceneNote note, Event e)
        {
            var world = WorldPosition(note);
            var gp = HandleUtility.WorldToGUIPointWithDepth(world);
            if (gp.z < 0) return false;
            var pin = new Vector2(gp.x, gp.y);

            titleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 6, 3, 3),
                fontSize = 11,
            };
            bodyStyle ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                wordWrap = true,
                padding = new RectOffset(8, 6, 2, 4),
                fontSize = 11,
            };

            var title = (note.resolved ? "✓ " : "") + $"{NoteEditorGUI.KindLabel(note.kind)}: {note.Title}";
            if (!string.IsNullOrEmpty(note.assignee)) title += $"  → {note.assignee}";
            var titleContent = new GUIContent(title);
            var size = titleStyle.CalcSize(titleContent);
            var rect = new Rect(pin.x - size.x / 2, pin.y - size.y - 14, size.x, size.y);

            var id = GUIUtility.GetControlID(note.id.GetHashCode(), FocusType.Passive);
            var hovered = rect.Contains(e.mousePosition);

            switch (e.type)
            {
                case EventType.Layout:
                    if (hovered) HandleUtility.AddControl(id, 0);
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && HandleUtility.nearestControl == id && hovered)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (id != 0 && GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        ActiveNoteId = note.id;
                        Handles.BeginGUI();
                        PopupWindow.Show(rect, new NotePopup(note, false));
                        Handles.EndGUI();
                    }
                    break;

                case EventType.Repaint:
                    Paint(note, pin, rect, titleContent, hovered || hoveredId == note.id);
                    break;
            }

            return hovered;
        }

        static void Paint(SceneNote note, Vector2 pin, Rect rect, GUIContent title, bool expanded)
        {
            var color = NoteEditorGUI.KindColor(note.kind);
            var alpha = note.resolved ? 0.45f : 1f;
            var active = note.id == ActiveNoteId;

            Handles.BeginGUI();

            // Stem and dot
            Handles.color = new Color(color.r, color.g, color.b, alpha);
            Handles.DrawAAPolyLine(2f, pin, new Vector2(pin.x, rect.yMax));
            EditorGUI.DrawRect(new Rect(pin.x - 3, pin.y - 3, 6, 6), Handles.color);

            // Background
            if (active) EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Color.white);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f, 0.92f * alpha));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), Handles.color);
            GUI.Label(rect, title, titleStyle);

            // Full text on hover
            if (expanded && !string.IsNullOrWhiteSpace(note.text))
            {
                var body = new GUIContent($"{note.text.Trim()}\n— {note.author}, {note.CreatedLocal}");
                const float width = 280;
                var height = bodyStyle.CalcHeight(body, width);
                var bodyRect = new Rect(rect.x, rect.yMax, Mathf.Max(width, rect.width), height);
                EditorGUI.DrawRect(bodyRect, new Color(0.1f, 0.1f, 0.1f, 0.95f));
                EditorGUI.DrawRect(new Rect(bodyRect.x, bodyRect.y, 4, bodyRect.height), Handles.color);
                GUI.Label(bodyRect, body, bodyStyle);
            }

            Handles.EndGUI();
        }

        static void DrawActiveHandle()
        {
            var note = SceneNoteStore.Get(ActiveNoteId);
            if (note == null) return;

            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(WorldPosition(note), Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                SetWorldPosition(note, moved);
                SceneNoteStore.MarkDirty(note);
            }
        }

        static void CreateNote(SceneView view, Vector2 mouse)
        {
            var atMouse = createAtMouse;
            createAtMouse = createAtPivot = false;

            Vector3 world;
            GameObject picked = null;
            if (atMouse)
            {
                picked = HandleUtility.PickGameObject(mouse, false);
                world = HandleUtility.PlaceObject(mouse, out var pos, out var normal)
                    ? pos + normal * 0.25f
                    : HandleUtility.GUIPointToWorldRay(mouse).GetPoint(view.cameraDistance);
            }
            else
            {
                world = view.pivot;
                mouse = HandleUtility.WorldToGUIPoint(world);
            }

            var scene = picked != null ? picked.scene : SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                TeamNotify.Warning("Save the scene first: notes are tied to the scene file.");
                return;
            }

            var note = SceneNoteStore.CreateNew(scene, world);
            if (picked != null) Anchor(note, picked);
            SceneNoteStore.Save(note);
            ActiveNoteId = note.id;

            Handles.BeginGUI();
            PopupWindow.Show(new Rect(mouse.x, mouse.y, 1, 1), new NotePopup(note, true));
            Handles.EndGUI();
        }
    }
}
