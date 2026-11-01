using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Spellmite
{
    // Boss of the top floor: a demon tree rooted in a planter at the end of the hall. It never moves. Every attack is
    // telegraphed first: an orb volley from the demon (glow at the cast point), energy pillars on closing rings under
    // the player, and from the second phase a beam at knee height swept from one corner of the hall to the other
    // (a thin flickering line shows where it starts, like the Stone Titan's laser; jump it or hide behind a pier).
    // The hall is kept stocked with skeletons that roll random spells: free they barely scratch the player, possessed
    // they are the only way to hurt the tree. Below each phase threshold the tree roars (invulnerable) and fights harder.
    [RequireComponent(typeof(Health))]
    public class TreeBoss : MonoBehaviour
    {
        private enum Attack { Orbs, Pillars, Beam }

        [Serializable]
        private class Phase
        {
            [field: SerializeField, Range(0f, 1f), Tooltip("Starts once health drops below this fraction")] public float Below { get; private set; } = 1f;
            [field: SerializeField, Min(0), Tooltip("Skeletons kept in the hall, possessed ones included")] public int Skeletons { get; private set; } = 3;
            [field: SerializeField, Tooltip("Random pause after each attack, seconds")] public Vector2 AttackPause { get; private set; } = new(2f, 3f);
            [field: SerializeField, Min(1)] public int Orbs { get; private set; } = 3;
            [field: SerializeField, Min(1)] public int Pillars { get; private set; } = 1;
            [field: SerializeField] public bool Beam { get; private set; }
            [field: SerializeField, Tooltip("The beam sweeps back again")] public bool BeamBack { get; private set; }

            public Phase() { }

            public Phase(float below, int skeletons, Vector2 attackPause, int orbs, int pillars, bool beam, bool beamBack)
            {
                Below = below;
                Skeletons = skeletons;
                AttackPause = attackPause;
                Orbs = orbs;
                Pillars = pillars;
                Beam = beam;
                BeamBack = beamBack;
            }
        }

        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int GrabHash = Animator.StringToHash("Grab");
        private static readonly int RoarHash = Animator.StringToHash("Roar");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const int RingSegments = 40;
        private const float BeamFlickerTime = 0.5f;

        [Header("Fight")]
        [SerializeField] private string nameKey = "boss.demon_tree";
        [SerializeField] private string subtitleKey = "boss.demon_tree.subtitle";
        [SerializeField, Tooltip("The fight starts when the player comes this close on the tree's floor")] private float startDistance = 30f;
        [SerializeField, Tooltip("Opening roar, seconds: invulnerable, the boss bar fills up meanwhile")] private float introDuration = 3f;
        [SerializeField, Tooltip("Roar between phases, seconds: invulnerable")] private float phaseRoarDuration = 2.5f;
        [SerializeField, Tooltip("Camera shake of a roar")] private float roarShake = 3f;
        [SerializeField, Tooltip("From full health down, thresholds descending")] private Phase[] phases =
        {
            new(1f, 3, new Vector2(2.5f, 3.5f), 3, 1, false, false),
            new(0.66f, 4, new Vector2(2f, 3f), 4, 3, true, false),
            new(0.33f, 5, new Vector2(1.2f, 2f), 5, 5, true, true),
        };

        [Header("Skeletons")]
        [SerializeField] private EnemyBrain skeletonPrefab;
        [SerializeField, Tooltip("Parent of the hall's spawn points (Tower/Floor5/SpawnPoints)")] private Transform spawnPoints;
        [SerializeField, Tooltip("Points closer to the player than this are skipped while there are others")] private float minSpawnDistance = 6f;
        [SerializeField, Tooltip("Seconds between two new skeletons")] private float spawnInterval = 2f;
        [SerializeField, Range(0f, 1f), Tooltip("A free skeleton's damage (spells and swings), fraction of normal")] private float skeletonDamageScale = 0.15f;
        [SerializeField, Tooltip("Skeletons roll their spells from these. Slot 1 always gets an Attack one, so a possessed skeleton can hurt the tree")] private List<SpellData> skeletonSpells = new();
        [SerializeField, Min(1)] private int spellsPerSkeleton = 3;

        [Header("Orbs")]
        [SerializeField, Tooltip("Projectile: damage, speed, homing, VFX. Its Cast Vfx glows at the cast point during the windup")] private SpellData orbSpell;
        [SerializeField] private Transform castPoint;
        [SerializeField] private float orbWindup = 1f;
        [SerializeField, Tooltip("Seconds between the orbs of a volley")] private float orbInterval = 0.3f;
        [SerializeField, Tooltip("Degrees across a volley")] private float orbSpread = 30f;

        [Header("Pillars")]
        [SerializeField, Tooltip("Barrage with its VFX on the ground: Damage, Knockback, Splash Radius, Barrage Delay (ring to hit), Area Vfx")] private SpellData pillarSpell;
        [SerializeField] private float pillarWindup = 0.5f;
        [SerializeField, Tooltip("Extra pillars land up to this far around the player")] private float pillarScatter = 5f;
        [SerializeField, Tooltip("Ring on the ground where a pillar will hit; it closes in until the hit")] private Material ringMaterial;
        [SerializeField] private float ringWidth = 0.1f;
        [SerializeField] private LayerMask groundMask = 1;

        [Header("Beam")]
        [SerializeField, Tooltip("Low on the trunk, forward into the hall. The beam stays at this height")] private Transform beamOrigin;
        [SerializeField, Tooltip("LaserBeamBlue (a LineRenderer)")] private LineRenderer beamLinePrefab;
        [SerializeField, Tooltip("LaserBeamEndBlue: sparks where the beam hits a wall")] private GameObject beamEndPrefab;
        [SerializeField, Tooltip("Seconds of the thin aiming line before the sweep; it flickers the last 0.5 s")] private float beamCharge = 1.8f;
        [SerializeField] private float beamAimWidth = 0.06f;
        [SerializeField] private float beamWidth = 0.5f;
        [SerializeField, Tooltip("Degrees from one side to the other")] private float beamSweepAngle = 150f;
        [SerializeField, Tooltip("Seconds for one sweep")] private float beamSweepTime = 3f;
        [SerializeField, Tooltip("Bodies this close to the beam's axis are hit, once per sweep")] private float beamRadius = 0.3f;
        [SerializeField] private float beamDamage = 20f;
        [SerializeField] private float beamKnockback = 8f;
        [SerializeField] private float beamLength = 60f;
        [SerializeField, Tooltip("Walls and piers stop the beam (bodies don't)")] private LayerMask beamBlockMask = 1;
        [SerializeField] private float beamTextureScroll = 5f;

        [Header("Visuals")]
        [SerializeField, Tooltip("The demon's. Triggers Attack, Grab, Roar")] private Animator animator;
        [SerializeField, Tooltip("Tinted for a moment on every hit")] private Renderer[] flashRenderers;
        [SerializeField] private Color flashColor = new(2.5f, 2.5f, 2.5f);
        [SerializeField] private float flashDuration = 0.12f;

        private readonly List<HostBody> skeletons = new();
        private readonly HashSet<Health> beamHit = new();
        private readonly RaycastHit[] beamBuffer = new RaycastHit[16];
        private Health health;
        private ParasiteBody player;
        private CancellationTokenSource fightCts;
        private MaterialPropertyBlock flashBlock;
        private Tween flashTween;
        private LineRenderer beamLine;
        private Material beamMaterial;
        private int phaseIndex;
        private Attack? lastAttack;
        private Attack? nextAttack;
        private float beamSign = 1f;
        private bool started;

        // Read by the boss bar and the HUD
        public string NameKey => nameKey;
        public string SubtitleKey => subtitleKey;
        public Health Health => health;
        public bool IsFightActive { get; private set; }
        public bool IsDefeated => health.IsDead;
        public float IntroDuration => introDuration;
        public int PhaseCount => phases.Length;
        public float PhaseThreshold(int phase) => phases[phase].Below;

        private Phase CurrentPhase => phases[phaseIndex];

        private void Awake()
        {
            health = GetComponent<Health>();
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            flashBlock = new MaterialPropertyBlock();

            if (beamLinePrefab != null)
            {
                beamLine = Instantiate(beamLinePrefab, transform);
                beamLine.useWorldSpace = true;
                beamLine.positionCount = 2;
                beamLine.enabled = false;
                beamMaterial = beamLine.material;
            }
        }

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
        }

        private void OnDestroy()
        {
            fightCts?.Cancel();
            fightCts?.Dispose();
        }

        private void Update()
        {
            if (started || health.IsDead || !PlayerNear()) return;
            started = true;
            fightCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            Fight(fightCts.Token).Forget();
        }

        private bool PlayerNear()
        {
            if (player == null || player.Health.IsDead) return false;
            Vector3 offset = player.ActiveBody.position - transform.position;
            return Mathf.Abs(offset.y) < 4f && new Vector2(offset.x, offset.z).magnitude <= startDistance;
        }

        private async UniTaskVoid Fight(CancellationToken token)
        {
            IsFightActive = true;
            KeepSkeletons(token).Forget();
            await Roar(introDuration, token);

            while (true)
            {
                // Past a threshold (or several at once): roar, then the new phase opens with its new attack
                int phase = PhaseForHealth();
                if (phase > phaseIndex)
                {
                    bool hadBeam = CurrentPhase.Beam;
                    phaseIndex = phase;
                    if (CurrentPhase.Beam && !hadBeam) nextAttack = Attack.Beam;
                    await Roar(phaseRoarDuration, token);
                    continue;
                }

                if (Target() == null)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    continue;
                }

                Attack attack = nextAttack ?? PickAttack();
                nextAttack = null;
                lastAttack = attack;
                switch (attack)
                {
                    case Attack.Orbs:
                        await OrbVolley(token);
                        break;
                    case Attack.Pillars:
                        await PillarStrike(token);
                        break;
                    case Attack.Beam:
                        await BeamSweep(token);
                        break;
                }

                Vector2 pause = CurrentPhase.AttackPause;
                await UniTask.Delay(TimeSpan.FromSeconds(Random.Range(pause.x, pause.y)), cancellationToken: token);
            }
        }

        private int PhaseForHealth()
        {
            float fraction = health.Current / health.Max;
            int phase = 0;
            for (int i = 1; i < phases.Length; i++)
            {
                if (fraction < phases[i].Below) phase = i;
            }
            return phase;
        }

        // Never the same attack twice in a row
        private Attack PickAttack()
        {
            int count = CurrentPhase.Beam ? 3 : 2;
            var attack = (Attack)Random.Range(0, count);
            if (attack == lastAttack) attack = (Attack)(((int)attack + 1 + Random.Range(0, count - 1)) % count);
            return attack;
        }

        private async UniTask Roar(float duration, CancellationToken token)
        {
            health.Invulnerable = true;
            if (animator != null) animator.SetTrigger(RoarHash);
            // Felt anywhere in the hall
            if (player != null) BodyCamera.ShakeExplosion(player.ActiveBody.position, roarShake);
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
            }
            finally
            {
                health.Invulnerable = false;
            }
        }

        // ---------- Orbs ----------

        private async UniTask OrbVolley(CancellationToken token)
        {
            if (animator != null) animator.SetTrigger(AttackHash);
            GameObject telegraph = orbSpell.CastVfx != null ? Instantiate(orbSpell.CastVfx, castPoint.position, castPoint.rotation, castPoint) : null;
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(orbWindup), cancellationToken: token);
            }
            finally
            {
                if (telegraph != null) Destroy(telegraph);
            }

            int count = CurrentPhase.Orbs;
            for (int i = 0; i < count; i++)
            {
                Transform target = Target();
                if (target == null) return;
                Vector3 origin = castPoint.position;
                // Fanned left to right; the orbs home in a little, so the player has to keep moving
                float angle = count > 1 ? Mathf.Lerp(-0.5f, 0.5f, i / (count - 1f)) * orbSpread : 0f;
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * (AimPoint(target) - origin).normalized;
                if (orbSpell.MuzzleVfx != null) Destroy(Instantiate(orbSpell.MuzzleVfx, origin, Quaternion.LookRotation(dir)), 2f);
                SpellProjectile.Launch(orbSpell, health, false, origin, dir);
                await UniTask.Delay(TimeSpan.FromSeconds(orbInterval), cancellationToken: token);
            }
        }

        // ---------- Pillars ----------

        // The first lands under the player, the rest around them. Each shows a closing ring until it hits
        private async UniTask PillarStrike(CancellationToken token)
        {
            if (animator != null) animator.SetTrigger(GrabHash);
            await UniTask.Delay(TimeSpan.FromSeconds(pillarWindup), cancellationToken: token);
            Transform target = Target();
            if (target == null) return;

            int count = CurrentPhase.Pillars;
            var points = new Vector3[count];
            var rings = new LineRenderer[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 at = target.position;
                    if (i > 0)
                    {
                        Vector2 offset = Random.insideUnitCircle * pillarScatter;
                        at += new Vector3(offset.x, 0f, offset.y);
                    }
                    points[i] = GroundPoint(at);
                    rings[i] = CreateRing(points[i], pillarSpell.SplashRadius);
                    _ = Tween.Scale(rings[i].transform, Vector3.one * 1.8f, Vector3.one, pillarSpell.BarrageDelay, Ease.InQuad);
                    // The orbital beam charges by itself and comes down about BarrageDelay later
                    if (pillarSpell.AreaVfx != null) Destroy(Instantiate(pillarSpell.AreaVfx, points[i], Quaternion.identity), 8f);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(pillarSpell.BarrageDelay), cancellationToken: token);
                foreach (var point in points)
                    SpellProjectile.DamageArea(pillarSpell, health, false, point + Vector3.up * 0.5f);
            }
            finally
            {
                foreach (var ring in rings)
                    if (ring != null) Destroy(ring.gameObject);
            }
        }

        private Vector3 GroundPoint(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out var hit, 20f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point : point;

        private LineRenderer CreateRing(Vector3 point, float radius)
        {
            var ring = new GameObject("PillarRing").AddComponent<LineRenderer>();
            ring.sharedMaterial = ringMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.widthMultiplier = ringWidth;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            ring.transform.position = point + Vector3.up * 0.05f;
            return ring;
        }

        // ---------- Beam ----------

        // Sides alternate: every beam starts in the corner where the last one ended
        private async UniTask BeamSweep(CancellationToken token)
        {
            if (beamLine == null) return;
            if (animator != null) animator.SetTrigger(AttackHash);
            beamSign = -beamSign;
            float from = -beamSign * beamSweepAngle * 0.5f;
            float to = -from;
            GameObject end = null;
            try
            {
                beamLine.enabled = true;
                // Charge: a thin line where the sweep starts, growing, then flickering just before it fires
                float grow = Mathf.Max(0.01f, beamCharge - BeamFlickerTime);
                for (float t = 0f; t < beamCharge; t += Time.deltaTime)
                {
                    bool flicker = t >= grow;
                    float width = flicker ? (Mathf.Repeat(t * 30f, 2f) < 1f ? beamAimWidth : 0f) : beamAimWidth * t / grow;
                    Vector3 dir = BeamDirection(from);
                    DrawBeam(dir, BeamLength(dir), width, null);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                end = beamEndPrefab != null ? Instantiate(beamEndPrefab) : null;
                await Sweep(from, to, end, token);
                if (CurrentPhase.BeamBack) await Sweep(to, from, end, token);
            }
            finally
            {
                beamLine.enabled = false;
                if (end != null) Destroy(end);
            }
        }

        private async UniTask Sweep(float from, float to, GameObject end, CancellationToken token)
        {
            beamHit.Clear();
            float sign = Mathf.Sign(to - from);
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / beamSweepTime);
                Vector3 dir = BeamDirection(Mathf.Lerp(from, to, t));
                float length = BeamLength(dir);
                DrawBeam(dir, length, beamWidth, end);
                HurtAlongBeam(dir, length, sign);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        private Vector3 BeamDirection(float yaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(beamOrigin.forward, Vector3.up).normalized;
            return Quaternion.AngleAxis(yaw, Vector3.up) * forward;
        }

        // Up to the first wall; bodies (anything with Health, the tree too) don't stop it
        private float BeamLength(Vector3 dir)
        {
            float nearest = beamLength;
            int count = Physics.RaycastNonAlloc(beamOrigin.position, dir, beamBuffer, beamLength, beamBlockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = beamBuffer[i];
                if (hit.distance < nearest && hit.collider.GetComponentInParent<Health>() == null) nearest = hit.distance;
            }
            return nearest;
        }

        private void DrawBeam(Vector3 dir, float length, float width, GameObject end)
        {
            Vector3 origin = beamOrigin.position;
            Vector3 tip = origin + dir * length;
            beamLine.SetPosition(0, origin);
            beamLine.SetPosition(1, tip);
            beamLine.widthMultiplier = width;
            // Texture tiles per metre and scrolls along, as Retro Arsenal's own beam script does
            beamMaterial.mainTextureScale = new Vector2(length, 1f);
            beamMaterial.mainTextureOffset -= new Vector2(Time.deltaTime * beamTextureScroll, 0f);
            if (end != null) end.transform.SetPositionAndRotation(tip, Quaternion.LookRotation(-dir));
        }

        // Player side only: the skeletons are the tree's own. Pushed along the sweep
        private void HurtAlongBeam(Vector3 dir, float length, float sweepSign)
        {
            Vector3 push = (Quaternion.AngleAxis(90f * sweepSign, Vector3.up) * dir + Vector3.up * 0.5f) * beamKnockback;
            int count = Physics.SphereCastNonAlloc(beamOrigin.position, beamRadius, dir, beamBuffer, length, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = beamBuffer[i].collider.GetComponentInParent<Health>();
                if (target == null || target == health || target.IsDead || !Teams.IsPlayerSide(target) || !beamHit.Add(target)) continue;
                target.TakeDamage(beamDamage, push);
            }
        }

        // ---------- Skeletons ----------

        private async UniTaskVoid KeepSkeletons(CancellationToken token)
        {
            while (true)
            {
                await UniTask.WaitUntil(() => AliveSkeletons() < CurrentPhase.Skeletons, cancellationToken: token);
                SpawnSkeleton();
                await UniTask.Delay(TimeSpan.FromSeconds(spawnInterval), cancellationToken: token);
            }
        }

        private int AliveSkeletons()
        {
            skeletons.RemoveAll(host => host == null || host.Health.IsDead);
            return skeletons.Count;
        }

        private void SpawnSkeleton()
        {
            if (skeletonPrefab == null || spawnPoints == null || spawnPoints.childCount == 0) return;
            Vector3 playerPosition = player != null ? player.ActiveBody.position : transform.position;
            Transform point = WaveSpawner.PickPoint(spawnPoints, playerPosition, minSpawnDistance);
            var brain = Instantiate(skeletonPrefab, point.position, point.rotation);
            var host = brain.GetComponent<HostBody>();
            host.FreeDamageScale = skeletonDamageScale;
            if (brain.TryGetComponent(out SpellCaster caster)) RollSpells(caster);
            skeletons.Add(host);
        }

        private void RollSpells(SpellCaster caster)
        {
            var pool = skeletonSpells.FindAll(spell => spell != null);
            var attacks = pool.FindAll(spell => spell.AiUse == SpellAiUse.Attack);
            for (int slot = 0; slot < spellsPerSkeleton && pool.Count > 0; slot++)
            {
                SpellData spell = slot == 0 && attacks.Count > 0 ? attacks[Random.Range(0, attacks.Count)] : pool[Random.Range(0, pool.Count)];
                pool.Remove(spell);
                caster.SetSpell(slot, spell);
            }
        }

        // ---------- Hits and death ----------

        private void OnDamaged(float amount, Vector3 impulse)
        {
            if (flashRenderers == null || flashRenderers.Length == 0) return;
            flashTween.Stop();
            flashTween = Tween.Custom(this, 1f, 0f, flashDuration, (boss, value) => boss.SetFlash(value));
        }

        private void SetFlash(float amount)
        {
            flashBlock.SetColor(BaseColorId, Color.LerpUnclamped(Color.white, flashColor, amount));
            foreach (var r in flashRenderers)
                if (r != null) r.SetPropertyBlock(flashBlock);
        }

        // Stops fighting; its skeletons fall with it (the one the player sits in dies when they leave it, as at the end
        // of a wave). The death itself (animation, effects) is still to come
        private void OnDied()
        {
            IsFightActive = false;
            fightCts?.Cancel();
            foreach (var host in skeletons)
            {
                if (host == null || host.Health.IsDead) continue;
                if (host.IsPossessed)
                {
                    WaveSpawner.KillOnRelease(host, destroyCancellationToken).Forget();
                    continue;
                }
                host.Health.Invulnerable = false;
                host.Health.TakeDamage(float.MaxValue, Vector3.zero);
            }
        }

        private Transform Target() => player != null && !player.Health.IsDead ? player.ActiveBody : null;

        private static Vector3 AimPoint(Transform target) =>
            target.TryGetComponent(out HostBody host) ? host.Center : target.position;

        // Start distance, and the beam's sweep from the origin
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, startDistance);
            if (beamOrigin == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(beamOrigin.position, BeamDirection(-beamSweepAngle * 0.5f) * 20f);
            Gizmos.DrawRay(beamOrigin.position, BeamDirection(beamSweepAngle * 0.5f) * 20f);
        }
    }
}
