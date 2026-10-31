using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spellmite
{
    // Simple AI for a free host, Half-Life squad style. Wander -> Reposition (target found, no attack token: strafe and
    // hold the spell range) -> Attack (holds a token from AttackDirector: stands and casts). It attacks only while the
    // player sees it (PlayerViewTracker), so damage never comes from off screen. Spells are picked by their AI hints,
    // slot order is the priority. Stops thinking while the host is possessed.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(HostBody))]
    public class EnemyBrain : MonoBehaviour
    {
        public static readonly List<EnemyBrain> All = new();
        private static int flankCounter;

        private const float RangeBand = 1.5f;
        private const int RingSegments = 40;
        // Turns tried when the way is blocked, multiplied by the strafe side so the choice doesn't flicker
        private static readonly float[] SteerAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };

        [SerializeField] private bool aggressive = true;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float wanderRadius = 6f;
        [SerializeField] private float wanderPause = 2f;

        [Header("Spells")]
        [SerializeField, Tooltip("Random pause after choosing a spell before the next one, seconds")] private Vector2 castPause = new(0.5f, 1.5f);
        [SerializeField, Tooltip("What blocks line of sight for attack spells")] private LayerMask sightMask = 1;

        [Header("Reposition (no attack token)")]
        [SerializeField, Range(0.3f, 1f), Tooltip("Hold this fraction of the first attack spell's max range")] private float holdRangeFraction = 0.8f;
        [SerializeField, Range(0f, 1f), Tooltip("Sideways speed, fraction of walk speed")] private float strafeSpeed = 0.6f;
        [SerializeField, Tooltip("Seconds between strafe direction changes")] private Vector2 strafeSwitchTime = new(0.8f, 2f);

        [Header("Seek view (target found, the player doesn't see us)")]
        [SerializeField, Tooltip("Enemies alternate left/right flanks and take a spot this many degrees from the camera's forward on their side")] private Vector2 slotAngleRange = new(15f, 35f);
        [SerializeField, Tooltip("Within this angle of its spot the enemy stops circling and walks straight in (it's behind cover)")] private float slotTolerance = 10f;

        [Header("Spacing")]
        [SerializeField, Range(0f, 0.5f), Tooltip("Each enemy holds its own distance: the hold range +- this fraction")] private float rangeJitter = 0.2f;
        [SerializeField, Tooltip("Enemies closer than this push apart")] private float separationRadius = 3f;
        [SerializeField] private float separationWeight = 1f;

        [Header("Obstacles")]
        [SerializeField, Tooltip("How far ahead to look for walls")] private float probeDistance = 1.2f;

        [Header("Telegraph")]
        [SerializeField, Tooltip("A random one plays when the enemy gets an attack token")] private AudioClip[] barks;
        [SerializeField, Range(0f, 1f)] private float barkChance = 0.6f;
        [SerializeField] private float barkVolume = 1f;
        [SerializeField, Tooltip("Ground ring under the target during the windup of splash spells. Empty = no ring")] private Material aimRingMaterial;
        [SerializeField] private float aimRingWidth = 0.08f;

        private HostBody body;
        private SpellCaster caster;
        private CharacterController controller;
        private ParasiteBody player;
        private Camera viewCamera;
        private Vector3 home;
        private Vector3 wanderTarget;
        private float nextWanderTime;
        private float nextCastTime;
        private float slotAngle;
        private float rangeScale = 1f;
        private float strafeSign = 1f;
        private float nextStrafeSwitch;
        private float lastInViewTime = float.NegativeInfinity;
        private float noticeTime;
        private bool castingWithToken;
        private SpellData castSpell;
        private LineRenderer aimRing;

        public bool IsThinking => !body.IsPossessed && !body.Health.IsDead;
        public Vector3 Center => body.Center;
        // Written by PlayerViewTracker
        public bool IsSeenByPlayer { get; private set; }
        public bool HasNoticedPlayer => IsSeenByPlayer && Time.time >= noticeTime;
        public float ScreenOffset { get; private set; } = 1f;

        private void Awake()
        {
            body = GetComponent<HostBody>();
            caster = GetComponent<SpellCaster>();
            controller = GetComponent<CharacterController>();
            // Left and right flank by turns, so a group closes in from both sides
            float flank = flankCounter++ % 2 == 0 ? 1f : -1f;
            slotAngle = flank * Random.Range(slotAngleRange.x, slotAngleRange.y);
            rangeScale = Random.Range(1f - rangeJitter, 1f + rangeJitter);
            home = transform.position;
            wanderTarget = home;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
            viewCamera = Camera.main;
            if (AttackDirector.Instance == null)
            {
                Debug.LogError("No AIDirector in the scene: enemies can't think. Add Prefabs/AIDirector.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (aimRing != null) Destroy(aimRing.gameObject);
        }

        // Called by PlayerViewTracker a few times a second. offset: distance from the screen center, for token priority
        public void SetView(bool inView, float offset)
        {
            var settings = AttackDirector.Instance.Settings;
            if (inView)
            {
                lastInViewTime = Time.time;
                ScreenOffset = offset;
            }

            bool seen = IsThinking && Time.time - lastInViewTime <= settings.SeenMemory;
            if (seen && !IsSeenByPlayer) noticeTime = Time.time + settings.ReactionDelay;
            IsSeenByPlayer = seen;
        }

        private void Update()
        {
            // The windup that took a token is over (fired or interrupted): hand the token back
            if (castingWithToken && !caster.IsCasting)
            {
                castingWithToken = false;
                AttackDirector.Instance.Release(this);
            }

            if (!IsThinking)
            {
                UpdateAimRing(null);
                return;
            }

            var input = new ControlInput
            {
                AimOrigin = transform.position + Vector3.up * 1.5f,
                Aim = transform.forward,
                CastTimeScale = AttackDirector.Instance.Settings.EnemyCastTimeScale,
            };
            Transform target = FindTarget();

            if (target != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
                float distance = toTarget.magnitude;
                Vector3 aimPoint = AimPoint(target);
                input.Aim = (aimPoint - input.AimOrigin).normalized;
                // Off the player's screen: run to where they look, so attacks never wait on an empty screen
                input.Sprint = !IsSeenByPlayer;
                Vector3 move = input.Sprint ? SeekView(toTarget, distance) : Reposition(toTarget, distance);
                input.Move = AvoidWalls(Vector3.ClampMagnitude(move + Separation(), 1f));
                // Melee too only where the player can see it coming; HostBody handles the cooldown
                if (distance <= attackRange && AttackDirector.Instance.MayAttack(this)) input.AttackPressed = true;
                input.CastSlot = ChooseSpell(target, distance, input.AimOrigin, aimPoint) + 1;
            }
            else
            {
                Wander(ref input);
                input.CastSlot = ChooseSpell(null, 0f, input.AimOrigin, input.AimOrigin) + 1;
            }

            // Attack: stand still during the windup so the telegraph reads
            if (caster != null && caster.IsCasting) input.Move = Vector3.zero;

            UpdateAimRing(target);
            body.SetInput(input);
        }

        // First ready spell whose AI hint fits; slot order is the priority. -1 = none.
        // Spells with an AiTokenCost also need a token, which AttackDirector gives only to enemies the player sees.
        private int ChooseSpell(Transform target, float distance, Vector3 from, Vector3 aimPoint)
        {
            if (caster == null || caster.IsCasting || body.IsStunned || Time.time < nextCastTime) return -1;

            for (int i = 0; i < caster.SlotCount; i++)
            {
                if (!caster.IsReady(i)) continue;
                var spell = caster.GetSpell(i);
                bool use = spell.AiUse switch
                {
                    SpellAiUse.HealWhenHurt => body.Health.Current < body.Health.Max * spell.AiHealthBelow,
                    SpellAiUse.BuffInCombat => target != null,
                    _ => target != null && distance >= spell.AiMinRange && distance <= spell.AiMaxRange && HasLineOfSight(from, aimPoint, target),
                };
                if (!use) continue;

                if (spell.AiTokenCost > 0)
                {
                    if (!AttackDirector.Instance.TryAcquire(this, spell.AiTokenCost)) continue;
                    castingWithToken = true;
                    Bark();
                }

                castSpell = spell;
                nextCastTime = Time.time + Random.Range(castPause.x, castPause.y);
                return i;
            }
            return -1;
        }

        // No token yet: hold the spell range and strafe, so the player feels surrounded. Melee-only bodies walk in.
        private Vector3 Reposition(Vector3 toTarget, float distance)
        {
            Vector3 forward = distance > 0.01f ? toTarget / distance : transform.forward;
            float approach = ApproachFactor(distance);

            if (Time.time >= nextStrafeSwitch)
            {
                strafeSign = Random.value < 0.5f ? -1f : 1f;
                nextStrafeSwitch = Time.time + Random.Range(strafeSwitchTime.x, strafeSwitchTime.y);
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // Off its flank: strafe back toward its spot (moving right lowers our ViewAngle)
            float toSlot = Mathf.DeltaAngle(ViewAngle(-forward), slotAngle);
            if (Mathf.Abs(toSlot) > slotTolerance * 2f && !IsBlocked(right * -Mathf.Sign(toSlot)))
            {
                strafeSign = -Mathf.Sign(toSlot);
                nextStrafeSwitch = Time.time + strafeSwitchTime.x;
            }
            // Strafing into a wall: go the other way
            else if (IsBlocked(right * strafeSign))
            {
                strafeSign = -strafeSign;
                nextStrafeSwitch = Time.time + Random.Range(strafeSwitchTime.x, strafeSwitchTime.y);
            }
            return Vector3.ClampMagnitude(forward * approach + right * (strafeSign * strafeSpeed), 1f);
        }

        // Not on the player's screen: circle the player to this enemy's spot on its flank, holding the spell range.
        // From behind it goes round its own side, never across the front. At the spot but still unseen means cover
        // is in the way: walk straight in.
        private Vector3 SeekView(Vector3 toTarget, float distance)
        {
            Vector3 forward = distance > 0.01f ? toTarget / distance : transform.forward;
            float angle = ViewAngle(-forward);
            float delta = Mathf.DeltaAngle(angle, slotAngle);
            if (Mathf.Abs(delta) <= slotTolerance) return distance > attackRange ? forward : Vector3.zero;
            if (Mathf.Abs(angle) > 90f && Mathf.Sign(angle) != Mathf.Sign(slotAngle)) delta -= 360f * Mathf.Sign(delta);

            // Moving along this turns us clockwise around the player (seen from above), raising the angle
            Vector3 around = Vector3.Cross(Vector3.up, -forward) * Mathf.Sign(delta);
            // Far away: spiral in at 45 degrees instead of circling at that distance
            return (around + forward * ApproachFactor(distance)).normalized;
        }

        // Where we stand around the player, degrees clockwise from the camera's forward: 0 = screen center, +-180 = behind
        private float ViewAngle(Vector3 fromPlayer)
        {
            Vector3 look = viewCamera != null ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up) : Vector3.zero;
            return look.sqrMagnitude < 1e-4f ? 0f : Vector3.SignedAngle(look, fromPlayer, Vector3.up);
        }

        // 1 = move closer, -1 = back off, 0 = inside the range band
        private float ApproachFactor(float distance)
        {
            float holdRange = HoldRange();
            if (holdRange <= 0f) return distance > attackRange * 0.8f ? 1f : 0f;
            if (distance > holdRange + RangeBand) return 1f;
            if (distance < holdRange - RangeBand) return -1f;
            return 0f;
        }

        // Push away from other enemies nearby, stronger the closer they are
        private Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var other in All)
            {
                if (other == this || !other.IsThinking) continue;
                Vector3 away = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                float d = away.magnitude;
                if (d >= separationRadius) continue;
                push += (d > 0.01f ? away / d : transform.right) * (1f - d / separationRadius);
            }
            return push * separationWeight;
        }

        // Slide around level geometry: the first free direction of the wanted one turned step by step
        private Vector3 AvoidWalls(Vector3 move)
        {
            if (move.sqrMagnitude < 0.01f) return move;
            foreach (float angle in SteerAngles)
            {
                Vector3 dir = Quaternion.AngleAxis(angle * strafeSign, Vector3.up) * move;
                if (!IsBlocked(dir)) return dir;
            }
            return Vector3.zero;
        }

        private bool IsBlocked(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-4f) return false;
            if (EnemyPocket.Blocks(body.Center, dir, controller.radius)) return true;
            return Physics.SphereCast(body.Center, controller.radius * 0.9f, dir.normalized, out _, probeDistance, sightMask, QueryTriggerInteraction.Ignore);
        }

        // Distance to keep from the target: inside the first attack spell's range, scaled per enemy. 0 = melee only
        private float HoldRange()
        {
            if (caster == null) return 0f;
            for (int i = 0; i < caster.SlotCount; i++)
            {
                var spell = caster.GetSpell(i);
                if (spell == null || spell.AiUse != SpellAiUse.Attack) continue;
                float min = spell.AiMinRange + RangeBand;
                float range = spell.AiMaxRange * holdRangeFraction * rangeScale;
                return Mathf.Max(min, Mathf.Min(range, spell.AiMaxRange - RangeBand));
            }
            return 0f;
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to, Transform target)
        {
            if (!Physics.Linecast(from, to, out var hit, sightMask, QueryTriggerInteraction.Ignore)) return true;
            return hit.transform.IsChildOf(target) || hit.transform.IsChildOf(transform);
        }

        private static Vector3 AimPoint(Transform target) =>
            target.TryGetComponent(out HostBody host) ? host.Center : target.position;

        private Transform FindTarget()
        {
            if (!aggressive || player == null || player.Health.IsDead) return null;
            // Enemies always know where the player is
            Transform target = player.ActiveBody;
            return target == transform ? null : target;
        }

        private void Wander(ref ControlInput input)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(wanderTarget - transform.position, Vector3.up);
            // Reached, or a wall in the way: pick another spot
            if (toTarget.magnitude < 0.5f || IsBlocked(toTarget))
            {
                if (Time.time < nextWanderTime) return;
                Vector2 random = Random.insideUnitCircle * wanderRadius;
                wanderTarget = home + new Vector3(random.x, 0f, random.y);
                nextWanderTime = Time.time + wanderPause;
                return;
            }

            input.Move = toTarget.normalized * 0.5f;
            input.Aim = toTarget.normalized;
            nextWanderTime = Time.time + wanderPause;
        }

        private void Bark()
        {
            if (barks == null || barks.Length == 0 || Random.value > barkChance) return;
            var clip = barks[Random.Range(0, barks.Length)];
            if (clip != null) AudioSource.PlayClipAtPoint(clip, body.Center, barkVolume);
        }

        // Ring on the ground under the target (a Nova: under us) while a splash spell winds up, so the player can step out of it
        private void UpdateAimRing(Transform target)
        {
            bool show = target != null && aimRingMaterial != null && castSpell != null && castSpell.SplashRadius > 0f
                        && caster != null && caster.IsCasting;
            if (!show)
            {
                if (aimRing != null) aimRing.enabled = false;
                return;
            }

            if (aimRing == null) aimRing = CreateAimRing();
            if (!aimRing.enabled)
            {
                float radius = castSpell.SplashRadius;
                for (int i = 0; i < RingSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / RingSegments;
                    aimRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
                aimRing.enabled = true;
            }

            bool nova = castSpell.Kind == SpellKind.Nova;
            Vector3 point = nova ? transform.position : target.position;
            Vector3 from = nova ? body.Center : AimPoint(target);
            if (Physics.Raycast(from, Vector3.down, out var hit, 10f, sightMask, QueryTriggerInteraction.Ignore)) point = hit.point;
            aimRing.transform.position = point + Vector3.up * 0.05f;
        }

        private LineRenderer CreateAimRing()
        {
            var ring = new GameObject($"{name} AimRing").AddComponent<LineRenderer>();
            ring.sharedMaterial = aimRingMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.widthMultiplier = aimRingWidth;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;
            return ring;
        }

        // Debug: grey = not seen by the player, yellow = seen without a token, red = holds a token
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || body == null || !IsThinking || AttackDirector.Instance == null) return;
            Gizmos.color = AttackDirector.Instance.HoldsToken(this) ? Color.red : IsSeenByPlayer ? Color.yellow : Color.gray;
            Gizmos.DrawSphere(body.Center + Vector3.up * 1.5f, 0.25f);
        }
    }
}
