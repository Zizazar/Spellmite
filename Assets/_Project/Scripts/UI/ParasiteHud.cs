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
        [SerializeField, Range(0f, 1f), Tooltip("Below this the bar flashes: the host is about to throw the parasite off")] private float lowResistance = 0.25f;
        [SerializeField, Tooltip("Flashes per second when low")] private float flashRate = 4f;

        private readonly Vector3[] corners = new Vector3[8];
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
        }

        private void LateUpdate()
        {
            UpdateTarget();

            bool hosted = parasite.IsHosted && parasite.Host != null;
            resistanceRoot.SetActive(hosted);
            if (!hosted) return;

            float resistance = parasite.Host.Resistance01;
            resistanceFill.fillAmount = resistance;
            bool flash = resistance <= lowResistance && Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
            resistanceFill.color = flash ? resistanceFlashColor : resistanceColor;
            if (resistanceLabel != null) resistanceLabel.color = flash ? resistanceFlashColor : resistanceColor;
        }

        private void UpdateTarget()
        {
            if (cam == null) cam = Camera.main;
            HostBody target = parasite.AimedHost;
            crosshair.color = target != null ? targetColor : idleColor;

            Rect rect = default;
            bool onScreen = target != null && cam != null && TryGetCanvasRect(target.Bounds, out rect);
            targetFrame.gameObject.SetActive(onScreen);
            possessHint.gameObject.SetActive(onScreen);
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
