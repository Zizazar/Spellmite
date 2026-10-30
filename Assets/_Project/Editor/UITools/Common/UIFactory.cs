using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UITOOLS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace GameJam.UITools
{
    /// <summary>Building blocks shared by the create menu and the UI Doctor fixes.</summary>
    static class UIFactory
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);
        const int UILayer = 5;

        public static void ApplyJamScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>Screen-space canvas scaled for 1920x1080, plus an EventSystem if the scene has none.</summary>
        public static Canvas CreateCanvas(Transform parent = null)
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = UILayer;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            ApplyJamScaler(go.GetComponent<CanvasScaler>());

            if (parent != null) go.transform.SetParent(parent, false);
            else StageUtility.PlaceGameObjectInCurrentStage(go);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");

            if (!UIScope.InPrefabStage && UIScope.FindAll<EventSystem>().Count == 0)
                CreateEventSystem();
            return canvas;
        }

        public static GameObject CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            AddInputModule(go, false);
            StageUtility.PlaceGameObjectInCurrentStage(go);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            return go;
        }

        /// <summary>Adds the input module that matches the project's Active Input Handling setting.</summary>
        public static void AddInputModule(GameObject go, bool undo)
        {
#if UITOOLS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (undo) Undo.AddComponent<InputSystemUIInputModule>(go);
            else go.AddComponent<InputSystemUIInputModule>();
#else
            if (undo) Undo.AddComponent<StandaloneInputModule>(go);
            else go.AddComponent<StandaloneInputModule>();
#endif
        }

        public static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Rect(GameObject go) => (RectTransform)go.transform;

        public static void Stretch(RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Sprite BuiltinSprite(string name) =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/" + name + ".psd");

        /// <summary>TextMeshPro needs its Essential Resources imported once per project before text can render.</summary>
        public static bool EnsureTmpEssentials()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;

            if (EditorUtility.DisplayDialog("TextMeshPro resources missing",
                    "This element uses TextMeshPro, but the project has no TMP Essential Resources yet.\n\n" +
                    "Import them now? Run the command again once the import finishes.",
                    "Import", "Cancel"))
            {
                if (!EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources"))
                    Debug.LogWarning("[UI Tools] Open Window > TextMeshPro > Import TMP Essential Resources to import them.");
            }
            return false;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = NewUI(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = alignment;
            t.raycastTarget = false;
            return t;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 size)
        {
            var go = NewUI(label + " Button", parent);
            Rect(go).sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.sprite = BuiltinSprite("UISprite");
            image.type = Image.Type.Sliced;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            var text = CreateText(go.transform, "Label", label, Mathf.Round(size.y * 0.42f));
            text.color = new Color(0.15f, 0.15f, 0.15f);
            Stretch(text.rectTransform);
            return button;
        }
    }
}
