using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameJam.UITools
{
    /// <summary>Lists UI setup problems found by <see cref="UIDoctor"/>, with select and one-click fix buttons. Rescans as the scene changes.</summary>
    public class UIDoctorWindow : EditorWindow
    {
        List<UIIssue> issues = new List<UIIssue>();
        readonly bool[] show = { true, true, true };
        Vector2 scroll;
        bool dirty = true;
        double rescanAt;

        static GUIContent[] icons;

        [MenuItem("Tools/UI/UI Doctor", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<UIDoctorWindow>();
            window.titleContent = new GUIContent("UI Doctor", EditorGUIUtility.IconContent("console.warnicon.sml").image);
            window.minSize = new Vector2(360, 240);
        }

        void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.hierarchyChanged -= MarkDirty;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            Undo.undoRedoPerformed -= MarkDirty;
            EditorApplication.update -= Tick;
        }

        void OnObjectChanges(ref ObjectChangeEventStream stream) => MarkDirty();
        void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b) => MarkDirty();
        void OnStageChanged(PrefabStage stage) => MarkDirty();

        void MarkDirty()
        {
            if (!dirty) rescanAt = EditorApplication.timeSinceStartup + 0.3; // debounce bursts of changes
            dirty = true;
        }

        void Tick()
        {
            if (!dirty || EditorApplication.timeSinceStartup < rescanAt) return;
            dirty = false;
            issues = UIDoctor.Run();
            Repaint();
        }

        void OnGUI()
        {
            if (icons == null)
                icons = new[]
                {
                    EditorGUIUtility.IconContent("console.erroricon.sml"),
                    EditorGUIUtility.IconContent("console.warnicon.sml"),
                    EditorGUIUtility.IconContent("console.infoicon.sml")
                };

            issues.RemoveAll(i => i.Target == null && !ReferenceEquals(i.Target, null)); // target destroyed since the scan
            DrawToolbar();

            var visible = issues.Where(i => show[(int)i.Severity]).ToList();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (issues.Count == 0)
                EditorGUILayout.HelpBox($"No UI problems found in {UIScope.Describe()}.", MessageType.Info);
            foreach (var issue in visible)
                DrawIssue(issue);
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                for (var i = 0; i < 3; i++)
                {
                    var count = issues.Count(x => (int)x.Severity == i);
                    var content = new GUIContent(count.ToString(), icons[i].image, ((IssueSeverity)i).ToString());
                    show[i] = GUILayout.Toggle(show[i], content, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                }

                GUILayout.Label(UIScope.Describe(), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                var fixable = issues.Where(i => i.Fix != null && i.Severity != IssueSeverity.Info).ToList();
                using (new EditorGUI.DisabledScope(fixable.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent($"Fix all ({fixable.Count})", "Apply every fix for errors and warnings (one undo step). Info items are left alone."),
                            EditorStyles.toolbarButton))
                    {
                        var group = Undo.GetCurrentGroup();
                        Undo.SetCurrentGroupName("UI Doctor: Fix All");
                        foreach (var issue in fixable) issue.Fix();
                        Undo.CollapseUndoOperations(group);
                        RescanNow();
                    }
                }

                if (GUILayout.Button("Rescan", EditorStyles.toolbarButton)) RescanNow();
            }
        }

        void DrawIssue(UIIssue issue)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(icons[(int)issue.Severity], GUILayout.Width(20), GUILayout.Height(20));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(issue.Message, EditorStyles.wordWrappedLabel);
                    if (issue.Target != null)
                    {
                        var path = issue.Target is Component c ? UIScope.PathOf(c.transform)
                            : issue.Target is GameObject go ? UIScope.PathOf(go.transform)
                            : issue.Target.name;
                        if (GUILayout.Button(path, EditorStyles.linkLabel))
                        {
                            Selection.activeObject = issue.Target is Component comp ? comp.gameObject : issue.Target;
                            EditorGUIUtility.PingObject(issue.Target);
                        }
                        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                    }
                }

                if (issue.Fix != null && GUILayout.Button(issue.FixLabel, GUILayout.ExpandWidth(false)))
                {
                    issue.Fix();
                    RescanNow();
                }
            }
        }

        void RescanNow()
        {
            issues = UIDoctor.Run();
            dirty = false;
            Repaint();
            GUIUtility.ExitGUI();
        }
    }
}
