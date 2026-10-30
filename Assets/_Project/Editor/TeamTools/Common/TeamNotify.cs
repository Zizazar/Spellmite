using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    /// <summary>Pop-up notifications in the Scene view, also written to the Console.</summary>
    public static class TeamNotify
    {
        public static void Info(string message)
        {
            Debug.Log("[Team] " + message);
            Show(message, 3);
        }

        public static void Warning(string message)
        {
            Debug.LogWarning("[Team] " + message);
            Show(message, 6);
        }

        static void Show(string message, double seconds)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.ShowNotification(new GUIContent(message), seconds);
        }
    }
}
