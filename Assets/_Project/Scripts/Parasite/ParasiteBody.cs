using System;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    public enum ParasiteState { Free, Leaping, Latching, Controlling }

    // Kinematic crawling parasite. Free -> Leaping -> Latching -> Controlling -> (exit leap) -> Leaping/Free.
    // It crawls on floors only; running into a wall scrambles a bit up it, then it slides off.
    // While controlling it forwards its brain's input to the host.
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Health), typeof(SphereCollider))]
    public class ParasiteBody : MonoBehaviour
    {
        [SerializeField] private BodyProfile profile;
        [SerializeField] private Transform visual;
        [SerializeField] private LayerMask surfaceMask = 1;
        [SerializeField] private LayerMask hostMask;

        [Header("Crawling")]
        [SerializeField] private float radius = 0.2f;
        [SerializeField] private float groundSnapDistance = 0.3f;
        [SerializeField, Tooltip("Steeper surfaces are walls: no crawling on them, only a short scramble and a slide")] private float maxFloorAngle = 45f;
        [SerializeField] private float rotationSharpness = 15f;

        [Header("Walls")]
        [SerializeField, Tooltip("How far up a wall a full-sprint run carries the parasite (less when slower)")] private float maxScrambleHeight = 1f;
        [SerializeField, Tooltip("Speed into the wall needed to scramble up it; slower just bumps")] private float minScrambleSpeed = 2.5f;
        [SerializeField] private float wallGravity = 15f;
        [SerializeField] private float wallSlideSpeed = 2f;
        [SerializeField, Tooltip("How fast sideways speed fades on a wall")] private float wallSideFriction = 10f;
        [SerializeField, Tooltip("Horizontal push of a wall jump; the upward part is the profile's jump")] private float wallJumpSpeed = 6f;

        [Header("Leap")]
        [SerializeField] private float leapSpeed = 24f;
        [SerializeField] private float leapRange = 14f;
        [SerializeField, Tooltip("Aim assist cone (deg) for picking a host")] private float leapAssistAngle = 8f;
        [SerializeField] private float leapMaxTime = 0.8f;
        [SerializeField] private float leapCooldown = 0.2f;
        [SerializeField, Tooltip("Gravity during a leap without a target")] private float leapGravityScale = 0.35f;
        [SerializeField] private int maxAirLeaps = 1;
        [SerializeField] private float latchContactDistance = 0.6f;

        [Header("Latch & exit")]
        [SerializeField] private float latchDuration = 0.4f;
        [SerializeField] private float exitSpeed = 15f;
        [SerializeField] private float exitUpSpeed = 4f;
        [SerializeField] private float ejectSpeed = 9f;
        [SerializeField, Tooltip("Can't re-latch the host you just left for this long")] private float relatchBlockTime = 0.6f;
        [SerializeField] private float exitInvulnerability = 0.3f;
        [SerializeField, Tooltip("Blood burst at the host's latch point when the parasite bites in")] private GameObject latchVfx;

        [Header("Losing control")]
        [SerializeField, Range(0f, 1f), Tooltip("Below this the HUD warns: edge vignette, the leap slot pulses")] private float lowControl = 0.25f;
        [SerializeField, Range(0f, 1f), Tooltip("Below this the host twitches and the next body to leap to gets brackets")] private float criticalControl = 0.1f;
        [SerializeField, Tooltip("Real seconds the game nearly freezes when the host throws the parasite off")] private float thrownOffHitstop = 0.12f;
        [SerializeField, Range(0f, 1f)] private float thrownOffTimeScale = 0.05f;

        [Header("Bite")]
        [SerializeField] private float biteDamage = 5f;
        [SerializeField] private float biteRadius = 0.5f;
        [SerializeField] private float biteCooldown = 0.4f;
        [SerializeField] private float biteLunge = 5f;

        [Header("Input forgiveness")]
        [SerializeField, Tooltip("A press while busy, on cooldown or without a target still fires within this many seconds")] private float inputBuffer = 0.15f;
        [SerializeField, Tooltip("Jump still works this long after crawling off a floor")] private float coyoteTime = 0.12f;

        [Header("Damage")]
        [SerializeField, Tooltip("Cap on knockback speed, so the light parasite isn't launched across the map")] private float maxKnockbackSpeed = 6f;
        [SerializeField, Tooltip("Only without a DeathTransition in the scene; it sets its own timing")] private float respawnDelay = 1.5f;

        [Header("Spawn pockets")]
        [SerializeField, Tooltip("Push out of an EnemyPocket at its back wall, in multiples of the body's acceleration. Walking in, the push wins past 1/this of the depth")] private float pocketPush = 4f;

        private Health health;
        private BuffHolder buffs;
        private SpellThief thief;
        private SphereCollider trigger;
        private ControlInput input;
        private Vector3 velocity;
        private Vector3 up = Vector3.up;
        private bool onSurface;
        private bool onWall;            // only meaningful while onSurface
        private Vector3 wallUp;         // up along the wall
        private Vector3 lastWallNormal; // wall jumped off since the last landing
        private int airLeaps;
        private float nextLeapTime;
        private float nextBiteTime;
        private float leapEndTime;
        private float invulnerableUntil;
        private float blockUntil;
        // Buffered presses: valid while Time.time <= the time, -1 = none
        private float jumpBufferedUntil = -1f;
        private float biteBufferedUntil = -1f;
        private float leapBufferedUntil = -1f;
        private float lastFloorTime = -1f;
        private float latchTimer;
        private HostBody leapTarget;
        private HostBody blockedHost;
        private Vector3 latchStartPosition;
        private Quaternion latchStartRotation;
        private Vector3 spawnPosition;
        private Vector3 visualBaseScale;
        private Tween punchTween;
        private readonly RaycastHit[] castBuffer = new RaycastHit[16];
        private readonly Collider[] overlapBuffer = new Collider[8];

        public ParasiteState State { get; private set; }
        public HostBody Host { get; private set; }
        public HostBody AimedHost { get; private set; }
        public Health Health => health;
        public SpellThief Thief => thief;
        public Vector3 Velocity => velocity;
        public bool IsOnSurface => State == ParasiteState.Free && onSurface;
        public bool IsHosted => State is ParasiteState.Latching or ParasiteState.Controlling;

        // What the camera and enemies should look at
        public Transform ActiveBody => IsHosted && Host != null ? Host.transform : transform;
        public BodyProfile ActiveProfile => IsHosted && Host != null ? Host.Profile : profile;
        public Vector3 ActiveVelocity => IsHosted && Host != null ? Host.Velocity : velocity;
        // The brain's sprint, for the camera (the host gets the same input)
        public bool IsSprinting => input.Sprint;
        // Control running out: 0 at lowControl, 1 when the host is about to throw the parasite off
        public float ControlDanger01 => IsControlLow ? 1f - Host.Resistance01 / Mathf.Max(0.001f, lowControl) : 0f;
        public bool IsControlLow => IsHosted && Host != null && !Host.EndlessPossession && Host.Resistance01 <= lowControl;
        public bool IsControlCritical => IsControlLow && Host.Resistance01 <= criticalControl;
        // While control is critical and nothing is aimed at: the host to leap to that needs the least turning
        public HostBody SuggestedHost { get; private set; }
        // The host ran out of resistance and threw the parasite off (not on a manual exit or the host's death)
        public event Action ThrownOff;
        // For ParasiteAudio
        public event Action Bit;
        public event Action Latched;

        private void Awake()
        {
            health = GetComponent<Health>();
            buffs = GetComponent<BuffHolder>();
            thief = GetComponent<SpellThief>();
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = radius;

            health.Damaged += OnDamaged;
            health.Died += OnDied;
            spawnPosition = transform.position;
            visualBaseScale = visual.localScale;
            airLeaps = maxAirLeaps;
        }

        public void SetInput(ControlInput value) => input = value;

        // Where the parasite comes back after dying
        public void SetSpawnPoint(Vector3 position) => spawnPosition = position;

        // Straight into the host from wherever the parasite is (the tutorial puts it back into a body after a fall)
        public void Possess(HostBody host)
        {
            if (health.IsDead || host == null || host.IsPossessed) return;
            if (IsHosted) ReleaseHost();
            transform.position = host.LatchPoint.position;
            BeginLatch(host);
        }

        private void Update()
        {
            if (health.IsDead) return;

            float dt = Time.deltaTime;
            health.Invulnerable = IsHosted || Time.time < invulnerableUntil;
            if (State == ParasiteState.Free && buffs != null && buffs.IsStunned) input = default;
            BufferPresses();
            PushOutOfPockets(dt);
            if (!IsHosted) PushOutOfWalls();

            switch (State)
            {
                case ParasiteState.Free: UpdateFree(dt); break;
                case ParasiteState.Leaping: UpdateLeap(dt); break;
                default: UpdateHosted(dt); break;
            }

            AimedHost = State is ParasiteState.Free or ParasiteState.Controlling
                ? FindLeapTarget(input.AimOrigin, input.Aim, Host, out _)
                : null;
            SuggestedHost = AimedHost == null && IsControlCritical ? FindClosestToAim(input.AimOrigin, input.Aim) : null;
        }

        private void LateUpdate()
        {
            if (health.IsDead) return;

            if (IsHosted && Host != null)
            {
                Transform point = Host.LatchPoint;
                float t = State == ParasiteState.Latching ? Mathf.SmoothStep(0f, 1f, latchTimer / latchDuration) : 1f;
                transform.SetPositionAndRotation(
                    Vector3.Lerp(latchStartPosition, point.position, t),
                    Quaternion.Slerp(latchStartRotation, point.rotation, t));
                up = point.up;
                return;
            }

            UpdateRotation(Time.deltaTime);
        }

        // Spawn pockets are for enemies: the player (or the host it drives) is eased back out, harder the deeper it goes
        private void PushOutOfPockets(float dt)
        {
            bool controlling = State == ParasiteState.Controlling && Host != null;
            if (State != ParasiteState.Free && !controlling) return;

            Vector3 push = EnemyPocket.PushAt(controlling ? Host.Center : transform.position) * (pocketPush * ActiveProfile.Acceleration * dt);
            if (push == Vector3.zero) return;
            if (controlling)
            {
                Host.AddImpulse(push * Host.Profile.Mass);
                return;
            }
            // Crawling on the back wall the push is along its normal and would be projected away: let go instead
            if (onSurface && Vector3.Dot(push.normalized, up) > 0.5f) onSurface = false;
            velocity += push;
        }

        // SphereCast ignores colliders it starts inside of, so a parasite left overlapping a wall
        // (crawling stops a hair closer than the full radius, latch points, knockback) would fly through it: step back out first
        private void PushOutOfWalls()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer, surfaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var other = overlapBuffer[i];
                if (!Physics.ComputePenetration(trigger, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation, out var dir, out var depth))
                    continue;
                if (depth < 0.005f) continue; // just resting on it
                transform.position += dir * (depth + 0.01f);
                float into = Vector3.Dot(velocity, dir);
                if (into < 0f) velocity -= dir * into;
            }
        }

        private void BufferPresses()
        {
            float until = Time.time + inputBuffer;
            if (input.JumpPressed) jumpBufferedUntil = until;
            if (input.AttackPressed) biteBufferedUntil = until;
            if (input.LeapPressed) leapBufferedUntil = until;
        }

        private bool JumpBuffered => Time.time <= jumpBufferedUntil;

        // ---------- Free ----------

        private void UpdateFree(float dt)
        {
            if (TryLeapFromInput()) return;
            if (Time.time <= biteBufferedUntil && Time.time >= nextBiteTime) Bite();

            if (onSurface && onWall) UpdateWall(dt);
            else if (onSurface) Crawl(dt);
            else if (!TryCoyoteJump()) Fly(dt, 1f, false);
        }

        // A buffered press keeps trying while it lasts, so RMB a moment before a host enters the crosshair still leaps
        private bool TryLeapFromInput()
        {
            if (Time.time > leapBufferedUntil || Time.time < nextLeapTime) return false;
            bool airborne = !onSurface || State == ParasiteState.Leaping;
            if (airborne && airLeaps <= 0) return false;

            // RMB only leaps at hosts; with nothing in the crosshair it does nothing
            var target = FindLeapTarget(input.AimOrigin, input.Aim, null, out _);
            if (target == null) return false;

            if (airborne) airLeaps--;
            leapBufferedUntil = -1f;
            StartLeap(target, Vector3.zero);
            return true;
        }

        private void Crawl(float dt)
        {
            lastFloorTime = Time.time;
            if (JumpBuffered)
            {
                FloorJump();
                return;
            }

            // Camera-relative input laid onto the floor (ramps included)
            Vector3 move = Vector3.ProjectOnPlane(input.Move, up);
            move = move.sqrMagnitude > 1e-4f ? move.normalized * input.Move.magnitude : Vector3.zero;
            float speed = (input.Sprint ? profile.SprintSpeed : profile.WalkSpeed) * (buffs != null ? buffs.SpeedMultiplier : 1f);
            float rate = move.sqrMagnitude > 0.01f ? profile.Acceleration : profile.Deceleration;
            velocity = Vector3.MoveTowards(Vector3.ProjectOnPlane(velocity, up), move * speed, rate * dt);

            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance > 1e-5f)
            {
                Vector3 dir = step / distance;
                if (Physics.SphereCast(transform.position, radius * 0.9f, dir, out var hit, distance, surfaceMask | hostMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 contact = dir * Mathf.Max(0f, hit.distance - 0.01f);
                    bool host = IsHost(hit.collider);

                    if (!host && IsFloor(hit.normal))
                    {
                        // Ramp ahead: carry on along it
                        transform.position += contact;
                        speed = velocity.magnitude;
                        up = hit.normal;
                        velocity = Vector3.ProjectOnPlane(velocity, up).normalized * speed;
                        return;
                    }

                    float into = Vector3.Dot(velocity, -HorizontalNormal(hit.normal));
                    if (!host && IsWall(hit.normal) && into >= minScrambleSpeed)
                    {
                        // Ran into a wall: scramble up it, higher the faster the run
                        transform.position += contact;
                        StartWall(hit.normal, MaxClimbSpeed * Mathf.Clamp01(into / profile.SprintSpeed));
                        return;
                    }

                    // Hosts, ceilings and slow bumps into walls just block
                    velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
                    step = contact;
                }
                transform.position += step;
            }

            // Walking off a ledge falls
            if (!StickToSurface()) onSurface = false;
        }

        // Off the floor along its normal (up keeps the last floor's normal after crawling off it)
        private void FloorJump()
        {
            velocity = Vector3.ProjectOnPlane(velocity, up) + (up + Vector3.up * 0.3f).normalized * profile.JumpSpeed;
            onSurface = false;
            jumpBufferedUntil = -1f;
            lastFloorTime = -1f;
        }

        // Crawled off a ledge a moment ago: still jumps as if on the floor
        private bool TryCoyoteJump()
        {
            if (!JumpBuffered || Time.time - lastFloorTime > coyoteTime) return false;
            FloorJump();
            return true;
        }

        private bool StickToSurface()
        {
            if (!Physics.Raycast(transform.position, -up, out var hit, radius + groundSnapDistance, surfaceMask, QueryTriggerInteraction.Ignore)
                || !IsFloor(hit.normal))
                return false;

            up = hit.normal;
            transform.position = hit.point + hit.normal * radius;
            return true;
        }

        // ---------- Walls ----------

        private float MaxClimbSpeed => Mathf.Sqrt(2f * wallGravity * maxScrambleHeight);

        private bool IsFloor(Vector3 normal) => Vector3.Angle(normal, Vector3.up) <= maxFloorAngle;

        private bool IsWall(Vector3 normal) => !IsFloor(normal) && normal.y > -0.3f;

        private static Vector3 HorizontalNormal(Vector3 normal) => Vector3.ProjectOnPlane(normal, Vector3.up).normalized;

        private void StartWall(Vector3 normal, float climbSpeed)
        {
            onSurface = true;
            onWall = true;
            up = normal;
            wallUp = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            lastWallNormal = normal;

            Vector3 side = Vector3.ProjectOnPlane(velocity, normal);
            side -= wallUp * Vector3.Dot(side, wallUp);
            velocity = side + wallUp * climbSpeed;

            if (State == ParasiteState.Leaping)
            {
                State = ParasiteState.Free;
                leapTarget = null;
            }
        }

        // Scramble up while the run's momentum lasts, then slide down slowly and drop off
        private void UpdateWall(float dt)
        {
            if (JumpBuffered)
            {
                WallJump();
                return;
            }

            // Pulling away from the wall lets go
            if (Vector3.Dot(input.Move, up) > 0.5f)
            {
                onSurface = false;
                return;
            }

            float climb = Mathf.Max(Vector3.Dot(velocity, wallUp) - wallGravity * dt, -wallSlideSpeed);
            Vector3 side = Vector3.ProjectOnPlane(velocity, up);
            side -= wallUp * Vector3.Dot(side, wallUp);
            side = Vector3.MoveTowards(side, Vector3.zero, wallSideFriction * dt);
            velocity = side + wallUp * climb;

            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance > 1e-5f)
            {
                Vector3 dir = step / distance;
                if (Physics.SphereCast(transform.position, radius * 0.9f, dir, out var hit, distance, surfaceMask | hostMask, QueryTriggerInteraction.Ignore))
                {
                    transform.position += dir * Mathf.Max(0f, hit.distance - 0.01f);
                    if (!IsHost(hit.collider) && IsFloor(hit.normal)) Land(hit.normal);
                    else velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
                    return;
                }
                transform.position += step;
            }

            if (StickToWall()) return;

            // Ran out of wall: over the top onto the ledge, otherwise fall
            if (climb <= 0f || !TryMantle()) onSurface = false;
        }

        private bool StickToWall()
        {
            if (!Physics.Raycast(transform.position, -up, out var hit, radius + groundSnapDistance, surfaceMask, QueryTriggerInteraction.Ignore)
                || !IsWall(hit.normal))
                return false;

            up = hit.normal;
            wallUp = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            transform.position = hit.point + hit.normal * radius;
            return true;
        }

        // Scrambled past the top of a low wall: step onto the ledge
        private bool TryMantle()
        {
            Vector3 over = transform.position - HorizontalNormal(up) * (radius * 1.5f) + Vector3.up * radius;
            if (!Physics.Raycast(over, Vector3.down, out var hit, radius * 3f, surfaceMask, QueryTriggerInteraction.Ignore)
                || !IsFloor(hit.normal))
                return false;

            velocity = -HorizontalNormal(up) * (profile.WalkSpeed * 0.5f);
            transform.position = hit.point + hit.normal * radius;
            Land(hit.normal);
            return true;
        }

        // Up and away from the wall, steered by the input (or the camera without input), never back into it
        private void WallJump()
        {
            Vector3 away = HorizontalNormal(up);
            Vector3 steer = input.Move.sqrMagnitude > 0.01f ? input.Move : Vector3.ProjectOnPlane(input.Aim, Vector3.up);
            steer -= away * Mathf.Min(0f, Vector3.Dot(steer, away));

            velocity = (away + steer.normalized).normalized * wallJumpSpeed + Vector3.up * profile.JumpSpeed;
            onSurface = false;
            jumpBufferedUntil = -1f;
            Punch(new Vector3(-0.15f, 0.3f, -0.15f));
        }

        private void Fly(float dt, float gravityScale, bool leaping)
        {
            velocity += Vector3.down * (profile.Gravity * gravityScale * dt);

            if (!leaping && input.Move.sqrMagnitude > 0.01f)
            {
                Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
                float maxSpeed = Mathf.Max(horizontal.magnitude, profile.SprintSpeed);
                horizontal = Vector3.MoveTowards(horizontal, input.Move * maxSpeed, profile.Acceleration * profile.AirControl * dt);
                velocity = horizontal + Vector3.up * velocity.y;
            }

            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance < 1e-6f) return;
            Vector3 dir = step / distance;

            if (Physics.SphereCast(transform.position, radius, dir, out var hit, distance, surfaceMask | hostMask, QueryTriggerInteraction.Ignore))
            {
                transform.position += dir * hit.distance;
                var host = hit.collider.GetComponentInParent<HostBody>();
                if (host != null)
                {
                    if (leaping && CanLatch(host))
                    {
                        BeginLatch(host);
                    }
                    else
                    {
                        velocity = Vector3.Reflect(velocity, hit.normal) * 0.3f;
                        State = ParasiteState.Free;
                    }
                    return;
                }

                if (IsFloor(hit.normal))
                {
                    Land(hit.normal);
                }
                else if (IsWall(hit.normal))
                {
                    // Jumping into a wall carries on up it a little; the wall just jumped off gives no extra height
                    float climb = Mathf.Min(velocity.y, MaxClimbSpeed);
                    if (Vector3.Dot(hit.normal, lastWallNormal) > 0.9f) climb = Mathf.Min(climb, 0f);
                    StartWall(hit.normal, climb);
                }
                else
                {
                    // Ceiling: bump along it
                    velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
                }
                return;
            }

            transform.position += step;
        }

        private void Land(Vector3 normal)
        {
            onSurface = true;
            onWall = false;
            lastWallNormal = Vector3.zero;
            up = normal;
            airLeaps = maxAirLeaps;
            velocity = Vector3.ProjectOnPlane(velocity, up);
            if (State == ParasiteState.Leaping)
            {
                State = ParasiteState.Free;
                leapTarget = null;
            }
            StickToSurface();
        }

        private void Bite()
        {
            nextBiteTime = Time.time + biteCooldown;
            biteBufferedUntil = -1f;
            Vector3 aim = Vector3.ProjectOnPlane(input.Aim, onSurface && !onWall ? up : Vector3.up);
            aim = aim.sqrMagnitude > 1e-4f ? aim.normalized : transform.forward;
            velocity += aim * biteLunge;
            Punch(new Vector3(-0.15f, -0.15f, 0.4f));
            Bit?.Invoke();

            Vector3 center = transform.position + aim * (radius + biteRadius * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(center, biteRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlapBuffer[i].GetComponentInParent<Health>();
                // The boss tree's bark is too hard for teeth: only possessed bodies hurt it
                if (target != null && target != health && !target.TryGetComponent(out TreeBoss _)) target.TakeDamage(biteDamage, aim * 2f);
            }
        }

        // ---------- Leap ----------

        private void StartLeap(HostBody target, Vector3 launchVelocity)
        {
            State = ParasiteState.Leaping;
            leapTarget = target;
            onSurface = false;
            lastFloorTime = -1f;
            velocity = target != null ? (target.LatchPoint.position - transform.position).normalized * leapSpeed : launchVelocity;
            leapEndTime = Time.time + leapMaxTime;
            nextLeapTime = Time.time + leapCooldown;
            Punch(new Vector3(-0.2f, -0.2f, 0.45f));
        }

        private void UpdateLeap(float dt)
        {
            if (TryLeapFromInput()) return;

            if (leapTarget != null && !CanLatch(leapTarget)) leapTarget = null;
            if (leapTarget != null)
            {
                // Instant capture: home straight in, touch = latch
                Vector3 toTarget = leapTarget.LatchPoint.position - transform.position;
                if (toTarget.magnitude <= latchContactDistance + leapSpeed * dt)
                {
                    BeginLatch(leapTarget);
                    return;
                }
                velocity = toTarget.normalized * leapSpeed;
            }

            Fly(dt, leapTarget != null ? 0f : leapGravityScale, true);

            if (State == ParasiteState.Leaping && Time.time >= leapEndTime)
            {
                State = ParasiteState.Free;
                leapTarget = null;
            }
        }

        private HostBody FindLeapTarget(Vector3 origin, Vector3 dir, HostBody ignore, out Vector3 aimPoint)
        {
            aimPoint = transform.position + transform.forward * leapRange;
            if (dir.sqrMagnitude < 1e-4f) return null;
            dir.Normalize();

            // Start the cast level with the parasite so nothing between the camera and it counts
            Vector3 start = origin + dir * Mathf.Max(0f, Vector3.Dot(transform.position - origin, dir));
            aimPoint = start + dir * leapRange;

            HostBody direct = null;
            float nearest = float.MaxValue;
            int count = Physics.SphereCastNonAlloc(start, 0.3f, dir, castBuffer, leapRange, surfaceMask | hostMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = castBuffer[i];
                if (hit.distance <= 0f) continue; // overlapping at start
                var host = hit.collider.GetComponentInParent<HostBody>();
                if (host != null && host == ignore) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                aimPoint = hit.point;
                direct = host;
            }
            if (direct != null && CanLatch(direct) && InLeapRange(direct)) return direct;

            // Aim assist: host closest to the aim ray inside a small cone
            HostBody best = null;
            float bestAngle = leapAssistAngle;
            foreach (var host in HostBody.All)
            {
                if (host == ignore || !CanLatch(host) || !InLeapRange(host)) continue;
                Vector3 point = host.LatchPoint.position;
                float angle = Vector3.Angle(dir, point - origin);
                if (angle >= bestAngle) continue;
                if (Physics.Linecast(transform.position, point, surfaceMask, QueryTriggerInteraction.Ignore)) continue;
                best = host;
                bestAngle = angle;
            }
            return best;
        }

        // Like the aim assist, without the cone
        private HostBody FindClosestToAim(Vector3 origin, Vector3 dir)
        {
            HostBody best = null;
            float bestAngle = float.MaxValue;
            foreach (var host in HostBody.All)
            {
                if (host == Host || !CanLatch(host) || !InLeapRange(host)) continue;
                Vector3 point = host.LatchPoint.position;
                float angle = Vector3.Angle(dir, point - origin);
                if (angle >= bestAngle) continue;
                if (Physics.Linecast(transform.position, point, surfaceMask, QueryTriggerInteraction.Ignore)) continue;
                best = host;
                bestAngle = angle;
            }
            return best;
        }

        private bool InLeapRange(HostBody host) =>
            (host.LatchPoint.position - transform.position).sqrMagnitude <= leapRange * leapRange;

        private bool CanLatch(HostBody host) =>
            host != null && !host.Health.IsDead && !host.IsPossessed && !(host == blockedHost && Time.time < blockUntil);

        private bool IsHost(Collider col) => (hostMask.value & (1 << col.gameObject.layer)) != 0;

        // ---------- Hosted ----------

        private void BeginLatch(HostBody host)
        {
            State = ParasiteState.Latching;
            Host = host;
            leapTarget = null;
            latchTimer = 0f;
            onSurface = false;
            velocity = Vector3.zero;
            airLeaps = maxAirLeaps;
            latchStartPosition = transform.position;
            latchStartRotation = transform.rotation;
            host.Possess();
            if (latchVfx != null) Destroy(Instantiate(latchVfx, host.LatchPoint.position, host.LatchPoint.rotation), 3f);
            Punch(new Vector3(0.45f, -0.35f, 0.45f));
            Latched?.Invoke();
        }

        private void UpdateHosted(float dt)
        {
            if (Host == null || Host.Health.IsDead)
            {
                Eject();
                return;
            }

            if (State == ParasiteState.Latching)
            {
                latchTimer += dt;
                Host.SetInput(default);
                if (latchTimer >= latchDuration) State = ParasiteState.Controlling;
                return;
            }

            if (Time.time <= leapBufferedUntil)
            {
                leapBufferedUntil = -1f;
                ExitHost();
                return;
            }

            if (Host.Resistance01 <= 0f)
            {
                ThrowOff();
                return;
            }

            ControlInput forwarded = input;
            forwarded.LeapPressed = false;
            thief.Devour(ref forwarded);
            Host.SetInput(forwarded);
            thief.CastStolen(Host, forwarded);
        }

        // Manual exit: a leap out of the body towards where the camera looks. Can chain straight into the next host.
        private void ExitHost()
        {
            HostBody host = ReleaseHost();
            var target = FindLeapTarget(input.AimOrigin, input.Aim, host, out _);
            Vector3 exitVelocity = input.Aim.normalized * exitSpeed + Vector3.up * exitUpSpeed + host.Velocity * 0.5f;
            StartLeap(target, exitVelocity);
        }

        // Host threw us off or died
        private void Eject()
        {
            HostBody host = ReleaseHost();
            Vector3 back = host != null ? -host.transform.forward : -transform.forward;
            velocity = (back + Vector3.up * 1.5f).normalized * ejectSpeed;
            State = ParasiteState.Free;
            Punch(new Vector3(0.3f, 0.3f, 0.3f));
        }

        // Control ran out: a beat the player can't miss. The host jerks, the camera shakes, time nearly stops for a moment
        private void ThrowOff()
        {
            HostBody host = Host;
            Eject();
            host.Flinch();
            BodyCamera.ShakeThrownOff();
            Hitstop().Forget();
            ThrownOff?.Invoke();
        }

        private async UniTaskVoid Hitstop()
        {
            float before = Time.timeScale;
            float stopped = before * thrownOffTimeScale;
            Time.timeScale = stopped;
            await UniTask.Delay(TimeSpan.FromSeconds(thrownOffHitstop), DelayType.UnscaledDeltaTime, cancellationToken: destroyCancellationToken)
                .SuppressCancellationThrow();
            // Someone else (the tutorial's slow motion) may have changed it meanwhile
            if (Mathf.Approximately(Time.timeScale, stopped)) Time.timeScale = before;
        }

        private HostBody ReleaseHost()
        {
            HostBody host = Host;
            Host = null;
            if (host != null)
            {
                host.Release();
                LeaveBodyClear(host);
            }

            blockedHost = host;
            blockUntil = Time.time + relatchBlockTime;
            invulnerableUntil = Time.time + exitInvulnerability;
            // Jump and attack presses made inside the host were the host's
            jumpBufferedUntil = biteBufferedUntil = -1f;
            onSurface = false;
            airLeaps = maxAirLeaps;
            return host;
        }

        // The latch point can be inside a wall the host leans on: come out of the body only as far as the way is clear
        private void LeaveBodyClear(HostBody host)
        {
            Vector3 from = host.Center;
            Vector3 path = transform.position - from;
            float length = path.magnitude;
            if (length < 1e-4f) return;
            Vector3 dir = path / length;
            if (Physics.SphereCast(from, radius, dir, out var hit, length, surfaceMask, QueryTriggerInteraction.Ignore))
                transform.position = from + dir * Mathf.Max(0f, hit.distance - 0.01f);
        }

        // ---------- Visuals / damage ----------

        private void UpdateRotation(float dt)
        {
            Vector3 targetUp = onSurface ? up : Vector3.up;
            // On a wall the head always points up, also while sliding down
            Vector3 forward = onSurface && onWall ? wallUp : Vector3.ProjectOnPlane(velocity, targetUp);
            if (forward.sqrMagnitude < 0.04f) forward = Vector3.ProjectOnPlane(transform.forward, targetUp);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(-transform.up, targetUp);
            if (forward.sqrMagnitude < 1e-4f) return;

            Quaternion target = Quaternion.LookRotation(forward.normalized, targetUp);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-rotationSharpness * dt));
        }

        private void Punch(Vector3 strength)
        {
            punchTween.Stop();
            visual.localScale = visualBaseScale;
            punchTween = Tween.PunchScale(visual, Vector3.Scale(strength, visualBaseScale), 0.25f);
        }

        private void OnDamaged(float amount, Vector3 impulse)
        {
            velocity += Vector3.ClampMagnitude(impulse / Mathf.Max(0.01f, profile.Mass), maxKnockbackSpeed);
            if (impulse.sqrMagnitude > 1f)
            {
                onSurface = false;
                if (State == ParasiteState.Leaping) State = ParasiteState.Free;
            }
            Punch(new Vector3(0.4f, -0.4f, 0.4f));
        }

        private void OnDied() => Respawn().Forget();

        // With a DeathTransition in the scene the move happens under its black, so the player never sees the teleport
        private async UniTaskVoid Respawn()
        {
            State = ParasiteState.Free;
            velocity = Vector3.zero;
            onSurface = false;

            DeathTransition transition = DeathTransition.Instance;
            if (transition != null)
            {
                punchTween.Stop();
                punchTween = Tween.Scale(visual, Vector3.zero, 0.2f, Ease.InBack, useUnscaledTime: true);
                await transition.Cover().AttachExternalCancellation(destroyCancellationToken);
            }
            else
            {
                await UniTask.Delay(TimeSpan.FromSeconds(respawnDelay), cancellationToken: destroyCancellationToken);
            }
            visual.gameObject.SetActive(false);

            transform.position = spawnPosition;
            up = Vector3.up;
            velocity = Vector3.zero;
            health.ResetHealth();
            invulnerableUntil = Time.time + 1f;
            punchTween.Stop();
            visual.localScale = visualBaseScale;
            visual.gameObject.SetActive(true);

            if (transition == null) return;
            await transition.Reveal().AttachExternalCancellation(destroyCancellationToken);
            Punch(new Vector3(0.5f, 0.5f, 0.5f));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + up * 0.5f);
        }
    }
}
