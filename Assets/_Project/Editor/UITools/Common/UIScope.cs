using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.UITools
{
    /// <summary>What the tools look at: the open prefab if one is being edited, otherwise all loaded scenes.</summary>
    static class UIScope
    {
        public static bool InPrefabStage => PrefabStageUtility.GetCurrentPrefabStage() != null;

        public static string Describe()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return $"Prefab '{stage.prefabContentsRoot.name}'";
            return SceneManager.sceneCount == 1
                ? $"Scene '{SceneManager.GetActiveScene().name}'"
                : $"{SceneManager.sceneCount} loaded scenes";
        }

        public static IEnumerable<GameObject> Roots()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                yield return stage.prefabContentsRoot;
                yield break;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    yield return root;
            }
        }

        /// <summary>All components of type T in scope, including ones on inactive objects.</summary>
        public static List<T> FindAll<T>()
        {
            var result = new List<T>();
            var buffer = new List<T>();
            foreach (var root in Roots())
            {
                root.GetComponentsInChildren(true, buffer);
                result.AddRange(buffer);
            }
            return result;
        }

        /// <summary>The canvas whose GraphicRaycaster would handle this object (nearest Canvas up the hierarchy).</summary>
        public static Canvas OwningCanvas(Component c) => c.GetComponentInParent<Canvas>(true);

        public static bool IsRootCanvas(Canvas c) =>
            c.transform.parent == null || c.transform.parent.GetComponentInParent<Canvas>(true) == null;

        public static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
