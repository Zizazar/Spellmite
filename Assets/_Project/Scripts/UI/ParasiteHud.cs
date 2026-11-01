using GameJam.Localization;
using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Spellmite
{
    // Crosshair that turns magenta over a host you can leap to, brackets around that host on screen
    // with its name and the possess key above them, and the current host's control bar
    // (time left before it throws the parasite off).
    // Control running out: a magenta edge vignette pulses, harder towards zero, and flashes when the host throws the parasite off;
    // when critical the next host to leap to gets faded, pulsing brackets (no name) even if it isn't aimed at.
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
        private Tween targetPunch;

        private void Start()
        {
            canvas = GetComponentInParent<Canvas>().rootCanvas;
            canvasRect = (RectTransform)canvas.transform;
            var action = InputSystem.actions.FindAction(leapActionName);
            possessHintText.SetArgs(action != null ? action.GetBindingDisplayString(group: "Keyboard&Mouse") : ""); // noloc

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

        private void LateUpdate()
        {
            UpdateTarget();
            UpdateVignette();

            bool hosted = parasite.IsHosted && parasite.Host != null;
            resistanceRoot.SetActive(hosted);
            if (!hosted) return;

            resistanceFill.fillAmount = parasite.Host.Resistance01;
            bool flash = parasite.IsControlLow && Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
            resistanceFill.color = flash ? resistanceFlashColor : resistanceColor;
            if (resistanceLabel != null) resistanceLabel.color = flash ? resistanceFlashColor : resistanceColor;
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

            if (target == shownTarget) return;
            shownTarget = target;
            targetName.SetKey(target.NameKey);
            targetPunch.Stop();
            targetFrame.localScale = Vector3.one;
            targetPunch = Tween.PunchScale(targetFrame, Vector3.one * 0.2f, 0.2f);
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
