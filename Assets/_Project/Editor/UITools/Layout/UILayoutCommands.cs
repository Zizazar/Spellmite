using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameJam.UITools
{
    public enum UIAlign { Left, Center, Right, Top, Middle, Bottom }

    /// <summary>
    /// Align / distribute / stack / match / anchor / group operations on selected RectTransforms.
    /// All are undoable and available from Tools > UI, the Scene view overlay, and the Shortcuts manager
    /// (Main Menu/Tools/UI/...) where they can be bound to keys.
    /// </summary>
    public static class UILayoutCommands
    {
        const string Menu = "Tools/UI/";

        /// <summary>Selected UI elements, excluding root canvases. Top level only: if a parent and child are both selected, only the parent moves.</summary>
        public static RectTransform[] SelectedTopLevel() =>
            Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable | SelectionMode.ExcludePrefab)
                .OfType<RectTransform>().Where(rt => rt.parent is RectTransform).ToArray();

        /// <summary>Every selected UI element, including nested ones (for anchor edits that don't move anything).</summary>
        public static RectTransform[] SelectedAll() =>
            Selection.GetTransforms(SelectionMode.Editable | SelectionMode.ExcludePrefab)
                .OfType<RectTransform>().Where(rt => rt.parent is RectTransform).ToArray();

        /// <summary>The element others are measured against: the last clicked (active) one when it's part of the selection.</summary>
        static RectTransform Key(RectTransform[] rts) =>
            Selection.activeTransform is RectTransform active && rts.Contains(active) ? active : rts[0];

        // ---------- Align ----------

        public static void Align(UIAlign align)
        {
            var rts = SelectedTopLevel();
            if (rts.Length == 0) return;
            var space = RectOps.SpaceFor(Key(rts));
            var toParent = rts.Length == 1 || UIToolsPrefs.AlignToParent;

            var bounds = Bounds(rts, space);
            Undo.RecordObjects(rts, "Align " + align);
            foreach (var rt in rts)
            {
                var target = toParent ? RectOps.GetRect((RectTransform)rt.parent, space) : bounds;
                var r = RectOps.GetRect(rt, space);
                Vector2 delta;
                switch (align)
                {
                    case UIAlign.Left: delta = new Vector2(target.xMin - r.xMin, 0); break;
                    case UIAlign.Center: delta = new Vector2(target.center.x - r.center.x, 0); break;
                    case UIAlign.Right: delta = new Vector2(target.xMax - r.xMax, 0); break;
                    case UIAlign.Top: delta = new Vector2(0, target.yMax - r.yMax); break;
                    case UIAlign.Middle: delta = new Vector2(0, target.center.y - r.center.y); break;
                    default: delta = new Vector2(0, target.yMin - r.yMin); break;
                }
                RectOps.Move(rt, delta, space);
            }
        }

        static Rect Bounds(RectTransform[] rts, Transform space)
        {
            var bounds = RectOps.GetRect(rts[0], space);
            foreach (var rt in rts.Skip(1))
            {
                var r = RectOps.GetRect(rt, space);
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                    Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
            }
            return bounds;
        }

        [MenuItem(Menu + "Align/Left", priority = 100)] static void AlignLeft() => Align(UIAlign.Left);
        [MenuItem(Menu + "Align/Center", priority = 101)] static void AlignCenter() => Align(UIAlign.Center);
        [MenuItem(Menu + "Align/Right", priority = 102)] static void AlignRight() => Align(UIAlign.Right);
        [MenuItem(Menu + "Align/Top", priority = 120)] static void AlignTop() => Align(UIAlign.Top);
        [MenuItem(Menu + "Align/Middle", priority = 121)] static void AlignMiddle() => Align(UIAlign.Middle);
        [MenuItem(Menu + "Align/Bottom", priority = 122)] static void AlignBottom() => Align(UIAlign.Bottom);

        [MenuItem(Menu + "Align/Left", true)]
        [MenuItem(Menu + "Align/Center", true)]
        [MenuItem(Menu + "Align/Right", true)]
        [MenuItem(Menu + "Align/Top", true)]
        [MenuItem(Menu + "Align/Middle", true)]
        [MenuItem(Menu + "Align/Bottom", true)]
        static bool HasSelection() => SelectedTopLevel().Length > 0;

        // ---------- Distribute & stack ----------

        /// <summary>Keeps the outermost two elements in place and makes the gaps between all of them equal.</summary>
        public static void Distribute(bool horizontal)
        {
            var rts = SelectedTopLevel();
            if (rts.Length < 3) return;
            var space = RectOps.SpaceFor(Key(rts));
            var items = rts.Select(rt => (rt, r: RectOps.GetRect(rt, space)))
                .OrderBy(i => horizontal ? i.r.center.x : i.r.center.y).ToArray();

            float Min(Rect r) => horizontal ? r.xMin : r.yMin;
            float Max(Rect r) => horizontal ? r.xMax : r.yMax;
            float Size(Rect r) => horizontal ? r.width : r.height;

            var start = items.Min(i => Min(i.r));
            var span = items.Max(i => Max(i.r)) - start;
            var gap = (span - items.Sum(i => Size(i.r))) / (items.Length - 1);

            Undo.RecordObjects(rts, horizontal ? "Distribute Horizontally" : "Distribute Vertically");
            var pos = start;
            foreach (var (rt, r) in items)
            {
                var d = pos - Min(r);
                RectOps.Move(rt, horizontal ? new Vector2(d, 0) : new Vector2(0, d), space);
                pos += Size(r) + gap;
            }
        }

        /// <summary>Lines elements up one after another with a fixed gap: left to right, or top to bottom. Starts at the first one.</summary>
        public static void Stack(bool horizontal, float gap)
        {
            var rts = SelectedTopLevel();
            if (rts.Length < 2) return;
            var space = RectOps.SpaceFor(Key(rts));
            var items = rts.Select(rt => (rt, r: RectOps.GetRect(rt, space)));
            items = horizontal ? items.OrderBy(i => i.r.xMin) : items.OrderByDescending(i => i.r.yMax);
            var list = items.ToArray();

            Undo.RecordObjects(rts, horizontal ? "Stack Horizontally" : "Stack Vertically");
            var pos = horizontal ? list[0].r.xMin : list[0].r.yMax;
            foreach (var (rt, r) in list)
            {
                if (horizontal)
                {
                    RectOps.Move(rt, new Vector2(pos - r.xMin, 0), space);
                    pos += r.width + gap;
                }
                else
                {
                    RectOps.Move(rt, new Vector2(0, pos - r.yMax), space);
                    pos -= r.height + gap;
                }
            }
        }

        [MenuItem(Menu + "Arrange/Distribute Horizontally", priority = 140)] static void DistributeH() => Distribute(true);
        [MenuItem(Menu + "Arrange/Distribute Vertically", priority = 141)] static void DistributeV() => Distribute(false);
        [MenuItem(Menu + "Arrange/Stack Horizontally", priority = 160)] static void StackH() => Stack(true, UIToolsPrefs.StackGap);
        [MenuItem(Menu + "Arrange/Stack Vertically", priority = 161)] static void StackV() => Stack(false, UIToolsPrefs.StackGap);
        [MenuItem(Menu + "Arrange/Match Width", priority = 180)] static void MatchW() => MatchSize(true);
        [MenuItem(Menu + "Arrange/Match Height", priority = 181)] static void MatchH() => MatchSize(false);

        [MenuItem(Menu + "Arrange/Distribute Horizontally", true)]
        [MenuItem(Menu + "Arrange/Distribute Vertically", true)]
        static bool HasThree() => SelectedTopLevel().Length >= 3;

        [MenuItem(Menu + "Arrange/Stack Horizontally", true)]
        [MenuItem(Menu + "Arrange/Stack Vertically", true)]
        [MenuItem(Menu + "Arrange/Match Width", true)]
        [MenuItem(Menu + "Arrange/Match Height", true)]
        static bool HasTwo() => SelectedTopLevel().Length >= 2;

        // ---------- Size ----------

        /// <summary>Gives every selected element the width (or height) of the active one.</summary>
        public static void MatchSize(bool width)
        {
            var rts = SelectedTopLevel();
            if (rts.Length < 2) return;
            var key = Key(rts);
            var axis = width ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
            var size = width ? key.rect.width : key.rect.height;
            Undo.RecordObjects(rts, width ? "Match Width" : "Match Height");
            foreach (var rt in rts.Where(rt => rt != key))
                rt.SetSizeWithCurrentAnchors(axis, size);
        }

        // ---------- Anchors ----------

        static void ForEachSelected(string undoName, Action<RectTransform> action)
        {
            var rts = SelectedAll();
            if (rts.Length == 0) return;
            Undo.RecordObjects(rts, undoName);
            foreach (var rt in rts) action(rt);
        }

        [MenuItem(Menu + "Anchors/Anchors to Rect Corners", priority = 200)]
        public static void AnchorsToRect() => ForEachSelected("Anchors to Rect", rt => RectOps.AnchorsToRect(rt));

        [MenuItem(Menu + "Anchors/Rect to Anchors", priority = 201)]
        public static void RectToAnchors() => ForEachSelected("Rect to Anchors", RectOps.RectToAnchors);

        [MenuItem(Menu + "Anchors/Anchor to Nearest Edge", priority = 202)]
        public static void AutoAnchor() => ForEachSelected("Auto Anchor", rt => RectOps.AutoAnchor(rt));

        [MenuItem(Menu + "Anchors/Fill Parent", priority = 220)]
        public static void FillParent() => ForEachSelected("Fill Parent", RectOps.FillParent);

        [MenuItem(Menu + "Anchors/Round to Whole Pixels", priority = 221)]
        public static void RoundPixels() => ForEachSelected("Round to Whole Pixels", RectOps.RoundOffsets);

        [MenuItem(Menu + "Anchors/Anchors to Rect Corners", true)]
        [MenuItem(Menu + "Anchors/Rect to Anchors", true)]
        [MenuItem(Menu + "Anchors/Anchor to Nearest Edge", true)]
        [MenuItem(Menu + "Anchors/Fill Parent", true)]
        [MenuItem(Menu + "Anchors/Round to Whole Pixels", true)]
        static bool HasAnySelection() => SelectedAll().Length > 0;

        // ---------- Group ----------

        /// <summary>Wraps the selection in a new empty RectTransform that exactly covers it.</summary>
        [MenuItem(Menu + "Group Selection", priority = 240)]
        public static void Group()
        {
            var rts = SelectedTopLevel();
            if (rts.Length == 0) return;
            var key = Key(rts);
            var parent = (RectTransform)key.parent;
            var bounds = Bounds(rts, parent);
            var siblingIndex = rts.Where(rt => rt.parent == parent).Min(rt => rt.GetSiblingIndex());

            var group = Undo.GetCurrentGroup();
            var go = UIFactory.NewUI("Group", parent);
            go.layer = key.gameObject.layer;
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Group UI");

            var g = (RectTransform)go.transform;
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
            g.sizeDelta = bounds.size;
            g.anchoredPosition = bounds.center - parent.rect.center;
            g.SetSiblingIndex(siblingIndex);

            foreach (var rt in rts.OrderBy(rt => rt.parent == parent ? 0 : 1).ThenBy(rt => rt.GetSiblingIndex()))
                Undo.SetTransformParent(rt, g, true, "Group UI");

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = go;
        }

        /// <summary>Moves the children of the selected group(s) up one level, in place, and deletes the group.</summary>
        [MenuItem(Menu + "Ungroup", priority = 241)]
        public static void Ungroup()
        {
            var groups = SelectedTopLevel().Where(rt => rt.childCount > 0).ToArray();
            if (groups.Length == 0) return;

            var withComponents = groups.Where(g => g.GetComponents<Component>().Length > 2 ||
                                                   (g.GetComponents<Component>().Length == 2 && !g.GetComponent<CanvasRenderer>()))
                .Select(g => g.name).ToArray();
            if (withComponents.Length > 0 && !EditorUtility.DisplayDialog("Ungroup",
                    $"{string.Join(", ", withComponents)} has components besides RectTransform (Image, Layout Group, ...). " +
                    "They will be deleted with it.", "Ungroup", "Cancel"))
                return;

            var undoGroup = Undo.GetCurrentGroup();
            var newSelection = new System.Collections.Generic.List<GameObject>();
            foreach (var g in groups)
            {
                var parent = g.parent;
                var index = g.GetSiblingIndex();
                var children = g.Cast<Transform>().ToList();
                for (var i = 0; i < children.Count; i++)
                {
                    Undo.SetTransformParent(children[i], parent, true, "Ungroup UI");
                    Undo.SetSiblingIndex(children[i], index + i, "Ungroup UI");
                    newSelection.Add(children[i].gameObject);
                }
                Undo.DestroyObjectImmediate(g.gameObject);
            }
            Undo.CollapseUndoOperations(undoGroup);
            Selection.objects = newSelection.ToArray();
        }

        [MenuItem(Menu + "Group Selection", true)]
        static bool CanGroup() => SelectedTopLevel().Length > 0;

        [MenuItem(Menu + "Ungroup", true)]
        static bool CanUngroup() => SelectedTopLevel().Any(rt => rt.childCount > 0);
    }
}
