using UnityEngine;

namespace Spellmite
{
    // A host body's sounds: its melee swing landing, getting hurt, dying
    [RequireComponent(typeof(HostBody))]
    public class HostAudio : MonoBehaviour
    {
        [SerializeField, Tooltip("3D, on the body")] private AudioSource source;
        [SerializeField] private Vector2 pitchRange = new(0.9f, 1.1f);

        [Header("Clips")]
        [SerializeField, Tooltip("A swing hit at least one body")] private AudioClip[] meleeHitClips;
        [SerializeField] private AudioClip[] hurtClips;
        [SerializeField] private AudioClip[] deathClips;

        private HostBody body;

        private void Awake()
        {
            body = GetComponent<HostBody>();
            body.MeleeHit += OnMeleeHit;
            body.Health.Damaged += OnDamaged;
            body.Health.Died += OnDied;
            Health.AnyHurtByPlayer += OnHurtByPlayer;
        }

        private void OnDestroy()
        {
            body.MeleeHit -= OnMeleeHit;
            body.Health.Damaged -= OnDamaged;
            body.Health.Died -= OnDied;
            Health.AnyHurtByPlayer -= OnHurtByPlayer;
        }

        private void OnMeleeHit() => source.PlayRandom(meleeHitClips, pitchRange);

        // The player's own body sounds hurt from any damage
        private void OnDamaged(float amount, Vector3 impulse)
        {
            if (body.IsPossessed) source.PlayRandom(hurtClips, pitchRange);
        }

        // An enemy sounds hurt only from the player's direct hits (no buff ticks)
        private void OnHurtByPlayer(Health target, float amount, bool overTime)
        {
            if (target != body.Health || overTime || body.IsPossessed) return;
            source.PlayRandom(hurtClips, pitchRange);
        }

        private void OnDied() => source.PlayRandom(deathClips, pitchRange);
    }
}
