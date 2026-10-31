#if UNITY_EDITOR
namespace Spellmite
{
    // Editor-only playtest cheats, toggled in Tools → Spellmite → Cheats. This class and every check of it sit in
    // #if UNITY_EDITOR, so builds never contain them.
    public static class Cheats
    {
        // No wave spawns (a running wave pauses), the lift goes up even with enemies left
        public static bool NoWaves;
        // Free enemies stand still and do nothing
        public static bool NoEnemyAI;
        // The parasite and the host it controls take no damage
        public static bool GodMode;
    }
}
#endif
