using GameJam.Localization;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // Risk of Rain style boss bar at the top of the screen while the boss fights: name and subtitle, health with a light
    // trail that drains behind each hit, current / max, and a pip at every phase threshold that dims once it's passed.
    // The bar fills up during the boss's opening roar.
    public class BossBar : MonoBehaviour
    {
        [SerializeField, Tooltip("Empty = the bar stays hidden (levels without a boss)")] private TreeBoss boss;
        [SerializeField, Tooltip("Shown while the fight is on")] private GameObject root;
        [SerializeField] private LocText nameText;
        [SerializeField] private LocText subtitleText;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal")] private Image healthFill;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal. Light bar that lags behind after a hit")] private Image healthTrail;
        [SerializeField] private TMP_Text healthText;
        [SerializeField, Tooltip("Inactive thin mark inside the bar, copied to every phase threshold")] private Graphic pipTemplate;
        [SerializeField, Range(0f, 1f)] private float passedPipAlpha = 0.2f;
        [SerializeField, Tooltip("Seconds the trail waits before it drains")] private float trailDelay = 0.4f;
        [SerializeField, Tooltip("Bar fractions per second")] private float trailSpeed = 0.6f;

        private Graphic[] pips;
        private float intro;
        private float trailHoldUntil;
        private bool shown;

        private void Start()
        {
            root.SetActive(false);
            if (boss == null)
            {
                enabled = false;
                return;
            }

            nameText.SetKey(boss.NameKey);
            subtitleText.SetKey(boss.SubtitleKey);
            // One where each phase after the first begins
            pips = new Graphic[Mathf.Max(0, boss.PhaseCount - 1)];
            for (int i = 0; i < pips.Length; i++)
            {
                var pip = Instantiate(pipTemplate, pipTemplate.transform.parent);
                // Under the health text, like the template
                pip.transform.SetSiblingIndex(pipTemplate.transform.GetSiblingIndex());
                var rect = pip.rectTransform;
                float x = boss.PhaseThreshold(i + 1);
                rect.anchorMin = new Vector2(x, rect.anchorMin.y);
                rect.anchorMax = new Vector2(x, rect.anchorMax.y);
                rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
                pip.gameObject.SetActive(true);
                pips[i] = pip;
            }
        }

        private void Update()
        {
            bool show = boss.IsFightActive;
            if (show != shown)
            {
                shown = show;
                root.SetActive(show);
                if (show)
                {
                    healthFill.fillAmount = healthTrail.fillAmount = 0f;
                    Tween.Custom(this, 0f, 1f, boss.IntroDuration, (bar, value) => bar.intro = value, Ease.OutCubic);
                }
            }
            if (!show) return;

            Health health = boss.Health;
            float fill = Mathf.Min(intro, health.Max > 0f ? health.Current / health.Max : 0f);
            if (fill < healthFill.fillAmount - 1e-4f) trailHoldUntil = Time.time + trailDelay;
            healthFill.fillAmount = fill;
            if (healthTrail.fillAmount <= fill) healthTrail.fillAmount = fill;
            else if (Time.time >= trailHoldUntil) healthTrail.fillAmount = Mathf.MoveTowards(healthTrail.fillAmount, fill, trailSpeed * Time.deltaTime);
            healthText.SetText("{0} / {1}", Mathf.Ceil(fill * health.Max), Mathf.Ceil(health.Max));

            for (int i = 0; i < pips.Length; i++)
            {
                Color color = pips[i].color;
                color.a = fill < boss.PhaseThreshold(i + 1) ? passedPipAlpha : 1f;
                pips[i].color = color;
            }
        }
    }
}
