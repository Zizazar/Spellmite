using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.Localization
{
    /// <summary>
    /// Puts the translation of <see cref="Key"/> into the TextMeshPro, UI Text or TextMesh on this object,
    /// and updates it when the language changes or a language file is edited.
    /// For dynamic values call <see cref="SetArgs"/>: <c>scoreLabel.SetArgs(score)</c>.
    /// </summary>
    [AddComponentMenu("UI/Loc Text")]
    [DisallowMultipleComponent]
    public class LocText : MonoBehaviour
    {
        [SerializeField] string key;

        object[] args;
        Component target;

        public string Key
        {
            get => key;
            set { key = value; Refresh(); }
        }

        public void SetKey(string newKey, params object[] newArgs)
        {
            key = newKey;
            args = newArgs;
            Refresh();
        }

        public void SetArgs(params object[] newArgs)
        {
            args = newArgs;
            Refresh();
        }

        void OnEnable()
        {
            Loc.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Loc.Changed -= Refresh;

        public void Refresh()
        {
            if (string.IsNullOrEmpty(key)) return;
            if (target == null) target = FindTextComponent(gameObject);
            if (target != null) SetText(target, Loc.T(key, args));
        }

        /// <summary>The text component LocText drives on <paramref name="go"/>: TMP_Text, Text or TextMesh.</summary>
        public static Component FindTextComponent(GameObject go)
        {
            if (go.TryGetComponent<TMP_Text>(out var tmp)) return tmp;
            if (go.TryGetComponent<Text>(out var text)) return text;
            if (go.TryGetComponent<TextMesh>(out var mesh)) return mesh;
            return null;
        }

        public static string GetText(Component c)
        {
            switch (c)
            {
                case TMP_Text tmp: return tmp.text;
                case Text text: return text.text;
                case TextMesh mesh: return mesh.text;
                default: return null;
            }
        }

        public static void SetText(Component c, string value)
        {
            switch (c)
            {
                case TMP_Text tmp: tmp.text = value; break;
                case Text text: text.text = value; break;
                case TextMesh mesh: mesh.text = value; break;
            }
        }
    }
}
