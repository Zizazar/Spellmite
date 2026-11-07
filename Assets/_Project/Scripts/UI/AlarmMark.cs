using PrimeTween;
using TMPro;
using UnityEngine;

namespace Spellmite
{
    // The "!" that pops up over an enemy when the alarm goes up. Built in code, like EnemyBrain's aim ring: nothing to wire.
    public class AlarmMark : MonoBehaviour
    {
        private const float HeadGap = 0.7f;
        private const float FontSize = 16f;
        private const float AppearTime = 0.15f;
        private const float HoldTime = 0.8f;
        private const float LeaveTime = 0.15f;
        private static readonly Color MarkColor = new(1f, 0.25f, 0.15f);

        private HostBody body;
        private Camera cam;

        public static void Show(HostBody body)
        {
            var text = new GameObject($"{body.name} AlarmMark").AddComponent<TextMeshPro>();
            text.text = "!";
            text.fontSize = FontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = MarkColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var mark = text.gameObject.AddComponent<AlarmMark>();
            mark.body = body;
            mark.cam = Camera.main;
            mark.LateUpdate();

            Transform t = text.transform;
            t.localScale = Vector3.zero;
            Sequence.Create()
                .Chain(Tween.Scale(t, Vector3.one, AppearTime, Ease.OutBack))
                .ChainDelay(HoldTime)
                .Chain(Tween.Scale(t, Vector3.zero, LeaveTime, Ease.InBack))
                .OnComplete(text.gameObject, go => Destroy(go));
        }

        private void LateUpdate()
        {
            if (body != null)
            {
                Bounds bounds = body.Bounds;
                transform.position = new Vector3(bounds.center.x, bounds.max.y + HeadGap, bounds.center.z);
            }
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
