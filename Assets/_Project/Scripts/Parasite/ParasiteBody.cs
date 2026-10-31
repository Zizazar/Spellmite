using System;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

namespace Spellmite
{
    public enum ParasiteState { Free, Leaping, Latching, Controlling }

    // Kinematic wall-crawling parasite. Free -> Leaping -> Latching -> Controlling -> (exit leap) -> Leaping/Free.
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
        [SerializeField, Tooltip("Min angle (deg) between surfaces to climb onto the one ahead")] private float climbAngle = 25f;
        [SerializeField] private float rotationSharpness = 15f;

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

        [Header("Bite")]
        [SerializeField] private float biteDamage = 5f;
        [SerializeField] private float biteRadius = 0.5f;
        [SerializeField] private float biteCooldown = 0.4f;
        [SerializeField] private float biteLunge = 5f;

        [Header("Damage")]
        [SerializeField, Tooltip("Cap on knockback speed, so the light parasite isn't launched across the map")] private float maxKnockbackSpeed = 6f;
        [SerializeField] private float respawnDelay = 1.5f;

        private Health health;
        private BuffHolder buffs;
        private ControlInput input;
        private Vector3 velocity;
        private Vector3 up = Vector3.up;
        private bool onSurface;
        private Quaternion moveFrame = Quaternion.identity;
        private bool moveFrameActive;
        private int airLeaps;
        private float nextLeapTime;
        private float nextBiteTime;
        private float leapEndTime;
        private float invulnerableUntil;
        private float blockUntil;
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
        public Vector3 Velocity => velocity;
        public Vector3 SurfaceUp => up;
        public bool IsOnSurface => State == ParasiteState.Free && onSurface;
        public bool IsHosted => State is ParasiteState.Latching or ParasiteState.Controlling;

        // What the camera and enemies should look at
        public Transform ActiveBody => IsHosted && Host != null ? Host.transform : transform;
        public BodyProfile ActiveProfile => IsHosted && Host != null ? Host.Profile : profile;
        public Vector3 ActiveVelocity => IsHosted && Host != null ? Host.Velocity : velocity;

        private void Awake()
        {
            health = GetComponent<Health>();
            buffs = GetComponent<BuffHolder>();
            var trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = radius;

            health.Damaged += OnDamaged;
            health.Died += OnDied;
            spawnPosition = transform.position;
            visualBaseScale = visual.localScale;
            airLeaps = maxAirLeaps;
        }

        public void SetInput(ControlInput value) => input = value;

        private void Update()
        {
            if (health.IsDead) return;

            float dt = Time.deltaTime;
            health.Invulnerable = IsHosted || Time.time < invulnerableUntil;
            if (State == ParasiteState.Free && buffs != null && buffs.IsStunned) input = default;

            switch (State)
            {
                case ParasiteState.Free: UpdateFree(dt); break;
                case ParasiteState.Leaping: UpdateLeap(dt); break;
                default: UpdateHosted(dt); break;
            }

            AimedHost = State is ParasiteState.Free or ParasiteState.Controlling
                ? FindLeapTarget(input.AimOrigin, input.Aim, Host, out _)
                : null;
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

        // ---------- Free ----------

        private void UpdateFree(float dt)
        {
            if (TryLeapFromInput()) return;
            if (input.AttackPressed && Time.time >= nextBiteTime) Bite();

            if (onSurface) Crawl(dt);
            else Fly(dt, 1f, false);
        }

        private bool TryLeapFromInput()
        {
            if (!input.LeapPressed || Time.time < nextLeapTime) return false;
            bool airborne = !onSurface || State == ParasiteState.Leaping;
            if (airborne && airLeaps <= 0) return false;

            // RMB only leaps at hosts; with nothing in the crosshair it does nothing
            var target = FindLeapTarget(input.AimOrigin, input.Aim, null, out _);
            if (target == null) return false;

            if (airborne) airLeaps--;
            StartLeap(target, Vector3.zero);
            return true;
        }

        private void Crawl(float dt)
        {
            if (input.JumpPressed)
            {
                velocity = Vector3.ProjectOnPlane(velocity, up) + (up + Vector3.up * 0.3f).normalized * profile.JumpSpeed;
                onSurface = false;
                return;
            }

            Vector3 move = SurfaceMove(input.Move);
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
                    if (IsHost(hit.collider))
                    {
                        // Can't crawl through hosts
                        velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
                        step = dir * Mathf.Max(0f, hit.distance - 0.01f);
                    }
                    else if (Vector3.Angle(hit.normal, up) >= climbAngle)
                    {
                        // Concave corner: climb onto the wall/ceiling ahead
                        transform.position += dir * Mathf.Max(0f, hit.distance - 0.01f);
                        ChangeSurface(hit.normal);
                        return;
                    }
                }
                transform.position += step;
            }

            if (!StickToSurface() && !WrapAroundEdge()) onSurface = false;

            // Back on a floor: input is camera-relative again
            if (Vector3.Dot(up, Vector3.up) > 0.7f) moveFrameActive = false;
        }

        // Camera-relative input mapped onto the current surface. Pushing into a wall climbs it.
        // After crossing an edge or corner the mapping is carried along (moveFrame), so holding a direction keeps crawling around.
        private Vector3 SurfaceMove(Vector3 worldMove)
        {
            Vector3 move;
            if (moveFrameActive)
            {
                move = Vector3.ProjectOnPlane(moveFrame * worldMove, up);
            }
            else
            {
                move = Vector3.ProjectOnPlane(worldMove, up);
                Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, up); // zero on floors and ceilings
                move += wallUp * Vector3.Dot(worldMove, -up);
            }
            return move.sqrMagnitude > 1e-4f ? move.normalized * worldMove.magnitude : Vector3.zero;
        }

        // Rotate velocity and the input mapping together with the surface
        private void ChangeSurface(Vector3 newUp)
        {
            Quaternion turn = Quaternion.FromToRotation(up, newUp);
            Vector3 move = SurfaceMove(input.Move);
            float speed = velocity.magnitude;

            up = newUp;
            velocity = Vector3.ProjectOnPlane(turn * velocity, up).normalized * speed;

            if (moveFrameActive)
            {
                moveFrame = turn * moveFrame;
            }
            else if (move.sqrMagnitude > 1e-4f)
            {
                moveFrame = Quaternion.FromToRotation(input.Move.normalized, (turn * move).normalized);
                moveFrameActive = true;
            }
        }

        private bool StickToSurface()
        {
            if (!Physics.Raycast(transform.position, -up, out var hit, radius + groundSnapDistance, surfaceMask, QueryTriggerInteraction.Ignore))
                return false;

            up = hit.normal;
            transform.position = hit.point + hit.normal * radius;
            return true;
        }

        // Convex edge: look back under the edge and continue on the face below it
        private bool WrapAroundEdge()
        {
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, up);
            if (tangent.sqrMagnitude < 0.01f) return false;

            Vector3 origin = transform.position - up * (radius + groundSnapDistance);
            if (!Physics.Raycast(origin, -tangent.normalized, out var hit, radius * 4f, surfaceMask, QueryTriggerInteraction.Ignore))
                return false;

            transform.position = hit.point + hit.normal * radius;
            ChangeSurface(hit.normal);
            return true;
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

                Land(hit.normal);
                return;
            }

            transform.position += step;
        }

        private void Land(Vector3 normal)
        {
            onSurface = true;
            up = normal;
            moveFrameActive = false;
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
            Vector3 aim = Vector3.ProjectOnPlane(input.Aim, onSurface ? up : Vector3.up);
            aim = aim.sqrMagnitude > 1e-4f ? aim.normalized : transform.forward;
            velocity += aim * biteLunge;
            Punch(new Vector3(-0.15f, -0.15f, 0.4f));

            Vector3 center = transform.position + aim * (radius + biteRadius * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(center, biteRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlapBuffer[i].GetComponentInParent<Health>();
                if (target != null && target != health) target.TakeDamage(biteDamage, aim * 2f);
            }
        }

        // ---------- Leap ----------

        private void StartLeap(HostBody target, Vector3 launchVelocity)
        {
            State = ParasiteState.Leaping;
            leapTarget = target;
            onSurface = false;
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
            Punch(new Vector3(0.45f, -0.35f, 0.45f));
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

            if (input.LeapPressed)
            {
                ExitHost();
                return;
            }

            if (Host.Resistance01 <= 0f)
            {
                Eject();
                return;
            }

            ControlInput forwarded = input;
            forwarded.LeapPressed = false;
            Host.SetInput(forwarded);
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

        private HostBody ReleaseHost()
        {
            HostBody host = Host;
            Host = null;
            if (host != null) host.Release();

            blockedHost = host;
            blockUntil = Time.time + relatchBlockTime;
            invulnerableUntil = Time.time + exitInvulnerability;
            onSurface = false;
            airLeaps = maxAirLeaps;
            return host;
        }

        // ---------- Visuals / damage ----------

        private void UpdateRotation(float dt)
        {
            Vector3 targetUp = onSurface ? up : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(velocity, targetUp);
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

        private async UniTaskVoid Respawn()
        {
            visual.gameObject.SetActive(false);
            State = ParasiteState.Free;
            velocity = Vector3.zero;
            onSurface = false;

            await UniTask.Delay(TimeSpan.FromSeconds(respawnDelay), cancellationToken: destroyCancellationToken);

            transform.position = spawnPosition;
            up = Vector3.up;
            health.ResetHealth();
            invulnerableUntil = Time.time + 1f;
            visual.gameObject.SetActive(true);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + up * 0.5f);
        }
    }
}
