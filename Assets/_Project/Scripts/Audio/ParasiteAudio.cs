using UnityEngine;

namespace Spellmite
{
    // The player parasite's sounds: a skitter loop while it crawls (louder and higher the faster), bite, latching onto a host
    public class ParasiteAudio : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("One-shots")] private AudioSource sfxSource;
        [SerializeField] private Vector2 pitchRange = new(0.9f, 1.1f);

        [Header("Crawl")]
        [SerializeField, Tooltip("Looping skitter, silent unless the parasite crawls")] private AudioSource crawlSource;
        [SerializeField] private float crawlVolume = 0.6f;
        [SerializeField, Tooltip("Pitch at a slow crawl and at sprint speed")] private Vector2 crawlPitch = new(0.85f, 1.2f);
        [SerializeField, Tooltip("Volume per second the loop fades in and out")] private float crawlFade = 6f;

        [Header("Clips")]
        [SerializeField] private AudioClip[] biteClips;
        [SerializeField, Tooltip("Sinking into a host")] private AudioClip[] latchClips;
        [SerializeField] private AudioClip[] hurtClips;
        [SerializeField] private AudioClip[] deathClips;

        private void Start()
        {
            parasite.Bit += OnBit;
            parasite.Latched += OnLatched;
            parasite.Health.Damaged += OnDamaged;
            parasite.Health.Died += OnDied;
            crawlSource.loop = true;
            crawlSource.volume = 0f;
            crawlSource.Play();
        }

        private void OnDestroy()
        {
            if (parasite == null) return;
            parasite.Bit -= OnBit;
            parasite.Latched -= OnLatched;
            parasite.Health.Damaged -= OnDamaged;
            parasite.Health.Died -= OnDied;
        }

        private void Update()
        {
            float speed01 = parasite.IsOnSurface ? Mathf.Clamp01(parasite.Velocity.magnitude / parasite.ActiveProfile.SprintSpeed) : 0f;
            crawlSource.volume = Mathf.MoveTowards(crawlSource.volume, speed01 * crawlVolume, crawlFade * Time.deltaTime);
            if (speed01 > 0f) crawlSource.pitch = Mathf.Lerp(crawlPitch.x, crawlPitch.y, speed01);
        }

        private void OnBit() => sfxSource.PlayRandom(biteClips, pitchRange);
        private void OnLatched() => sfxSource.PlayRandom(latchClips, pitchRange);
        private void OnDamaged(float amount, Vector3 impulse) => sfxSource.PlayRandom(hurtClips, pitchRange);
        private void OnDied() => sfxSource.PlayRandom(deathClips, pitchRange);
    }
}
