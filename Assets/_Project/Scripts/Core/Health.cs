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

        // amount, knockback impulse
        public event Action<float, Vector3> Damaged;
        public event Action Died;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(float amount, Vector3 impulse)
        {
            if (IsDead || Invulnerable || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, impulse);
            if (IsDead) Died?.Invoke();
        }

        public void ResetHealth()
        {
            Current = maxHealth;
        }
    }
}
