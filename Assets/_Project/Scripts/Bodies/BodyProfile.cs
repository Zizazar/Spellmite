using UnityEngine;

namespace Spellmite
{
    // Movement and camera feel of one body type (parasite, brute, grunt...).
    [CreateAssetMenu(menuName = "Spellmite/Body Profile")]
    public class BodyProfile : ScriptableObject
    {
        [field: Header("Movement")]
        [field: SerializeField] public float WalkSpeed { get; private set; } = 4f;
        [field: SerializeField] public float SprintSpeed { get; private set; } = 7f;
        [field: SerializeField] public float Acceleration { get; private set; } = 30f;
        [field: SerializeField] public float Deceleration { get; private set; } = 40f;
        [field: SerializeField, Range(0f, 1f)] public float AirControl { get; private set; } = 0.3f;
        [field: SerializeField, Tooltip("Degrees per second")] public float TurnSpeed { get; private set; } = 540f;

        [field: Header("Jump & weight")]
        [field: SerializeField] public float JumpHeight { get; private set; } = 1.2f;
        [field: SerializeField] public float Gravity { get; private set; } = 25f;
        [field: SerializeField, Tooltip("Heavier bodies get pushed less by hits")] public float Mass { get; private set; } = 1f;

        [field: Header("Camera")]
        [field: SerializeField, Tooltip("Nominal distance. Looking level the camera is at ~0.7 of it, closer looking up, further looking down")] public float CameraDistance { get; private set; } = 6f;
        [field: SerializeField, Tooltip("Height of the camera pivot above the body's origin. Well above the head keeps the crosshair clear")] public float CameraHeight { get; private set; } = 3f;
        [field: SerializeField, Tooltip("x1.3 while sprinting")] public float CameraFov { get; private set; } = 60f;

        public float JumpSpeed => Mathf.Sqrt(2f * Gravity * JumpHeight);
    }
}
