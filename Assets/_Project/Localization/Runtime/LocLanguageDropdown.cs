using System.Linq;
using TMPro;
using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>Fills a TMP_Dropdown with the available languages (by their <c>@name</c>) and switches <see cref="Loc.Language"/>.</summary>
    [AddComponentMenu("UI/Loc Language Dropdown")]
    [RequireComponent(typeof(TMP_Dropdown))]
    public class LocLanguageDropdown : MonoBehaviour
    {
        TMP_Dropdown dropdown;

        void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
            dropdown.onValueChanged.AddListener(i => Loc.Language = Loc.Languages[i]);
        }

        void OnEnable()
        {
            Loc.Changed += Fill;
            Fill();
        }

        void OnDisable() => Loc.Changed -= Fill;

        void Fill()
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(Loc.Languages.Select(Loc.DisplayName).ToList());
            dropdown.SetValueWithoutNotify(Mathf.Max(0, Loc.Languages.ToList().IndexOf(Loc.Language)));
        }
    }
}
