using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    // Tutorial ambience. Starts with the scene, under the menu (on WebGL the browser holds it until the first input):
    // the beginning plays once, then the middle is scheduled on the exact sample where it ends and repeats. Once the
    // room is cleared the middle crossfades into the end, which plays once and fades to silence by itself
    public class TutorialMusic : MonoBehaviour
    {
        [SerializeField] private SewerTutorial tutorial;
        [SerializeField, Tooltip("2D, plays the beginning once")] private AudioSource introSource;
        [SerializeField, Tooltip("2D, plays the middle over and over")] private AudioSource loopSource;
        [SerializeField, Tooltip("2D, plays the end once")] private AudioSource outroSource;
        [SerializeField] private AudioClip beginning;
        [SerializeField] private AudioClip middle;
        [SerializeField] private AudioClip end;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
        [SerializeField, Tooltip("Seconds, from the middle into the end (unscaled, slow-mo doesn't stretch it)")] private float crossfadeDuration = 2f;

        private void Start()
        {
            // A short lead so both sources are queued before the first sample plays
            double start = AudioSettings.dspTime + 0.1;
            introSource.clip = beginning;
            introSource.loop = false;
            introSource.volume = volume;
            introSource.PlayScheduled(start);

            loopSource.clip = middle;
            loopSource.loop = true;
            loopSource.volume = volume;
            loopSource.PlayScheduled(start + (double)beginning.samples / beginning.frequency);
        }

        private void Update()
        {
            if (!tutorial.IsRoomCleared) return;
            enabled = false;

            // Stop also cancels the loop if it's still only scheduled
            FadeOut(introSource);
            FadeOut(loopSource);
            outroSource.clip = end;
            outroSource.loop = false;
            outroSource.volume = 0f;
            outroSource.Play();
            Tween.AudioVolume(outroSource, volume, crossfadeDuration, useUnscaledTime: true);
        }

        private void FadeOut(AudioSource source)
        {
            Tween.AudioVolume(source, 0f, crossfadeDuration, useUnscaledTime: true).OnComplete(source, s => s.Stop());
        }
    }
}
