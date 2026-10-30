using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.TeamTools
{
    /// <summary>"Team" panel in the Scene view: lock status of open scenes and notes.</summary>
    [Overlay(typeof(SceneView), "gamejam-team-overlay", "Team", defaultDisplay = true)]
    class TeamOverlay : IMGUIOverlay
    {
        public override void OnCreated()
        {
            SceneLockService.Changed += Repaint;
            SceneNoteStore.Changed += Repaint;
        }

        public override void OnWillBeDestroyed()
        {
            SceneLockService.Changed -= Repaint;
            SceneNoteStore.Changed -= Repaint;
        }

        void Repaint() => containerWindow?.Repaint();

        public override void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(230)))
            {
                DrawLocks();
                EditorGUILayout.Space(4);
                DrawNotes();
            }
        }

        static void DrawLocks()
        {
            if (!SceneLockService.IsReady)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Locks: " + SceneLockService.AvailabilityText, EditorStyles.miniLabel);
                    if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(22))) SceneLocksWindow.Open();
                }
                return;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.path)) continue;
                DrawSceneRow(scene);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All scenes", EditorStyles.miniButtonLeft)) SceneLocksWindow.Open();
                using (new EditorGUI.DisabledScope(SceneLockService.IsRefreshing))
                    if (GUILayout.Button(SceneLockService.IsRefreshing ? "…" : "Refresh", EditorStyles.miniButtonRight))
                        SceneLockService.Refresh();
            }
        }

        static void DrawSceneRow(Scene scene)
        {
            var state = SceneLockService.GetState(scene.path);
            var pending = SceneLockService.IsPending(scene.path);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(state is LockState.Mine or LockState.Theirs
                        ? SceneLockIcons.Icon(state)
                        : EditorGUIUtility.IconContent("SceneAsset Icon"),
                    GUILayout.Width(18), GUILayout.Height(18));

                var old = GUI.contentColor;
                GUI.contentColor = state == LockState.Mine ? SceneLockIcons.MineColor
                    : state == LockState.Theirs ? SceneLockIcons.TheirsColor : old;
                GUILayout.Label($"{scene.name}: {SceneLockIcons.Describe(scene.path)}", EditorStyles.miniLabel);
                GUI.contentColor = old;
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(pending))
                {
                    if (state == LockState.Free && GUILayout.Button("Lock", EditorStyles.miniButton))
                        SceneLocksWindow.LockWithFeedback(scene.path);
                    else if (state == LockState.Mine && GUILayout.Button("Unlock", EditorStyles.miniButton))
                        SceneLockService.Unlock(scene.path);
                }
            }
        }

        static void DrawNotes()
        {
            var open = SceneNoteStore.ForLoadedScenes().Count(n => !n.resolved);
            using (new EditorGUILayout.HorizontalScope())
            {
                TeamSettings.NotesVisible = GUILayout.Toggle(TeamSettings.NotesVisible,
                    $"Notes ({open})", EditorStyles.miniButtonLeft);
                if (GUILayout.Button(new GUIContent("+", "New note (Alt+N under the cursor)"),
                        EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    SceneNotesSceneGUI.RequestCreateAtPivot();
                if (GUILayout.Button("List", EditorStyles.miniButtonRight)) SceneNotesWindow.Open();
            }
        }
    }
}
