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

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(float amount, Vector3 impulse)
        {
            if (IsDead || Invulnerable) return;
            amount *= DamageTakenMultiplier;
            if (amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, impulse);
            if (IsDead) Died?.Invoke();
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
    }
}
