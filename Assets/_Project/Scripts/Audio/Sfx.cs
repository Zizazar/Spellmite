using UnityEngine;

namespace Spellmite
{
    public static class Sfx
    {
        // A random clip of the set at a random pitch. Sets the source's pitch, so use it on one-shot sources only
        public static void PlayRandom(this AudioSource source, AudioClip[] clips, Vector2 pitch, float volume = 1f)
        {
            if (source == null || clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            source.pitch = Random.Range(pitch.x, pitch.y);
            source.PlayOneShot(clip, volume);
        }
    }
}
