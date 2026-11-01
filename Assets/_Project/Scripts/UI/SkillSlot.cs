using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Spellmite
{
    // One skill square of the HUD, Risk of Rain style: icon, key under it, dark radial cooldown with the seconds left.
    // GameHud feeds it every frame.
    public class SkillSlot : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField, Tooltip("Image Type = Filled, Radial 360")] private Image cooldownFill;
        [SerializeField] private TMP_Text cooldownText;
        [SerializeField] private TMP_Text keyText;
        [SerializeField, Tooltip("Input action whose key is shown under the slot")] private string actionName = "Player/Attack";
        [SerializeField] private Color unusableColor = new(0.35f, 0.35f, 0.35f, 1f);

        private int shownSeconds = -1;
        private Tween readyPunch;

        private void Start()
        {
            var action = InputSystem.actions.FindAction(actionName);
            keyText.text = action != null ? action.GetBindingDisplayString(group: "Keyboard&Mouse") : ""; // noloc
        }

        // remaining: seconds left on the cooldown, 0 = ready. A null sprite leaves the slot empty
        public void Show(Sprite sprite, float remaining, float cooldown, bool usable)
        {
            icon.enabled = sprite != null;
            icon.sprite = sprite;
            icon.color = usable ? Color.white : unusableColor;
            cooldownFill.fillAmount = sprite != null && cooldown > 0f ? remaining / cooldown : 0f;

            int seconds = sprite != null && remaining > 0f ? Mathf.CeilToInt(remaining) : 0;
            if (seconds == shownSeconds) return;
            // Came off cooldown: a little punch, like Risk of Rain's ready flash
            if (seconds == 0 && shownSeconds > 0)
            {
                readyPunch.Stop();
                transform.localScale = Vector3.one;
                readyPunch = Tween.PunchScale(transform, Vector3.one * 0.15f, 0.25f);
            }
            shownSeconds = seconds;
            if (seconds > 0) cooldownText.SetText("{0}", seconds);
            else cooldownText.SetText("");
        }
    }
}
