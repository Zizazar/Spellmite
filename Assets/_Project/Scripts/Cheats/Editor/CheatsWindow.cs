using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Spellmite
{
    // Playtest cheats: Tools → Spellmite → Cheats. Toggles last until Unity is closed (SessionState);
    // spawning and killing work in Play Mode.
    public class CheatsWindow : EditorWindow
    {
        private const string EnemyFolder = "Assets/_Project/Prefabs";
        private const float SpawnReach = 15f;
        private const float FallbackDistance = 4f;
        private const float SpawnSpread = 1.5f;

        private readonly List<EnemyBrain> enemyPrefabs = new();
        private int spawnCount = 1;

        [MenuItem("Tools/Spellmite/Cheats")]
        public static void Open() => GetWindow<CheatsWindow>("Cheats");

        // Static fields reset on every domain reload (entering Play Mode too): restore them
        [InitializeOnLoadMethod]
        private static void Load()
        {
            Cheats.NoWaves = SessionState.GetBool(nameof(Cheats.NoWaves), false);
            Cheats.NoEnemyAI = SessionState.GetBool(nameof(Cheats.NoEnemyAI), false);
            Cheats.GodMode = SessionState.GetBool(nameof(Cheats.GodMode), false);
            Cheats.StartFloor = SessionState.GetInt(nameof(Cheats.StartFloor), 0);
        }

        private void OnEnable() => LoadEnemyPrefabs();
        private void OnProjectChange() => LoadEnemyPrefabs();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Toggles", EditorStyles.boldLabel);
            Cheats.NoWaves = Toggle("Waves off", "No wave spawns (a running wave pauses), the lift goes up even with enemies left",
                nameof(Cheats.NoWaves), Cheats.NoWaves);
            Cheats.NoEnemyAI = Toggle("Enemy AI off", "Free enemies stand still and do nothing", nameof(Cheats.NoEnemyAI), Cheats.NoEnemyAI);
            Cheats.GodMode = Toggle("God mode", "The parasite and the host it controls take no damage", nameof(Cheats.GodMode), Cheats.GodMode);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tower", EditorStyles.boldLabel);
            // Shown 1-based like the HUD, stored 0-based
            int floor = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Start on floor",
                "The lift and the player start on this floor when Play Mode starts, the parasite respawns there. 1 = normal start"),
                Cheats.StartFloor + 1)) - 1;
            if (floor != Cheats.StartFloor)
            {
                Cheats.StartFloor = floor;
                SessionState.SetInt(nameof(Cheats.StartFloor), floor);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Enemies", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Spawning and killing work in Play Mode.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                spawnCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Count", "Enemies per click"), spawnCount));
                foreach (var prefab in enemyPrefabs)
                {
                    if (GUILayout.Button(new GUIContent($"Spawn {prefab.name}", "Where the camera looks"))) Spawn(prefab);
                }
                if (enemyPrefabs.Count == 0) EditorGUILayout.LabelField($"No prefabs with an EnemyBrain in {EnemyFolder}");

                EditorGUILayout.Space();
                if (GUILayout.Button(new GUIContent("Kill all enemies", "Every free (not possessed) enemy"))) KillAll();
            }
        }

        private static bool Toggle(string label, string tooltip, string key, bool value)
        {
            bool result = EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), value);
            if (result != value) SessionState.SetBool(key, result);
            return result;
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

        private void Spawn(EnemyBrain prefab)
        {
            var player = FindAnyObjectByType<ParasiteBody>();
            var camera = Camera.main;
            if (player == null || camera == null)
            {
                Debug.LogWarning("Cheats: no player or main camera in the scene.");
                return;
            }

            Transform body = player.ActiveBody;
            Vector3 point = AimPoint(camera, body);
            for (int i = 0; i < spawnCount; i++)
            {
                Vector2 offset = i == 0 ? Vector2.zero : UnityEngine.Random.insideUnitCircle * SpawnSpread;
                Vector3 position = point + new Vector3(offset.x, 0f, offset.y);
                // Facing the player
                Vector3 toPlayer = Vector3.ProjectOnPlane(body.position - position, Vector3.up);
                Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
                Instantiate(prefab, position, rotation);
            }
        }

        // The ground where the screen center points (bodies skipped), or a few meters ahead of the player
        private static Vector3 AimPoint(Camera camera, Transform playerBody)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, SpawnReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<HostBody>() != null || hit.collider.GetComponentInParent<ParasiteBody>() != null) continue;
                // Step off walls, then drop to the floor below
                Vector3 point = hit.point + hit.normal * 0.6f;
                return Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out var ground, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    ? ground.point
                    : point;
            }

            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            return playerBody.position + forward * FallbackDistance;
        }

        private static void KillAll()
        {
            foreach (var enemy in new List<EnemyBrain>(EnemyBrain.All))
            {
                if (!enemy.IsThinking) continue;
                var health = enemy.GetComponent<HostBody>().Health;
                health.Invulnerable = false;
                health.TakeDamage(float.MaxValue, Vector3.zero);
            }
        }
    }
}
