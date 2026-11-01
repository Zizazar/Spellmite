using UnityEngine;

namespace Spellmite
{
    // Humanoid look-at towards the crosshair while the player's body is aiming (Risk of Rain's AimAnimator):
    // the legs keep running where they go, the torso and head turn to the aim. Goes on the Animator's object,
    // the controller's layers need IK Pass on.
    [RequireComponent(typeof(Animator))]
    public class AimLookIK : MonoBehaviour
    {
        [SerializeField] private HostBody body;
        [SerializeField, Range(0f, 1f), Tooltip("How much of the turn the spine takes")] private float bodyWeight = 0.6f;
        [SerializeField, Range(0f, 1f)] private float headWeight = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = no limit, 0.5 = up to 90 degrees, 1 = locked")] private float clampWeight = 0.5f;
        [SerializeField, Tooltip("Weight change per second when aiming starts and ends")] private float blendSpeed = 5f;

        private Animator animator;
        private Transform head;
        private float weight;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (body == null) body = GetComponentInParent<HostBody>();
            head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }

        private void Update()
        {
            bool aiming = body != null && body.IsAiming && !body.Health.IsDead;
            weight = Mathf.MoveTowards(weight, aiming ? 1f : 0f, blendSpeed * Time.deltaTime);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (head == null || weight <= 0f) return;
            animator.SetLookAtWeight(weight, bodyWeight, headWeight, 0f, clampWeight);
            animator.SetLookAtPosition(head.position + body.AimDirection.normalized * 10f);
        }
    }
}
