using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Spellmite
{
    // Risk of Rain style damage numbers: every hit the player lands pops a number out of the body. It flies up and to a side,
    // falls back and fades. Bigger hits give bigger numbers; damage over time ticks are smaller and in their own color.
    // Sits under its own Canvas in the HUD and runs after the camera, like EnemyHealthBars. Unscaled time, so hitstop doesn't hold them.
    [DefaultExecutionOrder(101)]
    public class DamageNumbers : MonoBehaviour
    {
        private class Number
        {
            public TextMeshProUGUI Text;
            public RectTransform Rect;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Size;
        }

        [SerializeField, Tooltip("Inactive child, copied for every number")] private TextMeshProUGUI template;
        [SerializeField] private Color hitColor = Color.white;
        [SerializeField, Tooltip("Ticks of burning, poison and other buffs")] private Color tickColor = new(1f, 0.6f, 0.25f);
        [SerializeField] private float lifetime = 0.9f;
        [SerializeField, Tooltip("Upward launch speed, world units per second")] private float launchUp = 4.5f;
        [SerializeField, Tooltip("Sideways launch speed along the screen, random up to this")] private float launchSide = 1.8f;
        [SerializeField] private float gravity = 10f;
        [SerializeField, Tooltip("Scale of the smallest and the biggest hit")] private Vector2 sizeRange = new(0.8f, 1.8f);
        [SerializeField, Tooltip("Damage of those two sizes")] private Vector2 damageRange = new(5f, 60f);
        [SerializeField, Range(0f, 1f), Tooltip("Ticks are this much of a hit's size")] private float tickSize = 0.7f;
        [SerializeField, Tooltip("Numbers keep their size up to this distance, then shrink with it")] private float fullSizeDistance = 6f;
        [SerializeField, Range(0f, 1f)] private float minDistanceScale = 0.45f;
        [SerializeField, Tooltip("A number appears this much bigger and settles in Pop Time")] private float popScale = 1.6f;
        [SerializeField] private float popTime = 0.12f;
        [SerializeField, Range(0f, 1f), Tooltip("Part of the lifetime, at the end, spent fading and shrinking")] private float fadePart = 0.35f;
        [SerializeField, Tooltip("Past this many the oldest goes")] private int maxNumbers = 40;

        private readonly List<Number> numbers = new();
        private readonly Stack<Number> pool = new();
        private Canvas canvas;
        private RectTransform canvasRect;
        private Camera cam;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>().rootCanvas;
            canvasRect = (RectTransform)canvas.transform;
            template.gameObject.SetActive(false);
        }

        private void OnEnable() => Health.AnyHurtByPlayer += OnHurt;
        private void OnDisable() => Health.AnyHurtByPlayer -= OnHurt;

        private void OnHurt(Health target, float amount, bool overTime)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            if (numbers.Count >= maxNumbers) Recycle(0);

            Number number = pool.Count > 0 ? pool.Pop() : Create();
            Bounds bounds = BoundsOf(target);
            Vector3 extents = bounds.extents;
            number.Position = bounds.center + new Vector3(
                Random.Range(-0.4f, 0.4f) * extents.x,
                Random.Range(0.2f, 0.6f) * extents.y,
                Random.Range(-0.4f, 0.4f) * extents.z);
            number.Velocity = Vector3.up * launchUp + cam.transform.right * Random.Range(-launchSide, launchSide);
            number.Age = 0f;
            number.Size = Mathf.Lerp(sizeRange.x, sizeRange.y, Mathf.InverseLerp(damageRange.x, damageRange.y, amount)) * (overTime ? tickSize : 1f);
            number.Text.color = overTime ? tickColor : hitColor;
            number.Text.SetText("{0:0}", Mathf.Max(1f, Mathf.Round(amount))); // noloc
            number.Text.gameObject.SetActive(true);
            Place(number);
            numbers.Add(number);
        }

        private void LateUpdate()
        {
            if (numbers.Count == 0) return;
            if (cam == null) cam = Camera.main;
            float dt = Time.unscaledDeltaTime;

            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                Number number = numbers[i];
                number.Age += dt;
                if (number.Age >= lifetime || cam == null)
                {
                    Recycle(i);
                    continue;
                }
                number.Velocity += Vector3.down * (gravity * dt);
                number.Position += number.Velocity * dt;
                Place(number);
            }
        }

        private void Place(Number number)
        {
            Vector3 screen = cam.WorldToScreenPoint(number.Position);
            bool visible = screen.z > 0f;
            number.Text.enabled = visible;
            if (!visible) return;

            // Screen pixels to canvas units, relative to the canvas center
            number.Rect.anchoredPosition = (Vector2)screen / canvas.scaleFactor - canvasRect.rect.size * 0.5f;

            float t = number.Age;
            float pop = t < popTime ? Mathf.Lerp(popScale, 1f, 1f - (1f - t / popTime) * (1f - t / popTime)) : 1f;
            float fade = Mathf.InverseLerp(lifetime * (1f - fadePart), lifetime, t);
            float distance = Mathf.Clamp(fullSizeDistance / Mathf.Max(0.01f, screen.z), minDistanceScale, 1f);
            number.Rect.localScale = Vector3.one * (number.Size * distance * pop * (1f - fade * 0.5f));
            Color color = number.Text.color;
            color.a = 1f - fade;
            number.Text.color = color;
        }

        private Number Create()
        {
            TextMeshProUGUI text = Instantiate(template, transform);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return new Number { Text = text, Rect = rect };
        }

        private void Recycle(int index)
        {
            Number number = numbers[index];
            numbers.RemoveAt(index);
            number.Text.gameObject.SetActive(false);
            pool.Push(number);
        }

        private static Bounds BoundsOf(Health target)
        {
            if (target.TryGetComponent(out HostBody host)) return host.Bounds;
            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds : new Bounds(target.transform.position, Vector3.one);
        }
    }
}
