#if UNITY_6000_3_OR_NEWER
using System.Linq;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>
    /// "itch.io" dropdown in Unity's main toolbar: current state at a glance plus quick actions.
    /// Hide or move it from the toolbar's right-click menu.
    /// </summary>
    static class ItchMainToolbar
    {
        const string ElementPath = "Itch.io/Publisher";
        static double lastRefresh;

        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Right)]
        static MainToolbarElement Create()
        {
            var icon = EditorGUIUtility.IconContent("CloudConnect").image as Texture2D;
            return new MainToolbarDropdown(new MainToolbarContent(Text, icon, Tooltip), ShowMenu);
        }

        static string Text
        {
            get
            {
                if (PublishPipeline.IsRunning)
                    return PublishPipeline.Phase == PipelinePhase.Uploading
                        ? $"itch {PublishPipeline.PhaseProgress * 100:0}%"
                        : "itch: building…";
                var last = PublishPipeline.LastRun;
                return last == null ? "itch.io" : (last.success ? "itch ✓ " : "itch ✗ ") + Versioning.Current;
            }
        }

        static string Tooltip
        {
            get
            {
                if (PublishPipeline.IsRunning) return PublishPipeline.Status;
                var last = PublishPipeline.LastRun;
                var settings = ItchProjectSettings.instance;
                var game = settings.HasGame ? settings.GameTarget : "no game page set";
                return last == null
                    ? $"itch.io Publisher ({game})"
                    : $"itch.io Publisher ({game})\nLast: {last.summary}, {ItchState.Ago(last.utc)}";
            }
        }

        static void ShowMenu(Rect rect)
        {
            var menu = new GenericMenu();
            var settings = ItchProjectSettings.instance;
            var running = PublishPipeline.IsRunning;
            var targets = settings.EnabledTargets.ToList();
            var channels = targets.Count == 0 ? "no targets" : string.Join(", ", targets.Select(t => t.channel));

            void Item(string label, bool enabled, GenericMenu.MenuFunction action)
            {
                if (enabled) menu.AddItem(new GUIContent(label), false, action);
                else menu.AddDisabledItem(new GUIContent(label));
            }

            Item($"Build and Publish ({channels})", !running && targets.Count > 0, () => PublishPipeline.BuildAndPublish());
            Item("Build only", !running && targets.Count > 0, () => PublishPipeline.BuildOnly());
            Item("Publish last builds", !running && targets.Count > 0, () => PublishPipeline.PublishLastBuilds());
            if (running) Item("Cancel upload", PublishPipeline.CanCancel, PublishPipeline.Cancel);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Dry run"), ItchUserPrefs.DryRun, () => ItchUserPrefs.DryRun = !ItchUserPrefs.DryRun);
            menu.AddSeparator("");
            Item("Open game page", settings.HasGame, () => Application.OpenURL(settings.GameUrl));
            Item("Open builds folder", true, () => ItchPublisherWindow.RevealFolder(settings.BuildRootFull));
            menu.AddItem(new GUIContent("Publisher window…"), false, () => ItchPublisherWindow.Open(ItchPublisherWindow.Tab.Dashboard));
            menu.DropDown(rect);
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            PublishPipeline.Changed += Refresh;
            ItchProjectSettings.Changed += Refresh;
        }

        static void Refresh()
        {
            // Progress fires up to 10 times a second; the toolbar doesn't need that.
            if (PublishPipeline.IsRunning && EditorApplication.timeSinceStartup - lastRefresh < 0.5) return;
            lastRefresh = EditorApplication.timeSinceStartup;
            MainToolbar.Refresh(ElementPath);
        }
    }
}
#endif
