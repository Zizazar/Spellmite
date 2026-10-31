using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellmite
{
    // Third-person orbit camera that follows the parasite's active body. Distance, height and FOV come from the
    // body's profile (plus a bit with speed) and blend smoothly when switching bodies. Wall avoidance is done by
    // the CinemachineDeoccluder on the same camera.
    [DefaultExecutionOrder(-5)]
    public class BodyCamera : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("The CinemachineCamera tracks this transform")] private Transform anchor;
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private CinemachineOrbitalFollow orbit;

        [Header("Feel")]
        [SerializeField] private float transitionTime = 0.4f;
        [SerializeField] private float speedSmoothing = 3f;
        [SerializeField] private float mouseSensitivity = 0.1f;
        [SerializeField, Tooltip("Degrees per second at full stick")] private float gamepadSensitivity = 180f;
        [SerializeField, Tooltip("How fast the height offset turns to follow the surface the parasite crawls on")] private float surfaceUpSharpness = 6f;

        [Header("Auto-center")]
        [SerializeField, Tooltip("Seconds without look input before the camera swings behind the body")] private float recenterDelay = 0.5f;
        [SerializeField, Tooltip("Swing speed at full sprint")] private float recenterSharpness = 3f;
        [SerializeField] private float recenterMinSpeed = 0.5f;
        [SerializeField, Tooltip("Don't swing around when moving towards the camera")] private float recenterMaxAngle = 135f;

        private InputAction lookAction;
        private Transform currentBody;
        private Vector3 fromPosition;
        private float fromDistance;
        private float fromFov;
        private float transition = 1f;
        private float speed01;
        private Vector3 cameraUp = Vector3.up;
        private float lastLookTime;

        private void Awake()
        {
            lookAction = InputSystem.actions.FindAction("Player/Look", true);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateLook(dt);

            Transform body = parasite.ActiveBody;
            BodyProfile profile = parasite.ActiveProfile;

            if (body != currentBody)
            {
                // Start blending from wherever the camera is now
                fromPosition = anchor.position;
                fromDistance = orbit.Radius;
                fromFov = cinemachineCamera.Lens.FieldOfView;
                transition = currentBody == null ? 1f : 0f;
                currentBody = body;
            }

            float speed = parasite.ActiveVelocity.magnitude;
            float targetSpeed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, profile.SprintSpeed));
            speed01 = Mathf.Lerp(speed01, targetSpeed01, 1f - Mathf.Exp(-speedSmoothing * dt));

            transition = Mathf.Min(1f, transition + dt / transitionTime);
            float k = Mathf.SmoothStep(0f, 1f, transition);

            // Offset along the surface normal (off walls, below ceilings), turned smoothly so edges don't jerk
            Vector3 targetUp = parasite.IsOnSurface ? parasite.SurfaceUp : Vector3.up;
            cameraUp = Vector3.Slerp(cameraUp, targetUp, 1f - Mathf.Exp(-surfaceUpSharpness * dt)).normalized;
            Recenter(body, speed, dt);

            Vector3 targetPosition = body.position + cameraUp * profile.CameraHeight;
            float distance = profile.CameraDistance + profile.CameraSpeedDistance * speed01;
            float fov = profile.CameraFov + profile.CameraSpeedFov * speed01;

            anchor.position = Vector3.Lerp(fromPosition, targetPosition, k);
            orbit.Radius = Mathf.Lerp(fromDistance, distance, k);
            cinemachineCamera.Lens.FieldOfView = Mathf.Lerp(fromFov, fov, k);
        }

        private void UpdateLook(float dt)
        {
            Vector2 look = lookAction.ReadValue<Vector2>();
            bool gamepad = lookAction.activeControl?.device is Gamepad;
            if (!gamepad && Cursor.lockState != CursorLockMode.Locked) return;

            if (look.sqrMagnitude > (gamepad ? 0.01f : 1f)) lastLookTime = Time.time;
            look *= gamepad ? gamepadSensitivity * dt : mouseSensitivity;
            orbit.HorizontalAxis.Value += look.x;
            if (orbit.HorizontalAxis.Wrap) orbit.HorizontalAxis.Value = Mathf.Repeat(orbit.HorizontalAxis.Value + 180f, 360f) - 180f;

            Vector2 range = orbit.VerticalAxis.Range;
            orbit.VerticalAxis.Value = Mathf.Clamp(orbit.VerticalAxis.Value - look.y, range.x, range.y);
        }

        // Swing behind the body while it walks and the player isn't steering the camera
        private void Recenter(Transform body, float speed, float dt)
        {
            if (Time.time - lastLookTime < recenterDelay || speed < recenterMinSpeed) return;

            bool grounded = parasite.IsHosted ? parasite.Host != null && parasite.Host.IsGrounded : parasite.IsOnSurface;
            if (!grounded) return;

            // On a wall, also face into it (forward alone points straight up while climbing)
            Vector3 heading = Vector3.ProjectOnPlane(body.forward, Vector3.up);
            if (parasite.IsOnSurface) heading += Vector3.ProjectOnPlane(-parasite.SurfaceUp, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) return;

            float targetYaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(orbit.HorizontalAxis.Value, targetYaw);
            if (Mathf.Abs(delta) > recenterMaxAngle) return;

            orbit.HorizontalAxis.Value += delta * (1f - Mathf.Exp(-recenterSharpness * speed01 * dt));
            if (orbit.HorizontalAxis.Wrap) orbit.HorizontalAxis.Value = Mathf.Repeat(orbit.HorizontalAxis.Value + 180f, 360f) - 180f;
        }
    }
}
