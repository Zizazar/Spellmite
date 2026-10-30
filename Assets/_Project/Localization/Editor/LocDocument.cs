using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GameJam.Localization
{
    /// <summary>One language file, kept line by line so comments, blank lines and order survive edits.</summary>
    public class LocDocument
    {
        public class Line
        {
            public string Raw;
            public string Key;     // null for blank, comment and malformed lines
            public string Value;
            public bool Malformed;
            public int Number;     // 1-based, as loaded
        }

        public string Code { get; }
        public string Path { get; }
        public readonly List<Line> Lines = new List<Line>();
        readonly Dictionary<string, Line> index = new Dictionary<string, Line>(StringComparer.Ordinal);
        List<string> keys;

        public string DisplayName => index.TryGetValue(LocFormat.NameMeta, out var l) && l.Value.Length > 0 ? l.Value : Code;

        /// <summary>Entry keys in file order (no meta lines, duplicates once).</summary>
        public IReadOnlyList<string> Keys => keys ?? (keys = Lines.Where(l => l.Key != null && !LocFormat.IsMeta(l.Key)).Select(l => l.Key).Distinct().ToList());

        public IEnumerable<string> Duplicates => Lines.Where(l => l.Key != null).GroupBy(l => l.Key).Where(g => g.Count() > 1).Select(g => g.Key);
        public IEnumerable<Line> MalformedLines => Lines.Where(l => l.Malformed);

        LocDocument(string path)
        {
            Path = path.Replace('\\', '/');
            Code = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public static LocDocument Load(string path)
        {
            var doc = new LocDocument(path);
            var text = File.Exists(path) ? File.ReadAllText(path).TrimStart('﻿') : "";
            var raw = text.Replace("\r\n", "\n").Split('\n');
            int count = raw.Length > 0 && raw[raw.Length - 1].Length == 0 ? raw.Length - 1 : raw.Length; // drop the final newline
            for (int i = 0; i < count; i++)
            {
                var line = new Line { Raw = raw[i], Number = i + 1 };
                if (LocFormat.TryParseLine(raw[i], out var key, out var value, out var malformed))
                {
                    line.Key = key;
                    line.Value = value;
                    doc.index[key] = line; // last one wins, like the runtime
                }
                line.Malformed = malformed;
                doc.Lines.Add(line);
            }
            return doc;
        }

        public static LocDocument Create(string path, string displayName)
        {
            var doc = new LocDocument(path);
            doc.Set(LocFormat.NameMeta, displayName);
            doc.Lines.Add(new Line { Raw = "" });
            return doc;
        }

        public bool Has(string key) => index.ContainsKey(key);

        public bool TryGet(string key, out string value)
        {
            value = index.TryGetValue(key, out var l) ? l.Value : null;
            return value != null;
        }

        public string Get(string key) => index.TryGetValue(key, out var l) ? l.Value : null;

        /// <summary>Updates the key in place, or inserts it after <paramref name="afterKey"/>, after the last key with the same
        /// first segment ("menu.*"), or at the end.</summary>
        public void Set(string key, string value, string afterKey = null)
        {
            value = value ?? "";
            if (index.TryGetValue(key, out var existing))
            {
                existing.Value = value;
                existing.Raw = key + "=" + LocFormat.Escape(value);
                return;
            }

            var line = new Line { Key = key, Value = value, Raw = key + "=" + LocFormat.Escape(value) };
            int at = -1;
            if (afterKey != null && index.TryGetValue(afterKey, out var anchor)) at = Lines.IndexOf(anchor) + 1;
            if (at < 0)
            {
                int dot = key.IndexOf('.');
                if (dot > 0)
                {
                    var group = key.Substring(0, dot + 1);
                    at = Lines.FindLastIndex(l => l.Key != null && l.Key.StartsWith(group, StringComparison.Ordinal)) + 1;
                    if (at == 0) at = -1;
                }
            }
            if (at < 0)
            {
                while (Lines.Count > 0 && Lines[Lines.Count - 1].Key == null && Lines[Lines.Count - 1].Raw.Trim().Length == 0)
                    Lines.RemoveAt(Lines.Count - 1);
                if (Lines.Count > 0) Lines.Add(new Line { Raw = "" });
                at = Lines.Count;
            }
            Lines.Insert(at, line);
            index[key] = line;
            keys = null;
        }

        /// <summary>Removes every line with this key.</summary>
        public bool Remove(string key)
        {
            if (!index.Remove(key)) return false;
            Lines.RemoveAll(l => l.Key == key);
            keys = null;
            return true;
        }

        /// <summary>Drops earlier copies of duplicated keys, keeping the last (the one the runtime uses).</summary>
        public int RemoveDuplicates()
        {
            int before = Lines.Count;
            Lines.RemoveAll(l => l.Key != null && index[l.Key] != l);
            keys = null;
            return before - Lines.Count;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var l in Lines) sb.Append(l.Raw).Append('\n');
            File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
