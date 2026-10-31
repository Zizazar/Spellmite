using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // A flying spell, created at runtime by SpellCaster. The Retro Arsenal prefabs are only its visuals.
    // Flies through its own side and plain triggers, stops at walls and enemies of the caster.
    public class SpellProjectile : MonoBehaviour
    {
        private const float HomingAngle = 60f;

        private static readonly RaycastHit[] castBuffer = new RaycastHit[16];
        private static readonly Collider[] overlapBuffer = new Collider[32];
        private static readonly HashSet<Health> damaged = new();

        private SpellData spell;
        private Health ownerHealth;
        private bool fromPlayer;
        private Vector3 velocity;
        private float dieTime;
        private Collider homingTarget;
        private GameObject vfx;

        public static void Launch(SpellData spell, HostBody owner, bool fromPlayer, Vector3 position, Vector3 direction)
        {
            var rotation = Quaternion.LookRotation(direction);
            var projectile = new GameObject(spell.name).AddComponent<SpellProjectile>();
            projectile.transform.SetPositionAndRotation(position, rotation);
            projectile.spell = spell;
            projectile.ownerHealth = owner.Health;
            projectile.fromPlayer = fromPlayer;
            projectile.velocity = direction * spell.Speed;
            projectile.dieTime = Time.time + spell.Lifetime;
            if (spell.ProjectileVfx != null) projectile.vfx = Instantiate(spell.ProjectileVfx, position, rotation, projectile.transform);
            if (spell.HomingTurnRate > 0f) projectile.homingTarget = projectile.FindHomingTarget();
        }

        private void Update()
        {
            if (Time.time >= dieTime)
            {
                Finish();
                return;
            }

            float dt = Time.deltaTime;
            if (homingTarget != null)
            {
                Vector3 toTarget = homingTarget.bounds.center - transform.position;
                velocity = Vector3.RotateTowards(velocity, toTarget, spell.HomingTurnRate * Mathf.Deg2Rad * dt, 0f);
            }
            velocity += Vector3.down * (spell.Gravity * dt);

            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance < 1e-6f) return;
            Vector3 dir = step / distance;

            if (FindHit(dir, distance, out var hit))
            {
                // distance 0 = already overlapping at the start of the step
                Vector3 point = hit.distance > 0f ? hit.point : transform.position;
                Vector3 normal = hit.distance > 0f ? hit.normal : -dir;
                transform.position += dir * hit.distance;
                Explode(point, normal, hit.collider);
                return;
            }

            transform.position += step;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        private bool FindHit(Vector3 dir, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            // Collide: the free parasite only has a trigger
            int count = Physics.SphereCastNonAlloc(transform.position, spell.Radius, dir, castBuffer, distance, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = castBuffer[i];
                if (found && hit.distance >= nearest.distance) continue;
                if (!CanHit(hit.collider)) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        private bool CanHit(Collider col)
        {
            var health = col.GetComponentInParent<Health>();
            if (health == null) return !col.isTrigger;
            if (health == ownerHealth || health.IsDead) return false;
            return Teams.IsPlayerSide(health) != fromPlayer;
        }

        private void Explode(Vector3 point, Vector3 normal, Collider direct)
        {
            if (spell.ImpactVfx != null) Destroy(Instantiate(spell.ImpactVfx, point, Quaternion.FromToRotation(Vector3.up, normal)), 5f);

            damaged.Clear();
            var directHealth = direct.GetComponentInParent<Health>();
            if (directHealth != null) Damage(directHealth, point);

            if (spell.SplashRadius > 0f)
            {
                int count = Physics.OverlapSphereNonAlloc(point, spell.SplashRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++)
                {
                    var health = overlapBuffer[i].GetComponentInParent<Health>();
                    if (health != null && CanHit(overlapBuffer[i])) Damage(health, point);
                }
            }

            Finish();
        }

        private void Damage(Health target, Vector3 point)
        {
            if (target.Invulnerable || !damaged.Add(target)) return;

            Vector3 away = Vector3.ProjectOnPlane(target.transform.position - point, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = Vector3.ProjectOnPlane(velocity, Vector3.up);
            target.TakeDamage(spell.Damage, (away.normalized + Vector3.up * 0.4f) * spell.Knockback);

            if (spell.HitBuff != null && target.TryGetComponent(out BuffHolder buffs)) buffs.Apply(spell.HitBuff);
        }

        // Enemy of the caster closest to the flight direction
        private Collider FindHomingTarget()
        {
            Collider best = null;
            float bestAngle = HomingAngle;
            int count = Physics.OverlapSphereNonAlloc(transform.position, spell.HomingRange, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = overlapBuffer[i];
                if (col.GetComponentInParent<Health>() == null || !CanHit(col)) continue;
                float angle = Vector3.Angle(velocity, col.bounds.center - transform.position);
                if (angle >= bestAngle) continue;
                best = col;
                bestAngle = angle;
            }
            return best;
        }

        // Let the trail fade out instead of popping
        private void Finish()
        {
            if (vfx != null)
            {
                vfx.transform.SetParent(null);
                foreach (var particles in vfx.GetComponentsInChildren<ParticleSystem>())
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                Destroy(vfx, 2f);
            }
            Destroy(gameObject);
        }
    }
}
