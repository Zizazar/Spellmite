using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    // Boss theme. Starts with the fight: the first pass (intro + body) plays once, then the loop clip is scheduled on
    // the exact sample where the first pass ends and repeats (its end joins its start). Fades out when the boss dies
    public class BossMusic : MonoBehaviour
    {
        [SerializeField] private TreeBoss boss;
        [SerializeField, Tooltip("2D, plays the first pass once")] private AudioSource introSource;
        [SerializeField, Tooltip("2D, plays the loop clip over and over")] private AudioSource loopSource;
        [SerializeField, Tooltip("The intro and the first pass of the body")] private AudioClip intro;
        [SerializeField, Tooltip("The body: its end joins its start")] private AudioClip loop;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
        [SerializeField, Tooltip("Seconds, after the boss dies (unscaled, slow-mo doesn't stretch it)")] private float fadeOutDuration = 3f;

        private bool started;

        private void Update()
        {
            if (!started)
            {
                if (!boss.IsFightActive) return;
                started = true;
                Play();
            }
            else if (boss.IsDefeated)
            {
                FadeOut(introSource);
                FadeOut(loopSource);
                enabled = false;
            }
        }

        private void Play()
        {
            // A short lead so both sources are queued before the first sample plays
            double start = AudioSettings.dspTime + 0.1;
            introSource.clip = intro;
            introSource.loop = false;
            introSource.volume = volume;
            introSource.PlayScheduled(start);

            loopSource.clip = loop;
            loopSource.loop = true;
            loopSource.volume = volume;
            loopSource.PlayScheduled(start + (double)intro.samples / intro.frequency);
        }

        private void FadeOut(AudioSource source)
        {
            Tween.AudioVolume(source, 0f, fadeOutDuration, useUnscaledTime: true).OnComplete(source, s => s.Stop());
        }
    }
}
