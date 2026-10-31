using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Spells tab: list with search and tag filters, card with every field, stats and checks
    public partial class SpellbookWindow
    {
        private void BuildSpellsTab()
        {
            if (selectedSpell == null) selectedSpell = spells.FirstOrDefault();

            var filters = Div("sb-chips sb-filters");
            var detail = With(new ScrollView(), "sb-detail");
            Action refreshList = null;

            void FillFilters()
            {
                filters.Clear();
                foreach (var tag in tags)
                    filters.Add(TagChip(tag, tagFilter.Contains(tag), () =>
                    {
                        if (!tagFilter.Remove(tag)) tagFilter.Add(tag);
                        FillFilters();
                        refreshList();
                    }));
            }
            FillFilters();

            var side = Sidebar(spells, () => selectedSpell, s => s.Kind.ToString(), s => s.Icon, TagColors,
                s => tagFilter.All(t => s.Tags.Contains(t)),
                s =>
                {
                    selectedSpell = s;
                    ShowSpell(detail, s, refreshList);
                },
                () =>
                {
                    selectedSpell = CreateAsset<SpellData>(SpellFolder, "NewSpell");
                    RebuildLater();
                },
                filters, out refreshList);

            SplitView(side, detail);
            ShowSpell(detail, selectedSpell, refreshList);
        }

        private void ShowSpell(VisualElement detail, SpellData spell, Action refreshList)
        {
            detail.Clear();
            if (spell == null)
            {
                detail.Add(Text("No spells yet. Press + to create one.", "sb-empty"));
                refreshList();
                return;
            }

            var so = new SerializedObject(spell);
            var page = Div("sb-page");
            detail.Add(page);

            var head = AssetHeader(page, spell, out var icon, out var subtitle);
            head.Add(With(new Button(() =>
            {
                selectedSpell = Duplicate(spell);
                RebuildLater();
            }) { text = "Duplicate" }, "sb-btn"));
            head.Add(With(new Button(() => TestInSlot1(spell))
            {
                text = "Test in slot 1",
                tooltip = "Play Mode: puts this spell into slot 1 (key 1) of the body you control",
            }, "sb-btn sb-btn--accent"));

            var tagsBody = Card(page, "Tags");
            var chips = Div("sb-chips");
            tagsBody.Add(chips);
            var usedBy = Text("", "sb-muted");
            tagsBody.Add(usedBy);

            var grid = Div("sb-grid");
            page.Add(grid);

            var info = Card(grid, "Info");
            info.Add(Field(so, "NameKey", "Name key"));
            var nameText = Text("", "sb-muted");
            info.Add(nameText);
            info.Add(Field(so, "Icon"));

            var timings = Card(grid, "Timings");
            timings.Add(Field(so, "Kind"));
            timings.Add(Field(so, "Cooldown", "Cooldown, s"));
            timings.Add(Field(so, "CastTime", "Cast time, s"));
            timings.Add(Field(so, "ResistanceCost"));
            timings.Add(Field(so, "CastVfx", "Telegraph VFX"));
            timings.Add(Field(so, "SelfBuff"));

            var projectile = Card(grid, "Projectile");
            projectile.Add(Field(so, "Damage"));
            projectile.Add(Field(so, "Count"));
            projectile.Add(Field(so, "SpreadAngle", "Spread, °"));
            projectile.Add(Field(so, "Speed"));
            projectile.Add(Field(so, "Gravity"));
            projectile.Add(Field(so, "Lifetime", "Lifetime, s"));
            projectile.Add(Field(so, "Radius"));

            var hit = Card(grid, "Hit & homing");
            hit.Add(Field(so, "Knockback"));
            hit.Add(Field(so, "SplashRadius"));
            hit.Add(Field(so, "HitBuff"));
            hit.Add(Field(so, "HomingTurnRate", "Homing, °/s"));
            hit.Add(Field(so, "HomingRange"));

            var vfx = Card(grid, "Projectile VFX");
            vfx.Add(Field(so, "ProjectileVfx", "Projectile"));
            vfx.Add(Field(so, "ImpactVfx", "Impact"));
            vfx.Add(Field(so, "MuzzleVfx", "Muzzle"));

            var ai = Card(page, "AI", out var aiNote);
            ai.Add(Field(so, "AiUse", "Use"));
            var aiMin = Field(so, "AiMinRange", "Min range");
            var aiMax = Field(so, "AiMaxRange", "Max range");
            var aiHealth = Field(so, "AiHealthBelow", "Health below");
            ai.Add(aiMin);
            ai.Add(aiMax);
            ai.Add(aiHealth);
            var range = Div("sb-range");
            var rangeLabel = Text("", "sb-range__label");
            var track = Div("sb-range__track");
            var fill = Div("sb-range__fill");
            track.Add(fill);
            range.Add(rangeLabel);
            range.Add(track);
            ai.Add(range);

            var stats = Div("sb-stats");
            page.Add(stats);
            var power = Stat(stats);
            var time = Stat(stats);
            var check = Stat(stats);

            void Refresh()
            {
                if (spell == null) return; // asset deleted

                bool isProjectile = spell.Kind == SpellKind.Projectile;
                SetVisible(projectile.parent, isProjectile);
                SetVisible(hit.parent, isProjectile);
                SetVisible(vfx.parent, isProjectile);

                SetIcon(icon, spell.Icon, TagColors(spell).DefaultIfEmpty(Color.gray).First(), spell.name);
                subtitle.text = $"{spell.Kind} · {AssetPath(spell)}";
                nameText.text = NameText(spell.NameKey);
                var users = UsedBy(spell);
                usedBy.text = users.Count > 0 ? "Used by: " + string.Join(", ", users) : "No body uses it yet (see Compatibility)";

                chips.Clear();
                foreach (var tag in tags) chips.Add(TagChip(tag, spell.Tags.Contains(tag), () => ToggleTag(so, tag)));
                chips.Add(NewTagChip(so));

                bool attack = spell.AiUse == SpellAiUse.Attack;
                SetVisible(aiMin, attack);
                SetVisible(aiMax, attack);
                SetVisible(range, attack);
                SetVisible(aiHealth, spell.AiUse == SpellAiUse.HealWhenHurt);
                aiNote.text = spell.AiUse switch
                {
                    SpellAiUse.Attack => "attack in range, needs line of sight",
                    SpellAiUse.BuffInCombat => "cast when a target is near",
                    _ => $"cast below {spell.AiHealthBelow:P0} health",
                };
                float scale = Mathf.Max(20f, Mathf.Ceil(spell.AiMaxRange / 5f) * 5f);
                rangeLabel.text = $"Range {spell.AiMinRange:0.#} – {spell.AiMaxRange:0.#} m";
                fill.style.left = Length.Percent(Mathf.Clamp01(spell.AiMinRange / scale) * 100f);
                fill.style.width = Length.Percent(Mathf.Clamp01((spell.AiMaxRange - spell.AiMinRange) / scale) * 100f);

                if (isProjectile)
                {
                    float dot = -BuffTotal(spell.HitBuff);
                    string dotText = dot > 0f ? $" + {spell.HitBuff.name} {dot:0.#}" : "";
                    power.Set("DPS", Dps(spell).ToString("0.#"), $"{spell.Damage:0.#} × {spell.Count}{dotText} every {Cycle(spell):0.#} s");
                    time.Set("Time to hit", spell.Speed > 0f ? $"{TimeToHit(spell):0.##} s" : "—", $"windup + flight to {TestDistance(spell):0.#} m");
                }
                else
                {
                    power.Set("Heal / s", Dps(spell).ToString("0.#"), spell.SelfBuff != null ? $"{spell.SelfBuff.name} every {Cycle(spell):0.#} s" : "no self buff");
                    time.Set("Time to effect", $"{spell.CastTime:0.##} s", "windup");
                }
                SetCheck(check, Validate(spell));
                refreshList();
            }

            page.Bind(so);
            page.TrackSerializedObjectValue(so, _ => Refresh());
            Refresh();
        }

        // "+ tag" chip that turns into a name field. Enter creates the tag asset and adds it to the spell.
        private VisualElement NewTagChip(SerializedObject so)
        {
            var holder = Div("sb-chips");
            var chip = Text("+ tag", "sb-chip");
            var field = With(new TextField { isDelayed = true }, "sb-new-tag");
            SetVisible(field, false);

            chip.AddManipulator(new Clickable(() =>
            {
                SetVisible(chip, false);
                SetVisible(field, true);
                field.Focus();
            }));
            field.RegisterValueChangedCallback(e =>
            {
                string name = e.newValue.Trim();
                if (name.Length == 0) return;
                ToggleTag(so, CreateTag(name));
                RebuildLater();
            });
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (!string.IsNullOrWhiteSpace(field.value)) return;
                SetVisible(field, false);
                SetVisible(chip, true);
            });

            holder.Add(chip);
            holder.Add(field);
            return holder;
        }

        private static SpellTag CreateTag(string name)
        {
            var tag = CreateAsset<SpellTag>(TagFolder, name);
            var so = new SerializedObject(tag);
            so.FindProperty("<Color>k__BackingField").colorValue = Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 0.9f);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(tag);
            return tag;
        }

        private static void ToggleTag(SerializedObject so, SpellTag tag)
        {
            so.Update();
            var list = so.FindProperty("<Tags>k__BackingField");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != tag) continue;
                list.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                return;
            }
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = tag;
            so.ApplyModifiedProperties();
        }

        // Play Mode test: the body the player controls gets the spell in slot 1 (key 1)
        private void TestInSlot1(SpellData spell)
        {
            if (!EditorApplication.isPlaying)
            {
                ShowNotification(new GUIContent("Enter Play Mode first"));
                return;
            }

            var parasite = FindAnyObjectByType<ParasiteBody>();
            var caster = parasite != null && parasite.IsHosted && parasite.Host != null ? parasite.Host.GetComponent<SpellCaster>() : null;
            if (caster == null)
            {
                ShowNotification(new GUIContent("Possess a body with a SpellCaster first"));
                return;
            }

            caster.SetSpell(0, spell);
            ShowNotification(new GUIContent($"{spell.name} is in slot 1 of {caster.name}. Press 1"));
        }

        private static IEnumerable<Color> TagColors(SpellData spell) => spell.Tags.Where(t => t != null).Select(t => t.Color);

        private List<string> UsedBy(SpellData spell)
        {
            var users = new List<string>();
            foreach (var body in bodies)
                for (int i = 0; i < body.SlotCount; i++)
                    if (body.GetSpell(i) == spell) users.Add($"{body.name} (slot {i + 1})");
            return users;
        }

        private List<string> Validate(SpellData s)
        {
            var issues = new List<string>();
            CheckNameKey(s.NameKey, issues);
            if (s.Icon == null) issues.Add("No icon");
            if (s.Kind == SpellKind.Projectile)
            {
                if (s.ProjectileVfx == null) issues.Add("No projectile VFX");
                if (s.ImpactVfx == null) issues.Add("No impact VFX");
                if (s.Damage <= 0f && s.HitBuff == null) issues.Add("No damage and no hit buff");
                if (s.Speed <= 0f) issues.Add("Speed must be above 0");
                else if (s.AiUse == SpellAiUse.Attack && s.AiMaxRange > s.Speed * s.Lifetime)
                    issues.Add($"Flies {s.Speed * s.Lifetime:0.#} m, but AI max range is {s.AiMaxRange:0.#} m");
            }
            else if (s.SelfBuff == null)
            {
                issues.Add("Self spell without a self buff does nothing");
            }
            if (s.AiUse == SpellAiUse.Attack && s.AiMinRange > s.AiMaxRange) issues.Add("AI min range is above max range");
            if (UsedBy(s).Count == 0) issues.Add("No body uses it");
            return issues;
        }

        // ---------- Balance math ----------

        // Seconds between casts: windup + cooldown (the cooldown starts when the spell fires)
        private static float Cycle(SpellData s) => Mathf.Max(0.01f, s.CastTime + s.Cooldown);

        // Health change over the buff's whole duration: negative = damage, positive = heal
        private static float BuffTotal(BuffData b) =>
            b == null || b.HealthPerTick == 0f ? 0f : b.HealthPerTick * Mathf.Floor(b.Duration / Mathf.Max(0.05f, b.TickInterval));

        // Projectile: hit damage of every shot + hit buff damage, per cast cycle. Self: healing per second.
        private static float Dps(SpellData s) => s.Kind == SpellKind.Projectile
            ? (s.Damage * s.Count + Mathf.Max(0f, -BuffTotal(s.HitBuff))) / Cycle(s)
            : Mathf.Max(0f, BuffTotal(s.SelfBuff)) / Cycle(s);

        // Middle of the AI range: the typical distance the spell is cast from
        private static float TestDistance(SpellData s) => (s.AiMinRange + s.AiMaxRange) * 0.5f;

        private static float TimeToHit(SpellData s) =>
            s.Kind == SpellKind.Projectile && s.Speed > 0f ? s.CastTime + TestDistance(s) / s.Speed : s.CastTime;
    }
}
