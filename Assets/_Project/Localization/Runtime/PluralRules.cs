using System;

namespace GameJam.Localization
{
    /// <summary>
    /// CLDR plural categories (zero, one, two, few, many, other) for whole numbers.
    /// Keys use them as suffixes: <c>coins.one</c>, <c>coins.few</c>, <c>coins.many</c>, <c>coins.other</c>.
    /// </summary>
    public static class PluralRules
    {
        public static readonly string[] Categories = { "zero", "one", "two", "few", "many", "other" };

        /// <summary>Categories a translator should fill for a language, in display order.</summary>
        public static string[] FormsFor(string language)
        {
            switch (BaseCode(language))
            {
                case "ru": case "uk": case "be": case "pl": return new[] { "one", "few", "many" };
                case "cs": case "sk": case "sr": case "hr": case "bs": case "ro": return new[] { "one", "few", "other" };
                case "ar": return new[] { "zero", "one", "two", "few", "many", "other" };
                case "he": return new[] { "one", "two", "other" };
                case "ja": case "zh": case "ko": case "th": case "vi": case "id": case "ms": return new[] { "other" };
                default: return new[] { "one", "other" };
            }
        }

        public static string Category(string language, long n)
        {
            n = Math.Abs(n);
            long m10 = n % 10, m100 = n % 100;
            switch (BaseCode(language))
            {
                case "ru": case "uk": case "be":
                    if (m10 == 1 && m100 != 11) return "one";
                    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "few";
                    return "many";
                case "pl":
                    if (n == 1) return "one";
                    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "few";
                    return "many";
                case "sr": case "hr": case "bs":
                    if (m10 == 1 && m100 != 11) return "one";
                    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "few";
                    return "other";
                case "cs": case "sk":
                    if (n == 1) return "one";
                    if (n >= 2 && n <= 4) return "few";
                    return "other";
                case "ro":
                    if (n == 1) return "one";
                    if (n == 0 || (m100 >= 2 && m100 <= 19)) return "few";
                    return "other";
                case "fr": case "pt":
                    return n <= 1 ? "one" : "other";
                case "ar":
                    if (n == 0) return "zero";
                    if (n == 1) return "one";
                    if (n == 2) return "two";
                    if (m100 >= 3 && m100 <= 10) return "few";
                    if (m100 >= 11) return "many";
                    return "other";
                case "he":
                    if (n == 1) return "one";
                    if (n == 2) return "two";
                    return "other";
                case "ja": case "zh": case "ko": case "th": case "vi": case "id": case "ms":
                    return "other";
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        /// <summary>"coins.few" → "coins"; null when the key has no plural suffix.</summary>
        public static string BaseKey(string key)
        {
            int dot = key.LastIndexOf('.');
            if (dot <= 0) return null;
            return Array.IndexOf(Categories, key.Substring(dot + 1)) >= 0 ? key.Substring(0, dot) : null;
        }

        static string BaseCode(string language)
        {
            if (string.IsNullOrEmpty(language)) return "";
            int dash = language.IndexOfAny(new[] { '-', '_' });
            return (dash > 0 ? language.Substring(0, dash) : language).ToLowerInvariant();
        }
    }
}
