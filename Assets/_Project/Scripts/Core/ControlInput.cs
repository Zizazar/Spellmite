using UnityEngine;

namespace Spellmite
{
    // What a brain (player input or AI) wants a body to do this frame.
    public struct ControlInput
    {
        public Vector3 Move;        // world space, magnitude 0..1
        public Vector3 Aim;         // world-space aim direction (camera forward for the player)
        public Vector3 AimOrigin;   // where the aim ray starts (camera position for the player)
        public bool Sprint;
        public bool JumpPressed;
        public bool AttackPressed;
        public bool LeapPressed;    // parasite: leap at target / leap out of the host
    }
}
