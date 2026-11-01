using UnityEngine;

namespace Spellmite
{
    // Player only: heartbeats while the body the player is in (the parasite or its host) is low on health,
    // faster and louder the lower it gets
    public class LowHealthHeartbeat : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("2D")] private AudioSource source;
        [SerializeField, Tooltip("A single beat")] private AudioClip beat;
        [SerializeField, Range(0f, 1f), Tooltip("Beats below this fraction of health")] private float lowHealth = 0.35f;
        [SerializeField, Tooltip("Seconds between beats at lowHealth and at almost zero")] private Vector2 interval = new(1.1f, 0.55f);
        [SerializeField, Tooltip("Volume at lowHealth and at almost zero")] private Vector2 volume = new(0.4f, 1f);

        private float nextBeatTime;

        private void Update()
        {
            Health health = parasite.IsHosted && parasite.Host != null ? parasite.Host.Health : parasite.Health;
            float fraction = health.Current / health.Max;
            if (health.IsDead || fraction >= lowHealth || Time.time < nextBeatTime) return;

            float danger = 1f - fraction / lowHealth;
            source.PlayOneShot(beat, Mathf.Lerp(volume.x, volume.y, danger));
            nextBeatTime = Time.time + Mathf.Lerp(interval.x, interval.y, danger);
        }
    }
}
