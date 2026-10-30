using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#if UITOOLS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace GameJam.UITools
{
    public enum IssueSeverity { Error, Warning, Info }

    public class UIIssue
    {
        public IssueSeverity Severity;
        public string Message;
        public Object Target;
        public string FixLabel;
        public Action Fix;
    }

    /// <summary>Scans the open scenes (or prefab) for the UI mistakes that cost the most time at a jam.</summary>
    public static class UIDoctor
    {
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Dictionary<Type, FieldInfo[]> eventFieldCache = new Dictionary<Type, FieldInfo[]>();

        public static List<UIIssue> Run()
        {
            var issues = new List<UIIssue>();
            var canvases = UIScope.FindAll<Canvas>();
            var handlers = UIScope.FindAll<MonoBehaviour>().Where(m => m is IEventSystemHandler).ToList();

            CheckCanvases(canvases, handlers, issues);
            if (!UIScope.InPrefabStage) CheckEventSystems(canvases, handlers, issues);
            CheckSelectables(UIScope.FindAll<Selectable>(), issues);
            CheckEvents(issues);
            CheckGraphics(UIScope.FindAll<Graphic>(), issues);
            CheckCanvasGroups(UIScope.FindAll<CanvasGroup>(), issues);
            CheckMissingScripts(issues);

            return issues.OrderBy(i => i.Severity).ToList();
        }

        static void Add(List<UIIssue> issues, IssueSeverity severity, Object target, string message,
            string fixLabel = null, Action fix = null)
        {
            issues.Add(new UIIssue { Severity = severity, Target = target, Message = message, FixLabel = fixLabel, Fix = fix });
        }

        // ---------- Canvases ----------

        static void CheckCanvases(List<Canvas> canvases, List<MonoBehaviour> handlers, List<UIIssue> issues)
        {
            foreach (var canvas in canvases)
            {
                var name = canvas.name;
                var isRoot = UIScope.IsRootCanvas(canvas);

                if (isRoot && canvas.renderMode != RenderMode.WorldSpace)
                {
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler == null || scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                        Add(issues, IssueSeverity.Warning, canvas,
                            $"Canvas '{name}' uses a constant pixel size, so the UI looks tiny on big screens and huge in small windows (WebGL!).",
                            "Scale with 1920×1080", () =>
                            {
                                if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
                                Undo.RecordObject(scaler, "Scale With Screen Size");
                                UIFactory.ApplyJamScaler(scaler);
                            });

                    if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                        Add(issues, IssueSeverity.Warning, canvas,
                            $"Canvas '{name}' is 'Screen Space - Camera' but has no Render Camera, so it falls back to Overlay.",
                            Camera.main != null ? "Use Main Camera" : null,
                            Camera.main == null ? (Action)null : () =>
                            {
                                Undo.RecordObject(canvas, "Assign Canvas Camera");
                                canvas.worldCamera = Camera.main;
                            });
                }

                if (isRoot && canvas.renderMode == RenderMode.WorldSpace && canvas.transform.lossyScale.x > 0.2f)
                    Add(issues, IssueSeverity.Info, canvas,
                        $"World Space canvas '{name}' has scale ~1, so every UI pixel is a whole world unit. A scale around 0.01 is typical.",
                        "Set scale 0.01", () =>
                        {
                            Undo.RecordObject(canvas.transform, "Scale World Canvas");
                            canvas.transform.localScale = Vector3.one * 0.01f;
                        });

                var hasHandlers = handlers.Any(h => h != null && UIScope.OwningCanvas(h) == canvas);
                if (hasHandlers && canvas.GetComponent<GraphicRaycaster>() == null)
                    Add(issues, IssueSeverity.Error, canvas,
                        isRoot
                            ? $"Canvas '{name}' has buttons/handlers but no Graphic Raycaster — nothing on it can be clicked."
                            : $"Nested canvas '{name}' has buttons/handlers but no Graphic Raycaster of its own. The parent canvas's raycaster does not see them, so they can't be clicked.",
                        "Add Graphic Raycaster", () => Undo.AddComponent<GraphicRaycaster>(canvas.gameObject));
            }
        }

        // ---------- EventSystem ----------

        static void CheckEventSystems(List<Canvas> canvases, List<MonoBehaviour> handlers, List<UIIssue> issues)
        {
            var systems = UIScope.FindAll<EventSystem>();
            var needsInput = handlers.Count > 0 || canvases.Any(c => c.GetComponent<GraphicRaycaster>() != null);

            if (systems.Count == 0)
            {
                if (needsInput)
                    Add(issues, IssueSeverity.Error, null,
                        "There is no EventSystem in the scene — no button, slider or input field will respond.",
                        "Create EventSystem", () => UIFactory.CreateEventSystem());
                return;
            }

            var active = systems.Where(s => s.enabled && s.gameObject.activeInHierarchy).ToList();
            if (active.Count > 1)
                Add(issues, IssueSeverity.Warning, active[1],
                    $"{active.Count} active EventSystems in the loaded scenes. Only one is used and the rest spam warnings. Keep one (e.g. in a bootstrap scene).");

            foreach (var es in systems)
            {
                var modules = es.GetComponents<BaseInputModule>();
                if (modules.Length == 0)
                {
                    Add(issues, IssueSeverity.Error, es, $"EventSystem '{es.name}' has no Input Module, so it never receives input.",
                        "Add Input Module", () => UIFactory.AddInputModule(es.gameObject, true));
                    continue;
                }

#if UITOOLS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null)
                    Add(issues, IssueSeverity.Error, es,
                        $"EventSystem '{es.name}' uses StandaloneInputModule, but the old Input Manager is disabled in Player Settings — UI input throws errors in Play Mode.",
                        "Replace with Input System module", () =>
                        {
                            var go = es.gameObject;
                            Undo.DestroyObjectImmediate(legacy);
                            if (go.GetComponent<InputSystemUIInputModule>() == null)
                                Undo.AddComponent<InputSystemUIInputModule>(go);
                        });
#endif
            }
        }

        // ---------- Selectables ----------

        static void CheckSelectables(List<Selectable> selectables, List<UIIssue> issues)
        {
            foreach (var s in selectables)
            {
                var label = $"{s.GetType().Name} '{s.name}'";
                var ownGraphic = s.GetComponent<Graphic>();

                if (s.targetGraphic == null &&
                    (s.transition == Selectable.Transition.ColorTint || s.transition == Selectable.Transition.SpriteSwap))
                    Add(issues, IssueSeverity.Warning, s,
                        $"{label} has no Target Graphic, so it gives no hover/press feedback.",
                        ownGraphic != null ? "Use own " + ownGraphic.GetType().Name : null,
                        ownGraphic == null ? (Action)null : () =>
                        {
                            Undo.RecordObject(s, "Assign Target Graphic");
                            s.targetGraphic = ownGraphic;
                        });

                if (s.interactable && s.gameObject.activeInHierarchy &&
                    !s.GetComponentsInChildren<Graphic>(true).Any(g => g.raycastTarget && g.enabled))
                {
                    var fixTarget = s.targetGraphic != null ? s.targetGraphic : ownGraphic;
                    Add(issues, IssueSeverity.Error, s,
                        $"{label} can't be clicked: no Graphic on it or its children has Raycast Target enabled.",
                        fixTarget != null ? "Enable Raycast Target" : null,
                        fixTarget == null ? (Action)null : () =>
                        {
                            Undo.RecordObject(fixTarget, "Enable Raycast Target");
                            fixTarget.raycastTarget = true;
                        });
                }

                if (s is Button b && b.onClick.GetPersistentEventCount() == 0)
                    Add(issues, IssueSeverity.Info, s, $"{label} has no On Click listeners in the Inspector (fine if it's wired up from code).");
            }
        }

        // ---------- UnityEvents (On Click, On Value Changed, ...) ----------

        static void CheckEvents(List<UIIssue> issues)
        {
            foreach (var behaviour in UIScope.FindAll<MonoBehaviour>())
            {
                if (behaviour == null || !(behaviour.transform is RectTransform)) continue;
                foreach (var field in EventFields(behaviour.GetType()))
                {
                    if (!(field.GetValue(behaviour) is UnityEventBase ev)) continue;
                    var eventName = $"{behaviour.GetType().Name}.{ObjectNames.NicifyVariableName(field.Name)} on '{behaviour.name}'";

                    for (var i = 0; i < ev.GetPersistentEventCount(); i++)
                    {
                        var target = ev.GetPersistentTarget(i);
                        var method = ev.GetPersistentMethodName(i);
                        if (target == null)
                            Add(issues, IssueSeverity.Error, behaviour, $"{eventName}: listener #{i + 1} has no target object (deleted or never assigned).");
                        else if (string.IsNullOrEmpty(method))
                            Add(issues, IssueSeverity.Warning, behaviour, $"{eventName}: listener #{i + 1} has no function selected.");
                        else if (!HasMethod(target.GetType(), method))
                            Add(issues, IssueSeverity.Error, behaviour, $"{eventName}: calls {target.GetType().Name}.{method}, which no longer exists (renamed or deleted?).");
                    }
                }
            }
        }

        static FieldInfo[] EventFields(Type type)
        {
            if (eventFieldCache.TryGetValue(type, out var fields)) return fields;
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                list.AddRange(t.GetFields(AnyInstance).Where(f => typeof(UnityEventBase).IsAssignableFrom(f.FieldType)));
            return eventFieldCache[type] = list.ToArray();
        }

        static bool HasMethod(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.GetMethods(AnyInstance | BindingFlags.Static).Any(m => m.Name == name))
                    return true;
            return false;
        }

        // ---------- Graphics ----------

        static void CheckGraphics(List<Graphic> graphics, List<UIIssue> issues)
        {
            var inPrefab = UIScope.InPrefabStage;
            foreach (var g in graphics)
            {
                if (!inPrefab && g.GetComponentInParent<Canvas>(true) == null)
                    Add(issues, IssueSeverity.Error, g, $"'{g.name}' ({g.GetType().Name}) is not under a Canvas, so it won't render.");

                if (g is Text)
                    Add(issues, IssueSeverity.Info, g, $"'{g.name}' uses legacy Text. TextMeshPro stays crisp at any resolution.");

                if (g is TMP_Text tmp && tmp.font == null)
                {
                    var fallback = Resources.Load<TMP_Settings>("TMP Settings") != null ? TMP_Settings.defaultFontAsset : null;
                    Add(issues, IssueSeverity.Error, g, $"Text '{g.name}' has no Font Asset, so it's invisible.",
                        fallback != null ? "Use default font" : null,
                        fallback == null ? (Action)null : () =>
                        {
                            Undo.RecordObject(tmp, "Assign Font");
                            tmp.font = fallback;
                        });
                }

                if (g is Image image)
                {
                    if ((image.type == Image.Type.Sliced || image.type == Image.Type.Tiled) &&
                        image.sprite != null && image.sprite.border == Vector4.zero)
                        Add(issues, IssueSeverity.Warning, g,
                            $"Image '{g.name}' is {image.type} but sprite '{image.sprite.name}' has no 9-slice borders, so it just stretches. Set borders in the Sprite Editor.",
                            "Set to Simple", () =>
                            {
                                Undo.RecordObject(image, "Image Type Simple");
                                image.type = Image.Type.Simple;
                            });

                    if (image.sprite == null && image.color.a < 0.01f && image.raycastTarget && image.enabled && image.gameObject.activeInHierarchy)
                        Add(issues, IssueSeverity.Info, g,
                            $"Image '{g.name}' is invisible (alpha 0) but still catches clicks. Fine if it's a deliberate click blocker.",
                            "Disable Raycast Target", () =>
                            {
                                Undo.RecordObject(image, "Disable Raycast Target");
                                image.raycastTarget = false;
                            });
                }
            }
        }

        // ---------- CanvasGroups ----------

        static void CheckCanvasGroups(List<CanvasGroup> groups, List<UIIssue> issues)
        {
            foreach (var cg in groups)
            {
                if (!cg.enabled || !cg.gameObject.activeInHierarchy || cg.alpha > 0.001f || !cg.blocksRaycasts) continue;
                Add(issues, IssueSeverity.Warning, cg,
                    $"CanvasGroup on '{cg.name}' is invisible (alpha 0) but still blocks raycasts — a hidden panel that eats clicks.",
                    "Stop blocking", () =>
                    {
                        Undo.RecordObject(cg, "CanvasGroup Stop Blocking");
                        cg.blocksRaycasts = false;
                        cg.interactable = false;
                    });
            }
        }

        // ---------- Missing scripts ----------

        static void CheckMissingScripts(List<UIIssue> issues)
        {
            foreach (var rt in UIScope.FindAll<RectTransform>())
            {
                var go = rt.gameObject;
                var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count == 0) continue;
                Add(issues, IssueSeverity.Warning, go, $"'{go.name}' has {count} missing script(s).",
                    "Remove missing", () =>
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    });
            }
        }
    }
}
