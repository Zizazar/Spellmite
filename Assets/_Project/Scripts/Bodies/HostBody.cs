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

        [SerializeField] private BodyProfile profile;
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

        [Header("Visuals")]
        [SerializeField, Tooltip("Optional. Uses triggers AttackLeft, AttackRight, Hit, Death, Cast")] private Animator animator;
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
        private bool nextAttackLeft = true;
        private Tween visualTween;
        private Vector3 visualBaseScale = Vector3.one;
        private readonly Collider[] hitBuffer = new Collider[16];

        public BodyProfile Profile => profile;
        public Transform LatchPoint => latchPoint;
        public Health Health => health;
        public Vector3 Velocity => velocity;
        public Vector3 AimDirection => input.Aim;
        public Vector3 AimOrigin => input.AimOrigin;
        public Vector3 Center => controller.bounds.center;
        public bool IsGrounded => controller.isGrounded;
        public bool IsPossessed { get; private set; }
        public bool IsStunned => Time.time < stunUntil || (buffs != null && buffs.IsStunned);
        public float Resistance01 => resistance;

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
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void SetInput(ControlInput value) => input = value;

        public void Possess()
        {
            IsPossessed = true;
            input = default;
        }

        public void Release()
        {
            IsPossessed = false;
            input = default;
            Stun(stunAfterRelease);
        }

        public void Stun(float duration)
        {
            stunUntil = Mathf.Max(stunUntil, Time.time + duration);
        }

        public void AddImpulse(Vector3 impulse)
        {
            velocity += impulse / Mathf.Max(0.01f, profile.Mass);
        }

        // Spells cast while possessed cost resistance
        public void DrainResistance(float amount)
        {
            resistance = Mathf.Max(0f, resistance - amount);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool canAct = !health.IsDead && !IsStunned;

            resistance = IsPossessed
                ? Mathf.Max(0f, resistance - dt / resistDuration)
                : Mathf.Min(1f, resistance + dt * resistRecovery);

            Vector3 move = canAct ? input.Move : Vector3.zero;
            bool grounded = controller.isGrounded;

            // Horizontal
            Vector3 horizontal = new(velocity.x, 0f, velocity.z);
            float speed = (input.Sprint ? profile.SprintSpeed : profile.WalkSpeed) * (buffs != null ? buffs.SpeedMultiplier : 1f);
            float rate = move.sqrMagnitude > 0.01f ? profile.Acceleration : profile.Deceleration;
            if (!grounded) rate *= profile.AirControl;
            horizontal = Vector3.MoveTowards(horizontal, move * speed, rate * dt);

            // Vertical
            float vertical = velocity.y;
            if (grounded && vertical < 0f) vertical = -2f;
            if (canAct && grounded && input.JumpPressed) vertical = profile.JumpSpeed;
            vertical -= profile.Gravity * dt;

            velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            controller.Move(velocity * dt);

            if (canAct && input.CastSlot > 0 && caster != null && caster.TryCast(input.CastSlot - 1))
                PlayCast();

            if (canAct && input.AttackPressed && Time.time >= nextAttackTime && !IsCasting)
                Attack().Forget();

            UpdateFacing(horizontal, dt);
        }

        private void UpdateFacing(Vector3 horizontal, float dt)
        {
            if (health.IsDead) return;

            Vector3 face = horizontal;
            if (Time.time < attackFacingUntil || IsCasting) face = Vector3.ProjectOnPlane(input.Aim, Vector3.up);
            if (face.sqrMagnitude < 0.25f) return;

            Quaternion target = Quaternion.LookRotation(face.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, profile.TurnSpeed * dt);
        }

        private async UniTaskVoid Attack()
        {
            nextAttackTime = Time.time + attackCooldown;
            attackFacingUntil = Time.time + attackHitDelay;

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
        }

        private void PlayCast()
        {
            if (animator != null) animator.SetTrigger(CastHash);
            else PunchVisual(new Vector3(0.2f, 0.25f, -0.1f));
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
