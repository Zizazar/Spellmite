using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Buffs tab: same layout as Spells for BuffData
    public partial class SpellbookWindow
    {
        private static readonly Color DamageColor = new(0.9f, 0.35f, 0.25f);
        private static readonly Color HealColor = new(0.35f, 0.8f, 0.4f);
        private static readonly Color SpeedColor = new(0.35f, 0.65f, 0.95f);
        private static readonly Color StunColor = new(0.95f, 0.8f, 0.3f);
        private static readonly Color ShieldColor = new(0.65f, 0.5f, 0.95f);

        private void BuildBuffsTab()
        {
            if (selectedBuff == null) selectedBuff = buffs.FirstOrDefault();

            var detail = With(new ScrollView(), "sb-detail");
            Action refreshList = null;
            var side = Sidebar(buffs, () => selectedBuff, b => IsDebuff(b) ? "Debuffs" : "Buffs", b => b.Icon, EffectColors, _ => true,
                b =>
                {
                    selectedBuff = b;
                    ShowBuff(detail, b, refreshList);
                },
                () =>
                {
                    selectedBuff = CreateAsset<BuffData>(BuffFolder, "NewBuff");
                    RebuildLater();
                },
                null, out refreshList);

            SplitView(side, detail);
            ShowBuff(detail, selectedBuff, refreshList);
        }

        private void ShowBuff(VisualElement detail, BuffData buff, Action refreshList)
        {
            detail.Clear();
            if (buff == null)
            {
                detail.Add(Text("No buffs yet. Press + to create one.", "sb-empty"));
                refreshList();
                return;
            }

            var so = new SerializedObject(buff);
            var page = Div("sb-page");
            detail.Add(page);

            var head = AssetHeader(page, buff, out var icon, out var subtitle);
            head.Add(With(new Button(() =>
            {
                selectedBuff = Duplicate(buff);
                RebuildLater();
            }) { text = "Duplicate" }, "sb-btn"));

            var grid = Div("sb-grid");
            page.Add(grid);

            var info = Card(grid, "Info");
            info.Add(Field(so, "NameKey", "Name key"));
            var nameText = Text("", "sb-muted");
            info.Add(nameText);
            info.Add(Field(so, "Icon"));
            var usedBy = Text("", "sb-muted");
            info.Add(usedBy);

            var effect = Card(grid, "Effect");
            effect.Add(Field(so, "Duration", "Duration, s"));
            effect.Add(Field(so, "SpeedMultiplier", "Speed ×"));
            effect.Add(Field(so, "DamageTakenMultiplier", "Damage taken ×"));
            effect.Add(Field(so, "Stun"));
            effect.Add(Field(so, "HealthPerTick"));
            effect.Add(Field(so, "TickInterval", "Tick interval, s"));

            var vfx = Card(grid, "VFX");
            vfx.Add(Field(so, "Vfx", "Effect"));
            vfx.Add(Field(so, "VfxOffset", "Offset"));

            var stats = Div("sb-stats");
            page.Add(stats);
            var health = Stat(stats);
            var modifiers = Stat(stats);
            var check = Stat(stats);

            void Refresh()
            {
                if (buff == null) return; // asset deleted

                SetIcon(icon, buff.Icon, EffectColors(buff).DefaultIfEmpty(Color.gray).First(), buff.name);
                subtitle.text = $"{(IsDebuff(buff) ? "Debuff" : "Buff")} · {AssetPath(buff)}";
                nameText.text = NameText(buff.NameKey);
                var users = BuffUsers(buff);
                usedBy.text = users.Count > 0 ? "Used by: " + string.Join(", ", users) : "No spell uses it yet";

                float total = BuffTotal(buff);
                int ticks = Mathf.FloorToInt(buff.Duration / Mathf.Max(0.05f, buff.TickInterval));
                health.Set("Health over duration", total.ToString("+0.#;-0.#;0"),
                    buff.HealthPerTick != 0f ? $"{ticks} ticks of {buff.HealthPerTick:+0.#;-0.#} over {buff.Duration:0.#} s" : "no ticks");
                modifiers.Set("Modifiers", BuffSummary(buff), $"lasts {buff.Duration:0.#} s, reapplying refreshes it");
                SetCheck(check, ValidateBuff(buff));
                refreshList();
            }

            page.Bind(so);
            page.TrackSerializedObjectValue(so, _ => Refresh());
            Refresh();
        }

        private static bool IsDebuff(BuffData b) => b.HealthPerTick < 0f || b.SpeedMultiplier < 1f || b.DamageTakenMultiplier > 1f || b.Stun;

        // List dots: damage, heal, speed, stun, damage taken
        private static IEnumerable<Color> EffectColors(BuffData b)
        {
            if (b.HealthPerTick < 0f) yield return DamageColor;
            if (b.HealthPerTick > 0f) yield return HealColor;
            if (b.SpeedMultiplier != 1f) yield return SpeedColor;
            if (b.Stun) yield return StunColor;
            if (b.DamageTakenMultiplier != 1f) yield return ShieldColor;
        }

        private static string BuffSummary(BuffData b)
        {
            var parts = new List<string>();
            if (b.Stun) parts.Add("stun");
            if (b.SpeedMultiplier != 1f) parts.Add($"speed ×{b.SpeedMultiplier:0.##}");
            if (b.DamageTakenMultiplier != 1f) parts.Add($"damage taken ×{b.DamageTakenMultiplier:0.##}");
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        private List<string> BuffUsers(BuffData b)
        {
            var users = new List<string>();
            foreach (var spell in spells)
            {
                if (spell.HitBuff == b) users.Add($"{spell.name} (on hit)");
                if (spell.SelfBuff == b) users.Add($"{spell.name} (self)");
            }
            return users;
        }

        private List<string> ValidateBuff(BuffData b)
        {
            var issues = new List<string>();
            CheckNameKey(b.NameKey, issues);
            if (b.Icon == null) issues.Add("No icon");
            if (b.Vfx == null) issues.Add("No VFX: players won't see it");
            if (b.Duration <= 0f) issues.Add("Duration must be above 0");
            if (b.HealthPerTick == 0f && !b.Stun && b.SpeedMultiplier == 1f && b.DamageTakenMultiplier == 1f) issues.Add("Does nothing");
            if (BuffUsers(b).Count == 0) issues.Add("No spell uses it");
            return issues;
        }
    }
}
