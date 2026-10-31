using UnityEngine;

namespace Spellmite
{
    // Simple AI for a free host: wander around home, chase and hit the player's current body.
    // Stops thinking while the host is possessed.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(HostBody))]
    public class EnemyBrain : MonoBehaviour
    {
        [SerializeField] private bool aggressive = true;
        [SerializeField] private float detectRadius = 12f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float wanderRadius = 6f;
        [SerializeField] private float wanderPause = 2f;

        private HostBody body;
        private ParasiteBody player;
        private Vector3 home;
        private Vector3 wanderTarget;
        private float nextWanderTime;

        private void Awake()
        {
            body = GetComponent<HostBody>();
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
                input.Aim = toTarget.normalized;
                if (toTarget.magnitude > attackRange * 0.8f) input.Move = toTarget.normalized;
                if (toTarget.magnitude <= attackRange) input.AttackPressed = true; // HostBody handles cooldown
            }
            else
            {
                Wander(ref input);
            }

            body.SetInput(input);
        }

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
