using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameJam.UITools
{
    public enum RaycastKind { Interactive, InsideInteractive, Text, Blocker }

    /// <summary>Explains why a Graphic is a raycast target and whether it needs to be one.</summary>
    static class RaycastClassifier
    {
        static readonly List<IEventSystemHandler> handlers = new List<IEventSystemHandler>();
        static readonly List<CanvasGroup> groups = new List<CanvasGroup>();

        public static RaycastKind Classify(Graphic g)
        {
            if (HasHandler(g.gameObject)) return RaycastKind.Interactive;
            var selectable = g.GetComponentInParent<Selectable>(true);
            if (selectable != null && selectable.targetGraphic == g) return RaycastKind.Interactive;
            if (g is TMP_Text || g is Text) return RaycastKind.Text;
            for (var t = g.transform.parent; t != null; t = t.parent)
                if (HasHandler(t.gameObject)) return RaycastKind.InsideInteractive;
            return RaycastKind.Blocker;
        }

        static bool HasHandler(GameObject go)
        {
            go.GetComponents(handlers);
            return handlers.Count > 0;
        }

        /// <summary>True when the GraphicRaycaster would actually test this graphic (mirrors Graphic.Raycast's CanvasGroup rules).</summary>
        public static bool IsLive(Graphic g)
        {
            if (!g.raycastTarget || !g.isActiveAndEnabled) return false;
            for (var t = g.transform; t != null; t = t.parent)
            {
                t.GetComponents(groups);
                foreach (var cg in groups)
                {
                    if (!cg.enabled) continue;
                    if (!cg.blocksRaycasts) return false;
                    if (cg.ignoreParentGroups) return true;
                }
            }
            return true;
        }

        public static Color ColorOf(RaycastKind kind)
        {
            switch (kind)
            {
                case RaycastKind.Interactive: return new Color(0.3f, 0.9f, 0.4f);
                case RaycastKind.InsideInteractive: return new Color(0.3f, 0.8f, 1f);
                case RaycastKind.Text: return new Color(1f, 0.85f, 0.2f);
                default: return new Color(1f, 0.35f, 0.25f);
            }
        }

        public static string LabelOf(RaycastKind kind)
        {
            switch (kind)
            {
                case RaycastKind.Interactive: return "Interactive";
                case RaycastKind.InsideInteractive: return "Inside interactive";
                case RaycastKind.Text: return "Text";
                default: return "Blocks clicks";
            }
        }

        public static string DescriptionOf(RaycastKind kind)
        {
            switch (kind)
            {
                case RaycastKind.Interactive:
                    return "Has an event handler or is a Selectable's Target Graphic. Needed.";
                case RaycastKind.InsideInteractive:
                    return "Passes clicks up to a parent handler (button icon, scroll content). Often unnecessary — keep it only if it enlarges the clickable area.";
                case RaycastKind.Text:
                    return "Text rarely needs to catch clicks. Usually safe to turn off.";
                default:
                    return "Catches clicks but nothing handles them, so it blocks everything behind it. Keep only for intentional blockers like modal backdrops.";
            }
        }
    }
}
