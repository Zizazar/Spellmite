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
            if (readyTimes.Length < spells.Count) System.Array.Resize(ref readyTimes, spells.Count);
            readyTimes[slot] = 0f;
        }

        public bool TryCast(int slot)
        {
            if (IsCasting || !IsReady(slot)) return false;
            Cast(slot, spells[slot], destroyCancellationToken).Forget();
            return true;
        }

        private async UniTaskVoid Cast(int slot, SpellData spell, CancellationToken token)
        {
            IsCasting = true;
            bool possessed = body.IsPossessed;
            GameObject telegraph = spell.CastVfx != null ? Instantiate(spell.CastVfx, castPoint.position, castPoint.rotation, castPoint) : null;
            float fireTime = Time.time + spell.CastTime;
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
            if (spell.Kind != SpellKind.Projectile) return;

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
