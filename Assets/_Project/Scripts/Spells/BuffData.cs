using UnityEngine;

namespace Spellmite
{
    // A timed effect on a body: slow, freeze, burn, regeneration, shield...
    // Applying a buff the body already has refreshes its duration.
    [CreateAssetMenu(menuName = "Spellmite/Buff")]
    public class BuffData : ScriptableObject
    {
        [field: Header("Info")]
        [field: SerializeField, Tooltip("Localization key of the name")] public string NameKey { get; private set; }
        [field: SerializeField] public Sprite Icon { get; private set; }

        [field: Header("Effect")]
        [field: SerializeField] public float Duration { get; private set; } = 3f;
        [field: SerializeField, Tooltip("Multiplies move speed. 0 = rooted")] public float SpeedMultiplier { get; private set; } = 1f;
        [field: SerializeField, Tooltip("Multiplies incoming damage. 0.5 = shield, 2 = vulnerable")] public float DamageTakenMultiplier { get; private set; } = 1f;
        [field: SerializeField, Tooltip("Can't move, attack or cast")] public bool Stun { get; private set; }
        [field: SerializeField, Tooltip("Positive heals, negative damages. Applied every Tick Interval")] public float HealthPerTick { get; private set; }
        [field: SerializeField, Min(0.05f)] public float TickInterval { get; private set; } = 0.5f;

        [field: Header("VFX (Retro Arsenal)")]
        [field: SerializeField, Tooltip("Attached to the body while active (Magic/Aura, Regenerate, Stun...)")] public GameObject Vfx { get; private set; }
        [field: SerializeField, Tooltip("Local position on the body")] public Vector3 VfxOffset { get; private set; }
    }
}
