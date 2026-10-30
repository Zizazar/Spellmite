using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>Marks text on this object as intentionally not localized (names, debug labels), so the audit skips it.</summary>
    [AddComponentMenu("UI/Loc Ignore")]
    [DisallowMultipleComponent]
    public class LocIgnore : MonoBehaviour
    {
    }
}
