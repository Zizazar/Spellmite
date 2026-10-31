using UnityEngine;

namespace Spellmite
{
    // Label for grouping and filtering spells in the Spellbook window (Fire, Ice, Ranged...). The asset name is the tag name.
    [CreateAssetMenu(menuName = "Spellmite/Spell Tag")]
    public class SpellTag : ScriptableObject
    {
        [field: SerializeField] public Color Color { get; private set; } = new(0.5f, 0.5f, 0.5f);
    }
}
