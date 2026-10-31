using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spellmite
{
    // Waves: which enemies each tower floor spawns. Tools → Spellmite → Waves. Edits go into Data/TowerWaves.asset
    // with undo and apply in Play Mode too (the next spawn uses them). Floor 5 is the boss floor, it has no wave.
    public class WavesWindow : EditorWindow
    {
        private const string StylePath = "Assets/_Project/Scripts/Spells/Editor/Spellbook.uss";
        private const string WavesPath = "Assets/_Project/Data/TowerWaves.asset";
        private const string EnemyFolder = "Assets/_Project/Prefabs";
        private const int WaveFloors = 4; // floor 5 is the boss

        [SerializeField] private int floor;

        private readonly List<EnemyBrain> enemyPrefabs = new();
        private readonly List<VisualElement> tabButtons = new();
        private TowerWaves waves;
        private SerializedObject serialized;
        private VisualElement content;
        private Label status;
        private Label totalNote;

        [MenuItem("Tools/Spellmite/Waves")]
        public static void Open() => GetWindow<WavesWindow>("Waves");

        private void OnEnable() => Undo.undoRedoPerformed += Rebuild;
        private void OnDisable() => Undo.undoRedoPerformed -= Rebuild;
        private void OnProjectChange() => Rebuild();

        private void CreateGUI()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) rootVisualElement.styleSheets.Add(style);
            rootVisualElement.AddToClassList("sb-root");

            var header = Div("sb-header");
            header.Add(Text("Tower waves", "sb-header__title"));
            tabButtons.Clear();
            for (int i = 0; i <= WaveFloors; i++)
            {
                int index = i;
                string title = i < WaveFloors ? $"Floor {i + 1}" : $"Floor {i + 1} · Boss";
                var button = OnClick(Text(title, "sb-tab"), () =>
                {
                    floor = index;
                    Rebuild();
                });
                tabButtons.Add(button);
                header.Add(button);
            }
            rootVisualElement.Add(header);

            status = Text("", "sb-hint");
            rootVisualElement.Add(status);
            content = With(new ScrollView(), "sb-content");
            rootVisualElement.Add(content);
            rootVisualElement.schedule.Execute(UpdateStatus).Every(250);
            Rebuild();
        }

        private void Rebuild()
        {
            if (content == null) return;
            for (int i = 0; i < tabButtons.Count; i++) tabButtons[i].EnableInClassList("sb-tab--on", i == floor);
            content.Clear();
            totalNote = null;

            waves = AssetDatabase.LoadAssetAtPath<TowerWaves>(WavesPath);
            var page = Div("sb-page");
            content.Add(page);

            if (floor >= WaveFloors)
            {
                page.Add(Text("The boss floor has no wave. The boss is set up separately.", "sb-empty"));
                return;
            }
            if (waves == null)
            {
                page.Add(Text($"No waves asset at {WavesPath}", "sb-empty"));
                page.Add(With(new Button(CreateWaves) { text = "Create" }, "sb-btn sb-btn--accent"));
                return;
            }
            if (waves.Floors.Count < WaveFloors)
            {
                Undo.RecordObject(waves, "Add wave floors");
                while (waves.Floors.Count < WaveFloors) waves.Floors.Add(new TowerWaves.Wave());
                EditorUtility.SetDirty(waves);
            }

            LoadEnemyPrefabs();
            serialized = new SerializedObject(waves);
            var wave = serialized.FindProperty(Backing("Floors")).GetArrayElementAtIndex(floor);

            var settings = Card(page, "Settings", out _);
            settings.Add(new PropertyField(Rel(wave, "MaxAlive"), "Max alive at once"));
            settings.Add(new PropertyField(Rel(wave, "SpawnInterval"), "Seconds between spawns"));
            settings.Add(new PropertyField(Rel(wave, "NextGroupAt"), "Next group at ≤ free"));

            var groups = Rel(wave, "Groups");
            var list = Card(page, "Enemies, in spawn order", out totalNote);
            for (int i = 0; i < groups.arraySize; i++) list.Add(GroupRow(groups, i));
            if (groups.arraySize == 0) list.Add(Text("No enemies: this floor has no wave.", "sb-muted"));

            var add = Div("sb-row");
            add.style.flexWrap = Wrap.Wrap;
            add.style.marginTop = 6;
            foreach (var prefab in enemyPrefabs)
            {
                add.Add(With(new Button(() => AddGroup(groups, prefab)) { text = $"+ {prefab.name}", tooltip = "Add to the end of the wave" }, "sb-btn"));
            }
            if (enemyPrefabs.Count == 0) add.Add(Text($"No prefabs with an EnemyBrain in {EnemyFolder}", "sb-muted"));
            list.Add(add);

            content.Bind(serialized);
            UpdateStatus();
        }

        private VisualElement GroupRow(SerializedProperty groups, int index)
        {
            var group = groups.GetArrayElementAtIndex(index);
            var row = Div("sb-row");
            row.style.marginBottom = 2;

            var number = Text($"{index + 1}.", "sb-num");
            number.style.width = 28;
            row.Add(number);

            var prefab = new ObjectField { objectType = typeof(EnemyBrain), allowSceneObjects = false, tooltip = "Enemy prefab" };
            prefab.style.flexGrow = 1;
            prefab.BindProperty(Rel(group, "Prefab"));
            row.Add(prefab);

            var countProperty = Rel(group, "Count");
            var count = new IntegerField { tooltip = "How many" };
            count.style.width = 50;
            count.BindProperty(countProperty);
            count.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 1) count.value = 1;
            });
            row.Add(count);

            row.Add(SmallButton("▲", "Spawn earlier", index > 0, () => Move(groups, index, index - 1)));
            row.Add(SmallButton("▼", "Spawn later", index < groups.arraySize - 1, () => Move(groups, index, index + 1)));
            row.Add(SmallButton("✕", "Remove", true, () =>
            {
                groups.DeleteArrayElementAtIndex(index);
                Apply();
            }));
            return row;
        }

        private void AddGroup(SerializedProperty groups, EnemyBrain prefab)
        {
            int index = groups.arraySize;
            groups.InsertArrayElementAtIndex(index);
            var group = groups.GetArrayElementAtIndex(index);
            Rel(group, "Prefab").objectReferenceValue = prefab;
            Rel(group, "Count").intValue = 1;
            Apply();
        }

        private void Move(SerializedProperty groups, int from, int to)
        {
            groups.MoveArrayElement(from, to);
            Apply();
        }

        private void Apply()
        {
            serialized.ApplyModifiedProperties();
            Rebuild();
        }

        private void CreateWaves()
        {
            var asset = CreateInstance<TowerWaves>();
            for (int i = 0; i < WaveFloors; i++) asset.Floors.Add(new TowerWaves.Wave());
            AssetDatabase.CreateAsset(asset, WavesPath);
            AssetDatabase.SaveAssets();
            Rebuild();
        }

        private void LoadEnemyPrefabs()
        {
            enemyPrefabs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { EnemyFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.TryGetComponent(out EnemyBrain brain)) enemyPrefabs.Add(brain);
            }
        }

        private void UpdateStatus()
        {
            if (status == null) return;
            if (totalNote != null && waves != null && floor < waves.Floors.Count) totalNote.text = $"{waves.Floors[floor].Total} enemies";

            if (!EditorApplication.isPlaying)
            {
                status.text = "Edits also work in Play Mode: the next spawn uses them.";
                return;
            }
            var spawner = FindAnyObjectByType<WaveSpawner>();
            if (spawner == null) status.text = "No WaveSpawner in the scene.";
            else if (spawner.ActiveFloor < 0) status.text = "No wave yet: it starts when the player steps off the lift.";
            else
            {
                string state = spawner.IsSpawning ? "spawning" : spawner.AliveCount > 0 ? "all spawned" : "cleared";
                status.text = $"Floor {spawner.ActiveFloor + 1}: {state} · spawned {spawner.SpawnedCount}/{spawner.TotalCount} · alive {spawner.AliveCount}";
            }
        }

        // ---------- Helpers ----------

        private static string Backing(string name) => $"<{name}>k__BackingField";

        private static SerializedProperty Rel(SerializedProperty property, string name) => property.FindPropertyRelative(Backing(name));

        private static Button SmallButton(string text, string tooltip, bool enabled, Action action)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.style.width = 24;
            button.SetEnabled(enabled);
            return button;
        }

        // Returns the card body
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
    }
}
