using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellmite
{
    // Third-person orbit camera that follows the parasite's active body, set up like Risk of Rain's:
    // - the pivot hangs above the body (profile CameraHeight), so the body sits low on screen and the crosshair stays clear;
    // - looking up pulls the camera in and looking down pushes it out, so it never digs into the floor;
    // - it follows the body rigidly; sprinting widens the FOV and slows the look;
    // - the player's casts kick it, damage to the player, explosions and the player's landed swings shake it;
    // - characters between it and the player's body dissolve (PSX Lit materials with Camera Fade).
    // Distance, height and FOV come from the body's profile and blend when switching bodies. Only the player turns it.
    // Wall avoidance is done by the CinemachineDeoccluder on the same camera.
    [DefaultExecutionOrder(-5)]
    public class BodyCamera : MonoBehaviour
    {
        private const float PivotRadius = 0.1f;
        // Rendering layer bit on the active body's renderers: PsxLit.shader doesn't dissolve them as occluders
        private const uint PlayerRenderingLayer = 1u << 8;
        private static readonly int OccluderFadeId = Shader.PropertyToID("_PsxOccluderFade");

        public static BodyCamera Instance { get; private set; }

        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("The CinemachineCamera tracks this transform")] private Transform anchor;
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private CinemachineOrbitalFollow orbit;
        [SerializeField, Tooltip("The pivot is cast up from the body against these, so a low ceiling lowers it")] private LayerMask ceilingMask = 1;

        [Header("Feel")]
        [SerializeField] private float transitionTime = 0.4f;
        [SerializeField] private float mouseSensitivity = 0.1f;
        [SerializeField, Tooltip("Degrees per second at full stick")] private float gamepadSensitivity = 180f;
        [SerializeField, Tooltip("Characters within this many meters of the line from the camera to the player dissolve. 0 = off")] private float occluderFadeRadius = 1f;

        [Header("Sprint")]
        [SerializeField] private float sprintFovMultiplier = 1.3f;
        [SerializeField, Tooltip("Seconds the FOV takes to widen and narrow back")] private float sprintFovSmoothTime = 0.2f;
        [SerializeField, Tooltip("Look speed while sprinting, on top of the FOV scaling")] private float sprintLookScale = 0.5f;

        [Header("Recoil")]
        [SerializeField, Tooltip("Seconds a cast's kick takes to spring back")] private float recoilReturnTime = 0.08f;
        [SerializeField, Tooltip("Seconds the view takes to follow the kick")] private float recoilFollowTime = 0.05f;

        [Header("Shake (strength = about meters of camera offset)")]
        [SerializeField, Tooltip("When the player's body loses a quarter of its health or more at once; smaller hits shake less")] private float damageShake = 0.4f;
        [SerializeField, Tooltip("Per meter of explosion radius, right at the camera")] private float explosionShake = 0.08f;
        [SerializeField, Tooltip("Explosions further than this from the camera don't shake it")] private float explosionShakeRange = 25f;
        [SerializeField, Tooltip("When the player's swing hits something")] private float meleeHitShake = 0.1f;
        [SerializeField] private float maxShake = 0.6f;
        [SerializeField, Tooltip("How fast a shake dies out")] private float shakeDamping = 8f;
        [SerializeField] private float shakeFrequency = 25f;

        private InputAction lookAction;
        private Transform currentBody;
        private Collider bodyCollider;
        private Health bodyHealth;
        private Vector3 fromPosition;
        private float fromDistance;
        private float fromFov;
        // Profile values blended between bodies, before the pitch and sprint scaling
        private float distance;
        private float baseFov;
        private float transition = 1f;
        // The player's look; the recoil is added on top of it, so the view springs back
        private float yaw;
        private float pitch;
        private float fovMultiplier = 1f;
        private float fovVelocity;
        private Vector2 recoil, recoilVelocity, targetRecoil, targetRecoilVelocity;
        private float shake;

        private void Awake()
        {
            Instance = this;
            lookAction = InputSystem.actions.FindAction("Player/Look", true);
            yaw = orbit.HorizontalAxis.Value;
            pitch = orbit.VerticalAxis.Value;
        }

        private void OnDestroy()
        {
            SetBodyHealth(null);
            Shader.SetGlobalVector(OccluderFadeId, Vector4.zero);
        }

        // The player's cast fired: the view kicks up about this many degrees (a bit sideways) and springs back
        public static void Kick(float degrees)
        {
            if (Instance == null || degrees <= 0f) return;
            Instance.targetRecoil += new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.5f, 1f)) * degrees;
        }

        // An explosion or a hit of this radius at a point: bigger and closer to the camera shakes more
        public static void ShakeExplosion(Vector3 point, float radius)
        {
            if (Instance == null) return;
            float falloff = 1f - Vector3.Distance(point, Instance.cinemachineCamera.transform.position) / Instance.explosionShakeRange;
            if (falloff > 0f) Instance.AddShake(Instance.explosionShake * radius * falloff);
        }

        public static void ShakeMeleeHit()
        {
            if (Instance != null) Instance.AddShake(Instance.meleeHitShake);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Transform body = parasite.ActiveBody;
            BodyProfile profile = parasite.ActiveProfile;

            if (body != currentBody)
            {
                // Start blending from wherever the camera is now
                fromPosition = anchor.position;
                fromDistance = distance;
                fromFov = baseFov;
                transition = currentBody == null ? 1f : 0f;
                MarkPlayer(currentBody, false);
                MarkPlayer(body, true);
                currentBody = body;
                bodyCollider = body.TryGetComponent(out CharacterController controller) ? controller : body.GetComponent<Collider>();
                SetBodyHealth(body.GetComponent<Health>());
            }
            // A disabled collider has empty bounds at the origin
            Vector3 focus = bodyCollider != null && bodyCollider.enabled ? bodyCollider.bounds.center : body.position;
            Shader.SetGlobalVector(OccluderFadeId, new Vector4(focus.x, focus.y, focus.z, occluderFadeRadius));

            transition = Mathf.Min(1f, transition + dt / transitionTime);
            float k = Mathf.SmoothStep(0f, 1f, transition);
            distance = Mathf.Lerp(fromDistance, profile.CameraDistance, k);
            baseFov = Mathf.Lerp(fromFov, profile.CameraFov, k);

            bool sprinting = parasite.IsSprinting;
            fovMultiplier = Mathf.SmoothDamp(fovMultiplier, sprinting ? sprintFovMultiplier : 1f, ref fovVelocity, sprintFovSmoothTime);
            targetRecoil = Vector2.SmoothDamp(targetRecoil, Vector2.zero, ref targetRecoilVelocity, recoilReturnTime);
            recoil = Vector2.SmoothDamp(recoil, targetRecoil, ref recoilVelocity, recoilFollowTime);
            UpdateLook(dt, sprinting);

            // Pitch > 0 looks down. x0.71 of the distance level, x0.33 at -70 (close and low, but above the floor), x0.94 at +70
            float pitchScale = Mathf.Sqrt((1f + orbit.VerticalAxis.Value / 90f) * 0.5f);
            anchor.position = Vector3.Lerp(fromPosition, Pivot(body, profile.CameraHeight), k) + ShakeOffset(dt);
            orbit.Radius = Mathf.Max(0.25f, distance * pitchScale);
            cinemachineCamera.Lens.FieldOfView = baseFov * fovMultiplier;
        }

        private void UpdateLook(float dt, bool sprinting)
        {
            Vector2 look = lookAction.ReadValue<Vector2>();
            bool gamepad = lookAction.activeControl?.device is Gamepad;
            if (gamepad || Cursor.lockState == CursorLockMode.Locked)
            {
                // A wider FOV turns faster, sprinting turns slower
                look *= (gamepad ? gamepadSensitivity * dt : mouseSensitivity) * fovMultiplier * (sprinting ? sprintLookScale : 1f);
                yaw = Mathf.Repeat(yaw + look.x + 180f, 360f) - 180f;
                pitch -= look.y;
            }

            Vector2 range = orbit.VerticalAxis.Range;
            pitch = Mathf.Clamp(pitch, range.x, range.y);
            orbit.HorizontalAxis.Value = Mathf.Repeat(yaw + recoil.x + 180f, 360f) - 180f;
            orbit.VerticalAxis.Value = Mathf.Clamp(pitch - recoil.y, range.x, range.y);
        }

        // Cast up from the body, so under a low ceiling the pivot drops instead of going through it
        private Vector3 Pivot(Transform body, float height)
        {
            Vector3 origin = body.position;
            if (Physics.SphereCast(origin, PivotRadius, Vector3.up, out RaycastHit hit, height, ceilingMask, QueryTriggerInteraction.Ignore))
                height = hit.distance;
            return origin + Vector3.up * height;
        }

        // The anchor is both what the camera follows and looks at, so this moves the camera without turning it
        private Vector3 ShakeOffset(float dt)
        {
            shake *= Mathf.Exp(-shakeDamping * dt);
            float t = Time.time * shakeFrequency;
            var noise = new Vector3(Mathf.PerlinNoise(t, 0.37f), Mathf.PerlinNoise(0.71f, t), Mathf.PerlinNoise(t + 3.1f, 5.9f));
            return (noise * 2f - Vector3.one) * shake;
        }

        private static void MarkPlayer(Transform body, bool on)
        {
            if (body == null) return;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                r.renderingLayerMask = on ? r.renderingLayerMask | PlayerRenderingLayer : r.renderingLayerMask & ~PlayerRenderingLayer;
        }

        private void AddShake(float amount) => shake = Mathf.Min(maxShake, Mathf.Max(shake, amount));

        private void SetBodyHealth(Health health)
        {
            if (bodyHealth != null) bodyHealth.Damaged -= OnBodyDamaged;
            bodyHealth = health;
            if (bodyHealth != null) bodyHealth.Damaged += OnBodyDamaged;
        }

        private void OnBodyDamaged(float amount, Vector3 impulse)
        {
            AddShake(damageShake * Mathf.Clamp(4f * amount / Mathf.Max(1f, bodyHealth.Max), 0.25f, 1f));
        }
    }
}
