using UnityEngine;

namespace Spellmite
{
    // The knobs of enemy fairness and pressure. One asset per difficulty / wave, assigned to the AttackDirector.
    [CreateAssetMenu(menuName = "Spellmite/Difficulty Settings")]
    public class DifficultySettings : ScriptableObject
    {
        [field: Header("Attack tokens")]
        [field: SerializeField, Min(1), Tooltip("How many enemies may attack at once")] public int MaxTokens { get; private set; } = 2;
        [field: SerializeField, Tooltip("A token stays taken this long after the cast, so attacks come in waves")] public float TokenHoldTime { get; private set; } = 0.5f;
        [field: SerializeField, Tooltip("An enemy that just attacked waits this long before asking again")] public float EnemyTokenCooldown { get; private set; } = 1.5f;

        [field: Header("Fairness")]
        [field: SerializeField, Tooltip("Seconds an enemy must be on screen before it attacks")] public float ReactionDelay { get; private set; } = 0.6f;
        [field: SerializeField, Range(0f, 0.4f), Tooltip("Enemies closer to the screen edge than this (fraction of the screen) don't count as seen")] public float ScreenMargin { get; private set; } = 0.08f;
        [field: SerializeField, Tooltip("Still counts as seen this long after leaving the screen, so it doesn't flicker at the edge")] public float SeenMemory { get; private set; } = 0.4f;
        [field: SerializeField, Tooltip("No enemy attacks for this long after the player takes a new body")] public float PossessionGrace { get; private set; } = 1.5f;

        [field: Header("Stealth (tower waves)")]
        [field: SerializeField, Tooltip("Before the alarm, enemies notice a parasite crawling in the open within this distance. Keep it under the leap range, so the first move stays the player's")] public float NoticeRange { get; private set; } = 8f;
        [field: SerializeField, Tooltip("The alarm goes up by itself this many seconds into a wave. 0 = never")] public float AlarmTimeout { get; private set; } = 20f;

        [field: Header("Telegraph")]
        [field: SerializeField, Tooltip("Enemy spell windup = spell CastTime × this")] public float EnemyCastTimeScale { get; private set; } = 1.5f;
    }
}
