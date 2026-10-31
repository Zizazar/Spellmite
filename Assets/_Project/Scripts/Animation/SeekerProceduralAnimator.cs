using System;
using UnityEngine;

namespace Spellmite
{
    // Procedural knuckle-walk for The Rumbling Seeker, layered on top of the Animator (Idle + action clips).
    // Diagonal limb pairs step together, body bobs/leans/rolls, head turns to the aim.
    // Arm IK fades out while a state tagged "Action" plays (attacks, hit); everything fades out on death.
    [DefaultExecutionOrder(-20)]
    public class SeekerProceduralAnimator : MonoBehaviour
    {
        [Serializable]
        private class Limb
        {
            public Transform upper;
            public Transform mid;
            public Transform end;
            public bool isArm;
            public float stepHeight = 0.3f;

            [NonSerialized] public int Group;
            [NonSerialized] public Vector3 HomeLocal;
            [NonSerialized] public Vector3 PoleLocal;
            [NonSerialized] public float GroundOffset;
            [NonSerialized] public Vector3 Home;
            [NonSerialized] public Vector3 Planted;
            [NonSerialized] public Vector3 StepFrom;
            [NonSerialized] public float StepT = 1f;
            public bool Stepping => StepT < 1f;
        }

        private struct PoseGuard
        {
            public Vector3 BasePosition, WrittenPosition;
            public Quaternion BaseRotation, WrittenRotation;
        }

        [SerializeField] private HostBody host;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Limb leftArm;
        [SerializeField] private Limb rightArm;
        [SerializeField] private Limb leftLeg;
        [SerializeField] private Limb rightLeg;
        [SerializeField] private LayerMask groundMask = 1;

        [Header("Gait")]
        [SerializeField] private float stepDistanceWalk = 0.55f;
        [SerializeField] private float stepDistanceRun = 1.1f;
        [SerializeField] private float stepDurationWalk = 0.38f;
        [SerializeField] private float stepDurationRun = 0.22f;
        [SerializeField, Tooltip("Seconds of velocity to place limbs ahead")] private float stepLead = 0.22f;
        [SerializeField, Tooltip("Step distance when standing still, so limbs resettle")] private float idleStepDistance = 0.15f;

        [Header("Body")]
        [SerializeField] private float bobAmount = 0.1f;
        [SerializeField] private float runCrouch = 0.15f;
        [SerializeField] private float runLean = 10f;
        [SerializeField, Tooltip("Degrees per m/s² of acceleration")] private float accelLean = 1.2f;
        [SerializeField, Tooltip("Degrees of roll per deg/s of turning")] private float turnRoll = 0.04f;
        [SerializeField] private float maxTilt = 18f;

        [Header("Head")]
        [SerializeField] private float headTurnMax = 60f;
        [SerializeField] private float headSharpness = 8f;

        [Header("Blending")]
        [SerializeField] private float actionBlendTime = 0.12f;

        private Limb[] limbs;
        private float armWeight = 1f;
        private float legWeight = 1f;
        private Vector3 lastVelocity;
        private Vector3 smoothedAccel;
        private float lastYaw;
        private float turnRate;
        private float headYaw;
        private float airTime;
        private PoseGuard bodyGuard;
        private PoseGuard headGuard;

        private void Awake()
        {
            leftArm.Group = 0;
            rightLeg.Group = 0;
            rightArm.Group = 1;
            leftLeg.Group = 1;
            limbs = new[] { leftArm, rightArm, leftLeg, rightLeg };

            // Read rest pose (before the Animator's first update): limb ground contacts and bend directions
            Transform root = host.transform;
            foreach (var limb in limbs)
            {
                Vector3 endLocal = root.InverseTransformPoint(limb.end.position);
                limb.GroundOffset = endLocal.y;
                limb.HomeLocal = new Vector3(endLocal.x, 0f, endLocal.z);
                Vector3 chordMid = (limb.upper.position + limb.end.position) * 0.5f;
                limb.PoleLocal = root.InverseTransformPoint(limb.mid.position + (limb.mid.position - chordMid).normalized);
                limb.Planted = root.TransformPoint(limb.HomeLocal);
            }

            lastYaw = root.eulerAngles.y;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Restore(body, ref bodyGuard);
            Restore(head, ref headGuard);

            bool dead = host.Health.IsDead;
            armWeight = Mathf.MoveTowards(armWeight, dead || IsActionPlaying() ? 0f : 1f, dt / actionBlendTime);
            legWeight = Mathf.MoveTowards(legWeight, dead ? 0f : 1f, dt / actionBlendTime);

            if (legWeight > 0f || armWeight > 0f)
            {
                Transform root = host.transform;
                Vector3 up = root.up;
                Vector3 flatVelocity = Vector3.ProjectOnPlane(host.Velocity, up);
                float speed = flatVelocity.magnitude;
                float run01 = Mathf.Clamp01(speed / host.Profile.SprintSpeed);

                airTime = host.IsGrounded ? 0f : airTime + dt;
                bool grounded = airTime < 0.15f;

                UpdateBody(root, up, run01, dt);
                UpdateHomes(root, up, flatVelocity);

                if (grounded) TryStartStep(speed < 0.2f ? idleStepDistance : Mathf.Lerp(stepDistanceWalk, stepDistanceRun, run01));

                float duration = Mathf.Lerp(stepDurationWalk, stepDurationRun, run01);
                foreach (var limb in limbs) UpdateLimb(limb, root, up, grounded, duration, dt);

                UpdateHead(root, up, dt);
            }

            Remember(body, ref bodyGuard);
            Remember(head, ref headGuard);
        }

        private bool IsActionPlaying()
        {
            if (animator == null) return false;
            if (animator.GetCurrentAnimatorStateInfo(0).IsTag("Action")) return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Action");
        }

        private void UpdateBody(Transform root, Vector3 up, float run01, float dt)
        {
            Vector3 velocity = host.Velocity;
            Vector3 accel = Vector3.ProjectOnPlane((velocity - lastVelocity) / dt, up);
            lastVelocity = velocity;
            smoothedAccel = Vector3.Lerp(smoothedAccel, accel, 1f - Mathf.Exp(-6f * dt));
            Vector3 localAccel = root.InverseTransformDirection(smoothedAccel);

            float yaw = root.eulerAngles.y;
            turnRate = Mathf.Lerp(turnRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, 1f - Mathf.Exp(-8f * dt));
            lastYaw = yaw;

            float pitch = Mathf.Clamp(run01 * runLean + localAccel.z * accelLean, -maxTilt, maxTilt);
            float roll = Mathf.Clamp(-turnRate * turnRoll - localAccel.x * accelLean, -maxTilt, maxTilt);

            // Body rises while limbs swing, sinks when they plant
            float lift = 0f;
            foreach (var limb in limbs)
                if (limb.Stepping) lift += Mathf.Sin(limb.StepT * Mathf.PI);
            float bob = lift * 0.5f * bobAmount - runCrouch * run01;

            body.position += up * (bob * legWeight);
            body.rotation = Quaternion.AngleAxis(pitch * legWeight, root.right)
                            * Quaternion.AngleAxis(roll * legWeight, root.forward)
                            * body.rotation;
        }

        private void UpdateHomes(Transform root, Vector3 up, Vector3 flatVelocity)
        {
            foreach (var limb in limbs)
            {
                Vector3 home = root.TransformPoint(limb.HomeLocal) + flatVelocity * stepLead;
                if (Physics.Raycast(home + up * 1.5f, -up, out var hit, 3f, groundMask, QueryTriggerInteraction.Ignore))
                    home = hit.point;
                limb.Home = home;
            }
        }

        // One diagonal pair steps at a time: the pair whose limbs are furthest from home
        private void TryStartStep(float threshold)
        {
            foreach (var limb in limbs)
                if (limb.Stepping) return;

            int bestGroup = -1;
            float bestDistance = threshold;
            foreach (var limb in limbs)
            {
                float distance = Vector3.Distance(limb.Planted, limb.Home);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestGroup = limb.Group;
                }
            }
            if (bestGroup < 0) return;

            foreach (var limb in limbs)
            {
                if (limb.Group != bestGroup) continue;
                limb.StepFrom = limb.Planted;
                limb.StepT = 0f;
            }
        }

        private void UpdateLimb(Limb limb, Transform root, Vector3 up, bool grounded, float duration, float dt)
        {
            Vector3 contact;
            if (!grounded)
            {
                // Dangle under the body while airborne
                limb.Planted = root.TransformPoint(limb.HomeLocal) - up * 0.15f;
                limb.StepT = 1f;
                contact = limb.Planted;
            }
            else if (limb.Stepping)
            {
                limb.StepT = Mathf.Min(1f, limb.StepT + dt / duration);
                float t = Mathf.SmoothStep(0f, 1f, limb.StepT);
                contact = Vector3.Lerp(limb.StepFrom, limb.Home, t) + up * (Mathf.Sin(limb.StepT * Mathf.PI) * limb.stepHeight);
                if (!limb.Stepping) limb.Planted = limb.Home;
            }
            else
            {
                if (Vector3.Distance(limb.Planted, limb.Home) > 3f) limb.Planted = limb.Home; // teleported
                contact = limb.Planted;
            }

            float weight = limb.isArm ? armWeight : legWeight;
            TwoBoneIK.Solve(limb.upper, limb.mid, limb.end, contact + up * limb.GroundOffset, root.TransformPoint(limb.PoleLocal), weight);
        }

        private void UpdateHead(Transform root, Vector3 up, float dt)
        {
            Vector3 aim = Vector3.ProjectOnPlane(host.AimDirection, up);
            float target = aim.sqrMagnitude > 1e-4f
                ? Mathf.Clamp(Vector3.SignedAngle(root.forward, aim, up), -headTurnMax, headTurnMax)
                : 0f;
            headYaw = Mathf.Lerp(headYaw, target, 1f - Mathf.Exp(-headSharpness * dt));
            head.rotation = Quaternion.AngleAxis(headYaw * legWeight, up) * head.rotation;
        }

        // Bones the Animator doesn't write would accumulate our offsets; put them back to last frame's base pose
        private static void Restore(Transform t, ref PoseGuard guard)
        {
            if (t.localPosition == guard.WrittenPosition && t.localRotation == guard.WrittenRotation)
            {
                t.localPosition = guard.BasePosition;
                t.localRotation = guard.BaseRotation;
            }
            guard.BasePosition = t.localPosition;
            guard.BaseRotation = t.localRotation;
        }

        private static void Remember(Transform t, ref PoseGuard guard)
        {
            guard.WrittenPosition = t.localPosition;
            guard.WrittenRotation = t.localRotation;
        }
    }
}
