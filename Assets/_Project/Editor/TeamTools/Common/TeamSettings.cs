using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    /// <summary>Personal settings (stored in EditorPrefs, separate for each person).</summary>
    public static class TeamSettings
    {
        const string Prefix = "GameJam.TeamTools.";

        public static event Action Changed;

        public static bool LocksEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "LocksEnabled", true);
            set => SetBool("LocksEnabled", value);
        }

        public static bool AutoLockOnEdit
        {
            get => EditorPrefs.GetBool(Prefix + "AutoLockOnEdit", true);
            set => SetBool("AutoLockOnEdit", value);
        }

        public static bool AskUnlockOnQuit
        {
            get => EditorPrefs.GetBool(Prefix + "AskUnlockOnQuit", true);
            set => SetBool("AskUnlockOnQuit", value);
        }

        public static int RefreshIntervalSeconds
        {
            get => EditorPrefs.GetInt(Prefix + "RefreshInterval", 60);
            set { EditorPrefs.SetInt(Prefix + "RefreshInterval", Mathf.Clamp(value, 15, 600)); Changed?.Invoke(); }
        }

        public static bool NotesVisible
        {
            get => EditorPrefs.GetBool(Prefix + "NotesVisible", true);
            set { SetBool("NotesVisible", value); SceneView.RepaintAll(); }
        }

        public static bool ShowResolvedNotes
        {
            get => EditorPrefs.GetBool(Prefix + "ShowResolvedNotes", false);
            set { SetBool("ShowResolvedNotes", value); SceneView.RepaintAll(); }
        }

        public static string UserNameOverride
        {
            get => EditorPrefs.GetString(Prefix + "UserName", "");
            set { EditorPrefs.SetString(Prefix + "UserName", value.Trim()); cachedUserName = null; Changed?.Invoke(); }
        }

        static string cachedUserName;

        /// <summary>Name used for notes: setting → git config user.name → Windows user name.</summary>
        public static string UserName
        {
            get
            {
                if (!string.IsNullOrEmpty(UserNameOverride)) return UserNameOverride;
                if (cachedUserName != null) return cachedUserName;

                var r = Git.Run("config user.name", 5000);
                cachedUserName = r.Ok && !string.IsNullOrWhiteSpace(r.Output) ? r.Output.Trim() : Environment.UserName;
                return cachedUserName;
            }
        }

        static void SetBool(string key, bool value)
        {
            EditorPrefs.SetBool(Prefix + key, value);
            Changed?.Invoke();
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Team Tools", SettingsScope.User)
            {
                label = "Team Tools",
                keywords = new HashSet<string> { "lock", "scene", "notes", "team" },
                guiHandler = _ =>
                {
                    EditorGUIUtility.labelWidth = 260;

                    EditorGUILayout.LabelField("Scene Locks", EditorStyles.boldLabel);
                    LocksEnabled = EditorGUILayout.Toggle("Enabled", LocksEnabled);
                    using (new EditorGUI.DisabledScope(!LocksEnabled))
                    {
                        AutoLockOnEdit = EditorGUILayout.Toggle(
                            new GUIContent("Lock scene on first edit",
                                "As soon as you change something in a free scene, it is automatically locked for you."),
                            AutoLockOnEdit);
                        AskUnlockOnQuit = EditorGUILayout.Toggle("Offer to unlock scenes on quit", AskUnlockOnQuit);
                        RefreshIntervalSeconds = EditorGUILayout.IntSlider("Refresh status every (sec)", RefreshIntervalSeconds, 15, 600);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);
                    NotesVisible = EditorGUILayout.Toggle("Show in Scene view", NotesVisible);
                    ShowResolvedNotes = EditorGUILayout.Toggle("Show resolved", ShowResolvedNotes);
                    var name = EditorGUILayout.DelayedTextField(
                        new GUIContent("Author name (empty = git user.name)"), UserNameOverride);
                    if (name != UserNameOverride) UserNameOverride = name;
                    EditorGUILayout.LabelField("Currently used", UserName);
                }
            };
        }
    }
}
