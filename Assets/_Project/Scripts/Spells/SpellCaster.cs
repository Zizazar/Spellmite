using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Spellmite
{
    // The spells a body knows. They belong to the body: EnemyBrain and the parasite cast the same slots.
    // HostBody calls TryCast from its input; aim is read from the body when the spell fires.
    [RequireComponent(typeof(HostBody))]
    public class SpellCaster : MonoBehaviour
    {
        [SerializeField, Tooltip("Hand / muzzle the spells come from")] private Transform castPoint;
        [SerializeField] private List<SpellData> spells = new();
        [SerializeField, Tooltip("Length of the aim ray that projectiles converge on")] private float aimDistance = 100f;
        [SerializeField, Tooltip("Barrage spells land on this below the aim point")] private LayerMask groundMask = 1;

        private HostBody body;
        private BuffHolder buffs;
        private float[] readyTimes;
        private readonly RaycastHit[] aimBuffer = new RaycastHit[16];

        public int SlotCount => spells.Count;
        public bool IsCasting { get; private set; }

        private void Awake()
        {
            body = GetComponent<HostBody>();
            buffs = GetComponent<BuffHolder>();
            readyTimes = new float[spells.Count];

            if (castPoint == null)
            {
                castPoint = new GameObject("CastPoint").transform;
                castPoint.SetParent(transform, false);
                castPoint.localPosition = new Vector3(0f, 1.2f, 0.5f);
            }
        }

        public SpellData GetSpell(int slot) => slot >= 0 && slot < spells.Count ? spells[slot] : null;

        public bool IsReady(int slot) => GetSpell(slot) != null && Time.time >= readyTimes[slot];

        // Runtime only (Spellbook "Slot 1" test button): puts a spell into a slot, ready to cast
        public void SetSpell(int slot, SpellData spell)
        {
            while (spells.Count <= slot) spells.Add(null);
            spells[slot] = spell;
            if (readyTimes.Length < spells.Count) Array.Resize(ref readyTimes, spells.Count);
            readyTimes[slot] = 0f;
        }

        // castTimeScale: windup multiplier from the controller, 0 = the spell's CastTime as is
        public bool TryCast(int slot, float castTimeScale = 1f)
        {
            if (IsCasting || !IsReady(slot)) return false;
            float castTime = spells[slot].CastTime * (castTimeScale > 0f ? castTimeScale : 1f);
            Cast(slot, spells[slot], castTime, destroyCancellationToken).Forget();
            return true;
        }

        private async UniTaskVoid Cast(int slot, SpellData spell, float castTime, CancellationToken token)
        {
            IsCasting = true;
            bool possessed = body.IsPossessed;
            GameObject telegraph = spell.CastVfx != null ? Instantiate(spell.CastVfx, castPoint.position, castPoint.rotation, castPoint) : null;
            float fireTime = Time.time + castTime;
            try
            {
                // Stun, death or a change of controller cancels the windup
                while (Time.time < fireTime)
                {
                    await UniTask.NextFrame(token);
                    if (body.Health.IsDead || body.IsStunned || body.IsPossessed != possessed) return;
                }
                Fire(slot, spell);
            }
            finally
            {
                IsCasting = false;
                if (telegraph != null) Destroy(telegraph);
            }
        }

        private void Fire(int slot, SpellData spell)
        {
            readyTimes[slot] = Time.time + spell.Cooldown;
            if (body.IsPossessed) body.DrainResistance(spell.ResistanceCost);
            if (spell.SelfBuff != null && buffs != null) buffs.Apply(spell.SelfBuff);

            switch (spell.Kind)
            {
                case SpellKind.Projectile:
                    FireProjectiles(spell);
                    break;
                case SpellKind.Nova:
                    if (spell.AreaVfx != null) Destroy(Instantiate(spell.AreaVfx, transform.position, Quaternion.identity), 5f);
                    SpellProjectile.DamageArea(spell, body.Health, body.IsPossessed, body.Center);
                    break;
                case SpellKind.Barrage:
                    Barrage(spell, GroundPoint(AimPoint()), body.IsPossessed, destroyCancellationToken).Forget();
                    break;
            }
        }

        private void FireProjectiles(SpellData spell)
        {
            Vector3 origin = castPoint.position;
            Vector3 dir = AimPoint() - origin;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;

            if (spell.MuzzleVfx != null) Destroy(Instantiate(spell.MuzzleVfx, origin, Quaternion.LookRotation(dir)), 2f);

            for (int i = 0; i < spell.Count; i++)
            {
                float angle = spell.Count > 1 ? Mathf.Lerp(-0.5f, 0.5f, i / (spell.Count - 1f)) * spell.SpreadAngle : 0f;
                SpellProjectile.Launch(spell, body, body.IsPossessed, origin, Quaternion.AngleAxis(angle, Vector3.up) * dir);
            }
        }

        // The point is locked when the spell fires: the VFX falls from above, Count area hits land after BarrageDelay
        private async UniTaskVoid Barrage(SpellData spell, Vector3 point, bool fromPlayer, CancellationToken token)
        {
            // Euler(90, 0, 0) points the VFX's forward down
            if (spell.AreaVfx != null) Destroy(Instantiate(spell.AreaVfx, point + Vector3.up * spell.BarrageHeight, Quaternion.Euler(90f, 0f, 0f)), 5f);
            await UniTask.Delay(TimeSpan.FromSeconds(spell.BarrageDelay), cancellationToken: token);
            for (int i = 0; i < spell.Count; i++)
            {
                if (i > 0) await UniTask.Delay(TimeSpan.FromSeconds(spell.BarrageInterval), cancellationToken: token);
                SpellProjectile.DamageArea(spell, body.Health, fromPlayer, point + Vector3.up * 0.5f);
            }
        }

        // The floor under the aim point, so a barrage aimed at a body rains around its feet
        private Vector3 GroundPoint(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out var hit, 20f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point : point;

        // Where the controller's aim ray hits, so shots from the hand converge on the crosshair
        private Vector3 AimPoint()
        {
            Vector3 aim = body.AimDirection.sqrMagnitude > 1e-4f ? body.AimDirection.normalized : transform.forward;
            // Start the ray level with the cast point so nothing between the camera and the body counts
            Vector3 origin = body.AimOrigin;
            origin += aim * Mathf.Max(0f, Vector3.Dot(castPoint.position - origin, aim));

            Vector3 point = origin + aim * aimDistance;
            float nearest = aimDistance;
            int count = Physics.RaycastNonAlloc(origin, aim, aimBuffer, aimDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = aimBuffer[i];
                if (hit.distance >= nearest || hit.collider.GetComponentInParent<HostBody>() == body) continue;
                nearest = hit.distance;
                point = hit.point;
            }
            return point;
        }
    }
}
