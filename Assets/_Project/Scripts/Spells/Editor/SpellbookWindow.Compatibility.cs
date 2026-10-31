using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Compatibility: bodies × spells. A tick adds the spell to the body prefab's SpellCaster.
    public partial class SpellbookWindow
    {
        private void BuildCompatibilityTab()
        {
            content.Add(Text($"Tick a spell to give it to a body (saved into the prefab). Slot order = AI priority and the player's keys 1–{PlayerSlots}; reorder slots in the prefab's SpellCaster.", "sb-hint"));
            if (bodies.Count == 0)
            {
                content.Add(Text($"No prefabs with a SpellCaster in {BodyFolder}", "sb-empty"));
                return;
            }

            var table = With(new ScrollView(ScrollViewMode.VerticalAndHorizontal), "sb-table");
            var head = Div("sb-tr sb-tr--head");
            head.Add(Text("Spell", "sb-th sb-td--name"));
            foreach (var body in bodies)
            {
                var cell = OnClick(Text($"{body.name}  {body.SlotCount}/{PlayerSlots}", "sb-th sb-cell"), () =>
                {
                    Selection.activeObject = body.gameObject;
                    EditorGUIUtility.PingObject(body.gameObject);
                });
                cell.tooltip = body.SlotCount > PlayerSlots
                    ? $"More than {PlayerSlots} spells: the player can't reach the extra slots"
                    : "Click to select the prefab";
                cell.EnableInClassList("sb-warn-text", body.SlotCount > PlayerSlots);
                head.Add(cell);
            }
            table.Add(head);

            int index = 0;
            foreach (var spell in spells)
            {
                var row = Div("sb-tr");
                row.EnableInClassList("sb-tr--odd", index++ % 2 == 1);
                row.Add(OnClick(Text(spell.name, "sb-td sb-td--name sb-link"), () =>
                {
                    selectedSpell = spell;
                    SetTab(0);
                }));
                foreach (var body in bodies)
                {
                    int slot = SlotOf(body, spell);
                    var toggle = With(new Toggle { value = slot >= 0, text = slot >= 0 ? $"slot {slot + 1}" : "" }, "sb-cell");
                    toggle.RegisterValueChangedCallback(e => SetBodySpell(body, spell, e.newValue));
                    row.Add(toggle);
                }
                table.Add(row);
            }
            content.Add(table);
        }

        private static int SlotOf(SpellCaster body, SpellData spell)
        {
            for (int i = 0; i < body.SlotCount; i++)
                if (body.GetSpell(i) == spell) return i;
            return -1;
        }

        // Adds the spell to the end of the prefab's list, or removes it, and saves the prefab
        private void SetBodySpell(SpellCaster body, SpellData spell, bool on)
        {
            var so = new SerializedObject(body);
            var list = so.FindProperty("spells");
            if (on)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = spell;
            }
            else
            {
                for (int i = list.arraySize - 1; i >= 0; i--)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == spell) list.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedProperties();
            PrefabUtility.SavePrefabAsset(body.gameObject);
            RebuildLater();
        }
    }
}
