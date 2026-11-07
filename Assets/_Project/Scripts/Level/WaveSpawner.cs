using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Spellmite
{
    // Runs a floor's wave once the player steps off the lift there (TowerElevator calls StartWave). Enemies come in
    // the order of the wave's list, never more than MaxAlive at once, each group once NextGroupAt or fewer are left;
    // possessed ones don't count. Floors without a wave (the boss floor) count as done.
    public class WaveSpawner : MonoBehaviour
    {
        [SerializeField] private TowerWaves waves;
        [SerializeField, Tooltip("Parent of each floor's spawn points, bottom up")] private Transform[] spawnPoints;
        [SerializeField, Tooltip("Points closer to the player than this are skipped while there are others")] private float minPlayerDistance = 6f;
        [SerializeField, Range(0f, 1f), Tooltip("Before the alarm enemies wander around a spot this far from their spawn point to the middle of the floor. The lift is in the middle: at 1 they walk right up to it")] private float wanderTowardCenter = 0.5f;

        private readonly HashSet<int> started = new();
        private readonly List<EnemyBrain> spawned = new();
        private ParasiteBody player;
        private bool celebrating;

        // Read by the HUD and the Waves window in Play Mode
        public int ActiveFloor { get; private set; } = -1;
        public int SpawnedCount { get; private set; }
        public bool IsSpawning { get; private set; }
        public int TotalCount => ActiveFloor >= 0 ? waves.Floors[ActiveFloor].Total : 0;

        public int AliveCount
        {
            get
            {
                int alive = 0;
                foreach (var enemy in spawned)
                {
                    if (enemy != null && enemy.IsThinking) alive++;
                }
                return alive;
            }
        }

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
        }

        public void StartWave(int floor)
        {
#if UNITY_EDITOR
            if (Cheats.NoWaves) return;
#endif
            if (!HasWave(floor) || !started.Add(floor)) return;
            if (floor >= spawnPoints.Length || spawnPoints[floor] == null || spawnPoints[floor].childCount == 0)
            {
                Debug.LogError($"No spawn points for floor {floor + 1}: its wave is skipped.", this);
                return;
            }
            RunWave(floor, destroyCancellationToken).Forget();
        }

        public bool IsWaveDone(int floor)
        {
            if (!HasWave(floor)) return true;
            if (!started.Contains(floor)) return false;
            return floor != ActiveFloor || (!IsSpawning && !celebrating && AliveCount == 0);
        }

        private bool HasWave(int floor) => waves != null && floor < waves.Floors.Count && waves.Floors[floor].Total > 0;

        private async UniTaskVoid RunWave(int floor, CancellationToken token)
        {
            ActiveFloor = floor;
            SpawnedCount = 0;
            spawned.Clear();
            IsSpawning = true;
            AttackDirector.Instance.BeginStealth();

            // Read the wave every step, so edits in the Waves window apply right away
            TowerWaves.Wave Wave() => waves.Floors[floor];
            for (int g = 0; g < Wave().Groups.Count; g++)
            {
                if (Wave().Groups[g].Prefab == null) continue;
                // The next group comes while a few are still around, so mid-wave the player never hunts one straggler
                if (SpawnedCount > 0) await UniTask.WaitUntil(() => AliveCount <= Wave().NextGroupAt, cancellationToken: token);
                for (int i = 0; g < Wave().Groups.Count && i < Wave().Groups[g].Count; i++)
                {
                    if (Wave().Groups[g].Prefab == null) break;
                    await UniTask.WaitUntil(() => AliveCount < Wave().MaxAlive, cancellationToken: token);
                    if (SpawnedCount > 0) await UniTask.Delay(TimeSpan.FromSeconds(Wave().SpawnInterval), cancellationToken: token);
#if UNITY_EDITOR
                    await UniTask.WaitUntil(() => !Cheats.NoWaves, cancellationToken: token);
#endif
                    if (g >= Wave().Groups.Count || Wave().Groups[g].Prefab == null) break;
                    Spawn(Wave().Groups[g].Prefab, floor);
                }
            }

            IsSpawning = false;

            // Possessed counts as defeated: once no free enemy is left the wave is won, and a host the player still
            // sits in dies when they leave it (or it throws them off) instead of turning hostile again
            await UniTask.WaitUntil(() => AliveCount == 0, cancellationToken: token);
            AttackDirector.Instance.RaiseAlarm();
            foreach (var enemy in spawned)
            {
                if (enemy != null && enemy.TryGetComponent(out HostBody host) && host.IsPossessed) KillOnRelease(host, token).Forget();
            }

            // The lift stays locked until the beat ends: leaving now would turn eating off before it is offered
            celebrating = true;
            await VictoryBeat.Instance.Play("hud.victory.floor");
            celebrating = false;
            if (player != null && player.Host != null) player.Host.Devourable = true;
        }

        // The tutorial room follows the same rule
        public static async UniTaskVoid KillOnRelease(HostBody host, CancellationToken token)
        {
            await UniTask.WaitUntil(() => host == null || !host.IsPossessed, cancellationToken: token);
            if (host == null) return;
            host.Health.Invulnerable = false;
            host.Health.TakeDamage(float.MaxValue, Vector3.zero);
        }

        private void Spawn(EnemyBrain prefab, int floor)
        {
            Vector3 playerPosition = player != null ? player.ActiveBody.position : spawnPoints[floor].position;
            Transform point = PickPoint(spawnPoints[floor], playerPosition, minPlayerDistance);
            EnemyBrain enemy = Instantiate(prefab, point.position, point.rotation);
            enemy.SetHome(Vector3.Lerp(point.position, FloorCenter(spawnPoints[floor]), wanderTowardCenter));
            spawned.Add(enemy);
            SpawnedCount++;
        }

        private static Vector3 FloorCenter(Transform points)
        {
            Vector3 sum = Vector3.zero;
            foreach (Transform point in points) sum += point.position;
            return sum / points.childCount;
        }

        // A random child of parent at least minDistance from the player, or the farthest one when all are close.
        // The boss spawns its skeletons with it too
        public static Transform PickPoint(Transform parent, Vector3 playerPosition, float minDistance)
        {
            var far = new List<Transform>();
            Transform farthest = parent.GetChild(0);
            float maxDistance = -1f;
            foreach (Transform point in parent)
            {
                float distance = Vector3.Distance(point.position, playerPosition);
                if (distance >= minDistance) far.Add(point);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthest = point;
                }
            }
            return far.Count > 0 ? far[UnityEngine.Random.Range(0, far.Count)] : farthest;
        }
    }
}
