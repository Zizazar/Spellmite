using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GameJam.Localization
{
    /// <summary>
    /// Editor view of all language files. Polls the folder twice a second (also while Unity is in the background),
    /// so edits made by hand or by an AI agent reload everywhere, including a running Play Mode session.
    /// </summary>
    [InitializeOnLoad]
    public static class LocDatabase
    {
        public static event Action Changed;

        static readonly Dictionary<string, LocDocument> docs = new Dictionary<string, LocDocument>();
        static readonly Dictionary<string, DateTime> stamps = new Dictionary<string, DateTime>();
        static List<string> languages = new List<string>();
        static List<string> keys = new List<string>();
        static List<string> selectableKeys = new List<string>();
        static double nextPoll;

        static readonly HashSet<string> pendingImport = new HashSet<string>();

        static LocDatabase()
        {
            Load(false);
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) ImportPending(); };
        }

        public static string Folder => Loc.EditorFolder;
        public static IReadOnlyList<string> Languages => languages;
        public static LocDocument Base => Get(Loc.DefaultLanguage);
        public static LocDocument Get(string language) => language != null && docs.TryGetValue(language, out var d) ? d : null;

        /// <summary>Every key: English order first, then keys that only exist in other languages.</summary>
        public static IReadOnlyList<string> Keys => keys;

        /// <summary>Keys as code and LocText use them: plural forms (coins.one, coins.few) collapsed to "coins".</summary>
        public static IReadOnlyList<string> SelectableKeys => selectableKeys;

        public static string Value(string language, string key) => Get(language)?.Get(key);

        /// <summary>Defined in English, directly or as plural forms.</summary>
        public static bool IsDefined(string key)
        {
            var b = Base;
            return b != null && !string.IsNullOrEmpty(key) && (b.Has(key) || PluralRules.Categories.Any(c => b.Has(key + "." + c)));
        }

        /// <summary>Plural form keys that exist for <paramref name="key"/> in any language.</summary>
        public static List<string> PluralKeys(string key) =>
            PluralRules.Categories.Select(c => key + "." + c).Where(k => docs.Values.Any(d => d.Has(k))).ToList();

        /// <summary>Keys <paramref name="language"/> still needs, plural-aware: when English has coins.one/coins.other,
        /// Russian is expected to have coins.one/coins.few/coins.many.</summary>
        public static List<string> MissingKeys(string language)
        {
            var result = new List<string>();
            var doc = Get(language);
            var b = Base;
            if (doc == null || b == null || doc == b) return result;
            var pluralDone = new HashSet<string>();
            foreach (var k in b.Keys)
            {
                var baseKey = PluralRules.BaseKey(k);
                if (baseKey == null)
                {
                    if (string.IsNullOrEmpty(doc.Get(k))) result.Add(k);
                }
                else if (pluralDone.Add(baseKey))
                {
                    foreach (var form in PluralRules.FormsFor(language))
                        if (string.IsNullOrEmpty(doc.Get(baseKey + "." + form))) result.Add(baseKey + "." + form);
                }
            }
            return result;
        }

        /// <summary>English text to translate for a key; for a plural form English lacks (coins.few), the closest English form.</summary>
        public static string SourceText(string key)
        {
            var b = Base;
            if (b == null) return null;
            var v = b.Get(key);
            if (!string.IsNullOrEmpty(v)) return v;
            var baseKey = PluralRules.BaseKey(key);
            if (baseKey == null) return v;
            return b.Get(baseKey + ".other") ?? b.Get(baseKey + ".one");
        }

        /// <summary>Key with this exact English text, if any.</summary>
        public static string FindKeyByValue(string value)
        {
            var b = Base;
            if (b == null || string.IsNullOrEmpty(value)) return null;
            return b.Keys.FirstOrDefault(k => b.Get(k) == value);
        }

        public static void Set(string language, string key, string value)
        {
            var doc = Get(language);
            if (doc == null) return;
            doc.Set(key, value, AnchorFor(doc, key));
            Write(doc);
        }

        /// <summary>Adds an English key, making a unique name if it is taken. Returns the key used.</summary>
        public static string AddKey(string key, string englishValue)
        {
            key = UniqueKey(key);
            if (Base == null) CreateLanguage(Loc.DefaultLanguage, "English");
            Base.Set(key, englishValue);
            Write(Base);
            return key;
        }

        public static string UniqueKey(string key)
        {
            if (!IsDefined(key)) return key;
            for (int i = 2; ; i++)
                if (!IsDefined(key + "_" + i)) return key + "_" + i;
        }

        /// <summary>Removes keys (and their plural forms) from every language file.</summary>
        public static int RemoveKeys(IEnumerable<string> toRemove)
        {
            int removed = 0;
            var list = toRemove.ToList();
            foreach (var doc in docs.Values)
            {
                bool changed = false;
                foreach (var k in list)
                {
                    changed |= doc.Remove(k);
                    foreach (var c in PluralRules.Categories) changed |= doc.Remove(k + "." + c);
                }
                if (changed) { doc.Save(); removed++; }
            }
            AfterWrite();
            return removed;
        }

        public static void Write(params LocDocument[] written)
        {
            foreach (var doc in written) doc.Save();
            AfterWrite();
        }

        public static LocDocument CreateLanguage(string code, string displayName)
        {
            Directory.CreateDirectory(Folder);
            var path = Folder + "/" + code + ".txt";
            if (File.Exists(path)) return Get(code);
            var doc = LocDocument.Create(path, displayName);
            doc.Save();
            AfterWrite();
            return Get(code);
        }

        public static void Reload()
        {
            Load(true);
            ImportPending();
            Loc.Reload();
            Changed?.Invoke();
        }

        // Reload from disk rather than trusting the in-memory edits.
        static void AfterWrite() => Reload();

        // Changed files are imported so the Resources TextAssets (what builds use) match the disk; deferred during Play Mode.
        static void ImportPending()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || pendingImport.Count == 0) return;
            foreach (var path in pendingImport) AssetDatabase.ImportAsset(path);
            pendingImport.Clear();
        }

        static void Load(bool trackChanges)
        {
            var old = new Dictionary<string, DateTime>(stamps);
            docs.Clear();
            stamps.Clear();
            if (Directory.Exists(Folder))
                foreach (var path in Directory.GetFiles(Folder, "*.txt"))
                {
                    var doc = LocDocument.Load(path);
                    docs[doc.Code] = doc;
                    stamps[doc.Path] = File.GetLastWriteTimeUtc(path);
                    if (trackChanges && (!old.TryGetValue(doc.Path, out var t) || t != stamps[doc.Path])) pendingImport.Add(doc.Path);
                }

            languages = docs.Keys.OrderBy(c => c == Loc.DefaultLanguage ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal).ToList();
            var seen = new HashSet<string>();
            keys = new List<string>();
            foreach (var code in languages)
                foreach (var k in docs[code].Keys)
                    if (seen.Add(k)) keys.Add(k);
            selectableKeys = keys.Select(k => PluralRules.BaseKey(k) ?? k).Distinct().ToList();
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;

            var files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.txt") : new string[0];
            bool changed = files.Length != stamps.Count;
            if (!changed)
                foreach (var f in files)
                    if (!stamps.TryGetValue(f.Replace('\\', '/'), out var t) || t != File.GetLastWriteTimeUtc(f)) { changed = true; break; }
            if (!changed) return;

            Reload();
            if (EditorApplication.isPlaying) UnityEngine.Debug.Log("[Loc] Language files changed, reloaded.");
        }

        // A new key in a translation goes after the key that precedes it in English, so files stay in the same order.
        static string AnchorFor(LocDocument doc, string key)
        {
            var b = Base;
            if (b == null || doc == b) return null;
            var order = b.Keys;
            int i = -1;
            for (int j = 0; j < order.Count; j++) if (order[j] == key) { i = j; break; }
            if (i < 0)
            {
                var baseKey = PluralRules.BaseKey(key); // coins.few in ru anchors after coins.one
                if (baseKey != null)
                    return doc.Keys.LastOrDefault(k => PluralRules.BaseKey(k) == baseKey) ?? order.LastOrDefault(k => k.StartsWith(baseKey + ".", StringComparison.Ordinal) && doc.Has(k));
                return null;
            }
            for (int j = i - 1; j >= 0; j--) if (doc.Has(order[j])) return order[j];
            return null;
        }
    }
}
