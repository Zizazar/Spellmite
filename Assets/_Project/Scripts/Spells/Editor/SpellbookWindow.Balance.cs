using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Balance table: every spell in one sortable table, numbers editable in place
    public partial class SpellbookWindow
    {
        // Field = SpellData property edited in the cell; null = computed, read-only
        private static readonly (string Title, string Field, Func<SpellData, float> Value, bool ProjectileOnly)[] BalanceColumns =
        {
            ("Cooldown", "Cooldown", s => s.Cooldown, false),
            ("Cast", "CastTime", s => s.CastTime, false),
            ("Cost", "ResistanceCost", s => s.ResistanceCost, false),
            ("Damage", "Damage", s => s.Damage, true),
            ("Count", "Count", s => s.Count, true),
            ("Speed", "Speed", s => s.Speed, true),
            ("Splash", "SplashRadius", s => s.SplashRadius, true),
            ("AI min", "AiMinRange", s => s.AiMinRange, false),
            ("AI max", "AiMaxRange", s => s.AiMaxRange, false),
            ("DPS/HPS", null, Dps, false),
            ("Hit, s", null, TimeToHit, false),
        };

        [SerializeField] private int sortColumn = -1; // -1 = by name
        [SerializeField] private bool sortDescending;

        private void BuildBalanceTab()
        {
            content.Add(Text("Click a column to sort, a name to open the spell. DPS includes hit buff damage; Self spells show healing per second.", "sb-hint"));

            var table = With(new ScrollView(ScrollViewMode.VerticalAndHorizontal), "sb-table");
            var head = Div("sb-tr sb-tr--head");
            head.Add(SortHeader("Spell", -1, "sb-td--name"));
            head.Add(Text("Kind", "sb-th sb-td--kind"));
            for (int i = 0; i < BalanceColumns.Length; i++) head.Add(SortHeader(BalanceColumns[i].Title, i, null));
            table.Add(head);

            IEnumerable<SpellData> rows = sortColumn >= 0 && sortColumn < BalanceColumns.Length
                ? spells.OrderBy(s => BalanceColumns[sortColumn].Value(s))
                : spells.OrderBy(s => s.name);
            if (sortDescending) rows = rows.Reverse();

            int index = 0;
            foreach (var spell in rows)
            {
                var so = new SerializedObject(spell);
                var row = Div("sb-tr");
                row.EnableInClassList("sb-tr--odd", index++ % 2 == 1);
                row.Add(OnClick(Text(spell.name, "sb-td sb-td--name sb-link"), () =>
                {
                    selectedSpell = spell;
                    SetTab(0);
                }));
                row.Add(Text(spell.Kind.ToString(), "sb-td sb-td--kind"));

                var computed = new List<(Label Label, Func<SpellData, float> Value)>();
                foreach (var column in BalanceColumns)
                {
                    if (column.Field == null)
                    {
                        var label = Text("", "sb-td sb-num");
                        computed.Add((label, column.Value));
                        row.Add(label);
                        continue;
                    }

                    string path = $"<{column.Field}>k__BackingField";
                    VisualElement cell;
                    if (column.Field == "Count")
                    {
                        var count = new IntegerField { bindingPath = path };
                        count.RegisterValueChangedCallback(e => { if (e.newValue < 1) count.value = 1; });
                        cell = count;
                    }
                    else
                    {
                        cell = new FloatField { bindingPath = path };
                    }
                    cell.AddToClassList("sb-td");
                    cell.SetEnabled(!column.ProjectileOnly || spell.Kind == SpellKind.Projectile);
                    row.Add(cell);
                }

                void UpdateComputed()
                {
                    foreach (var (label, value) in computed) label.text = value(spell).ToString("0.##");
                }
                UpdateComputed();
                row.Bind(so);
                row.TrackSerializedObjectValue(so, _ => UpdateComputed());
                table.Add(row);
            }
            content.Add(table);
        }

        private VisualElement SortHeader(string title, int column, string extraClass)
        {
            string arrow = sortColumn == column ? (sortDescending ? " ↓" : " ↑") : "";
            var header = OnClick(Text(title + arrow, "sb-th"), () =>
            {
                if (sortColumn == column) sortDescending = !sortDescending;
                else
                {
                    sortColumn = column;
                    sortDescending = column >= 0; // numbers: biggest first
                }
                Rebuild();
            });
            if (extraClass != null) header.AddToClassList(extraClass);
            return header;
        }
    }
}
