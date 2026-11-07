using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // Risk of Rain style health bars over enemies: one comes up when an enemy is hurt, stays a few seconds after the last hit
    // and for as long as the crosshair is on the enemy. A possessed body has its health in the HUD, the boss has BossBar.
    // Runs after ParasiteHud, which runs after the camera, so the bars don't lag a frame behind the enemies.
    // Sits under its own Canvas: bars move every frame, and this keeps the rest of the HUD from being rebuilt.
    [DefaultExecutionOrder(101)]
    public class EnemyHealthBars : MonoBehaviour
    {
        [SerializeField] private ParasiteHud hud;
        [SerializeField, Tooltip("Instantiated under this object when needed, then reused")] private EnemyHealthBar barPrefab;
        [SerializeField, Tooltip("Seconds a bar stays after the last hit")] private float hitDuration = 5f;
        [SerializeField, Tooltip("Seconds a bar stays after the crosshair leaves the enemy")] private float aimDuration = 1f;
        [SerializeField, Tooltip("Canvas units from the top of the enemy to the bar")] private float headGap = 10f;
        [SerializeField, Tooltip("Canvas units from the aimed enemy's brackets to the bar. It goes under the possess hint: keep ParasiteHud's Hint Gap above this + the bar's height")] private float frameGap = 4f;

        private readonly List<EnemyHealthBar> bars = new();
        private readonly Stack<EnemyHealthBar> pool = new();
        private Canvas canvas;
        private RectTransform canvasRect;
        private Camera cam;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>().rootCanvas;
            canvasRect = (RectTransform)canvas.transform;
        }

        private void OnEnable() => HostBody.AnyDamaged += OnHostDamaged;
        private void OnDisable() => HostBody.AnyDamaged -= OnHostDamaged;

        private void OnHostDamaged(HostBody host, float amount)
        {
            if (host.IsPossessed) return;
            EnemyHealthBar bar = GetBar(host, amount);
            bar.KeepUntil(Time.time + hitDuration);
            bar.Hit(amount);
        }

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            HostBody aimed = hud.FramedHost;
            if (aimed != null) GetBar(aimed, 0f).KeepUntil(Time.time + aimDuration);

            for (int i = bars.Count - 1; i >= 0; i--)
            {
                EnemyHealthBar bar = bars[i];
                bar.Tick();
                if (bar.Finished)
                {
                    bar.Hide();
                    bars.RemoveAt(i);
                    pool.Push(bar);
                }
                else if (cam != null)
                {
                    Place(bar, aimed);
                }
            }
        }

        private EnemyHealthBar GetBar(HostBody host, float justLost)
        {
            foreach (EnemyHealthBar shown in bars)
                if (shown.Host == host) return shown;

            EnemyHealthBar bar = pool.Count > 0 ? pool.Pop() : Instantiate(barPrefab, transform);
            bar.Show(host, justLost);
            bars.Add(bar);
            return bar;
        }

        private void Place(EnemyHealthBar bar, HostBody aimed)
        {
            HostBody host = bar.Host;
            if (host != null && host.isActiveAndEnabled)
            {
                Bounds bounds = host.Bounds;
                bar.Anchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }

            Vector3 screen = cam.WorldToScreenPoint(bar.Anchor);
            // Screen pixels to canvas units, relative to the canvas center
            Vector2 position = (Vector2)screen / canvas.scaleFactor - canvasRect.rect.size * 0.5f + Vector2.up * headGap;
            // The aimed enemy's bar steps up over the top of its brackets, between them and the possess hint
            float lift = host != null && host == aimed ? Mathf.Max(0f, hud.FrameTop.y + frameGap - position.y) : 0f;
            bar.Follow(position, lift, screen.z > 0f);
        }
    }
}
