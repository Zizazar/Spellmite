using UnityEditor;
using UnityEngine;

namespace GameJam.UITools
{
    /// <summary>
    /// Arrow keys in the Scene view move selected UI elements by 1 canvas pixel (Shift: 10).
    /// Only kicks in when everything selected is UI, so normal Scene view navigation is untouched otherwise.
    /// </summary>
    [InitializeOnLoad]
    static class UINudge
    {
        const double CollapseWindow = 1.0;
        static double lastNudgeTime;
        static int undoGroup = -1;

        static UINudge()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += () => undoGroup = -1;
        }

        [MenuItem("Tools/UI/Arrow-Key Nudge in Scene View", priority = 40)]
        static void Toggle() => UIToolsPrefs.NudgeEnabled = !UIToolsPrefs.NudgeEnabled;

        [MenuItem("Tools/UI/Arrow-Key Nudge in Scene View", true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/UI/Arrow-Key Nudge in Scene View", UIToolsPrefs.NudgeEnabled);
            return true;
        }

        static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !UIToolsPrefs.NudgeEnabled || EditorGUIUtility.editingTextField) return;
            if (e.control || e.command || e.alt) return;

            Vector2 dir;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: dir = Vector2.left; break;
                case KeyCode.RightArrow: dir = Vector2.right; break;
                case KeyCode.UpArrow: dir = Vector2.up; break;
                case KeyCode.DownArrow: dir = Vector2.down; break;
                default: return;
            }

            var rts = UILayoutCommands.SelectedTopLevel();
            if (rts.Length == 0 || rts.Length != Selection.transforms.Length) return;

            // Hold-to-repeat and quick taps collapse into one undo step.
            var now = EditorApplication.timeSinceStartup;
            if (undoGroup < 0 || now - lastNudgeTime > CollapseWindow) undoGroup = Undo.GetCurrentGroup();
            lastNudgeTime = now;

            var step = e.shift ? 10f : 1f;
            Undo.RecordObjects(rts, "Nudge UI");
            foreach (var rt in rts)
                RectOps.Move(rt, dir * step, RectOps.SpaceFor(rt));
            Undo.CollapseUndoOperations(undoGroup);
            e.Use();
        }
    }
}
