using UnityEngine;

namespace Spellmite
{
    // Simple AI for a free host: wander around home, chase and hit the player's current body,
    // cast the body's spells (SpellCaster) by their AI hints. Stops thinking while the host is possessed.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(HostBody))]
    public class EnemyBrain : MonoBehaviour
    {
        [SerializeField] private bool aggressive = true;
        [SerializeField] private float detectRadius = 12f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float wanderRadius = 6f;
        [SerializeField] private float wanderPause = 2f;

        [Header("Spells")]
        [SerializeField, Tooltip("Stop approaching at this distance (ranged casters). 0 = walk into melee range")] private float keepDistance;
        [SerializeField, Tooltip("Random pause after choosing a spell before the next one, seconds")] private Vector2 castPause = new(0.5f, 1.5f);
        [SerializeField, Tooltip("What blocks line of sight for attack spells")] private LayerMask sightMask = 1;

        private HostBody body;
        private SpellCaster caster;
        private ParasiteBody player;
        private Vector3 home;
        private Vector3 wanderTarget;
        private float nextWanderTime;
        private float nextCastTime;

        private void Awake()
        {
            body = GetComponent<HostBody>();
            caster = GetComponent<SpellCaster>();
            home = transform.position;
            wanderTarget = home;
        }

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
        }

        private void Update()
        {
            if (body.IsPossessed || body.Health.IsDead) return;

            var input = new ControlInput { AimOrigin = transform.position + Vector3.up * 1.5f, Aim = transform.forward };
            Transform target = FindTarget();

            if (target != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
                float distance = toTarget.magnitude;
                Vector3 aimPoint = AimPoint(target);
                input.Aim = (aimPoint - input.AimOrigin).normalized;
                if (distance > Mathf.Max(attackRange * 0.8f, keepDistance)) input.Move = toTarget.normalized;
                if (distance <= attackRange) input.AttackPressed = true; // HostBody handles cooldown
                input.CastSlot = ChooseSpell(target, distance, input.AimOrigin, aimPoint) + 1;
            }
            else
            {
                Wander(ref input);
                input.CastSlot = ChooseSpell(null, 0f, input.AimOrigin, input.AimOrigin) + 1;
            }

            // Stand still during the windup so the telegraph reads
            if (caster != null && caster.IsCasting) input.Move = Vector3.zero;

            body.SetInput(input);
        }

        // First ready spell whose AI hint fits; slot order is the priority. -1 = none.
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

                nextCastTime = Time.time + Random.Range(castPause.x, castPause.y);
                return i;
            }
            return -1;
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
            Transform target = player.ActiveBody;
            if (target == transform) return null;
            return (target.position - transform.position).sqrMagnitude <= detectRadius * detectRadius ? target : null;
        }

        private void Wander(ref ControlInput input)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(wanderTarget - transform.position, Vector3.up);
            if (toTarget.magnitude < 0.5f)
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectRadius);
        }
    }
}
