using System;
using System.Collections.Generic;
using System.Linq;
using GameJam.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Spellbook: browse, edit and balance spells and buffs, and choose which bodies know which spells.
    // Tools → Spellmite → Spellbook. Edits go straight into the assets and prefabs, with undo.
    public partial class SpellbookWindow : EditorWindow
    {
        private const string StylePath = "Assets/_Project/Scripts/Spells/Editor/Spellbook.uss";
        private const string DataFolder = "Assets/_Project/Data/";
        private const string SpellFolder = DataFolder + "Spells";
        private const string BuffFolder = DataFolder + "Buffs";
        private const string TagFolder = DataFolder + "SpellTags";
        private const string BodyFolder = "Assets/_Project/Prefabs";
        private const int PlayerSlots = 4; // cast keys 1-4
        private static readonly string[] TabNames = { "Spells", "Balance table", "Compatibility", "Buffs" };

        [SerializeField] private int tab;
        [SerializeField] private string search = "";
        [SerializeField] private SpellData selectedSpell;
        [SerializeField] private BuffData selectedBuff;
        [SerializeField] private List<SpellTag> tagFilter = new();

        private readonly List<SpellData> spells = new();
        private readonly List<BuffData> buffs = new();
        private readonly List<SpellTag> tags = new();
        private readonly List<SpellCaster> bodies = new(); // prefab assets with a SpellCaster
        private readonly List<VisualElement> tabButtons = new();
        private VisualElement content;

        [MenuItem("Tools/Spellmite/Spellbook")]
        public static void Open() => GetWindow<SpellbookWindow>("Spellbook");

        private void CreateGUI()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) rootVisualElement.styleSheets.Add(style);
            rootVisualElement.AddToClassList("sb-root");

            var header = Div("sb-header");
            header.Add(Text("Spellbook", "sb-header__title"));
            tabButtons.Clear();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var button = OnClick(Text(TabNames[i], "sb-tab"), () => SetTab(index));
                tabButtons.Add(button);
                header.Add(button);
            }
            rootVisualElement.Add(header);

            content = Div("sb-content");
            rootVisualElement.Add(content);
            Rebuild();
        }

        private void OnProjectChange() => Rebuild();

        private void SetTab(int index)
        {
            tab = index;
            Rebuild();
        }

        private void RebuildLater() => rootVisualElement.schedule.Execute(Rebuild);

        private void Rebuild()
        {
            if (content == null) return;
            LoadData();
            for (int i = 0; i < tabButtons.Count; i++) tabButtons[i].EnableInClassList("sb-tab--on", i == tab);
            content.Clear();
            switch (tab)
            {
                case 1: BuildBalanceTab(); break;
                case 2: BuildCompatibilityTab(); break;
                case 3: BuildBuffsTab(); break;
                default: BuildSpellsTab(); break;
            }
        }

        private void LoadData()
        {
            spells.Clear();
            spells.AddRange(LoadAll<SpellData>());
            buffs.Clear();
            buffs.AddRange(LoadAll<BuffData>());
            tags.Clear();
            tags.AddRange(LoadAll<SpellTag>());
            tagFilter.RemoveAll(t => t == null);

            bodies.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { BodyFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.TryGetComponent(out SpellCaster caster)) bodies.Add(caster);
            }
        }

        private static IEnumerable<T> LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name);

        // ---------- Shared panels ----------

        // Searchable list grouped by groupOf. refresh re-fills it (selection, names, dots).
        private VisualElement Sidebar<T>(List<T> items, Func<T> selected, Func<T, string> groupOf, Func<T, Sprite> iconOf,
            Func<T, IEnumerable<Color>> dotsOf, Func<T, bool> filter, Action<T> select, Action create, VisualElement filters,
            out Action refresh) where T : ScriptableObject
        {
            var side = Div("sb-side");
            var top = Div("sb-row");
            var searchField = With(new ToolbarSearchField { value = search }, "sb-search");
            top.Add(searchField);
            top.Add(With(new Button(create) { text = "+", tooltip = "Create a new asset" }, "sb-add"));
            side.Add(top);
            if (filters != null) side.Add(filters);
            var list = With(new ScrollView(), "sb-list");
            side.Add(list);

            void Fill()
            {
                list.Clear();
                string query = search ?? "";
                var visible = items.Where(i => i != null && i.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && filter(i));
                foreach (var group in visible.GroupBy(groupOf).OrderBy(g => g.Key))
                {
                    list.Add(Text($"{group.Key} · {group.Count()}", "sb-group"));
                    foreach (var item in group)
                    {
                        var row = OnClick(Div("sb-item"), () => select(item));
                        row.EnableInClassList("sb-item--on", item == selected());
                        var dots = dotsOf(item).ToList();
                        var icon = Div("sb-item__icon");
                        SetIcon(icon, iconOf(item), dots.Count > 0 ? dots[0] : Color.gray, null);
                        row.Add(icon);
                        row.Add(Text(item.name, "sb-item__name"));
                        foreach (var color in dots) row.Add(Dot(color));
                        list.Add(row);
                    }
                }
            }

            searchField.RegisterValueChangedCallback(e =>
            {
                search = e.newValue;
                Fill();
            });
            Fill();
            refresh = Fill;
            return side;
        }

        private VisualElement SplitView(VisualElement side, VisualElement detail)
        {
            var split = With(new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal), "sb-split");
            split.Add(side);
            split.Add(detail);
            content.Add(split);
            return split;
        }

        // Icon, editable name (renames the asset), subtitle and Select button. Callers add their own buttons.
        private VisualElement AssetHeader(VisualElement page, ScriptableObject asset, out VisualElement icon, out Label subtitle)
        {
            var head = Div("sb-head");
            icon = Div("sb-head__icon");
            head.Add(icon);

            var titles = Div("sb-head__titles");
            var title = With(new TextField { value = asset.name, isDelayed = true, tooltip = "Rename the asset" }, "sb-title");
            title.RegisterValueChangedCallback(e =>
            {
                string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(asset), e.newValue.Trim());
                if (string.IsNullOrEmpty(error)) return;
                ShowNotification(new GUIContent(error));
                title.SetValueWithoutNotify(asset.name);
            });
            titles.Add(title);
            subtitle = Text("", "sb-subtitle");
            titles.Add(subtitle);
            head.Add(titles);

            head.Add(With(new Button(() =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }) { text = "Select", tooltip = "Select the asset in the Project window" }, "sb-btn"));
            page.Add(head);
            return head;
        }

        private static VisualElement Card(VisualElement parent, string title) => Card(parent, title, out _);

        // Returns the card body; the card itself is body.parent
        private static VisualElement Card(VisualElement parent, string title, out Label note)
        {
            var card = Div("sb-card");
            var head = Div("sb-card__head");
            head.Add(Text(title, "sb-card__title"));
            note = Text("", "sb-card__note");
            head.Add(note);
            card.Add(head);
            var body = Div("sb-card__body");
            card.Add(body);
            parent.Add(card);
            return body;
        }

        private class StatTile
        {
            public VisualElement Root;
            public Label Title, Value, Caption;

            public void Set(string title, string value, string caption)
            {
                Title.text = title;
                Value.text = value;
                Caption.text = caption;
            }
        }

        private static StatTile Stat(VisualElement parent)
        {
            var tile = new StatTile
            {
                Root = Div("sb-stat"),
                Title = Text("", "sb-stat__title"),
                Value = Text("", "sb-stat__value"),
                Caption = Text("", "sb-stat__caption"),
            };
            tile.Root.Add(tile.Title);
            tile.Root.Add(tile.Value);
            tile.Root.Add(tile.Caption);
            parent.Add(tile.Root);
            return tile;
        }

        private static void SetCheck(StatTile tile, List<string> issues)
        {
            tile.Root.EnableInClassList("sb-stat--ok", issues.Count == 0);
            tile.Root.EnableInClassList("sb-stat--warn", issues.Count > 0);
            string value = issues.Count switch { 0 => "OK", 1 => "1 issue", _ => $"{issues.Count} issues" };
            tile.Set("Check", value, string.Join("\n", issues));
        }

        private VisualElement TagChip(SpellTag tag, bool on, Action click)
        {
            var chip = Chip(tag.name, tag.Color, on, click);
            chip.tooltip = "Right-click to edit the tag";
            chip.AddManipulator(new ContextualMenuManipulator(e =>
                e.menu.AppendAction("Edit tag (name, color)", _ => Selection.activeObject = tag)));
            return chip;
        }

        private static VisualElement Chip(string text, Color color, bool on, Action click)
        {
            var chip = OnClick(Text(text, "sb-chip"), click);
            if (on)
            {
                chip.style.backgroundColor = new Color(color.r, color.g, color.b, 0.35f);
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = color;
            }
            return chip;
        }

        private static VisualElement Dot(Color color)
        {
            var dot = Div("sb-dot");
            dot.style.backgroundColor = color;
            return dot;
        }

        private static void SetIcon(VisualElement box, Sprite icon, Color tint, string name)
        {
            box.Clear();
            box.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.35f);
            box.style.backgroundImage = icon != null ? new StyleBackground(Background.FromSprite(icon)) : new StyleBackground(StyleKeyword.None);
            if (icon == null && !string.IsNullOrEmpty(name)) box.Add(Text(name.Substring(0, 1).ToUpperInvariant(), "sb-head__letter"));
        }

        // PropertyField for a [field: SerializeField] auto-property; bound when the page calls Bind
        private static PropertyField Field(SerializedObject so, string name, string label = null) =>
            new(so.FindProperty($"<{name}>k__BackingField"), label ?? ObjectNames.NicifyVariableName(name));

        // ---------- Assets ----------

        private static T CreateAsset<T>(string folder, string name) where T : ScriptableObject
        {
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
            var asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
            return asset;
        }

        private static T Duplicate<T>(T asset) where T : ScriptableObject
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CopyAsset(path, copy);
            return AssetDatabase.LoadAssetAtPath<T>(copy);
        }

        private static string AssetPath(UnityEngine.Object asset) => AssetDatabase.GetAssetPath(asset).Replace(DataFolder, "");

        private static string NameText(string key)
        {
            if (string.IsNullOrEmpty(key)) return "No name key";
            return Loc.Has(key) ? $"Text: {Loc.T(key)}" : "Key not found in the language files";
        }

        private static void CheckNameKey(string key, List<string> issues)
        {
            if (string.IsNullOrEmpty(key)) issues.Add("Name key is empty");
            else if (!Loc.Has(key)) issues.Add($"'{key}' is missing in the language files");
        }

        // ---------- Element helpers ----------

        private static T With<T>(T element, string classes) where T : VisualElement
        {
            foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) element.AddToClassList(c);
            return element;
        }

        private static VisualElement Div(string classes) => With(new VisualElement(), classes);

        private static Label Text(string text, string classes) => With(new Label(text), classes);

        private static T OnClick<T>(T element, Action action) where T : VisualElement
        {
            element.AddManipulator(new Clickable(action));
            return element;
        }

        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
