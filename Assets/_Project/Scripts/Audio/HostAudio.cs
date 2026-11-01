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
        }

        private void OnDestroy()
        {
            body.MeleeHit -= OnMeleeHit;
            body.Health.Damaged -= OnDamaged;
            body.Health.Died -= OnDied;
        }

        private void OnMeleeHit() => source.PlayRandom(meleeHitClips, pitchRange);
        private void OnDamaged(float amount, Vector3 impulse) => source.PlayRandom(hurtClips, pitchRange);
        private void OnDied() => source.PlayRandom(deathClips, pitchRange);
    }
}
