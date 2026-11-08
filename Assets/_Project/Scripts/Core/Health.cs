using System;
using UnityEngine;

namespace Spellmite
{
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        public float Current { get; private set; }
        public float Max => maxHealth;
        public bool IsDead => Current <= 0f;
        public bool Invulnerable { get; set; }
        public float DamageTakenMultiplier { get; set; } = 1f; // set by BuffHolder

        // amount, knockback impulse
        public event Action<float, Vector3> Damaged;
        public event Action Died;
        // Damage the player dealt, to any body: DamageNumbers. target, amount, damage over time (a buff's tick)
        public static event Action<Health, float, bool> AnyHurtByPlayer;
        // A body the player's damage finished off: RunStats
        public static event Action<Health> AnyKilledByPlayer;

        private void Awake()
        {
            Current = maxHealth;
        }

        // byPlayer: the player's bite, swing, spell or a buff they put on (kills and damage numbers count only these)
        public void TakeDamage(float amount, Vector3 impulse, bool byPlayer = false, bool overTime = false)
        {
            if (IsDead || Invulnerable) return;
#if UNITY_EDITOR
            if (Cheats.GodMode && IsPlayer()) return;
#endif
            amount *= DamageTakenMultiplier;
            if (amount <= 0f) return;

            ApplyDamage(amount, impulse, byPlayer, overTime);
        }

        // Eaten from inside by the parasite: armor, invulnerability and god mode don't apply
        public void Consume(float amount)
        {
            if (!IsDead) ApplyDamage(amount, Vector3.zero, true, false);
        }

        private void ApplyDamage(float amount, Vector3 impulse, bool byPlayer, bool overTime)
        {
            Current = Mathf.Max(0f, Current - amount);
            if (byPlayer) AnyHurtByPlayer?.Invoke(this, amount, overTime);
            Damaged?.Invoke(amount, impulse);
            if (!IsDead) return;
            if (byPlayer) AnyKilledByPlayer?.Invoke(this);
            Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(maxHealth, Current + amount);
        }

        public void ResetHealth()
        {
            Current = maxHealth;
        }

#if UNITY_EDITOR
        private bool IsPlayer() => TryGetComponent(out ParasiteBody _) || (TryGetComponent(out HostBody host) && host.IsPossessed);
#endif
    }
}
