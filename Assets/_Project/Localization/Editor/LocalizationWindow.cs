using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>Keys × languages table with inline editing, per-language missing translations, and the project audit.</summary>
    public class LocalizationWindow : EditorWindow
    {
        enum Tab { Keys, Missing, Audit }
        static readonly string[] TabNames = { "Keys", "Missing", "Audit" };
        const float RowHeight = 20;

        [SerializeField] Tab tab;
        [SerializeField] string search = "";
        [SerializeField] bool missingOnly;
        [SerializeField] string missingLanguage;
        [SerializeField] bool[] auditOpen = { true, true, true, true };

        SearchField searchField;
        [NonSerialized] List<string> rows; // rebuilt after domain reload too
        Vector2 scroll;
        string newKey = "", newValue = "", newLangCode = "", newLangName = "";
        LocAuditResult audit;
        DateTime auditTime;

        static readonly Color MissingTint = new Color(1f, 0.25f, 0.2f, 0.18f);

        [MenuItem("Tools/Localization/Localization", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<LocalizationWindow>();
            window.titleContent = new GUIContent("Localization", EditorGUIUtility.IconContent("d_FilterByLabel").image);
            window.minSize = new Vector2(480, 260);
        }

        [MenuItem("Tools/Localization/Run Audit", priority = 1)]
        static void OpenAudit()
        {
            Open();
            var window = GetWindow<LocalizationWindow>();
            window.tab = Tab.Audit;
            window.RunAudit();
        }

        [MenuItem("Tools/Localization/Open Languages Folder", priority = 20)]
        static void RevealFolder()
        {
            var first = LocDatabase.Languages.Select(l => LocDatabase.Get(l).Path).FirstOrDefault();
            if (first != null) EditorUtility.RevealInFinder(first);
        }

        void OnEnable()
        {
            LocDatabase.Changed += OnDatabaseChanged;
            Loc.Changed += Repaint;
        }

        void OnDisable()
        {
            LocDatabase.Changed -= OnDatabaseChanged;
            Loc.Changed -= Repaint;
        }

        void OnDatabaseChanged()
        {
            rows = null;
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();
            if (LocDatabase.Languages.Count == 0)
            {
                EditorGUILayout.HelpBox($"No language files in {LocDatabase.Folder}.", MessageType.Warning);
                if (GUILayout.Button("Create en.txt")) LocDatabase.CreateLanguage(Loc.DefaultLanguage, "English");
                return;
            }
            switch (tab)
            {
                case Tab.Keys: DrawKeys(); break;
                case Tab.Missing: DrawMissing(); break;
                case Tab.Audit: DrawAudit(); break;
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(210));
                GUILayout.FlexibleSpace();

                var langs = LocDatabase.Languages.ToList();
                if (langs.Count > 0)
                {
                    GUILayout.Label(new GUIContent("Game language", "Loc.Language: what LocText and Loc.T show (Play Mode updates live)."), EditorStyles.miniLabel);
                    int current = Mathf.Max(0, langs.IndexOf(Loc.Language));
                    int picked = EditorGUILayout.Popup(current, langs.Select(l => $"{LocDatabase.Get(l).DisplayName} ({l})").ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(130));
                    if (picked != current) Loc.Language = langs[picked];
                }
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Reload language files"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                    LocDatabase.Reload();
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("FolderOpened Icon").image, "Show language files"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                    RevealFolder();
            }
        }

        // ---------- Keys ----------

        void DrawKeys()
        {
            var langs = LocDatabase.Languages;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (searchField == null) searchField = new SearchField();
                var s = searchField.OnToolbarGUI(search);
                if (s != search) { search = s; rows = null; }
                var m = GUILayout.Toggle(missingOnly, "Missing only", EditorStyles.toolbarButton, GUILayout.Width(90));
                if (m != missingOnly) { missingOnly = m; rows = null; }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("New", GUILayout.Width(32));
                newKey = EditorGUILayout.TextField(newKey, GUILayout.Width(position.width * 0.3f));
                newValue = EditorGUILayout.TextField(newValue);
                bool valid = LocFormat.IsValidKey(newKey) && !LocFormat.IsMeta(newKey) && !LocDatabase.IsDefined(newKey);
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button(new GUIContent("Add", valid || newKey.Length == 0 ? "Add the key to English" : "Key is taken or has invalid characters"), GUILayout.Width(50)))
                    {
                        LocDatabase.AddKey(newKey, LocFormat.Unescape(newValue));
                        search = newKey;
                        newKey = newValue = "";
                        GUI.FocusControl(null);
                    }
            }

            if (rows == null) rows = FilterRows();

            var area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout) return;

            float keyW = Mathf.Clamp(area.width * 0.28f, 140, 320);
            float colW = (area.width - keyW - 14) / langs.Count;

            // Header with completion per language.
            var header = new Rect(area.x, area.y, area.width, RowHeight);
            EditorGUI.DrawRect(header, new Color(0, 0, 0, 0.12f));
            GUI.Label(new Rect(header.x + 4, header.y, keyW - 4, RowHeight), $"Key ({rows.Count})", EditorStyles.boldLabel);
            for (int i = 0; i < langs.Count; i++)
            {
                var doc = LocDatabase.Get(langs[i]);
                var missing = langs[i] == Loc.DefaultLanguage ? 0 : LocDatabase.MissingKeys(langs[i]).Count;
                var label = $"{doc.DisplayName} ({langs[i]})" + (missing > 0 ? $"  −{missing}" : "");
                GUI.Label(new Rect(header.x + keyW + i * colW + 4, header.y, colW - 4, RowHeight), label, EditorStyles.boldLabel);
            }

            var view = new Rect(area.x, area.y + RowHeight, area.width, area.height - RowHeight);
            var content = new Rect(0, 0, view.width - 14, rows.Count * RowHeight);
            scroll = GUI.BeginScrollView(view, scroll, content);
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int last = Mathf.Min(rows.Count, first + (int)(view.height / RowHeight) + 2);
            for (int r = first; r < last; r++)
                DrawKeyRow(rows[r], new Rect(0, r * RowHeight, content.width, RowHeight), keyW, colW, r);
            GUI.EndScrollView();
        }

        void DrawKeyRow(string key, Rect rect, float keyW, float colW, int index)
        {
            if (index % 2 == 1) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.05f));
            var keyRect = new Rect(rect.x + 4, rect.y + 1, keyW - 8, rect.height - 2);
            GUI.Label(keyRect, new GUIContent(key, key));
            if (Event.current.type == EventType.ContextClick && keyRect.Contains(Event.current.mousePosition))
            {
                KeyMenu(key).ShowAsContext();
                Event.current.Use();
            }

            var langs = LocDatabase.Languages;
            for (int i = 0; i < langs.Count; i++)
            {
                var cell = new Rect(rect.x + keyW + i * colW, rect.y + 1, colW - 4, rect.height - 2);
                var value = LocDatabase.Value(langs[i], key);
                var edited = EditorGUI.DelayedTextField(cell, LocFormat.Escape(value));
                if (string.IsNullOrEmpty(value) && IsRequired(langs[i], key)) EditorGUI.DrawRect(cell, MissingTint);
                if (edited != LocFormat.Escape(value)) LocDatabase.Set(langs[i], key, LocFormat.Unescape(edited));
            }
        }

        GenericMenu KeyMenu(string key)
        {
            var menu = new GenericMenu();
            var name = PluralRules.BaseKey(key) ?? key;
            menu.AddItem(new GUIContent("Copy key"), false, () => EditorGUIUtility.systemCopyBuffer = name);
            menu.AddItem(new GUIContent("Copy Loc.T(...)"), false, () => EditorGUIUtility.systemCopyBuffer = $"Loc.T(\"{name}\")");
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Delete '{name}' from all languages"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Delete key", $"Remove '{name}' (and its plural forms) from every language file?", "Delete", "Cancel"))
                    LocDatabase.RemoveKeys(new[] { name });
            });
            return menu;
        }

        // A plural form a language does not use (Russian has no "other", English no "few") is not missing.
        static bool IsRequired(string language, string key)
        {
            var baseKey = PluralRules.BaseKey(key);
            if (baseKey == null) return true;
            return PluralRules.FormsFor(language).Contains(key.Substring(baseKey.Length + 1));
        }

        List<string> FilterRows()
        {
            var langs = LocDatabase.Languages;
            IEnumerable<string> keys = LocDatabase.Keys;
            // Plural forms other languages need (ru coins.few) get rows too, next to the English ones.
            var extra = new List<string>();
            foreach (var lang in langs)
                foreach (var k in LocDatabase.MissingKeys(lang))
                    if (!LocDatabase.Keys.Contains(k)) extra.Add(k);
            keys = keys.Concat(extra.Distinct()).OrderBy(k => OrderOf(k)).ToList();

            if (!string.IsNullOrEmpty(search))
                keys = keys.Where(k => k.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                                       || langs.Any(l => (LocDatabase.Value(l, k) ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            if (missingOnly)
                keys = keys.Where(k => langs.Any(l => string.IsNullOrEmpty(LocDatabase.Value(l, k)) && IsRequired(l, k)));
            return keys.ToList();
        }

        // English file order with every plural form next to its group; keys English lacks go last.
        static int OrderOf(string key)
        {
            var all = LocDatabase.Base?.Keys ?? LocDatabase.Keys;
            var baseKey = PluralRules.BaseKey(key);
            for (int i = 0; i < all.Count; i++)
            {
                if (baseKey == null && all[i] == key) return i * 10;
                if (baseKey != null && PluralRules.BaseKey(all[i]) == baseKey) return i * 10 + Array.IndexOf(PluralRules.Categories, key.Substring(baseKey.Length + 1));
            }
            return int.MaxValue;
        }

        // ---------- Missing ----------

        void DrawMissing()
        {
            var others = LocDatabase.Languages.Where(l => l != Loc.DefaultLanguage).ToList();
            if (LocDatabase.Base == null)
            {
                EditorGUILayout.HelpBox($"{Loc.DefaultLanguage}.txt is missing; it is the source and fallback for every language.", MessageType.Error);
                return;
            }
            if (missingLanguage == null || !others.Contains(missingLanguage)) missingLanguage = others.FirstOrDefault();

            EditorGUILayout.Space(4);
            int total = LocDatabase.Base.Keys.Count;
            foreach (var lang in others)
            {
                var missing = LocDatabase.MissingKeys(lang).Count;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = lang == missingLanguage;
                    if (GUILayout.Toggle(selected, $"{LocDatabase.Get(lang).DisplayName} ({lang})", EditorStyles.radioButton, GUILayout.Width(170)) && !selected)
                        missingLanguage = lang;
                    var r = GUILayoutUtility.GetRect(100, 18, GUILayout.ExpandWidth(true));
                    float done = total == 0 ? 1 : Mathf.Clamp01(1f - (float)missing / Mathf.Max(total, missing));
                    EditorGUI.ProgressBar(r, done, missing == 0 ? "Complete" : $"{missing} missing");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Add language", GUILayout.Width(90));
                newLangCode = EditorGUILayout.TextField(newLangCode, GUILayout.Width(60));
                newLangName = EditorGUILayout.TextField(newLangName);
                bool valid = LocFormat.IsValidKey(newLangCode) && LocDatabase.Get(newLangCode) == null;
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button(new GUIContent("Add", "Code is the file name (de, fr, pt-BR); name is what players see."), GUILayout.Width(50)))
                    {
                        LocDatabase.CreateLanguage(newLangCode, string.IsNullOrEmpty(newLangName) ? newLangCode : newLangName);
                        missingLanguage = newLangCode;
                        newLangCode = newLangName = "";
                        GUI.FocusControl(null);
                    }
            }

            if (missingLanguage == null)
            {
                EditorGUILayout.HelpBox("Only English so far. Add a language above, or drop a <code>.txt file into the languages folder.", MessageType.Info);
                return;
            }

            var keys = LocDatabase.MissingKeys(missingLanguage);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"Missing in {LocDatabase.Get(missingLanguage).DisplayName}: {keys.Count}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(keys.Count == 0))
                    if (GUILayout.Button(new GUIContent("Copy for translation", "Copies the missing lines with English text, ready for a translator or an AI to fill in."), EditorStyles.toolbarButton))
                        EditorGUIUtility.systemCopyBuffer = TranslationRequest(missingLanguage, keys);
                if (GUILayout.Button(new GUIContent("Paste translations", "Reads key=value lines from the clipboard into this language."), EditorStyles.toolbarButton))
                    PasteTranslations(missingLanguage);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var key in keys)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(key, key), GUILayout.Width(Mathf.Clamp(position.width * 0.25f, 120, 260)));
                    var source = LocDatabase.SourceText(key) ?? "";
                    GUILayout.Label(new GUIContent(LocAudit.Short(source, 80), source), EditorStyles.miniLabel, GUILayout.Width(position.width * 0.35f));
                    var edited = EditorGUILayout.DelayedTextField("");
                    if (edited.Length > 0) LocDatabase.Set(missingLanguage, key, LocFormat.Unescape(edited));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static string TranslationRequest(string lang, List<string> keys)
        {
            var sb = new StringBuilder();
            var name = LocDatabase.Get(lang).DisplayName;
            sb.AppendLine($"# Translate the values below from English to {name} ({lang}). Keep the keys, {{0}}-style placeholders and \\n line breaks.");
            var forms = PluralRules.FormsFor(lang);
            if (keys.Any(k => PluralRules.BaseKey(k) != null))
                sb.AppendLine($"# Plural forms for {lang}: {string.Join(", ", forms)}. Each line shows the closest English form.");
            foreach (var k in keys) sb.AppendLine($"{k}={LocFormat.Escape(LocDatabase.SourceText(k))}");
            return sb.ToString();
        }

        static void PasteTranslations(string lang)
        {
            var pasted = LocFormat.Parse(EditorGUIUtility.systemCopyBuffer);
            var doc = LocDatabase.Get(lang);
            int count = 0;
            foreach (var pair in pasted)
            {
                var baseKey = PluralRules.BaseKey(pair.Key);
                if (pair.Value.Length == 0 || !(LocDatabase.IsDefined(pair.Key) || (baseKey != null && LocDatabase.IsDefined(baseKey)))) continue;
                doc.Set(pair.Key, pair.Value);
                count++;
            }
            if (count > 0) LocDatabase.Write(doc);
            EditorUtility.DisplayDialog("Paste translations", count > 0 ? $"Wrote {count} lines to {lang}.txt." : "No key=value lines with known keys on the clipboard.", "OK");
        }

        // ---------- Audit ----------

        void RunAudit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            audit = LocAudit.Run();
            auditTime = DateTime.Now;
        }

        void DrawAudit()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Run audit", EditorStyles.toolbarButton, GUILayout.Width(80))) RunAudit();
                if (audit != null)
                    GUILayout.Label($"{auditTime:HH:mm:ss}  ·  {audit.ScannedScenes} scenes, {audit.ScannedPrefabs} prefabs, {audit.ScannedScripts} scripts  ·  {audit.Total} issues", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }

            if (audit == null)
            {
                EditorGUILayout.HelpBox("Scans scenes, prefabs and scripts for hardcoded text, keys used but not defined, keys never used, and problems in the language files.", MessageType.Info);
                return;
            }
            if (audit.Total == 0)
            {
                EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (Section(0, "Hardcoded text", audit.Hardcoded.Count, "Localize all", "Adds a key (reusing one with the same English text) and a LocText to every scene/prefab object. Code lines must be changed by hand.",
                    () => LocAudit.FixHardcoded(audit.Hardcoded)))
            {
                foreach (var site in audit.Hardcoded.ToList())
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(EditorGUIUtility.IconContent(site.IsCode ? "cs Script Icon" : site.Asset.EndsWith(".unity") ? "SceneAsset Icon" : "Prefab Icon").image, GUILayout.Width(18), GUILayout.Height(18));
                        GUILayout.Label(new GUIContent(site.ToString(), site.IsCode ? site.ObjectPath : site.Asset), GUILayout.Width(position.width * 0.4f));
                        GUILayout.Label(new GUIContent($"\"{LocAudit.Short(site.Text, 50)}\"", site.Text), EditorStyles.miniLabel);
                        if (GUILayout.Button(site.IsCode ? "Open" : "Select", EditorStyles.miniButtonLeft, GUILayout.Width(50))) LocAudit.Reveal(site);
                        if (site.IsCode)
                        {
                            GUILayout.Label(new GUIContent("?", "Replace with Loc.T(\"key\"), or add a // noloc comment to the line to skip it."), EditorStyles.miniButtonRight, GUILayout.Width(70));
                        }
                        else
                        {
                            if (GUILayout.Button(new GUIContent("Localize", "Create a key from this text and add a LocText"), EditorStyles.miniButtonMid, GUILayout.Width(60))) Fix(() => LocAudit.FixHardcoded(new[] { site }));
                            if (GUILayout.Button(new GUIContent("Ignore", "Add LocIgnore: this text stays as is"), EditorStyles.miniButtonRight, GUILayout.Width(50))) Fix(() => LocAudit.Ignore(new[] { site }));
                        }
                    }
            }

            if (Section(1, "Undefined keys", audit.Undefined.Count, "Define all", "Adds each key to English, using the object's current text or a readable form of the key.",
                    () => LocAudit.DefineKeys(audit.Undefined, audit)))
            {
                foreach (var key in audit.Undefined.ToList())
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(key, EditorStyles.boldLabel, GUILayout.Width(position.width * 0.3f));
                        var sites = audit.Usages[key];
                        GUILayout.Label(new GUIContent(sites[0] + (sites.Count > 1 ? $"  +{sites.Count - 1}" : ""), string.Join("\n", sites)), EditorStyles.miniLabel);
                        if (GUILayout.Button(sites[0].IsCode ? "Open" : "Select", EditorStyles.miniButtonLeft, GUILayout.Width(50))) LocAudit.Reveal(sites[0]);
                        if (GUILayout.Button("Define", EditorStyles.miniButtonRight, GUILayout.Width(50))) Fix(() => LocAudit.DefineKeys(new[] { key }, audit));
                    }
            }

            if (Section(2, "Unused keys", audit.Unused.Count, "Remove all", "Removes the keys from every language file. Keys built at runtime count as used when code contains their prefix, like \"item.\".",
                    () =>
                    {
                        if (EditorUtility.DisplayDialog("Remove unused keys", $"Remove {audit.Unused.Count} keys from every language file?\n\n{string.Join("\n", audit.Unused.Take(20))}{(audit.Unused.Count > 20 ? "\n…" : "")}", "Remove", "Cancel"))
                            LocDatabase.RemoveKeys(audit.Unused);
                    }))
            {
                foreach (var key in audit.Unused.ToList())
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(key, GUILayout.Width(position.width * 0.3f));
                        var english = LocDatabase.SourceText(LocDatabase.Base.Has(key) ? key : key + ".other");
                        GUILayout.Label(LocAudit.Short(english, 60), EditorStyles.miniLabel);
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60))) Fix(() => LocDatabase.RemoveKeys(new[] { key }));
                    }
            }

            if (Section(3, "Language file problems", audit.FileProblems.Count, "Fix all", "Removes duplicate lines (keeping the last) and keys English does not have. Malformed lines and placeholder mismatches need a hand edit.",
                    () => LocAudit.FixFileProblems(audit.FileProblems)))
            {
                foreach (var p in audit.FileProblems.ToList())
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"{p.Language}.txt:{p.Line}", GUILayout.Width(80));
                        GUILayout.Label(new GUIContent(p.Message, p.Message), EditorStyles.wordWrappedMiniLabel);
                        if (GUILayout.Button("Open", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(LocDatabase.Get(p.Language).Path), p.Line);
                        using (new EditorGUI.DisabledScope(!p.Fixable))
                            if (GUILayout.Button("Fix", EditorStyles.miniButtonRight, GUILayout.Width(40))) Fix(() => LocAudit.FixFileProblems(new[] { p }));
                    }
            }

            EditorGUILayout.EndScrollView();
        }

        bool Section(int i, string title, int count, string fixLabel, string fixTooltip, Action fixAll)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                auditOpen[i] = EditorGUILayout.Foldout(auditOpen[i], $"{title} ({count})", true);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button(new GUIContent(fixLabel, fixTooltip), EditorStyles.toolbarButton)) Fix(fixAll);
            }
            return auditOpen[i] && count > 0;
        }

        // Fixes change files and assets; rerun afterwards so the list matches reality.
        void Fix(Action action)
        {
            action();
            RunAudit();
            GUIUtility.ExitGUI();
        }
    }
}
