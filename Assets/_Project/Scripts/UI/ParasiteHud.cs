using GameJam.Localization;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Spellmite
{
    // Crosshair that turns magenta over a host you can leap to, brackets around that host on screen
    // with its name, the possess key and its spells above them, and the current host's control bar
    // (time left before it throws the parasite off).
    // Control running out: a magenta edge vignette pulses, harder towards zero, and flashes when the host throws the parasite off;
    // when critical the next host to leap to gets faded, pulsing brackets (no name) even if it isn't aimed at.
    // While the current host can be eaten (SpellThief) a window slides in at the top of the screen with the keys and the host's
    // spells; the body sits in the lower half of the screen, so nothing covers it or the crosshair.
    // Runs after the camera (Cinemachine updates in LateUpdate), so the brackets don't lag a frame behind the host.
    [DefaultExecutionOrder(100)]
    public class ParasiteHud : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;

        [Header("Crosshair")]
        [SerializeField] private Image crosshair;
        [SerializeField] private Color idleColor = new(1f, 1f, 1f, 0.8f);
        [SerializeField] private Color targetColor = new(1f, 0.25f, 0.6f, 1f);

        [Header("Target")]
        [SerializeField, Tooltip("Sliced brackets stretched over the aimed host's bounds on screen")] private RectTransform targetFrame;
        [SerializeField, Tooltip("On targetFrame: fades the brackets of a suggested (not aimed) host")] private CanvasGroup targetFrameGroup;
        [SerializeField, Tooltip("Alpha of the suggested host's brackets swings between these")] private Vector2 suggestedAlpha = new(0.25f, 0.7f);
        [SerializeField] private float suggestedPulseRate = 3f;
        [SerializeField, Tooltip("Canvas pixels added around the host's bounds")] private float framePadding = 10f;
        [SerializeField, Tooltip("Far hosts still get brackets at least this big")] private float minFrameSize = 60f;
        [SerializeField, Tooltip("Shown above the brackets, pivot at its bottom")] private RectTransform possessHint;
        [SerializeField, Tooltip("Gap between the brackets and the hint")] private float hintGap = 6f;
        [SerializeField] private LocText targetName;
        [SerializeField, Tooltip("Key hud.hint.possess, {0} = the leap key")] private LocText possessHintText;
        [SerializeField, Tooltip("Input action whose key the hint names")] private string leapActionName = "Player/Leap";
        [SerializeField, Tooltip("Row of spell icons inside the hint, hidden for a host without spells")] private GameObject spellIconsRoot;
        [SerializeField, Tooltip("One per spell slot of a body. Darkened with its cooldown: a host's cooldowns carry over on possession")] private SkillSlot[] spellIcons;
        [SerializeField, Tooltip("Seconds between the icons popping up on a new target")] private float spellPopStagger = 0.04f;

        [Header("Devour window")]
        [SerializeField, Tooltip("Shown while the current host can be eaten. Anchored to the top of the screen, slides in from above it")] private RectTransform devourHint;
        [SerializeField, Tooltip("Key hud.hint.devour, {0} = the attack key")] private LocText devourText;
        [SerializeField, Tooltip("Key hud.hint.devour.pick, {0} and {1} = the first and last cast keys")] private LocText devourPickText;
        [SerializeField, Tooltip("One per spell slot of a body, with its key. The chosen one grows and gets the risky frame color")] private SkillSlot[] devourIcons;
        [SerializeField, Tooltip("Same order as the icons")] private LocText[] devourNames;
        [SerializeField] private Color devourNameColor = new(0.914f, 0.863f, 0.769f, 1f);
        [SerializeField] private Color devourChosenNameColor = new(1f, 0.55f, 0.75f, 1f);
        [SerializeField] private float devourChosenScale = 1.12f;
        [SerializeField] private float devourSlideTime = 0.25f;

        [Header("Control")]
        [SerializeField] private GameObject resistanceRoot;
        [SerializeField, Tooltip("Image Type = Filled")] private Image resistanceFill;
        [SerializeField] private Graphic resistanceLabel;
        [SerializeField] private Color resistanceColor = new(1f, 0.25f, 0.6f, 1f);
        [SerializeField] private Color resistanceFlashColor = Color.white;
        [SerializeField, Tooltip("Flashes per second when low (ParasiteBody's lowControl)")] private float flashRate = 4f;

        [Header("Vignette")]
        [SerializeField, Tooltip("Full-screen Image, raycast off. Its sprite is made in code: clear center, opaque edges")] private Image vignette;
        [SerializeField] private Color vignetteColor = new(1f, 0.25f, 0.6f, 1f);
        [SerializeField, Tooltip("Pulse alpha right before the throw-off; fades in from 0 at lowControl")] private float vignetteMaxAlpha = 0.55f;
        [SerializeField, Tooltip("Pulses per second at lowControl and right before the throw-off")] private Vector2 vignettePulseRate = new(1f, 3.5f);
        [SerializeField, Range(0f, 1f), Tooltip("Radius (0 = center, 1 = corner) where the edge starts to tint")] private float vignetteInner = 0.45f;
        [SerializeField, Tooltip("Real seconds the full flash takes to fade after a throw-off")] private float vignetteFlashTime = 0.6f;

        private readonly Vector3[] corners = new Vector3[8];
        private float vignetteFlash;
        private RectTransform canvasRect;
        private Canvas canvas;
        private Camera cam;
        private HostBody shownTarget;
        private SpellCaster shownCaster;
        private Tween targetPunch;
        private Tween devourSlide;
        private int shownDevourSlot = -1;

        // The aimed host on screen, null for a suggested one, and the top of its brackets in canvas units (EnemyHealthBars)
        public HostBody FramedHost { get; private set; }
        public Vector2 FrameTop { get; private set; }

        private void Start()
        {
            canvas = GetComponentInParent<Canvas>().rootCanvas;
            canvasRect = (RectTransform)canvas.transform;
            var action = InputSystem.actions.FindAction(leapActionName);
            possessHintText.SetArgs(action != null ? action.GetBindingDisplayString(group: "Keyboard&Mouse") : ""); // noloc
            // The attack has more than one key: name the mouse button, the first one listed
            devourText.SetKey("hud.hint.devour", KeyName("Player/Attack").Split(" | ")[0]);
            devourPickText.SetKey("hud.hint.devour.pick", KeyName("Player/Cast1"), KeyName("Player/Cast4"));

            if (vignette != null)
            {
                vignette.sprite = MakeVignetteSprite(vignetteInner);
                vignette.raycastTarget = false;
                vignette.color = Color.clear;
            }
            parasite.ThrownOff += OnThrownOff;
        }

        private void OnDestroy() => parasite.ThrownOff -= OnThrownOff;

        private void OnThrownOff() => vignetteFlash = 1f;

        private static string KeyName(string actionName)
        {
            var action = InputSystem.actions.FindAction(actionName);
            return action != null ? action.GetBindingDisplayString(group: "Keyboard&Mouse") : ""; // noloc
        }

        private void LateUpdate()
        {
            UpdateTarget();
            UpdateVignette();
            UpdateDevourHint();

            bool hosted = parasite.IsHosted && parasite.Host != null;
            resistanceRoot.SetActive(hosted);
            if (!hosted) return;

            resistanceFill.fillAmount = parasite.Host.Resistance01;
            bool flash = parasite.IsControlLow && Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
            resistanceFill.color = flash ? resistanceFlashColor : resistanceColor;
            if (resistanceLabel != null) resistanceLabel.color = flash ? resistanceFlashColor : resistanceColor;
        }

        private void UpdateDevourHint()
        {
            SpellThief thief = parasite.Thief;
            bool show = thief.DevourTarget != null;
            bool appeared = show && !devourHint.gameObject.activeSelf;
            devourHint.gameObject.SetActive(show);
            if (!show)
            {
                shownDevourSlot = -1;
                return;
            }

            if (appeared)
            {
                devourSlide.Stop();
                devourSlide = Tween.UIAnchoredPositionY(devourHint, devourHint.rect.height, 0f, devourSlideTime, Ease.OutCubic);
            }

            SpellCaster caster = thief.DevourCaster;
            bool newChoice = thief.ChosenSlot != shownDevourSlot;
            shownDevourSlot = thief.ChosenSlot;
            for (int i = 0; i < devourIcons.Length; i++)
            {
                SpellData spell = caster.GetSpell(i);
                SkillSlot slot = devourIcons[i];
                slot.gameObject.SetActive(spell != null);
                if (spell == null) continue;

                bool chosen = i == shownDevourSlot;
                slot.Show(spell.Icon, 0f, 0f, true, chosen);
                if (appeared) devourNames[i].SetKey(spell.NameKey);
                if (!newChoice) continue;
                slot.Select(chosen, devourChosenScale);
                devourNames[i].GetComponent<TMP_Text>().color = chosen ? devourChosenNameColor : devourNameColor;
            }
        }

        private void UpdateVignette()
        {
            if (vignette == null) return;
            // Unscaled: the throw-off's hitstop shouldn't hold the flash
            vignetteFlash = Mathf.MoveTowards(vignetteFlash, 0f, Time.unscaledDeltaTime / vignetteFlashTime);

            float danger = parasite.ControlDanger01;
            float alpha = 0f;
            if (danger > 0f)
            {
                float rate = Mathf.Lerp(vignettePulseRate.x, vignettePulseRate.y, danger);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate * 2f * Mathf.PI);
                alpha = vignetteMaxAlpha * danger * Mathf.Lerp(0.5f, 1f, pulse);
            }
            alpha = Mathf.Max(alpha, vignetteFlash);
            vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, vignetteColor.a * alpha);
        }

        private void UpdateTarget()
        {
            if (cam == null) cam = Camera.main;
            HostBody aimed = parasite.AimedHost;
            HostBody target = aimed != null ? aimed : parasite.SuggestedHost;
            bool suggested = aimed == null && target != null;
            crosshair.color = aimed != null ? targetColor : idleColor;

            Rect rect = default;
            bool onScreen = target != null && cam != null && TryGetCanvasRect(target.Bounds, out rect);
            targetFrame.gameObject.SetActive(onScreen);
            possessHint.gameObject.SetActive(onScreen && !suggested);
            FramedHost = onScreen && !suggested ? target : null;
            if (targetFrameGroup != null)
                targetFrameGroup.alpha = suggested
                    ? Mathf.Lerp(suggestedAlpha.x, suggestedAlpha.y, 0.5f + 0.5f * Mathf.Sin(Time.time * suggestedPulseRate * 2f * Mathf.PI))
                    : 1f;
            if (!onScreen)
            {
                shownTarget = null;
                return;
            }

            targetFrame.anchoredPosition = rect.center;
            targetFrame.sizeDelta = rect.size;
            possessHint.anchoredPosition = new Vector2(rect.center.x, rect.yMax + hintGap);
            FrameTop = new Vector2(rect.center.x, rect.yMax);

            bool newTarget = target != shownTarget;
            if (newTarget)
            {
                shownTarget = target;
                shownCaster = target.GetComponent<SpellCaster>();
                targetName.SetKey(target.NameKey);
                targetPunch.Stop();
                targetFrame.localScale = Vector3.one;
                targetPunch = Tween.PunchScale(targetFrame, Vector3.one * 0.2f, 0.2f);
            }
            if (!suggested) UpdateSpellIcons(newTarget);
        }

        private void UpdateSpellIcons(bool pop)
        {
            int shown = 0;
            for (int i = 0; i < spellIcons.Length; i++)
            {
                SpellData spell = shownCaster != null ? shownCaster.GetSpell(i) : null;
                SkillSlot slot = spellIcons[i];
                slot.gameObject.SetActive(spell != null);
                if (spell == null) continue;

                float remaining = shownCaster.CooldownRemaining(i);
                slot.Show(spell.Icon, remaining, spell.Cooldown, remaining <= 0f);
                if (pop) slot.PopIn(shown * spellPopStagger);
                shown++;
            }
            spellIconsRoot.SetActive(shown > 0);
        }

        // White, clear inside `inner`, fading to opaque at the corners. Stretched over the screen, so the falloff follows its aspect
        private static Sprite MakeVignetteSprite(float inner)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) / size * 2f - Vector2.one;
                float t = Mathf.InverseLerp(inner, 1f, p.magnitude / Mathf.Sqrt(2f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(t * t * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // Screen box of the bounds in canvas units (relative to the canvas center), false when the host is behind the camera
        private bool TryGetCanvasRect(Bounds bounds, out Rect rect)
        {
            rect = default;
            Vector3 min = bounds.min, max = bounds.max;
            for (int i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);

            Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector3 screen = cam.WorldToScreenPoint(corner);
                if (screen.z <= 0f) return false;
                lo = Vector2.Min(lo, screen);
                hi = Vector2.Max(hi, screen);
            }

            // Screen pixels to canvas units (CanvasScaler)
            float scale = canvas.scaleFactor;
            Vector2 half = canvasRect.rect.size * 0.5f;
            lo = lo / scale - half;
            hi = hi / scale - half;
            Vector2 size = Vector2.Max(hi - lo + Vector2.one * (framePadding * 2f), Vector2.one * minFrameSize);
            rect = new Rect((lo + hi) * 0.5f - size * 0.5f, size);
            return true;
        }
    }
}
