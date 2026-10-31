using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Spellmite
{
    // Tells every enemy whether the player sees it: inside the camera frame (minus a margin) and not behind a wall.
    // One pass over all enemies a few times a second instead of raycasts in every brain. The camera follows the
    // player's current body, so after a possession the check runs from the new body by itself.
    public class PlayerViewTracker : MonoBehaviour
    {
        [SerializeField] private float interval = 0.1f;
        [SerializeField, Tooltip("What blocks the view. Bodies never do")] private LayerMask wallMask = 1;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
        private Camera viewCamera;

        private void Start()
        {
            viewCamera = Camera.main;
            Track(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid Track(CancellationToken token)
        {
            while (true)
            {
                float margin = AttackDirector.Instance.Settings.ScreenMargin;
                foreach (var enemy in EnemyBrain.All)
                {
                    float offset = 1f;
                    bool inView = enemy.IsThinking && IsInView(enemy.Center, margin, out offset);
                    enemy.SetView(inView, offset);
                }
                await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: token);
            }
        }

        // offset: distance from the screen center in viewport units
        private bool IsInView(Vector3 point, float margin, out float offset)
        {
            Vector3 viewport = viewCamera.WorldToViewportPoint(point);
            offset = Vector2.Distance(viewport, new Vector2(0.5f, 0.5f));
            if (viewport.z <= 0f) return false;
            if (viewport.x < margin || viewport.x > 1f - margin || viewport.y < margin || viewport.y > 1f - margin) return false;
            return !IsBlocked(viewCamera.transform.position, point);
        }

        private bool IsBlocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / distance, hitBuffer, distance, wallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hitBuffer[i].collider.GetComponentInParent<Health>() == null) return true;
            return false;
        }
    }
}
