using UnityEngine;

namespace Spellmite
{
    // Two sides: the player (the parasite and the body it controls) and the free enemies.
    public static class Teams
    {
        public static bool IsPlayerSide(Component component)
        {
            if (component.GetComponentInParent<ParasiteBody>() != null) return true;
            var host = component.GetComponentInParent<HostBody>();
            return host != null && host.IsPossessed;
        }
    }
}
