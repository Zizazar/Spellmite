using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameJam.Localization
{
    /// <summary>
    /// The language file format: one <c>key=value</c> per line, <c>#</c> comments, <c>@name=...</c> meta lines,
    /// and <c>\n</c>, <c>\t</c>, <c>\\</c> escapes in values. Keys and values are trimmed.
    /// </summary>
    public static class LocFormat
    {
        public const string NameMeta = "@name";

        /// <summary>Parses one line. False for blank, comment and malformed lines; <paramref name="malformed"/> tells the last two apart.</summary>
        public static bool TryParseLine(string line, out string key, out string value, out bool malformed)
        {
            key = value = null;
            malformed = false;
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') return false;

            int eq = t.IndexOf('=');
            if (eq <= 0 || !IsValidKey(t.Substring(0, eq).Trim()))
            {
                malformed = true;
                return false;
            }
            key = t.Substring(0, eq).Trim();
            value = Unescape(t.Substring(eq + 1).Trim());
            return true;
        }

        /// <summary>Letters, digits, <c>_ . -</c>; meta keys start with <c>@</c>.</summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' || (c == '@' && i == 0)) continue;
                return false;
            }
            return true;
        }

        public static bool IsMeta(string key) => key.Length > 0 && key[0] == '@';

        /// <summary>Entries of a language file; meta lines go to <paramref name="meta"/> when given. Later duplicates win.</summary>
        public static Dictionary<string, string> Parse(string text, Dictionary<string, string> meta = null)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return dict;
            using (var reader = new StringReader(text.TrimStart('\uFEFF')))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!TryParseLine(line, out var key, out var value, out _)) continue;
                    if (IsMeta(key)) { if (meta != null) meta[key] = value; }
                    else dict[key] = value;
                }
            }
            return dict;
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    switch (s[i + 1])
                    {
                        case 'n': sb.Append('\n'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
