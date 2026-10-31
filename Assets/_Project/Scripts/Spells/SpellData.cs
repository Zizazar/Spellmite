using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    public enum SpellKind { Projectile, Self }

    // When an AI brain picks the spell
    public enum SpellAiUse { Attack, BuffInCombat, HealWhenHurt }

    // One spell. Listed on a body's SpellCaster; whoever controls the body (AI or parasite) casts it.
    [CreateAssetMenu(menuName = "Spellmite/Spell")]
    public class SpellData : ScriptableObject
    {
        [field: Header("Info")]
        [field: SerializeField, Tooltip("Localization key of the name")] public string NameKey { get; private set; }
        [field: SerializeField] public Sprite Icon { get; private set; }
        [field: SerializeField] public List<SpellTag> Tags { get; private set; } = new();

        [field: Header("Cast")]
        [field: SerializeField] public SpellKind Kind { get; private set; } = SpellKind.Projectile;
        [field: SerializeField] public float Cooldown { get; private set; } = 2f;
        [field: SerializeField, Tooltip("Windup before the spell fires. A stun interrupts it")] public float CastTime { get; private set; } = 0.4f;
        [field: SerializeField, Range(0f, 1f), Tooltip("Resistance a possessed body loses per cast")] public float ResistanceCost { get; private set; } = 0.1f;
        [field: SerializeField, Tooltip("Telegraph at the cast point during the windup (Magic/AuraCast, Magic/Enchant...)")] public GameObject CastVfx { get; private set; }
        [field: SerializeField, Tooltip("Applied to the caster when the spell fires")] public BuffData SelfBuff { get; private set; }

        [field: Header("Projectile")]
        [field: SerializeField] public float Damage { get; private set; } = 10f;
        [field: SerializeField] public float Speed { get; private set; } = 20f;
        [field: SerializeField, Tooltip("0 = flies straight")] public float Gravity { get; private set; }
        [field: SerializeField] public float Lifetime { get; private set; } = 3f;
        [field: SerializeField] public float Radius { get; private set; } = 0.2f;
        [field: SerializeField] public float Knockback { get; private set; } = 5f;
        [field: SerializeField, Min(1)] public int Count { get; private set; } = 1;
        [field: SerializeField, Tooltip("Fan width in degrees when Count > 1")] public float SpreadAngle { get; private set; } = 20f;
        [field: SerializeField, Tooltip("Degrees per second, 0 = no homing")] public float HomingTurnRate { get; private set; }
        [field: SerializeField] public float HomingRange { get; private set; } = 15f;
        [field: SerializeField, Tooltip("0 = only the body that was hit")] public float SplashRadius { get; private set; }
        [field: SerializeField, Tooltip("Applied to every body damaged")] public BuffData HitBuff { get; private set; }

        [field: Header("Projectile VFX (Retro Arsenal)")]
        [field: SerializeField, Tooltip("Combat/Missiles")] public GameObject ProjectileVfx { get; private set; }
        [field: SerializeField, Tooltip("Combat/Explosions")] public GameObject ImpactVfx { get; private set; }
        [field: SerializeField, Tooltip("Combat/Muzzleflash")] public GameObject MuzzleVfx { get; private set; }

        [field: Header("AI")]
        [field: SerializeField] public SpellAiUse AiUse { get; private set; } = SpellAiUse.Attack;
        [field: SerializeField] public float AiMinRange { get; private set; }
        [field: SerializeField] public float AiMaxRange { get; private set; } = 12f;
        [field: SerializeField, Range(0f, 1f), Tooltip("HealWhenHurt: cast below this health fraction")] public float AiHealthBelow { get; private set; } = 0.5f;
    }
}
