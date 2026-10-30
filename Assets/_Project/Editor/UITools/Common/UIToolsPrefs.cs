using UnityEditor;

namespace GameJam.UITools
{
    /// <summary>Per-user toggles for the UI tools, stored in EditorPrefs.</summary>
    static class UIToolsPrefs
    {
        const string Prefix = "GameJam.UITools.";

        /// <summary>With several objects selected, align each to its parent instead of to the selection bounds.</summary>
        public static bool AlignToParent
        {
            get => EditorPrefs.GetBool(Prefix + "AlignToParent", false);
            set => EditorPrefs.SetBool(Prefix + "AlignToParent", value);
        }

        public static bool NudgeEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "Nudge", true);
            set => EditorPrefs.SetBool(Prefix + "Nudge", value);
        }

        public static bool ShowRaycastTargets
        {
            get => EditorPrefs.GetBool(Prefix + "ShowRaycasts", false);
            set
            {
                EditorPrefs.SetBool(Prefix + "ShowRaycasts", value);
                SceneView.RepaintAll();
            }
        }

        public static float StackGap
        {
            get => EditorPrefs.GetFloat(Prefix + "StackGap", 10f);
            set => EditorPrefs.SetFloat(Prefix + "StackGap", value);
        }
    }
}
