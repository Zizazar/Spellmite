using UnityEngine;

namespace GameJam.UITools
{
    /// <summary>RectTransform math. "Space" is a transform whose local units are used for measuring (normally the root canvas, so 1 unit = 1 reference pixel).</summary>
    static class RectOps
    {
        static readonly Vector3[] corners = new Vector3[4];

        public static Transform SpaceFor(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas.rootCanvas.transform;
            return rt.parent != null ? rt.parent : rt;
        }

        /// <summary>Axis-aligned bounds of the rect, measured in <paramref name="space"/>.</summary>
        public static Rect GetRect(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(corners);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in corners)
            {
                Vector2 p = space.InverseTransformPoint(corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Moves by a delta measured in <paramref name="space"/>.</summary>
        public static void Move(RectTransform rt, Vector2 delta, Transform space)
        {
            if (delta.sqrMagnitude < 1e-8f || rt.parent == null) return;
            var world = space.TransformVector(delta);
            rt.anchoredPosition += (Vector2)rt.parent.InverseTransformVector(world);
        }

        static bool ParentSize(RectTransform rt, out Vector2 size)
        {
            size = rt.parent is RectTransform parent ? parent.rect.size : Vector2.zero;
            return size.x > 0.0001f && size.y > 0.0001f;
        }

        /// <summary>Changes anchors without moving or resizing the element.</summary>
        public static void SetAnchorsKeepRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (!(rt.parent is RectTransform parent)) return;
            var size = parent.rect.size;
            // Edges relative to the parent's bottom-left corner, in parent units.
            var edgeMin = rt.anchorMin * size + rt.offsetMin;
            var edgeMax = rt.anchorMax * size + rt.offsetMax;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = edgeMin - anchorMin * size;
            rt.offsetMax = edgeMax - anchorMax * size;
        }

        /// <summary>Puts the anchors on the element's corners so it scales with its parent (resolution independent).</summary>
        public static bool AnchorsToRect(RectTransform rt)
        {
            if (!ParentSize(rt, out var size)) return false;
            var edgeMin = rt.anchorMin * size + rt.offsetMin;
            var edgeMax = rt.anchorMax * size + rt.offsetMax;
            rt.anchorMin = new Vector2(edgeMin.x / size.x, edgeMin.y / size.y);
            rt.anchorMax = new Vector2(edgeMax.x / size.x, edgeMax.y / size.y);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return true;
        }

        /// <summary>Snaps the element's edges onto its anchors.</summary>
        public static void RectToAnchors(RectTransform rt)
        {
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors (and pivots) the element to the nearest of the 9 parent regions, keeping its size fixed. Good for HUD elements.</summary>
        public static bool AutoAnchor(RectTransform rt)
        {
            if (!ParentSize(rt, out var size)) return false;
            var center = (rt.anchorMin * size + rt.offsetMin + rt.anchorMax * size + rt.offsetMax) * 0.5f;
            var point = new Vector2(Snap(center.x / size.x), Snap(center.y / size.y));
            SetAnchorsKeepRect(rt, point, point);
            SetPivotKeepRect(rt, point);
            return true;

            float Snap(float t) => t < 1f / 3f ? 0f : t > 2f / 3f ? 1f : 0.5f;
        }

        public static void SetPivotKeepRect(RectTransform rt, Vector2 pivot)
        {
            var shift = Vector2.Scale(pivot - rt.pivot, rt.rect.size);
            rt.pivot = pivot;
            var parentShift = rt.localRotation * Vector3.Scale(shift, rt.localScale);
            rt.anchoredPosition += (Vector2)parentShift;
        }

        public static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Rounds edge offsets to whole units to avoid blurry half-pixel UI.</summary>
        public static void RoundOffsets(RectTransform rt)
        {
            rt.offsetMin = new Vector2(Mathf.Round(rt.offsetMin.x), Mathf.Round(rt.offsetMin.y));
            rt.offsetMax = new Vector2(Mathf.Round(rt.offsetMax.x), Mathf.Round(rt.offsetMax.y));
        }
    }
}
