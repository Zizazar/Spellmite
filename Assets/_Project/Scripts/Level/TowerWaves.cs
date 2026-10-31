using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // Enemy waves of the tower, one per floor from the bottom up. The boss floor on top has none.
    // Edited in Tools → Spellmite → Waves.
    [CreateAssetMenu(menuName = "Spellmite/Tower Waves")]
    public class TowerWaves : ScriptableObject
    {
        [Serializable]
        public class Group
        {
            [field: SerializeField] public EnemyBrain Prefab { get; private set; }
            [field: SerializeField, Min(1)] public int Count { get; private set; } = 1;
        }

        [Serializable]
        public class Wave
        {
            [field: SerializeField, Min(1), Tooltip("No new enemy appears while this many are alive")] public int MaxAlive { get; private set; } = 4;
            [field: SerializeField, Min(0f), Tooltip("Seconds between two spawns")] public float SpawnInterval { get; private set; } = 1f;
            [field: SerializeField, Min(0), Tooltip("The next group starts once this many free enemies or fewer are left")] public int NextGroupAt { get; private set; } = 2;
            [field: SerializeField, Tooltip("Spawned in this order")] public List<Group> Groups { get; private set; } = new();

            public int Total
            {
                get
                {
                    int total = 0;
                    foreach (var group in Groups)
                    {
                        if (group.Prefab != null) total += group.Count;
                    }
                    return total;
                }
            }
        }

        [field: SerializeField, Tooltip("Element 0 = floor 1")] public List<Wave> Floors { get; private set; } = new();
    }
}
