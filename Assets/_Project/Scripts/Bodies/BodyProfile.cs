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
        [field: SerializeField] public float CameraDistance { get; private set; } = 4f;
        [field: SerializeField] public float CameraHeight { get; private set; } = 1.5f;
        [field: SerializeField] public float CameraFov { get; private set; } = 60f;
        [field: SerializeField, Tooltip("Extra distance at sprint speed")] public float CameraSpeedDistance { get; private set; } = 0.5f;
        [field: SerializeField, Tooltip("Extra FOV at sprint speed")] public float CameraSpeedFov { get; private set; } = 6f;

        public float JumpSpeed => Mathf.Sqrt(2f * Gravity * JumpHeight);
    }
}
