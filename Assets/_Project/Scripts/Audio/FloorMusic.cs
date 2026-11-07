using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    // Tower floor themes. A floor's track starts with its wave (the player steps off the lift) and loops; it fades out
    // once the last enemy is down, on the victory beat. The ride up stays quiet. The boss floor has BossMusic instead
    public class FloorMusic : MonoBehaviour
    {
        [SerializeField] private WaveSpawner waves;
        [SerializeField, Tooltip("2D, loops the floor's track")] private AudioSource source;
        [SerializeField, Tooltip("Element 0 = floor 1. Empty = no music on that floor")] private AudioClip[] floorTracks;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
        [SerializeField, Tooltip("Seconds, after the last enemy of the wave is down (unscaled, slow-mo doesn't stretch it)")] private float fadeOutDuration = 2f;

        private int playingFloor = -1;
        private Tween fade;

        private void Update()
        {
            int floor = waves.ActiveFloor;
            if (waves.IsSpawning && floor >= 0 && floor != playingFloor) Play(floor);
            else if (playingFloor >= 0 && !waves.IsSpawning && waves.AliveCount == 0) FadeOut();
        }

        private void Play(int floor)
        {
            playingFloor = floor;
            fade.Stop();
            AudioClip clip = floor < floorTracks.Length ? floorTracks[floor] : null;
            if (clip == null)
            {
                source.Stop();
                return;
            }
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.Play();
        }

        private void FadeOut()
        {
            playingFloor = -1;
            fade.Stop();
            if (!source.isPlaying) return;
            fade = Tween.AudioVolume(source, 0f, fadeOutDuration, useUnscaledTime: true).OnComplete(source, s => s.Stop());
        }
    }
}
