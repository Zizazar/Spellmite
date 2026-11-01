using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    // A body the parasite can take over. Driven by whoever calls SetInput: EnemyBrain when free, ParasiteBody when possessed.
    [DefaultExecutionOrder(0)]
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public class HostBody : MonoBehaviour
    {
        public static readonly List<HostBody> All = new();

        private static readonly int AttackLeftHash = Animator.StringToHash("AttackLeft");
        private static readonly int AttackRightHash = Animator.StringToHash("AttackRight");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int CastHash = Animator.StringToHash("Cast");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int CastOffsetHash = Animator.StringToHash("CastOffset");

        [SerializeField] private BodyProfile profile;
        [SerializeField, Tooltip("Localization key of the name the HUD shows while possessed")] private string nameKey = "body.grunt";
        [SerializeField] private Transform latchPoint;

        [Header("Possession")]
        [SerializeField, Tooltip("Seconds of control before the host throws the parasite off")] private float resistDuration = 8f;
        [SerializeField, Tooltip("Resistance refilled per second while free (1 = full)")] private float resistRecovery = 0.25f;
        [SerializeField] private float stunAfterRelease = 1.5f;

        [Header("Attack")]
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackReach = 1.4f;
        [SerializeField] private float attackRadius = 1f;
        [SerializeField] private float attackHeight = 1f;
        [SerializeField] private float attackHitDelay = 0.3f;
        [SerializeField] private float attackCooldown = 0.8f;
        [SerializeField] private float attackKnockback = 8f;

        [Header("Input forgiveness")]
        [SerializeField, Tooltip("Player only: a press while busy, on cooldown or in the air still fires within this many seconds")] private float inputBuffer = 0.15f;
        [SerializeField, Tooltip("Jump still works this long after leaving the ground")] private float coyoteTime = 0.12f;

        [Header("Aim (player)")]
        [SerializeField, Tooltip("After a cast or a swing the body keeps facing the crosshair this long, so you can strafe")] private float aimModeDuration = 2f;
        [SerializeField, Tooltip("Optional, Humanoid only: turns the torso to the aim so the legs can keep running their way")] private AimLookIK aimIK;
        [SerializeField, Tooltip("With aimIK: up to this angle between aim and run the torso twists; past it the whole body turns")] private float maxTorsoTwist = 70f;
        [SerializeField, Range(0f, 0.9f), Tooltip("The player's spell fires at once: the Cast clip starts this far in (0..1), past its windup")] private float playerCastAnimSkip = 0.3f;

        [Header("Visuals")]
        [SerializeField, Tooltip("Optional. Uses triggers AttackLeft, AttackRight, Hit, Death, Cast, floats Speed and CastOffset (optional)")] private Animator animator;
        [SerializeField, Tooltip("Punched on hits/attacks when there is no Animator")] private Transform visual;
        [SerializeField] private float destroyDelayAfterDeath = 4f;

        private CharacterController controller;
        private Health health;
        private SpellCaster caster;
        private BuffHolder buffs;
        private ControlInput input;
        private Vector3 velocity;
        private float resistance = 1f;
        private float stunUntil;
        private float nextAttackTime;
        private float attackFacingUntil;
        private float aimModeUntil;
        private bool nextAttackLeft = true;
        private bool hasCastOffset;
        // Buffered presses: valid while Time.time <= the time, -1 = none
        private float jumpBufferedUntil = -1f;
        private float attackBufferedUntil = -1f;
        private float castBufferedUntil = -1f;
        private int bufferedCastSlot;
        private float lastGroundedTime = -1f;
        private Tween visualTween;
        private Vector3 visualBaseScale = Vector3.one;
        private readonly Collider[] hitBuffer = new Collider[16];

        public BodyProfile Profile => profile;
        public string NameKey => nameKey;
        public Transform LatchPoint => latchPoint;
        public Health Health => health;
        public Vector3 Velocity => velocity;
        public Vector3 AimDirection => input.Aim;
        public Vector3 AimOrigin => input.AimOrigin;
        public Vector3 Center => controller.bounds.center;
        public Bounds Bounds => controller.bounds;
        public bool IsGrounded => controller.isGrounded;
        public bool IsPossessed { get; private set; }
        public bool IsStunned => Time.time < stunUntil || (buffs != null && buffs.IsStunned);
        public float Resistance01 => resistance;
        // Tutorial: possessing this body never runs out and its spells cost no resistance
        public bool EndlessPossession { get; set; }
        // Player only: right after a cast or a swing, while the body should look where the crosshair is
        public bool IsAiming => IsPossessed && (Time.time < aimModeUntil || IsCasting);
        // Player only: when the last cast or swing went off (PlayerBrain drops the sprint on it)
        public float LastActionTime { get; private set; } = -1f;

        private bool IsCasting => caster != null && caster.IsCasting;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            caster = GetComponent<SpellCaster>();
            buffs = GetComponent<BuffHolder>();
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            if (visual != null) visualBaseScale = visual.localScale;
            if (animator != null)
                foreach (var p in animator.parameters)
                    if (p.nameHash == CastOffsetHash) hasCastOffset = true;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void SetInput(ControlInput value) => input = value;

        public void Possess()
        {
            IsPossessed = true;
            input = default;
            ClearBuffers();
        }

        public void Release()
        {
            IsPossessed = false;
            input = default;
            ClearBuffers();
            Stun(stunAfterRelease);
        }

        private void ClearBuffers()
        {
            jumpBufferedUntil = attackBufferedUntil = castBufferedUntil = -1f;
        }

        // Enemies get no buffer, so they act exactly when their brain (and AttackDirector) says so
        private void BufferPresses()
        {
            float until = Time.time + (IsPossessed ? inputBuffer : 0f);
            if (input.JumpPressed) jumpBufferedUntil = until;
            if (input.AttackPressed) attackBufferedUntil = until;
            if (input.CastSlot > 0)
            {
                castBufferedUntil = until;
                bufferedCastSlot = input.CastSlot;
            }
        }

        public void Stun(float duration)
        {
            stunUntil = Mathf.Max(stunUntil, Time.time + duration);
        }

        public void AddImpulse(Vector3 impulse)
        {
            velocity += impulse / Mathf.Max(0.01f, profile.Mass);
        }

        // Puts the body somewhere at once, standing still (the tutorial brings a fallen body back)
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            velocity = Vector3.zero;
        }

        // Spells cast while possessed cost resistance
        public void DrainResistance(float amount)
        {
            if (EndlessPossession) return;
            resistance = Mathf.Max(0f, resistance - amount);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool canAct = !health.IsDead && !IsStunned;

            if (!EndlessPossession)
                resistance = IsPossessed
                    ? Mathf.Max(0f, resistance - dt / resistDuration)
                    : Mathf.Min(1f, resistance + dt * resistRecovery);

            BufferPresses();
            Vector3 move = canAct ? input.Move : Vector3.zero;
            bool grounded = controller.isGrounded;
            if (grounded) lastGroundedTime = Time.time;

            // Horizontal
            Vector3 horizontal = new(velocity.x, 0f, velocity.z);
            float speed = (input.Sprint ? profile.SprintSpeed : profile.WalkSpeed) * (buffs != null ? buffs.SpeedMultiplier : 1f);
            float rate = move.sqrMagnitude > 0.01f ? profile.Acceleration : profile.Deceleration;
            if (!grounded) rate *= profile.AirControl;
            horizontal = Vector3.MoveTowards(horizontal, move * speed, rate * dt);

            // Vertical
            float vertical = velocity.y;
            if (grounded && vertical < 0f) vertical = -2f;
            // isGrounded flickers on uneven floors, and a jump just after a ledge should still count
            if (canAct && Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= coyoteTime)
            {
                vertical = profile.JumpSpeed;
                // Like Risk of Rain: the jump goes where the stick points at full running speed, straight up with it released
                horizontal = move.sqrMagnitude > 1e-4f ? move.normalized * speed : Vector3.zero;
                jumpBufferedUntil = -1f;
                lastGroundedTime = -1f;
            }
            vertical -= profile.Gravity * dt;

            velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            controller.Move(velocity * dt);

            // Sprinting ends aim mode: the body turns to where it runs
            if (input.Sprint) aimModeUntil = -1f;

            // Face the crosshair first: the player's spell leaves the cast point inside TryCast
            if (canAct && Time.time <= castBufferedUntil && caster != null && caster.CanCast(bufferedCastSlot - 1))
            {
                castBufferedUntil = -1f;
                SnapToAim();
                caster.TryCast(bufferedCastSlot - 1, input.CastTimeScale);
                PlayCast();
                MarkAction();
            }

            if (canAct && Time.time <= attackBufferedUntil && Time.time >= nextAttackTime && !IsCasting)
            {
                attackBufferedUntil = -1f;
                Attack().Forget();
            }

            UpdateFacing(horizontal, dt);
            if (animator != null) animator.SetFloat(SpeedHash, horizontal.magnitude);
        }

        private void UpdateFacing(Vector3 horizontal, float dt)
        {
            if (health.IsDead) return;

            Vector3 aim = Vector3.ProjectOnPlane(input.Aim, Vector3.up);
            Vector3 face = horizontal;
            if (Time.time < attackFacingUntil || IsCasting)
            {
                face = aim;
            }
            else if (IsAiming)
            {
                // Aim mode: the torso IK covers small angles while the legs run their way; past that the whole body turns
                bool twist = aimIK != null && horizontal.sqrMagnitude >= 0.25f && Vector3.Angle(aim, horizontal) <= maxTorsoTwist;
                if (!twist) face = aim;
            }
            if (face.sqrMagnitude < 0.25f) return;

            Quaternion target = Quaternion.LookRotation(face.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, profile.TurnSpeed * dt);
        }

        // A cast or a swing went off: the body keeps facing the crosshair for a while, the player's sprint drops
        private void MarkAction()
        {
            aimModeUntil = Time.time + aimModeDuration;
            if (IsPossessed) LastActionTime = Time.time;
        }

        // The player's body faces the crosshair at once, so spells and swings leave towards the target.
        // Enemies keep turning at TurnSpeed, which keeps their attacks readable
        private void SnapToAim()
        {
            if (!IsPossessed) return;
            Vector3 face = Vector3.ProjectOnPlane(input.Aim, Vector3.up);
            if (face.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
        }

        private async UniTaskVoid Attack()
        {
            nextAttackTime = Time.time + attackCooldown;
            attackFacingUntil = Time.time + attackHitDelay;
            MarkAction();
            SnapToAim();

            if (animator != null)
            {
                animator.SetTrigger(nextAttackLeft ? AttackLeftHash : AttackRightHash);
                nextAttackLeft = !nextAttackLeft;
            }
            else
            {
                PunchVisual(new Vector3(0.15f, -0.1f, 0.35f));
            }

            await UniTask.Delay(TimeSpan.FromSeconds(attackHitDelay), cancellationToken: destroyCancellationToken);
            if (health.IsDead || IsStunned) return;

            Vector3 center = transform.position + transform.up * attackHeight + transform.forward * attackReach;
            int count = Physics.OverlapSphereNonAlloc(center, attackRadius, hitBuffer, ~0, QueryTriggerInteraction.Collide);
            var damaged = new HashSet<Health>();
            for (int i = 0; i < count; i++)
            {
                var target = hitBuffer[i].GetComponentInParent<Health>();
                if (target == null || target == health || !damaged.Add(target)) continue;

                Vector3 push = (Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up).normalized + Vector3.up * 0.4f) * attackKnockback;
                target.TakeDamage(attackDamage, push);
            }
            if (IsPossessed && damaged.Count > 0) BodyCamera.ShakeMeleeHit();
        }

        private void PlayCast()
        {
            if (animator == null)
            {
                PunchVisual(new Vector3(0.2f, 0.25f, -0.1f));
                return;
            }
            // The player's spell is already out: the Cast state's cycle offset (CastOffset) skips the clip's windup
            if (hasCastOffset) animator.SetFloat(CastOffsetHash, IsPossessed ? playerCastAnimSkip : 0f);
            animator.SetTrigger(CastHash);
        }

        private void OnDamaged(float amount, Vector3 impulse)
        {
            AddImpulse(impulse);
            if (health.IsDead) return;

            if (animator != null)
            {
                // Don't interrupt our own swing with a flinch
                if (Time.time >= nextAttackTime - attackCooldown + attackHitDelay) animator.SetTrigger(HitHash);
            }
            else
            {
                PunchVisual(new Vector3(0.25f, -0.25f, 0.25f));
            }
        }

        private void OnDied()
        {
            input = default;
            if (animator != null)
            {
                animator.SetTrigger(DeathHash);
            }
            else if (visual != null)
            {
                visualTween.Stop();
                Tween.LocalRotation(visual, new Vector3(90f, 0f, 0f), 0.4f, Ease.OutBounce);
            }
            Destroy(gameObject, destroyDelayAfterDeath);
        }

        private void PunchVisual(Vector3 strength)
        {
            if (visual == null) return;
            visualTween.Stop();
            visual.localScale = visualBaseScale;
            visualTween = Tween.PunchScale(visual, Vector3.Scale(strength, visualBaseScale), 0.3f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + transform.up * attackHeight + transform.forward * attackReach, attackRadius);
        }
    }
}
