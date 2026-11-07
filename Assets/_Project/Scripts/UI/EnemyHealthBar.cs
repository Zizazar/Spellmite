using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // One enemy's floating health bar. Unrolls when it appears; a hit drops the fill at once, leaves a flashing chunk that
    // drains after a moment and punches and shakes the bar by the size of the hit. Pulses at low health, collapses on death.
    // EnemyHealthBars places and drives it. Hit effects run on unscaled time, so hitstop doesn't hold them.
    public class EnemyHealthBar : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField, Tooltip("Takes the punch and the shake, while the root follows the enemy. Its Image is the bar's backing")] private Image content;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal")] private Image fill;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal. Light bar that lags behind after a hit")] private Image trail;

        [Header("Look")]
        [SerializeField] private Color healthColor = new(0.85f, 0.2f, 0.15f);
        [SerializeField, Tooltip("Low health: the fill pulses towards this")] private Color lowHealthColor = new(1f, 0.75f, 0.6f);
        [SerializeField] private Color trailColor = new(0.949f, 0.886f, 0.761f);
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField, Range(0f, 1f)] private float lowHealth = 0.25f;
        [SerializeField, Tooltip("Pulses per second")] private float lowHealthPulseRate = 2.5f;
        [SerializeField, Tooltip("Bar width in canvas units, tougher enemies get longer bars")] private Vector2 widthRange = new(100f, 180f);
        [SerializeField, Tooltip("Max health at those two widths")] private Vector2 healthRange = new(60f, 200f);

        [Header("Health")]
        [SerializeField, Tooltip("Seconds the trail waits before it drains")] private float trailDelay = 0.35f;
        [SerializeField, Tooltip("Bar fractions per second")] private float trailSpeed = 0.9f;
        [SerializeField, Tooltip("Bar fractions per second while healing")] private float healSpeed = 1f;

        [Header("Hit")]
        [SerializeField, Tooltip("A hit of this fraction of max health gets the strongest effects")] private float bigHit = 0.3f;
        [SerializeField] private float flashTime = 0.2f;
        [SerializeField] private float hitTime = 0.22f;
        [SerializeField, Tooltip("Scale added by the smallest and the biggest hit")] private Vector2 punch = new(0.06f, 0.2f);
        [SerializeField, Tooltip("Canvas units, smallest and biggest hit")] private Vector2 shake = new(1.5f, 6f);

        [Header("Appear, leave")]
        [SerializeField] private float appearTime = 0.18f;
        [SerializeField] private float fadeTime = 0.25f;
        [SerializeField, Tooltip("Seconds a dead enemy's bar holds, so the last hit reads")] private float deathHold = 0.2f;
        [SerializeField] private float deathTime = 0.2f;
        [SerializeField, Tooltip("Canvas units per second the aimed enemy's bar moves onto its brackets")] private float liftSpeed = 400f;

        private RectTransform rect;
        private Color backingColor;
        private Tween scaleTween;
        private Tween alphaTween;
        private Tween punchTween;
        private Tween shakeTween;
        private float hideAt;
        private float finishTime;
        private float trailHoldUntil;
        private float flash;
        private float lift;
        private bool leaving;
        private bool dying;

        public HostBody Host { get; private set; }
        // Last known top of the enemy, the bar stays there while it collapses
        public Vector3 Anchor { get; set; }
        public bool Finished => leaving && Time.unscaledTime >= finishTime;

        private void Awake()
        {
            rect = (RectTransform)transform;
            backingColor = content.color;
        }

        // justLost: health the hit that brought the bar up took, so that hit shows on it too
        public void Show(HostBody host, float justLost)
        {
            Host = host;
            leaving = dying = false;
            hideAt = 0f;
            flash = 0f;
            lift = 0f;

            Health health = host.Health;
            fill.fillAmount = trail.fillAmount = health.Max > 0f ? Mathf.Clamp01((health.Current + justLost) / health.Max) : 0f;
            float width = Mathf.Lerp(widthRange.x, widthRange.y, Mathf.InverseLerp(healthRange.x, healthRange.y, health.Max));
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            ResetContent();
            group.alpha = 0f;
            gameObject.SetActive(true);
            Appear();
        }

        public void Hide()
        {
            StopTweens();
            Host = null;
            gameObject.SetActive(false);
        }

        public void KeepUntil(float time)
        {
            hideAt = Mathf.Max(hideAt, time);
            if (leaving && !dying) Appear();
        }

        public void Hit(float amount)
        {
            float strength = Mathf.Clamp01(amount / Mathf.Max(1f, Host.Health.Max) / bigHit);
            flash = Mathf.Max(flash, Mathf.Lerp(0.6f, 1f, strength));

            punchTween.Stop();
            shakeTween.Stop();
            ResetContent();
            float size = Mathf.Lerp(punch.x, punch.y, strength);
            float distance = Mathf.Lerp(shake.x, shake.y, strength);
            // The bar is thin: it swells more in height than in width
            punchTween = Tween.PunchScale(content.transform, new Vector3(size, size * 2f, 0f), hitTime, useUnscaledTime: true);
            shakeTween = Tween.ShakeLocalPosition(content.transform, new Vector3(distance, distance * 0.6f, 0f), hitTime, frequency: 30f, useUnscaledTime: true);
        }

        public void Tick()
        {
            Health health = Host != null ? Host.Health : null;
            float target = health != null && health.Max > 0f ? health.Current / health.Max : 0f;
            UpdateFill(target);

            flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime / flashTime);
            float pulse = target > 0f && target <= lowHealth ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * lowHealthPulseRate * 2f * Mathf.PI) : 0f;
            fill.color = Color.Lerp(Color.Lerp(healthColor, lowHealthColor, pulse), flashColor, flash * 0.6f);
            trail.color = Color.Lerp(trailColor, flashColor, flash);
            content.color = Color.Lerp(backingColor, flashColor, flash * 0.5f);

            if (leaving) return;
            if (Host == null || !Host.isActiveAndEnabled || health.IsDead) Leave(true);
            else if (Host.IsPossessed || Time.time >= hideAt) Leave(false);
        }

        // position: canvas units. lift: extra height the bar eases to (the aimed enemy's brackets)
        public void Follow(Vector2 position, float targetLift, bool visible)
        {
            lift = Mathf.MoveTowards(lift, targetLift, liftSpeed * Time.unscaledDeltaTime);
            rect.anchoredPosition = position + Vector2.up * lift;
            content.gameObject.SetActive(visible);
        }

        private void UpdateFill(float target)
        {
            if (target < fill.fillAmount - 1e-4f)
            {
                fill.fillAmount = target;
                trailHoldUntil = Time.time + trailDelay;
            }
            else
            {
                fill.fillAmount = Mathf.MoveTowards(fill.fillAmount, target, healSpeed * Time.deltaTime);
            }

            if (trail.fillAmount <= fill.fillAmount) trail.fillAmount = fill.fillAmount;
            else if (Time.time >= trailHoldUntil) trail.fillAmount = Mathf.MoveTowards(trail.fillAmount, fill.fillAmount, trailSpeed * Time.deltaTime);
        }

        private void Appear()
        {
            leaving = false;
            scaleTween.Stop();
            alphaTween.Stop();
            alphaTween = Tween.Alpha(group, 1f, appearTime * 0.5f, useUnscaledTime: true);
            scaleTween = Tween.Scale(transform, new Vector3(0.2f, 1f, 1f), Vector3.one, appearTime, Ease.OutBack, useUnscaledTime: true);
        }

        private void Leave(bool died)
        {
            leaving = true;
            dying = died;
            if (died) flash = 1f;

            float delay = died ? deathHold : 0f;
            float time = died ? deathTime : fadeTime;
            finishTime = Time.unscaledTime + delay + time;
            scaleTween.Stop();
            alphaTween.Stop();
            alphaTween = Tween.Alpha(group, 0f, time, Ease.InQuad, startDelay: delay, useUnscaledTime: true);
            scaleTween = Tween.Scale(transform, new Vector3(died ? 1.15f : 1f, 0f, 1f), time, Ease.InBack, startDelay: delay, useUnscaledTime: true);
        }

        private void StopTweens()
        {
            scaleTween.Stop();
            alphaTween.Stop();
            punchTween.Stop();
            shakeTween.Stop();
        }

        private void ResetContent()
        {
            content.rectTransform.localScale = Vector3.one;
            content.rectTransform.anchoredPosition = Vector2.zero;
        }
    }
}
