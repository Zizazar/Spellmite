using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // Active buffs on a body. Bodies read SpeedMultiplier / IsStunned; damage taken and ticks go through Health.
    [RequireComponent(typeof(Health))]
    public class BuffHolder : MonoBehaviour
    {
        private class ActiveBuff
        {
            public BuffData Data;
            public float EndTime;
            public float NextTick;
            public GameObject Vfx;
        }

        private readonly List<ActiveBuff> active = new();
        private Health health;

        public float SpeedMultiplier { get; private set; } = 1f;
        public bool IsStunned { get; private set; }

        private void Awake()
        {
            health = GetComponent<Health>();
            health.Died += Clear;
        }

        public void Apply(BuffData buff)
        {
            if (buff == null || health.IsDead) return;

            var existing = active.Find(b => b.Data == buff);
            if (existing != null)
            {
                existing.EndTime = Time.time + buff.Duration;
                return;
            }

            active.Add(new ActiveBuff
            {
                Data = buff,
                EndTime = Time.time + buff.Duration,
                NextTick = Time.time + buff.TickInterval,
                Vfx = buff.Vfx != null ? Instantiate(buff.Vfx, transform.TransformPoint(buff.VfxOffset), transform.rotation, transform) : null,
            });
            Recalculate();
        }

        private void Update()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var buff = active[i];
                if (buff.Data.HealthPerTick != 0f && Time.time >= buff.NextTick)
                {
                    buff.NextTick += buff.Data.TickInterval;
                    if (buff.Data.HealthPerTick > 0f) health.Heal(buff.Data.HealthPerTick);
                    else health.TakeDamage(-buff.Data.HealthPerTick, Vector3.zero);
                    if (health.IsDead) return; // Died already cleared the list
                }

                if (Time.time >= buff.EndTime)
                {
                    if (buff.Vfx != null) Destroy(buff.Vfx);
                    active.RemoveAt(i);
                    Recalculate();
                }
            }
        }

        private void Clear()
        {
            foreach (var buff in active)
                if (buff.Vfx != null) Destroy(buff.Vfx);
            active.Clear();
            Recalculate();
        }

        private void Recalculate()
        {
            float speed = 1f;
            float damageTaken = 1f;
            bool stunned = false;
            foreach (var buff in active)
            {
                speed *= buff.Data.SpeedMultiplier;
                damageTaken *= buff.Data.DamageTakenMultiplier;
                stunned |= buff.Data.Stun;
            }
            SpeedMultiplier = speed;
            IsStunned = stunned;
            health.DamageTakenMultiplier = damageTaken;
        }
    }
}
