using System;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // The parasite's death as one hard cut instead of a visible teleport, all in real time:
    // a flash and a near-freeze on the killing hit, then the PSX picture crunches down to a few huge pixels
    // in slow motion while it fades to black. The parasite is moved under the black; after a beat for the camera
    // to settle, the picture snaps back sharp with a second flash.
    // ParasiteBody awaits Cover() before moving and Reveal() after. Lives on the HUD, above everything else.
    public class DeathTransition : MonoBehaviour
    {
        public static DeathTransition Instance { get; private set; }

        [SerializeField, Tooltip("Full-screen black Image, raycast off")] private Image cover;
        [SerializeField, Tooltip("Full-screen white Image above the cover, raycast off. Tinted with flashColor")] private Image flash;
        [SerializeField] private Color flashColor = new(1f, 0.25f, 0.6f, 1f);

        [Header("Death")]
        [SerializeField, Tooltip("Real seconds the game nearly freezes on the killing hit")] private float hitstopTime = 0.12f;
        [SerializeField, Range(0f, 1f)] private float hitstopTimeScale = 0.05f;
        [SerializeField, Range(0f, 1f), Tooltip("Time scale while the picture crunches")] private float crunchTimeScale = 0.3f;
        [SerializeField, Tooltip("Real seconds from the full picture to black")] private float crunchTime = 0.35f;
        [SerializeField, Tooltip("Rows of pixels the picture crunches down to")] private int crunchLines = 6;
        [SerializeField, Range(0f, 1f)] private float deathFlashAlpha = 0.6f;

        [Header("Back")]
        [SerializeField, Tooltip("Real seconds of black after the move, while the camera settles at the spawn")] private float holdTime = 0.3f;
        [SerializeField, Tooltip("Real seconds from black and crunched back to the full picture")] private float revealTime = 0.3f;
        [SerializeField, Range(0f, 1f)] private float revealFlashAlpha = 0.8f;

        private PsxCamera psx;
        // The camera's own resolution, read on the first death
        private int fullLines;
        private float slowedTimeScale = -1f;
        private float timeScaleBefore = 1f;
        private Tween crunchTween;
        private Tween flashTween;

        private void Awake()
        {
            Instance = this;
            cover.raycastTarget = flash.raycastTarget = false;
            SetCrunch(0f);
            SetFlash(0f);
        }

        private void OnDestroy()
        {
            crunchTween.Stop();
            flashTween.Stop();
            if (psx != null && fullLines > 0) psx.VerticalResolution = fullLines;
            RestoreTimeScale();
        }

        // Returns at full black, with time running normally again
        public async UniTask Cover()
        {
            var token = destroyCancellationToken;
            if (psx == null && Camera.main != null) psx = Camera.main.GetComponent<PsxCamera>();
            if (psx != null && fullLines <= 0) fullLines = psx.VerticalResolution > 0 ? psx.VerticalResolution : Camera.main.pixelHeight;

            Flash(deathFlashAlpha, hitstopTime + crunchTime);
            SetTimeScale(hitstopTimeScale);
            await UniTask.Delay(TimeSpan.FromSeconds(hitstopTime), DelayType.UnscaledDeltaTime, cancellationToken: token);
            SetTimeScale(crunchTimeScale);
            crunchTween.Stop();
            crunchTween = Tween.Custom(new TweenSettings<float>(0f, 1f, crunchTime, Ease.InQuad, useUnscaledTime: true), SetCrunch);
            await crunchTween.ToUniTask().AttachExternalCancellation(token);
            RestoreTimeScale();
        }

        // Returns once the picture starts coming back, so the parasite can pop in with it
        public async UniTask Reveal()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(holdTime), DelayType.UnscaledDeltaTime, cancellationToken: destroyCancellationToken);
            Flash(revealFlashAlpha, revealTime * 1.5f);
            crunchTween.Stop();
            crunchTween = Tween.Custom(new TweenSettings<float>(1f, 0f, revealTime, Ease.OutQuad, useUnscaledTime: true), SetCrunch);
        }

        // 0 = full picture, 1 = black and crunched. Rows shrink evenly in log space, so the big steps don't come all at the end
        private void SetCrunch(float t)
        {
            cover.color = new Color(0f, 0f, 0f, t);
            if (psx == null || fullLines <= 0) return;
            float lines = Mathf.Exp(Mathf.Lerp(Mathf.Log(fullLines), Mathf.Log(Mathf.Max(1, crunchLines)), t));
            psx.VerticalResolution = t <= 0f ? fullLines : Mathf.Max(1, Mathf.RoundToInt(lines));
        }

        private void Flash(float alpha, float fadeTime)
        {
            flashTween.Stop();
            SetFlash(alpha);
            flashTween = Tween.Custom(new TweenSettings<float>(alpha, 0f, fadeTime, Ease.OutQuad, useUnscaledTime: true), SetFlash);
        }

        private void SetFlash(float alpha) => flash.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * alpha);

        private void SetTimeScale(float scale)
        {
            if (slowedTimeScale < 0f) timeScaleBefore = Time.timeScale;
            slowedTimeScale = timeScaleBefore * scale;
            Time.timeScale = slowedTimeScale;
        }

        // Someone else (the tutorial's slow motion) may have changed it meanwhile
        private void RestoreTimeScale()
        {
            if (slowedTimeScale < 0f) return;
            if (Mathf.Approximately(Time.timeScale, slowedTimeScale)) Time.timeScale = timeScaleBefore;
            slowedTimeScale = -1f;
        }
    }
}
