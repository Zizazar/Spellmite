using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    /// <summary>Lock icons in the Project and Hierarchy windows, plus a red banner in the Scene view.</summary>
    [InitializeOnLoad]
    static class SceneLockIcons
    {
        public static readonly Color MineColor = new(0.35f, 0.8f, 0.4f);
        public static readonly Color TheirsColor = new(0.95f, 0.35f, 0.3f);

        static GUIContent mineIcon, theirsIcon;
        static GUIStyle badgeStyle, bannerStyle;

        static SceneLockIcons()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectItem;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
            SceneView.duringSceneGui += DrawBanner;
        }

        public static GUIContent Icon(LockState state)
        {
            mineIcon ??= EditorGUIUtility.IconContent("P4_LockedLocal");
            theirsIcon ??= EditorGUIUtility.IconContent("P4_LockedRemote");
            return state == LockState.Mine ? mineIcon : state == LockState.Theirs ? theirsIcon : GUIContent.none;
        }

        public static string Describe(string assetPath)
        {
            if (SceneLockService.IsPending(assetPath)) return "…";
            var info = SceneLockService.GetLock(assetPath);
            return SceneLockService.GetState(assetPath) switch
            {
                LockState.Mine => "Yours",
                LockState.Theirs => $"Locked: {info.owner}",
                LockState.Free => "Free",
                _ => "—",
            };
        }

        static void OnProjectItem(string guid, Rect rect)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!SceneLockService.IsLockable(path)) return;
            var state = SceneLockService.GetState(path);
            if (state != LockState.Mine && state != LockState.Theirs) return;

            Rect iconRect;
            if (rect.height > 20) // grid mode (large icons)
                iconRect = new Rect(rect.x + rect.width - 18, rect.y + rect.height - 34, 16, 16);
            else
                iconRect = new Rect(rect.xMax - 18, rect.y, 16, 16);

            var info = SceneLockService.GetLock(path);
            var tooltip = state == LockState.Mine ? "Scene is locked by you" : $"{info.owner} is editing this scene ({info.Since})";
            GUI.Label(iconRect, new GUIContent(Icon(state).image, tooltip));
        }

        static void OnHierarchyItem(int instanceId, Rect rect)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if ((int)scene.handle != instanceId) continue;
                if (string.IsNullOrEmpty(scene.path)) return;

                var state = SceneLockService.GetState(scene.path);
                if (state == LockState.Unknown) return;

                badgeStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                var info = SceneLockService.GetLock(scene.path);
                var text = state == LockState.Mine ? "yours" : state == LockState.Theirs ? info.owner : "free";
                var size = badgeStyle.CalcSize(new GUIContent(text));
                // To the right of the scene name and left of the "⋮" menu button (~20 px).
                var nameEnd = rect.x + EditorStyles.boldLabel.CalcSize(new GUIContent(scene.name + (scene.isDirty ? "*" : ""))).x + 22;
                var iconRect = new Rect(rect.xMax - 38, rect.y, 16, 16);
                var labelRect = new Rect(iconRect.x - size.x - 2, rect.y, size.x, rect.height);
                var tooltip = state == LockState.Theirs ? $"{info.owner} is editing this scene ({info.Since})" : Describe(scene.path);

                var old = GUI.contentColor;
                GUI.contentColor = state == LockState.Mine ? MineColor : state == LockState.Theirs ? TheirsColor : Color.gray;
                if (labelRect.x > nameEnd) GUI.Label(labelRect, new GUIContent(text, tooltip), badgeStyle);
                GUI.contentColor = old;
                if (state != LockState.Free && iconRect.x > nameEnd)
                    GUI.Label(iconRect, new GUIContent(Icon(state).image, tooltip));
                return;
            }
        }

        static void DrawBanner(SceneView view)
        {
            if (!SceneLockService.IsReady) return;

            string text = null;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (SceneLockService.GetState(scene.path) != LockState.Theirs) continue;
                var info = SceneLockService.GetLock(scene.path);
                text = $"{info.owner} is editing scene \"{scene.name}\": your changes won't be saved";
                break;
            }
            if (text == null) return;

            bannerStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                padding = new RectOffset(10, 10, 4, 4),
            };

            Handles.BeginGUI();
            var size = bannerStyle.CalcSize(new GUIContent(text));
            var rect = new Rect((view.position.width - size.x) / 2, 4, size.x, size.y);
            EditorGUI.DrawRect(rect, new Color(0.7f, 0.12f, 0.1f, 0.9f));
            GUI.Label(rect, text, bannerStyle);
            Handles.EndGUI();
        }
    }
}
