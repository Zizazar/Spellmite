using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameJam.UITools
{
    /// <summary>
    /// Answers "why can't I click this?": in Play Mode shows what's under the pointer and who receives the click;
    /// in Edit Mode lists every Raycast Target with a verdict, so unneeded ones can be switched off in bulk.
    /// </summary>
    public class RaycastInspectorWindow : EditorWindow
    {
        static readonly RaycastKind[] Kinds = { RaycastKind.Blocker, RaycastKind.Text, RaycastKind.InsideInteractive, RaycastKind.Interactive };

        bool onlySelection;
        Vector2 scroll;
        readonly Dictionary<RaycastKind, bool> expanded = new Dictionary<RaycastKind, bool>
        {
            { RaycastKind.Blocker, true }, { RaycastKind.Text, true },
            { RaycastKind.InsideInteractive, false }, { RaycastKind.Interactive, false }
        };
        Dictionary<RaycastKind, List<Graphic>> targets;
        bool dirty = true;

        readonly List<RaycastResult> hits = new List<RaycastResult>();
        Vector2 pointer;
        string liveError;
        double nextLiveUpdate;

        [MenuItem("Tools/UI/Raycast Inspector", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<RaycastInspectorWindow>();
            window.titleContent = new GUIContent("UI Raycasts", EditorGUIUtility.ObjectContent(null, typeof(EventSystem)).image);
            window.minSize = new Vector2(340, 260);
        }

        void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= Tick;
            RaycastGizmos.LiveHover = null;
        }

        void OnObjectChanges(ref ObjectChangeEventStream stream) => dirty = true;

        void OnSelectionChanged()
        {
            if (onlySelection) dirty = true;
        }

        void Tick()
        {
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup > nextLiveUpdate)
            {
                nextLiveUpdate = EditorApplication.timeSinceStartup + 0.1;
                UpdateLive();
                Repaint();
            }
            else if (!EditorApplication.isPlaying && RaycastGizmos.LiveHover != null)
            {
                RaycastGizmos.LiveHover = null;
                SceneView.RepaintAll();
            }

            if (dirty)
            {
                Rescan();
                Repaint();
            }
        }

        void Rescan()
        {
            dirty = false;
            IEnumerable<Graphic> graphics = onlySelection
                ? Selection.gameObjects.Where(go => go.scene.IsValid()).SelectMany(go => go.GetComponentsInChildren<Graphic>(true)).Distinct()
                : UIScope.FindAll<Graphic>();

            targets = Kinds.ToDictionary(k => k, k => new List<Graphic>());
            foreach (var g in graphics.Where(g => g.raycastTarget))
                targets[RaycastClassifier.Classify(g)].Add(g);
        }

        // ---------- Live (Play Mode) ----------

        void UpdateLive()
        {
            hits.Clear();
            liveError = null;
            var es = EventSystem.current;
            if (es == null)
            {
                liveError = "No active EventSystem — nothing in the UI can receive input.";
                SetHover(null);
                return;
            }

            if (!TryGetPointer(out pointer))
            {
                liveError = "No mouse device found.";
                SetHover(null);
                return;
            }

            es.RaycastAll(new PointerEventData(es) { position = pointer }, hits);
            SetHover(hits.Count > 0 ? hits[0].gameObject.GetComponent<Graphic>() : null);
        }

        static void SetHover(Graphic g)
        {
            if (RaycastGizmos.LiveHover == g) return;
            RaycastGizmos.LiveHover = g;
            SceneView.RepaintAll();
        }

        static bool TryGetPointer(out Vector2 position)
        {
#if UITOOLS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            position = Input.mousePosition;
            return true;
#else
            position = default;
            return false;
#endif
        }

        void DrawLive()
        {
            EditorGUILayout.LabelField("Under the pointer (Game view)", EditorStyles.boldLabel);
            if (liveError != null)
            {
                EditorGUILayout.HelpBox(liveError, MessageType.Error);
                return;
            }

            var es = EventSystem.current;
            EditorGUILayout.LabelField("Pointer", $"{pointer.x:0}, {pointer.y:0}");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Selected");
                var selected = es.currentSelectedGameObject;
                if (selected == null) GUILayout.Label("none", EditorStyles.miniLabel);
                else ObjectLink(selected);
            }

            if (hits.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing under the pointer — clicks go to the game world.", MessageType.None);
                return;
            }

            var top = hits[0].gameObject;
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
            var drag = ExecuteEvents.GetEventHandler<IDragHandler>(top);
            if (click != null)
                Verdict($"Click → {click.name}", RaycastClassifier.ColorOf(RaycastKind.Interactive), click);
            else
                Verdict($"Click is blocked by '{top.name}' — nothing handles it", RaycastClassifier.ColorOf(RaycastKind.Blocker), top);
            if (drag != null && drag != click)
                Verdict($"Drag → {drag.name}", RaycastClassifier.ColorOf(RaycastKind.InsideInteractive), drag);

            EditorGUILayout.Space(2);
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label((i + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(18));
                    ObjectLink(hit.gameObject);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(hit.module != null ? hit.module.GetType().Name : "", EditorStyles.miniLabel);
                }
            }
        }

        static void Verdict(string text, Color color, GameObject target)
        {
            var old = GUI.color;
            GUI.color = color;
            if (GUILayout.Button(text, EditorStyles.helpBox))
                Select(target);
            GUI.color = old;
        }

        // ---------- Targets list ----------

        void OnGUI()
        {
            DrawToolbar();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (EditorApplication.isPlaying)
            {
                DrawLive();
                EditorGUILayout.Space(8);
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see what's under the mouse and which object gets the click.", MessageType.None);
            }

            if (targets != null)
                foreach (var kind in Kinds)
                    DrawKind(kind, targets[kind]);

            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var newOnlySelection = GUILayout.Toggle(onlySelection, new GUIContent("Selection only", "Only list targets under the selected objects"), EditorStyles.toolbarButton);
                if (newOnlySelection != onlySelection)
                {
                    onlySelection = newOnlySelection;
                    dirty = true;
                }
                GUILayout.Label(onlySelection ? "" : UIScope.Describe(), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                var show = GUILayout.Toggle(UIToolsPrefs.ShowRaycastTargets, new GUIContent("Show in Scene", "Outline raycast targets in the Scene view"), EditorStyles.toolbarButton);
                if (show != UIToolsPrefs.ShowRaycastTargets) UIToolsPrefs.ShowRaycastTargets = show;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) dirty = true;
            }
        }

        void DrawKind(RaycastKind kind, List<Graphic> list)
        {
            list.RemoveAll(g => g == null);
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10));
                rect.y += 3;
                rect.height = 10;
                EditorGUI.DrawRect(rect, RaycastClassifier.ColorOf(kind));
                expanded[kind] = EditorGUILayout.Foldout(expanded[kind], $"{RaycastClassifier.LabelOf(kind)} ({list.Count})", true, EditorStyles.foldoutHeader);

                if (kind != RaycastKind.Interactive && list.Count > 0 &&
                    GUILayout.Button(new GUIContent("Turn all off", "Disable Raycast Target on every object in this group (undoable)"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    if (kind != RaycastKind.Blocker || EditorUtility.DisplayDialog("Turn off blockers",
                            $"Disable Raycast Target on {list.Count} graphics that block clicks?\n\nIf any of them is an intentional blocker (a modal backdrop, an invisible click catcher), it will stop blocking.",
                            "Turn off", "Cancel"))
                    {
                        SetRaycastTarget(list, false);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (!expanded[kind]) return;
            EditorGUILayout.LabelField(RaycastClassifier.DescriptionOf(kind), EditorStyles.wordWrappedMiniLabel);

            foreach (var g in list)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14);
                    var on = EditorGUILayout.Toggle(g.raycastTarget, GUILayout.Width(16));
                    if (on != g.raycastTarget) SetRaycastTarget(new[] { g }, on);

                    using (new EditorGUI.DisabledScope(!g.gameObject.activeInHierarchy))
                        ObjectLink(g.gameObject, g.GetType().Name);
                }
            }
            EditorGUILayout.Space(4);
        }

        void SetRaycastTarget(IList<Graphic> graphics, bool value)
        {
            Undo.RecordObjects(graphics.Cast<Object>().ToArray(), value ? "Enable Raycast Target" : "Disable Raycast Target");
            foreach (var g in graphics) g.raycastTarget = value;
            dirty = true;
        }

        static void ObjectLink(GameObject go, string suffix = null)
        {
            var label = new GUIContent(suffix == null ? go.name : $"{go.name}  ·  {suffix}", UIScope.PathOf(go.transform));
            if (GUILayout.Button(label, EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                Select(go);
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        }

        static void Select(GameObject go)
        {
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }
    }
}
