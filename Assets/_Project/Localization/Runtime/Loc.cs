using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>
    /// Runtime localization. Language files are <c>Resources/Languages/&lt;code&gt;.txt</c> (en.txt, ru.txt, pt-BR.txt),
    /// loaded as TextAssets so they work in every build, WebGL included. Lookups fall back to English, then to the key itself.
    /// <code>
    /// label.text = Loc.T("hud.score", score);   // hud.score=Score: {0}
    /// label.text = Loc.T("coins", count);       // coins.one / coins.few / coins.many / coins.other
    /// </code>
    /// </summary>
    public static class Loc
    {
        public const string DefaultLanguage = "en";
        public const string ResourcesFolder = "Languages";
        /// <summary>Where the editor reads and writes language files. Change it if you move the folder.</summary>
        public const string EditorFolder = "Assets/_Project/Localization/Resources/" + ResourcesFolder;
        const string PrefsKey = "loc.language";

        /// <summary>Raised after the language changes or the files are reloaded.</summary>
        public static event Action Changed;

        static readonly Dictionary<string, Dictionary<string, string>> tables = new Dictionary<string, Dictionary<string, string>>();
        static readonly Dictionary<string, string> names = new Dictionary<string, string>();
        static readonly List<string> languages = new List<string>();
        static readonly HashSet<string> reportedMissing = new HashSet<string>();
        static Dictionary<string, string> current = new Dictionary<string, string>();
        static Dictionary<string, string> fallback = new Dictionary<string, string>();
        static string language = DefaultLanguage;
        static bool loaded;

        /// <summary>Language codes found, English first.</summary>
        public static IReadOnlyList<string> Languages { get { EnsureLoaded(); return languages; } }

        /// <summary>Current language code. Setting it saves the choice to PlayerPrefs.</summary>
        public static string Language
        {
            get { EnsureLoaded(); return language; }
            set { EnsureLoaded(); SetLanguage(value, true); }
        }

        /// <summary>Translated text for <paramref name="key"/>, formatted with <paramref name="args"/> ({0}, {1}...).
        /// When only plural forms exist (key.one, key.few...), the first argument picks the form.</summary>
        public static string T(string key, params object[] args)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(key)) return "";
            if (TryGet(key, out var s)) return Format(key, s, args);
            if (args != null && args.Length > 0 && TryCount(args[0], out long n) && TryPlural(key, n, out s)) return Format(key, s, args);
            ReportMissing(key);
            return key;
        }

        /// <summary>Plural lookup: <paramref name="count"/> picks the form and is {0}; <paramref name="args"/> follow as {1}, {2}...</summary>
        public static string P(string key, long count, params object[] args)
        {
            EnsureLoaded();
            var all = new object[(args?.Length ?? 0) + 1];
            all[0] = count;
            args?.CopyTo(all, 1);
            if (TryPlural(key, count, out var s) || TryGet(key, out s)) return Format(key, s, all);
            ReportMissing(key);
            return key;
        }

        /// <summary>True when the key (or one of its plural forms) exists in the current language or in English.</summary>
        public static bool Has(string key)
        {
            EnsureLoaded();
            return TryGet(key, out _) || TryPlural(key, 1, out _);
        }

        /// <summary>The language's <c>@name=</c> line, or the code when there is none.</summary>
        public static string DisplayName(string code)
        {
            EnsureLoaded();
            return names.TryGetValue(code, out var n) && !string.IsNullOrEmpty(n) ? n : code;
        }

        /// <summary>Re-reads every language file and raises <see cref="Changed"/>. The editor calls it when a file changes.</summary>
        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            tables.Clear();
            names.Clear();
            languages.Clear();
            reportedMissing.Clear();

            foreach (var (code, text) in LoadSources())
            {
                var meta = new Dictionary<string, string>();
                tables[code] = LocFormat.Parse(text, meta);
                meta.TryGetValue(LocFormat.NameMeta, out var name);
                names[code] = name;
                languages.Add(code);
            }
            languages.Sort((a, b) => a == DefaultLanguage ? -1 : b == DefaultLanguage ? 1 : string.CompareOrdinal(a, b));
            if (languages.Count == 0) Debug.LogWarning($"[Loc] No language files in Resources/{ResourcesFolder}.");

            fallback = tables.TryGetValue(DefaultLanguage, out var en) ? en : new Dictionary<string, string>();
            var saved = PlayerPrefs.GetString(PrefsKey, "");
            SetLanguage(tables.ContainsKey(saved) ? saved : FromSystemLanguage(), false);
        }

        static IEnumerable<(string code, string text)> LoadSources()
        {
#if UNITY_EDITOR
            // Read straight from disk so edits show up in Play Mode before Unity re-imports the files.
            if (System.IO.Directory.Exists(EditorFolder))
            {
                foreach (var path in System.IO.Directory.GetFiles(EditorFolder, "*.txt"))
                    yield return (System.IO.Path.GetFileNameWithoutExtension(path), System.IO.File.ReadAllText(path));
                yield break;
            }
#endif
            foreach (var asset in Resources.LoadAll<TextAsset>(ResourcesFolder))
                yield return (asset.name, asset.text);
        }

        static void SetLanguage(string code, bool save)
        {
            if (string.IsNullOrEmpty(code) || !tables.ContainsKey(code))
            {
                var baseCode = code?.Split('-', '_')[0];
                code = baseCode != null && tables.ContainsKey(baseCode) ? baseCode
                    : tables.ContainsKey(DefaultLanguage) ? DefaultLanguage
                    : languages.Count > 0 ? languages[0] : DefaultLanguage;
            }
            bool changed = code != language;
            language = code;
            current = tables.TryGetValue(code, out var t) ? t : fallback;
            if (save)
            {
                PlayerPrefs.SetString(PrefsKey, code);
                PlayerPrefs.Save();
            }
            if (changed && save) Changed?.Invoke();
        }

        static bool TryGet(string key, out string value)
        {
            if (current.TryGetValue(key, out value) && value.Length > 0) return true;
            return fallback.TryGetValue(key, out value) && value.Length > 0;
        }

        static bool TryPlural(string key, long n, out string value)
        {
            return TryPluralIn(current, language, key, n, out value)
                || TryPluralIn(fallback, DefaultLanguage, key, n, out value);
        }

        static bool TryPluralIn(Dictionary<string, string> table, string lang, string key, long n, out string value)
        {
            if (n == 0 && table.TryGetValue(key + ".zero", out value) && value.Length > 0) return true; // optional "No coins" form
            var category = PluralRules.Category(lang, n);
            foreach (var form in new[] { category, "other", "many", "one" })
                if (table.TryGetValue(key + "." + form, out value) && value.Length > 0) return true;
            value = null;
            return false;
        }

        static bool TryCount(object arg, out long n)
        {
            switch (arg)
            {
                case int i: n = i; return true;
                case long l: n = l; return true;
                case short s: n = s; return true;
                case byte b: n = b; return true;
                case uint u: n = u; return true;
                case float f: n = (long)Math.Floor(f); return true;
                case double d: n = (long)Math.Floor(d); return true;
                case decimal m: n = (long)Math.Floor(m); return true;
                default: n = 0; return false;
            }
        }

        static string Format(string key, string text, object[] args)
        {
            if (args == null || args.Length == 0) return text;
            try { return string.Format(CultureInfo.InvariantCulture, text, args); }
            catch (FormatException)
            {
                Debug.LogWarning($"[Loc] Bad placeholders in '{key}' ({language}): {text}");
                return text;
            }
        }

        static void ReportMissing(string key)
        {
            if (reportedMissing.Add(key)) Debug.LogWarning($"[Loc] Missing key '{key}'. Add it to {DefaultLanguage}.txt.");
        }

        static string FromSystemLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Belarusian: return "be";
                case SystemLanguage.German: return "de";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Slovak: return "sk";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Norwegian: return "no";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.SerboCroatian: return "sr";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: return "zh";
                case SystemLanguage.ChineseTraditional: return "zh-TW";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.Indonesian: return "id";
                default: return DefaultLanguage;
            }
        }

        // Keeps state correct when domain reload on entering Play Mode is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => loaded = false;
    }
}
