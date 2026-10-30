using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace GameJam.UITools
{
    /// <summary>Scene view panel with layout buttons. Appears automatically while a UI element is selected.</summary>
    [Overlay(typeof(SceneView), "gamejam-ui-tools", "UI Tools", true)]
    class UIToolsOverlay : IMGUIOverlay, ITransientOverlay
    {
        const float LabelWidth = 52;
        const float IconButton = 26;

        public bool visible => Selection.activeTransform is RectTransform;

        public override void OnGUI()
        {
            var count = UILayoutCommands.SelectedTopLevel().Length;
            var anchorCount = UILayoutCommands.SelectedAll().Length;

            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(250)))
            {
                AlignRow(count);
                ArrangeRow(count);
                SizeRow(count, anchorCount);
                AnchorRow(anchorCount);
                MiscRow(count);
            }
        }

        void AlignRow(int count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                RowLabel("Align");
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    IconStrip(("align-left", "Align left edges", () => UILayoutCommands.Align(UIAlign.Left)),
                        ("align-center", "Align horizontal centers", () => UILayoutCommands.Align(UIAlign.Center)),
                        ("align-right", "Align right edges", () => UILayoutCommands.Align(UIAlign.Right)));
                    GUILayout.Space(4);
                    IconStrip(("align-top", "Align top edges", () => UILayoutCommands.Align(UIAlign.Top)),
                        ("align-middle", "Align vertical centers", () => UILayoutCommands.Align(UIAlign.Middle)),
                        ("align-bottom", "Align bottom edges", () => UILayoutCommands.Align(UIAlign.Bottom)));
                }
                GUILayout.Space(4);

                var toParent = count <= 1 || UIToolsPrefs.AlignToParent;
                using (new EditorGUI.DisabledScope(count <= 1))
                {
                    var label = new GUIContent(toParent ? "to Parent" : "to Selection",
                        "Align to the bounds of the whole selection, or each element to its own parent.\nA single element always aligns to its parent.");
                    if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(78)))
                        UIToolsPrefs.AlignToParent = !UIToolsPrefs.AlignToParent;
                }
            }
        }

        void ArrangeRow(int count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                RowLabel("Spread");
                using (new EditorGUI.DisabledScope(count < 3))
                    IconStrip(("distribute-h", "Distribute horizontally: equal gaps, outer elements stay (3+ selected)", () => UILayoutCommands.Distribute(true)),
                        ("distribute-v", "Distribute vertically: equal gaps, outer elements stay (3+ selected)", () => UILayoutCommands.Distribute(false)));
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(count < 2))
                    IconStrip(("stack-h", "Stack left to right with the gap on the right", () => UILayoutCommands.Stack(true, UIToolsPrefs.StackGap)),
                        ("stack-v", "Stack top to bottom with the gap on the right", () => UILayoutCommands.Stack(false, UIToolsPrefs.StackGap)));

                GUILayout.Label(new GUIContent("gap", "Gap used by Stack, in canvas pixels"), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                UIToolsPrefs.StackGap = EditorGUILayout.FloatField(UIToolsPrefs.StackGap, GUILayout.Width(36));
            }
        }

        void SizeRow(int count, int anchorCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                RowLabel("Size");
                using (new EditorGUI.DisabledScope(count < 2))
                    IconStrip(("match-w", "Match width of the active (last clicked) element", () => UILayoutCommands.MatchSize(true)),
                        ("match-h", "Match height of the active (last clicked) element", () => UILayoutCommands.MatchSize(false)));
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(anchorCount == 0))
                {
                    TextButton("Fill", "Stretch to fill the parent (anchors 0–1, no offsets)", EditorStyles.miniButtonLeft, UILayoutCommands.FillParent);
                    TextButton("Round px", "Round edge offsets to whole pixels (fixes blurry text/sprites)", EditorStyles.miniButtonRight, UILayoutCommands.RoundPixels);
                }
            }
        }

        void AnchorRow(int anchorCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                RowLabel("Anchors");
                using (new EditorGUI.DisabledScope(anchorCount == 0))
                {
                    TextButton("→ Corners", "Move anchors to the element's corners so it scales with its parent at any resolution", EditorStyles.miniButtonLeft, UILayoutCommands.AnchorsToRect);
                    TextButton("Rect →", "Snap the element's edges onto its anchors", EditorStyles.miniButtonMid, UILayoutCommands.RectToAnchors);
                    TextButton("Nearest", "Anchor and pivot to the nearest corner/edge/center of the parent, keeping size (good for HUD)", EditorStyles.miniButtonRight, UILayoutCommands.AutoAnchor);
                }
            }
        }

        void MiscRow(int count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                RowLabel("");
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    TextButton("Group", "Wrap the selection in a new RectTransform that covers it", EditorStyles.miniButtonLeft, UILayoutCommands.Group);
                    TextButton("Ungroup", "Move children up one level and delete the group", EditorStyles.miniButtonRight, UILayoutCommands.Ungroup);
                }
                GUILayout.Space(4);

                var showRaycasts = GUILayout.Toggle(UIToolsPrefs.ShowRaycastTargets,
                    new GUIContent("Raycasts", "Outline every Raycast Target in the Scene view.\nGreen: interactive · Cyan: inside interactive · Yellow: text · Red: blocks clicks for nothing"),
                    EditorStyles.miniButtonLeft);
                if (showRaycasts != UIToolsPrefs.ShowRaycastTargets) UIToolsPrefs.ShowRaycastTargets = showRaycasts;

                TextButton("Doctor", "Open UI Doctor: finds common UI setup problems", EditorStyles.miniButtonRight, UIDoctorWindow.Open);
            }
        }

        static void RowLabel(string text) =>
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(LabelWidth));

        static void TextButton(string text, string tooltip, GUIStyle style, Action action)
        {
            if (GUILayout.Button(new GUIContent(text, tooltip), style, GUILayout.ExpandWidth(false)))
                Run(action);
        }

        // Commands can change the selection or open dialogs, which breaks the current IMGUI layout pass; restart it.
        static void Run(Action action)
        {
            action();
            GUIUtility.ExitGUI();
        }

        static void IconStrip(params (string icon, string tooltip, Action action)[] buttons)
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                var style = buttons.Length == 1 ? EditorStyles.miniButton
                    : i == 0 ? EditorStyles.miniButtonLeft
                    : i == buttons.Length - 1 ? EditorStyles.miniButtonRight
                    : EditorStyles.miniButtonMid;
                var content = new GUIContent(UIIcons.Get(buttons[i].icon), buttons[i].tooltip);
                if (GUILayout.Button(content, style, GUILayout.Width(IconButton), GUILayout.Height(20)))
                    Run(buttons[i].action);
            }
        }
    }
}
