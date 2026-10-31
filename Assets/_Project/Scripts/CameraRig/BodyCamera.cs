using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellmite
{
    // Third-person orbit camera that follows the parasite's active body. Distance, height and FOV come from the
    // body's profile (plus a bit with speed) and blend smoothly when switching bodies. Only the player turns it.
    // Wall avoidance is done by the CinemachineDeoccluder on the same camera.
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
        [SerializeField, Tooltip("How fast the camera follows the body up and down; lower ignores jumps and bumps more")] private float verticalSharpness = 8f;

        private InputAction lookAction;
        private Transform currentBody;
        private Vector3 fromPosition;
        private float fromDistance;
        private float fromFov;
        private float transition = 1f;
        private float speed01;
        private float followY;

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
                if (currentBody == null) followY = body.position.y;
                currentBody = body;
            }

            float speed = parasite.ActiveVelocity.magnitude;
            float targetSpeed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, profile.SprintSpeed));
            speed01 = Mathf.Lerp(speed01, targetSpeed01, 1f - Mathf.Exp(-speedSmoothing * dt));

            transition = Mathf.Min(1f, transition + dt / transitionTime);
            float k = Mathf.SmoothStep(0f, 1f, transition);

            // Height follows with a lag, so jumps, wall scrambles and uneven floors don't shake the view
            followY = Mathf.Lerp(followY, body.position.y, 1f - Mathf.Exp(-verticalSharpness * dt));
            Vector3 targetPosition = new Vector3(body.position.x, followY + profile.CameraHeight, body.position.z);
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

            look *= gamepad ? gamepadSensitivity * dt : mouseSensitivity;
            orbit.HorizontalAxis.Value += look.x;
            if (orbit.HorizontalAxis.Wrap) orbit.HorizontalAxis.Value = Mathf.Repeat(orbit.HorizontalAxis.Value + 180f, 360f) - 180f;

            Vector2 range = orbit.VerticalAxis.Range;
            orbit.VerticalAxis.Value = Mathf.Clamp(orbit.VerticalAxis.Value - look.y, range.x, range.y);
        }
    }
}
