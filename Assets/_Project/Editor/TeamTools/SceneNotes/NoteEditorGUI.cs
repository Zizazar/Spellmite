using System;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    public enum NoteAction
    {
        None,
        Close,
        Delete,
    }

    /// <summary>Shared note editor, used in the pop-up and in the list window.</summary>
    public static class NoteEditorGUI
    {
        static readonly string[] KindLabels = { "Task", "Bug", "Idea", "Info" };
        static GUIStyle textStyle;

        public static string KindLabel(NoteKind kind) => KindLabels[(int)kind];

        public static Color KindColor(NoteKind kind) => kind switch
        {
            NoteKind.Todo => new Color(1f, 0.66f, 0.15f),
            NoteKind.Bug => new Color(0.95f, 0.3f, 0.28f),
            NoteKind.Idea => new Color(0.55f, 0.5f, 1f),
            _ => new Color(0.4f, 0.75f, 0.95f),
        };

        public static NoteAction Draw(SceneNote note, bool focusText, bool showCloseButton)
        {
            textStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            var action = NoteAction.None;

            EditorGUI.BeginChangeCheck();

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = KindColor(note.kind);
            note.kind = (NoteKind)GUILayout.Toolbar((int)note.kind, KindLabels);
            GUI.backgroundColor = oldBg;

            GUI.SetNextControlName("TeamNoteText");
            note.text = EditorGUILayout.TextArea(note.text, textStyle, GUILayout.MinHeight(70), GUILayout.ExpandHeight(true));
            if (focusText) EditorGUI.FocusTextInControl("TeamNoteText");

            using (new EditorGUILayout.HorizontalScope())
            {
                note.assignee = EditorGUILayout.TextField("Assigned to", note.assignee);
                if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("— nobody —"), string.IsNullOrEmpty(note.assignee), () => SetAssignee(note, ""));
                    foreach (var person in SceneNoteStore.KnownPeople)
                    {
                        var p = person;
                        menu.AddItem(new GUIContent(p), note.assignee == p, () => SetAssignee(note, p));
                    }
                    menu.ShowAsContext();
                }
            }

            if (EditorGUI.EndChangeCheck()) SceneNoteStore.MarkDirty(note);

            DrawAnchor(note);

            var meta = $"{note.author} · {note.CreatedLocal}";
            if (note.resolved) meta += $" · resolved ({note.resolvedBy})";
            EditorGUILayout.LabelField(meta, EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(note.resolved ? "Reopen" : "✓ Resolve"))
                {
                    note.resolved = !note.resolved;
                    note.resolvedBy = note.resolved ? TeamSettings.UserName : null;
                    SceneNoteStore.Save(note);
                    if (note.resolved && showCloseButton) action = NoteAction.Close;
                }

                if (GUILayout.Button("Delete", GUILayout.Width(70)) &&
                    EditorUtility.DisplayDialog("Delete note?", note.Title, "Delete", "Cancel"))
                    action = NoteAction.Delete;

                if (showCloseButton && GUILayout.Button("Done", GUILayout.Width(70)))
                    action = NoteAction.Close;
            }

            if (showCloseButton && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return && (Event.current.control || Event.current.command))
            {
                action = NoteAction.Close;
                Event.current.Use();
            }

            return action;
        }

        static void SetAssignee(SceneNote note, string name)
        {
            note.assignee = name;
            SceneNoteStore.Save(note);
        }

        static void DrawAnchor(SceneNote note)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (note.IsAnchored)
                {
                    var go = SceneNotesSceneGUI.ResolveAnchor(note);
                    var label = go != null ? $"Anchored to: {go.name}" : $"Anchored to: {note.anchorName} (object not found)";
                    EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                    if (GUILayout.Button("Detach", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        SceneNotesSceneGUI.Unanchor(note);
                        SceneNoteStore.Save(note);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Not anchored to an object", EditorStyles.miniLabel);
                    var selected = Selection.activeGameObject;
                    var canAnchor = selected != null && selected.scene.IsValid() &&
                                    string.Equals(AssetDatabase.AssetPathToGUID(selected.scene.path), note.sceneGuid,
                                        StringComparison.Ordinal);
                    using (new EditorGUI.DisabledScope(!canAnchor))
                    {
                        if (GUILayout.Button(new GUIContent("To selection", "Anchor to the selected object; the note will move with it"),
                                EditorStyles.miniButton, GUILayout.Width(100)))
                        {
                            SceneNotesSceneGUI.Anchor(note, selected);
                            SceneNoteStore.Save(note);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Note pop-up in the Scene view.</summary>
    class NotePopup : PopupWindowContent
    {
        readonly SceneNote note;
        readonly bool isNew;
        bool focused;

        public NotePopup(SceneNote note, bool isNew)
        {
            this.note = note;
            this.isNew = isNew;
        }

        public override Vector2 GetWindowSize() => new(340, 230);

        public override void OnGUI(Rect rect)
        {
            using (new GUILayout.AreaScope(new Rect(6, 6, rect.width - 12, rect.height - 12)))
            {
                var action = NoteEditorGUI.Draw(note, !focused, true);
                focused = true;

                if (action == NoteAction.Delete)
                {
                    SceneNoteStore.Delete(note);
                    editorWindow.Close();
                }
                else if (action == NoteAction.Close)
                {
                    editorWindow.Close();
                }
            }
        }

        public override void OnClose()
        {
            if (!SceneNoteStore.Exists(note)) return;
            if (isNew && string.IsNullOrWhiteSpace(note.text)) SceneNoteStore.Delete(note);
            else SceneNoteStore.Save(note);
        }
    }
}
