using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>Searchable key picker: keys grouped by their dot segments (menu › play), with the English text beside each.</summary>
    public class LocKeyDropdown : AdvancedDropdown
    {
        class KeyItem : AdvancedDropdownItem
        {
            public readonly string Key;
            public KeyItem(string label, string key) : base(label) { Key = key; }
        }

        readonly Action<string> onPicked;

        public LocKeyDropdown(Action<string> onPicked) : base(new AdvancedDropdownState())
        {
            this.onPicked = onPicked;
            minimumSize = new Vector2(320, 360);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Localization keys");
            var groups = new Dictionary<string, AdvancedDropdownItem>();
            foreach (var key in LocDatabase.SelectableKeys)
            {
                var parent = root;
                var parts = key.Split('.');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var path = string.Join(".", parts, 0, i + 1);
                    if (!groups.TryGetValue(path, out var group))
                    {
                        groups[path] = group = new AdvancedDropdownItem(parts[i]);
                        parent.AddChild(group);
                    }
                    parent = group;
                }
                // Full key plus English, so search matches both "menu.play" and "Play".
                var english = LocDatabase.SourceText(LocDatabase.Base != null && LocDatabase.Base.Has(key) ? key : key + ".other");
                parent.AddChild(new KeyItem(key + (string.IsNullOrEmpty(english) ? "" : "   — " + LocAudit.Short(english, 40)), key));
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is KeyItem k) onPicked(k.Key);
        }
    }
}
