using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.UITools
{
    /// <summary>Draws raycast hit areas (including Raycast Padding) in the Scene view, plus the live hover target in Play Mode.</summary>
    [InitializeOnLoad]
    static class RaycastGizmos
    {
        static List<Graphic> cache;
        static double cacheTime;
        static readonly Vector3[] verts = new Vector3[4];

        /// <summary>Set by the Raycast Inspector while playing: the graphic currently under the pointer.</summary>
        public static Graphic LiveHover;

        static RaycastGizmos()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += () => cache = null;
        }

        [MenuItem("Tools/UI/Show Raycast Targets in Scene View", priority = 41)]
        static void Toggle() => UIToolsPrefs.ShowRaycastTargets = !UIToolsPrefs.ShowRaycastTargets;

        [MenuItem("Tools/UI/Show Raycast Targets in Scene View", true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/UI/Show Raycast Targets in Scene View", UIToolsPrefs.ShowRaycastTargets);
            return true;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;

            if (UIToolsPrefs.ShowRaycastTargets)
            {
                if (cache == null || EditorApplication.timeSinceStartup - cacheTime > 1.0)
                {
                    cache = UIScope.FindAll<Graphic>();
                    cacheTime = EditorApplication.timeSinceStartup;
                }

                foreach (var g in cache)
                {
                    if (g == null || !RaycastClassifier.IsLive(g)) continue;
                    var color = RaycastClassifier.ColorOf(RaycastClassifier.Classify(g));
                    DrawHitArea(g, new Color(color.r, color.g, color.b, 0.07f), color);
                }
            }

            if (LiveHover != null)
                DrawHitArea(LiveHover, new Color(1, 1, 1, 0.2f), Color.white);
        }

        public static void DrawHitArea(Graphic g, Color fill, Color outline)
        {
            var rt = g.rectTransform;
            var r = rt.rect;
            var pad = g.raycastPadding; // x=left, y=bottom, z=right, w=top; positive shrinks the hit area
            r.xMin += pad.x;
            r.yMin += pad.y;
            r.xMax -= pad.z;
            r.yMax -= pad.w;

            verts[0] = rt.TransformPoint(new Vector3(r.xMin, r.yMin));
            verts[1] = rt.TransformPoint(new Vector3(r.xMin, r.yMax));
            verts[2] = rt.TransformPoint(new Vector3(r.xMax, r.yMax));
            verts[3] = rt.TransformPoint(new Vector3(r.xMax, r.yMin));
            Handles.DrawSolidRectangleWithOutline(verts, fill, outline);
        }
    }
}
