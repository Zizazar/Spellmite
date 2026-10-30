using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.Localization
{
    /// <summary>Where a key is used, or where hardcoded text was found. Scene/prefab objects are found again by sibling-index path.</summary>
    public class LocSite
    {
        public string Asset;       // scene, prefab or script path
        public string ObjectPath;  // hierarchy path for display
        public int[] IndexPath;    // sibling indices from the scene roots / prefab root
        public int Line;           // script line, 0 for objects
        public string Text;
        public string Key;

        public bool IsCode => Line > 0;
        public override string ToString() => IsCode ? $"{Asset}:{Line}" : $"{Path.GetFileName(Asset)} › {ObjectPath}";
    }

    public class LocFileProblem
    {
        public string Language;
        public string Key;
        public int Line;
        public string Message;
        public bool Fixable;
    }

    public class LocAuditResult
    {
        public readonly List<LocSite> Hardcoded = new List<LocSite>();
        public readonly Dictionary<string, List<LocSite>> Usages = new Dictionary<string, List<LocSite>>();
        public readonly List<string> Undefined = new List<string>();
        public readonly List<string> Unused = new List<string>();
        public readonly List<LocFileProblem> FileProblems = new List<LocFileProblem>();
        public readonly HashSet<string> CodeLiterals = new HashSet<string>();
        public int ScannedScenes, ScannedPrefabs, ScannedScripts;

        public int Total => Hardcoded.Count + Undefined.Count + Unused.Count + FileProblems.Count;
    }

    /// <summary>
    /// Project-wide localization check: hardcoded text (scenes, prefabs, <c>.text = "..."</c> in code),
    /// keys used but not defined in English, keys defined but never used, and problems inside the language files.
    /// Agents can run <c>GameJam.Localization.LocAudit.Report()</c> through <c>unity command eval</c>.
    /// </summary>
    public static class LocAudit
    {
        /// <summary>Folders the audit never looks in: third-party and sample content.</summary>
        public static readonly string[] Excluded = { "Assets/TextMesh Pro/", "Assets/TutorialInfo/", "Assets/Plugins/", "Assets/_Project/Localization/" };

        static readonly Regex UsageRx = new Regex(@"\b(?:Loc\.(?:T|P|Has)|SetKey)\s*\(\s*""((?:[^""\\]|\\.)*)""");
        static readonly Regex LiteralRx = new Regex(@"""((?:[^""\\\r\n]|\\.)*)""");
        static readonly Regex InterpolatedPrefixRx = new Regex(@"\$""([A-Za-z0-9_.\-]+\.)\{");
        static readonly Regex HardcodedRx = new Regex(@"(?:\.text\s*=|\.SetText\s*\()\s*\$?@?""((?:[^""\\]|\\.)*)""");
        static readonly Regex PlaceholderRx = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
        static readonly Regex LetterRx = new Regex(@"\p{L}");

        static bool IsExcluded(string path) => Excluded.Any(e => path.StartsWith(e, StringComparison.OrdinalIgnoreCase));

        public static LocAuditResult Run()
        {
            var r = new LocAuditResult();
            LocDatabase.Reload();
            try
            {
                ScanScenes(r);
                ScanPrefabs(r);
            }
            finally { EditorUtility.ClearProgressBar(); }
            ScanCode(r);
            CheckKeys(r);
            CheckFiles(r);
            return r;
        }

        /// <summary>Runs the audit and returns a plain-text report (also logged).</summary>
        public static string Report()
        {
            var r = Run();
            var sb = new StringBuilder();
            sb.AppendLine($"Localization audit: {r.ScannedScenes} scenes, {r.ScannedPrefabs} prefabs, {r.ScannedScripts} scripts.");
            sb.AppendLine($"Hardcoded text ({r.Hardcoded.Count}):");
            foreach (var h in r.Hardcoded) sb.AppendLine($"  {h} \"{Short(h.Text)}\"");
            sb.AppendLine($"Undefined keys ({r.Undefined.Count}):");
            foreach (var k in r.Undefined) sb.AppendLine($"  {k}  used at {string.Join(", ", r.Usages[k].Select(u => u.ToString()))}");
            sb.AppendLine($"Unused keys ({r.Unused.Count}):");
            foreach (var k in r.Unused) sb.AppendLine($"  {k}");
            sb.AppendLine($"File problems ({r.FileProblems.Count}):");
            foreach (var p in r.FileProblems) sb.AppendLine($"  {p.Language}.txt:{p.Line} {p.Message}");
            foreach (var lang in LocDatabase.Languages.Where(l => l != Loc.DefaultLanguage))
                sb.AppendLine($"Missing in {lang}: {LocDatabase.MissingKeys(lang).Count}");
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        public static string Short(string s, int max = 60)
        {
            s = (s ?? "").Replace("\n", "\\n");
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        // ---------- scanning ----------

        static void ScanScenes(LocAuditResult r)
        {
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !IsExcluded(p)).ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                EditorUtility.DisplayProgressBar("Localization audit", path, (float)i / paths.Count);
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = false;
                if (!scene.isLoaded)
                {
                    var text = File.ReadAllText(path);
                    if (!text.Contains("m_text:") && !text.Contains("m_Text:")) continue; // no text components at all
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened = true;
                }
                r.ScannedScenes++;
                var roots = scene.GetRootGameObjects();
                for (int j = 0; j < roots.Length; j++) ScanObject(roots[j], path, new List<int> { j }, roots[j].name, r);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ScanPrefabs(LocAuditResult r)
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !IsExcluded(p)).ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Localization audit", paths[i], (float)i / paths.Count);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (root == null || root.GetComponentsInChildren<Transform>(true).All(t => LocText.FindTextComponent(t.gameObject) == null)) continue;
                r.ScannedPrefabs++;
                ScanObject(root, paths[i], new List<int>(), root.name, r);
            }
        }

        static void ScanObject(GameObject go, string asset, List<int> indexPath, string objectPath, LocAuditResult r)
        {
            var loc = go.GetComponent<LocText>();
            var target = LocText.FindTextComponent(go);
            if (loc != null)
            {
                if (!string.IsNullOrEmpty(loc.Key))
                    AddUsage(r, loc.Key, new LocSite { Asset = asset, ObjectPath = objectPath, IndexPath = indexPath.ToArray(), Key = loc.Key, Text = target != null ? LocText.GetText(target) : null });
            }
            else if (target != null && go.GetComponent<LocIgnore>() == null && IsHardcoded(target))
            {
                r.Hardcoded.Add(new LocSite { Asset = asset, ObjectPath = objectPath, IndexPath = indexPath.ToArray(), Text = LocText.GetText(target) });
            }

            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i).gameObject;
                indexPath.Add(i);
                ScanObject(child, asset, indexPath, objectPath + "/" + child.name, r);
                indexPath.RemoveAt(indexPath.Count - 1);
            }
        }

        static bool IsHardcoded(Component target)
        {
            var text = LocText.GetText(target);
            if (string.IsNullOrWhiteSpace(text) || !LetterRx.IsMatch(text)) return false; // numbers, symbols, empty
            // Text inherited unchanged from a nested prefab is reported on that prefab instead.
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(target) as Component;
                if (source != null && LocText.GetText(source) == text) return false;
            }
            // Filled at runtime: dropdown captions/items and what the player types into input fields.
            if (target.GetComponentInParent<TMP_Dropdown>(true) != null || target.GetComponentInParent<Dropdown>(true) != null) return false;
            var tmpInput = target.GetComponentInParent<TMP_InputField>(true);
            if (tmpInput != null && tmpInput.textComponent == target) return false;
            var input = target.GetComponentInParent<InputField>(true);
            if (input != null && input.textComponent == target) return false;
            return true;
        }

        static void ScanCode(LocAuditResult r)
        {
            foreach (var file in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                if (IsExcluded(path)) continue;
                r.ScannedScripts++;
                bool editorCode = path.Contains("/Editor/");
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;
                    if (line.IndexOf('"') < 0) continue;

                    foreach (Match m in UsageRx.Matches(line))
                        AddUsage(r, m.Groups[1].Value, new LocSite { Asset = path, Line = i + 1, Key = m.Groups[1].Value, Text = trimmed });
                    foreach (Match m in LiteralRx.Matches(line)) r.CodeLiterals.Add(m.Groups[1].Value);
                    foreach (Match m in InterpolatedPrefixRx.Matches(line)) r.CodeLiterals.Add(m.Groups[1].Value);

                    if (editorCode || line.Contains("noloc")) continue;
                    foreach (Match m in HardcodedRx.Matches(line))
                        if (LetterRx.IsMatch(m.Groups[1].Value))
                            r.Hardcoded.Add(new LocSite { Asset = path, Line = i + 1, Text = m.Groups[1].Value, ObjectPath = trimmed });
                }
            }
        }

        static void AddUsage(LocAuditResult r, string key, LocSite site)
        {
            if (!r.Usages.TryGetValue(key, out var list)) r.Usages[key] = list = new List<LocSite>();
            list.Add(site);
        }

        static void CheckKeys(LocAuditResult r)
        {
            var b = LocDatabase.Base;
            // Loc.T("item." + id) is a prefix: defined when some key starts with it.
            r.Undefined.AddRange(r.Usages.Keys
                .Where(k => k.EndsWith(".") ? b == null || !b.Keys.Any(x => x.StartsWith(k, StringComparison.Ordinal)) : !LocDatabase.IsDefined(k))
                .OrderBy(k => k, StringComparer.Ordinal));

            if (b == null) return;
            // Literals ending in '.' ("item.") are prefixes of keys built at runtime: Loc.T("item." + id).
            var prefixes = r.CodeLiterals.Where(l => l.Length > 1 && l.EndsWith(".") && LocFormat.IsValidKey(l)).ToList();
            foreach (var key in b.Keys)
            {
                var name = PluralRules.BaseKey(key) ?? key;
                bool used = r.Usages.ContainsKey(name) || r.Usages.ContainsKey(key) || r.CodeLiterals.Contains(name) || r.CodeLiterals.Contains(key)
                            || prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
                if (!used && !r.Unused.Contains(name)) r.Unused.Add(name);
            }
        }

        static void CheckFiles(LocAuditResult r)
        {
            var b = LocDatabase.Base;
            if (b == null)
            {
                r.FileProblems.Add(new LocFileProblem { Language = Loc.DefaultLanguage, Message = $"{Loc.DefaultLanguage}.txt is missing: it is the fallback for every other language." });
                return;
            }
            foreach (var lang in LocDatabase.Languages)
            {
                var doc = LocDatabase.Get(lang);
                foreach (var dup in doc.Duplicates)
                    r.FileProblems.Add(new LocFileProblem { Language = lang, Key = dup, Line = doc.Lines.First(l => l.Key == dup).Number, Message = $"'{dup}' is defined more than once (the last one wins).", Fixable = true });
                foreach (var bad in doc.MalformedLines)
                    r.FileProblems.Add(new LocFileProblem { Language = lang, Line = bad.Number, Message = $"Not a key=value line: \"{Short(bad.Raw)}\"" });
                if (doc == b) continue;

                foreach (var key in doc.Keys)
                {
                    var baseKey = PluralRules.BaseKey(key);
                    if (!b.Has(key) && !(baseKey != null && LocDatabase.IsDefined(baseKey)))
                    {
                        r.FileProblems.Add(new LocFileProblem { Language = lang, Key = key, Line = LineOf(doc, key), Message = $"'{key}' is not in {Loc.DefaultLanguage}.txt.", Fixable = true });
                        continue;
                    }
                    var value = doc.Get(key);
                    var source = LocDatabase.SourceText(key);
                    if (string.IsNullOrEmpty(value) || source == null) continue;
                    var expected = Placeholders(source);
                    var actual = Placeholders(value);
                    if (!expected.SetEquals(actual))
                        r.FileProblems.Add(new LocFileProblem { Language = lang, Key = key, Line = LineOf(doc, key), Message = $"'{key}' has placeholders {{{string.Join("},{", actual)}}} but English has {{{string.Join("},{", expected)}}}." });
                }
            }
        }

        static int LineOf(LocDocument doc, string key) => doc.Lines.LastOrDefault(l => l.Key == key)?.Number ?? 0;

        static SortedSet<int> Placeholders(string s) => new SortedSet<int>(PlaceholderRx.Matches(s).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)));

        // ---------- fixes ----------

        /// <summary>Adds a key for each hardcoded scene/prefab text (reusing a key with the same English text) and a LocText pointing at it.
        /// Code sites are skipped. Returns how many objects were fixed.</summary>
        public static int FixHardcoded(IEnumerable<LocSite> sites)
        {
            var b = LocDatabase.Base ?? LocDatabase.CreateLanguage(Loc.DefaultLanguage, "English");
            int fixedCount = ModifyObjects(sites, (go, asset, undo) => Localize(go, b, Path.GetFileNameWithoutExtension(asset), undo));
            LocDatabase.Write(b);
            return fixedCount;
        }

        /// <summary>Adds <see cref="LocIgnore"/> to the objects so the audit stops reporting them.</summary>
        public static int Ignore(IEnumerable<LocSite> sites) => ModifyObjects(sites, (go, asset, undo) =>
        {
            if (go.GetComponent<LocIgnore>() != null) return false;
            if (undo) Undo.AddComponent<LocIgnore>(go);
            else go.AddComponent<LocIgnore>();
            return true;
        });

        // Runs the edit on each site's object: loaded scenes are edited with undo and left for the user to save,
        // closed scenes are opened additively and saved, prefabs are edited through LoadPrefabContents.
        static int ModifyObjects(IEnumerable<LocSite> sites, Func<GameObject, string, bool, bool> edit)
        {
            int fixedCount = 0;
            foreach (var group in sites.Where(s => !s.IsCode).GroupBy(s => s.Asset))
            {
                var asset = group.Key;
                if (asset.EndsWith(".unity"))
                {
                    var scene = SceneManager.GetSceneByPath(asset);
                    bool opened = !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(asset, OpenSceneMode.Additive);
                    var roots = scene.GetRootGameObjects();
                    foreach (var site in group)
                    {
                        var go = Resolve(roots[site.IndexPath[0]], site.IndexPath, 1);
                        if (go != null && edit(go, asset, !opened)) fixedCount++;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (opened)
                    {
                        EditorSceneManager.SaveScene(scene);
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
                else
                {
                    var root = PrefabUtility.LoadPrefabContents(asset);
                    try
                    {
                        foreach (var site in group)
                        {
                            var go = Resolve(root, site.IndexPath, 0);
                            if (go != null && edit(go, asset, false)) fixedCount++;
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, asset);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            return fixedCount;
        }

        static GameObject Resolve(GameObject root, int[] path, int start)
        {
            var t = root.transform;
            for (int i = start; i < path.Length; i++)
            {
                if (path[i] >= t.childCount) return null;
                t = t.GetChild(path[i]);
            }
            return t.gameObject;
        }

        static bool Localize(GameObject go, LocDocument b, string context, bool undo)
        {
            var target = LocText.FindTextComponent(go);
            if (target == null || go.GetComponent<LocText>() != null) return false;
            var text = LocText.GetText(target);
            var key = LocDatabase.FindKeyByValue(text);
            if (key == null)
            {
                key = LocDatabase.UniqueKey(SuggestKey(go, context, text));
                b.Set(key, text);
            }
            var loc = undo ? Undo.AddComponent<LocText>(go) : go.AddComponent<LocText>();
            var so = new SerializedObject(loc);
            so.FindProperty("key").stringValue = key;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static readonly HashSet<string> GenericNames = new HashSet<string> { "text", "tmp", "text_tmp", "label", "title", "caption", "new_text", "textmeshpro", "txt" };

        /// <summary>"MainMenu" + "PlayButton/Text (TMP)" → "mainmenu.playbutton".</summary>
        public static string SuggestKey(GameObject go, string context, string text)
        {
            var ctx = Slug(context);
            var name = Slug(go.name);
            if (GenericNames.Contains(name) && go.transform.parent != null) name = Slug(go.transform.parent.name);
            if (string.IsNullOrEmpty(name) || GenericNames.Contains(name) || name == ctx)
                name = Slug(string.Join(" ", (text ?? "").Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(3)));
            if (string.IsNullOrEmpty(name)) name = "text";
            return string.IsNullOrEmpty(ctx) ? name : ctx + "." + name;
        }

        public static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                if (sb.Length >= 32) break;
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>Adds undefined keys to English: the text currently on a LocText's component, else a readable form of the key.</summary>
        public static void DefineKeys(IEnumerable<string> keys, LocAuditResult r)
        {
            var b = LocDatabase.Base ?? LocDatabase.CreateLanguage(Loc.DefaultLanguage, "English");
            foreach (var key in keys)
            {
                if (LocDatabase.IsDefined(key)) continue;
                var text = r != null && r.Usages.TryGetValue(key, out var sites) ? sites.Select(s => s.IsCode ? null : s.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) : null;
                b.Set(key, text ?? Humanize(key));
            }
            LocDatabase.Write(b);
        }

        static string Humanize(string key)
        {
            var last = key.Substring(key.LastIndexOf('.') + 1).Replace('_', ' ').Replace('-', ' ');
            return last.Length > 0 ? char.ToUpperInvariant(last[0]) + last.Substring(1) : key;
        }

        public static void FixFileProblems(IEnumerable<LocFileProblem> problems)
        {
            foreach (var group in problems.Where(p => p.Fixable).GroupBy(p => p.Language))
            {
                var doc = LocDatabase.Get(group.Key);
                if (doc == null) continue;
                doc.RemoveDuplicates();
                foreach (var p in group)
                    if (p.Key != null && !LocDatabase.Base.Has(p.Key) && !(PluralRules.BaseKey(p.Key) is string bk && LocDatabase.IsDefined(bk)))
                        doc.Remove(p.Key); // extra key
                doc.Save();
            }
            LocDatabase.Reload();
        }

        /// <summary>Selects the object (opening its scene or prefab if needed) or opens the script at the line.</summary>
        public static void Reveal(LocSite site)
        {
            if (site.IsCode)
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(site.Asset), site.Line);
                return;
            }
            if (site.Asset.EndsWith(".unity"))
            {
                var scene = SceneManager.GetSceneByPath(site.Asset);
                if (!scene.isLoaded)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    scene = EditorSceneManager.OpenScene(site.Asset, OpenSceneMode.Single);
                }
                var go = Resolve(scene.GetRootGameObjects()[site.IndexPath[0]], site.IndexPath, 1);
                if (go != null) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
            }
            else
            {
                var stage = PrefabStageUtility.OpenPrefab(site.Asset);
                var go = stage != null ? Resolve(stage.prefabContentsRoot, site.IndexPath, 0) : null;
                if (go != null) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
            }
        }
    }
}
