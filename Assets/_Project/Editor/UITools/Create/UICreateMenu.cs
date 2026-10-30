using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UITools
{
    /// <summary>
    /// GameObject > UI Jam: ready-made UI pieces with sane defaults (scaled canvas, correct input module,
    /// text that doesn't block clicks, layout groups already set up). Also in the Hierarchy "+" / right-click menu.
    /// </summary>
    static class UICreateMenu
    {
        const string Menu = "GameObject/UI Jam/";
        const int Priority = 10;

        [MenuItem(Menu + "Canvas (1920x1080 scaled)", false, Priority)]
        static void CreateCanvas(MenuCommand cmd)
        {
            var parent = cmd.context is GameObject go ? go.transform : PrefabRootOrNull();
            Selection.activeGameObject = UIFactory.CreateCanvas(parent).gameObject;
        }

        [MenuItem(Menu + "Text (no raycast)", false, Priority + 1)]
        static void CreateText(MenuCommand cmd) => Build(cmd, "Text", parent =>
        {
            var text = UIFactory.CreateText(parent, "Text", "New Text", 36);
            text.rectTransform.sizeDelta = new Vector2(400, 80);
            return text.gameObject;
        }, needsTmp: true);

        [MenuItem(Menu + "Button", false, Priority + 2)]
        static void CreateButton(MenuCommand cmd) => Build(cmd, "Button",
            parent => UIFactory.CreateButton(parent, "Button", new Vector2(320, 80)).gameObject, needsTmp: true);

        [MenuItem(Menu + "Fullscreen Panel", false, Priority + 20)]
        static void CreatePanel(MenuCommand cmd) => Build(cmd, "Panel", parent =>
        {
            var go = UIFactory.NewUI("Panel", parent);
            UIFactory.Stretch(UIFactory.Rect(go));
            go.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            go.AddComponent<CanvasGroup>();
            return go;
        });

        [MenuItem(Menu + "Menu (Title + Buttons)", false, Priority + 21)]
        static void CreateMenu(MenuCommand cmd) => Build(cmd, "Menu", parent =>
        {
            var go = UIFactory.NewUI("Menu", parent);
            UIFactory.Rect(go).sizeDelta = new Vector2(560, 0);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = UIFactory.CreateText(go.transform, "Title", "GAME TITLE", 80);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 140;

            foreach (var label in new[] { "Play", "Settings", "Quit" })
                UIFactory.CreateButton(go.transform, label, new Vector2(400, 90));
            return go;
        }, needsTmp: true);

        [MenuItem(Menu + "Modal Dialog", false, Priority + 22)]
        static void CreateModal(MenuCommand cmd) => Build(cmd, "Modal", parent =>
        {
            var root = UIFactory.NewUI("Modal", parent);
            UIFactory.Stretch(UIFactory.Rect(root));
            root.AddComponent<CanvasGroup>();

            // Full-screen dim layer: intentionally catches clicks so the game behind can't be clicked.
            var blocker = UIFactory.NewUI("Blocker", root.transform);
            UIFactory.Stretch(UIFactory.Rect(blocker));
            blocker.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var window = UIFactory.NewUI("Window", root.transform);
            UIFactory.Rect(window).sizeDelta = new Vector2(760, 440);
            var bg = window.AddComponent<Image>();
            bg.sprite = UIFactory.BuiltinSprite("Background");
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.16f, 0.17f, 0.2f);

            var layout = window.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 32);
            layout.spacing = 20;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = UIFactory.CreateText(window.transform, "Title", "Are you sure?", 48, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            var body = UIFactory.CreateText(window.transform, "Body", "Dialog text goes here.", 32, TextAlignmentOptions.TopLeft);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var row = UIFactory.NewUI("Buttons", window.transform);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20;
            rowLayout.childAlignment = TextAnchor.MiddleRight;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().preferredHeight = 80;
            UIFactory.CreateButton(row.transform, "Cancel", new Vector2(220, 76));
            UIFactory.CreateButton(row.transform, "OK", new Vector2(220, 76));
            return root;
        }, needsTmp: true);

        [MenuItem(Menu + "HUD (anchored corners)", false, Priority + 23)]
        static void CreateHud(MenuCommand cmd) => Build(cmd, "HUD", parent =>
        {
            const float margin = 24;
            var hud = UIFactory.NewUI("HUD", parent);
            UIFactory.Stretch(UIFactory.Rect(hud));

            var corners = new (string name, Vector2 anchor, TextAnchor align)[]
            {
                ("Top Left", new Vector2(0, 1), TextAnchor.UpperLeft),
                ("Top Center", new Vector2(0.5f, 1), TextAnchor.UpperCenter),
                ("Top Right", new Vector2(1, 1), TextAnchor.UpperRight),
                ("Bottom Left", new Vector2(0, 0), TextAnchor.LowerLeft),
                ("Bottom Center", new Vector2(0.5f, 0), TextAnchor.LowerCenter),
                ("Bottom Right", new Vector2(1, 0), TextAnchor.LowerRight),
            };
            foreach (var (name, anchor, align) in corners)
            {
                var slot = UIFactory.NewUI(name, hud.transform);
                var rt = UIFactory.Rect(slot);
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.sizeDelta = new Vector2(480, 140);
                rt.anchoredPosition = new Vector2(Inward(anchor.x), Inward(anchor.y));

                // Drop elements in and they line up from the corner automatically.
                var layout = slot.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = align;
                layout.spacing = 12;
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            }
            return hud;

            float Inward(float a) => a == 0 ? margin : a == 1 ? -margin : 0;
        });

        [MenuItem(Menu + "Progress Bar", false, Priority + 24)]
        static void CreateProgressBar(MenuCommand cmd) => Build(cmd, "Progress Bar", parent =>
        {
            var bar = UIFactory.NewUI("Progress Bar", parent);
            UIFactory.Rect(bar).sizeDelta = new Vector2(400, 36);
            var bg = bar.AddComponent<Image>();
            bg.sprite = UIFactory.BuiltinSprite("UISprite");
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            bg.raycastTarget = false;

            var fillGo = UIFactory.NewUI("Fill", bar.transform);
            UIFactory.Stretch(UIFactory.Rect(fillGo), 4);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = UIFactory.BuiltinSprite("UISprite");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0.75f;
            fill.color = new Color(0.35f, 0.85f, 0.4f);
            fill.raycastTarget = false;
            return bar;
        });

        // ---------- helpers ----------

        static void Build(MenuCommand cmd, string undoName, System.Func<Transform, GameObject> create, bool needsTmp = false)
        {
            if (needsTmp && !UIFactory.EnsureTmpEssentials()) return;

            var undoGroup = Undo.GetCurrentGroup();
            var parent = ResolveParent(cmd.context as GameObject);
            var go = create(parent);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Create " + undoName);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = go;
        }

        /// <summary>The clicked/selected UI object, else the first canvas in scope, else a new canvas.</summary>
        static Transform ResolveParent(GameObject context)
        {
            var candidate = context != null ? context : Selection.activeGameObject;
            if (candidate != null && !EditorUtility.IsPersistent(candidate) &&
                candidate.transform is RectTransform && candidate.GetComponentInParent<Canvas>(true) != null)
                return candidate.transform;

            var canvas = UIScope.FindAll<Canvas>().FirstOrDefault(c => UIScope.IsRootCanvas(c) && c.gameObject.activeInHierarchy);
            if (canvas != null) return canvas.transform;

            var prefabRoot = PrefabRootOrNull();
            if (prefabRoot is RectTransform) return prefabRoot;
            return UIFactory.CreateCanvas(prefabRoot).transform;
        }

        // In Prefab Mode new roots must live under the prefab root or they won't be saved.
        static Transform PrefabRootOrNull() =>
            PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot.transform;
    }
}
