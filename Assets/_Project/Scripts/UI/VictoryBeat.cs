using System;
using Cysharp.Threading.Tasks;
using GameJam.Localization;
using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    // The moment a fight is won: time slows down, a sound plays, a line comes up above the crosshair. Whoever ends the
    // fight awaits it and only then offers the body for eating, so the clicks still coming from the fight can't bite it.
    // Lives on the HUD.
    public class VictoryBeat : MonoBehaviour
    {
        public static VictoryBeat Instance { get; private set; }

        [SerializeField, Tooltip("Hidden outside the beat")] private CanvasGroup banner;
        [SerializeField] private LocText bannerText;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0.05f, 1f)] private float slowTimeScale = 0.25f;
        [SerializeField, Tooltip("Real seconds of slow motion")] private float slowTime = 1f;
        [SerializeField, Tooltip("Real seconds the banner takes to pop in")] private float bannerInTime = 0.15f;
        [SerializeField, Tooltip("Real seconds the banner takes to fade once time runs normally again")] private float bannerOutTime = 0.4f;
        [SerializeField, Tooltip("The banner starts this much bigger and settles")] private float bannerStartScale = 1.3f;

        private void Awake()
        {
            Instance = this;
            banner.gameObject.SetActive(false);
        }

        // Returns once time runs normally again
        public async UniTask Play(string textKey)
        {
            var token = destroyCancellationToken;
            banner.gameObject.SetActive(true);
            bannerText.SetKey(textKey);
            Tween.Alpha(banner, 0f, 1f, bannerInTime, useUnscaledTime: true);
            Tween.Scale(banner.transform, Vector3.one * bannerStartScale, Vector3.one, bannerInTime, Ease.OutBack, useUnscaledTime: true);
            audioSource.PlayOneShot(clip);
            Time.timeScale = slowTimeScale;

            // Scaled time, so the pause menu holds the beat instead of letting it run out behind the menu
            await UniTask.Delay(TimeSpan.FromSeconds(slowTime * slowTimeScale), cancellationToken: token);
            // The pause menu or a hitstop may be holding time below the slow scale: they give it back when they end
            await UniTask.WaitUntil(() => Time.timeScale >= slowTimeScale, cancellationToken: token);
            // Someone else (the tutorial's slow motion) may have changed it meanwhile
            if (Mathf.Approximately(Time.timeScale, slowTimeScale)) Time.timeScale = 1f;

            Tween.Alpha(banner, 0f, bannerOutTime, useUnscaledTime: true)
                .OnComplete(banner.gameObject, go => go.SetActive(false));
        }
    }
}
