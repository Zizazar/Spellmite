using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // Crosshair that lights up over a host you can leap to, and the current host's resistance bar.
    public class ParasiteHud : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField] private Image crosshair;
        [SerializeField] private Color idleColor = new(1f, 1f, 1f, 0.6f);
        [SerializeField] private Color targetColor = new(1f, 0.25f, 0.6f, 1f);
        [SerializeField] private GameObject resistanceRoot;
        [SerializeField, Tooltip("Image Type = Filled")] private Image resistanceFill;

        private void Update()
        {
            bool hasTarget = parasite.AimedHost != null;
            crosshair.color = hasTarget ? targetColor : idleColor;
            crosshair.rectTransform.localScale = Vector3.one * (hasTarget ? 1.5f : 1f);

            bool hosted = parasite.IsHosted && parasite.Host != null;
            resistanceRoot.SetActive(hosted);
            if (hosted) resistanceFill.fillAmount = parasite.Host.Resistance01;
        }
    }
}
