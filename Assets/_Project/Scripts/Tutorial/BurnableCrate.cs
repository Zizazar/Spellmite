using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Spellmite
{
    // A wooden crate that only fire spells set alight. The fire spreads to the crates it touches; a burnt crate
    // collapses onto the floor as smouldering embers and no longer blocks anything.
    [RequireComponent(typeof(Collider))]
    public class BurnableCrate : MonoBehaviour
    {
        // Colors of the PSX Mega Pack GLB material (UnityGLTF/PBRGraph)
        private static readonly int BaseColorId = Shader.PropertyToID("baseColorFactor");
        private static readonly int EmissionId = Shader.PropertyToID("emissiveFactor");
        private static readonly Collider[] overlapBuffer = new Collider[32];
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        [SerializeField, Tooltip("Spells with this tag set the crate alight")] private SpellTag fireTag;
        [SerializeField, Tooltip("Looping fire while it burns (Retro Arsenal Environment/Fire)")] private GameObject fireVfx;
        [SerializeField, Tooltip("From the crate's center")] private Vector3 fireOffset = new(0f, 0.25f, 0f);
        [SerializeField, Tooltip("Charred while it burns, squashed into embers after. Its pivot is at the bottom")] private Transform model;
        [SerializeField] private float burnTime = 3f;
        [SerializeField, Tooltip("Seconds after catching fire until the crates it touches catch fire too")] private Vector2 spreadDelay = new(0.3f, 0.7f);
        [SerializeField, Tooltip("Crates whose collider is this close to the center catch fire from this one")] private float spreadRadius = 0.9f;
        [SerializeField] private Color charColor = new(0.12f, 0.09f, 0.07f);
        [SerializeField, ColorUsage(false, true)] private Color emberColor = new(1.6f, 0.45f, 0.1f);
        [SerializeField, Tooltip("Embers fall onto the first surface below that isn't a crate")] private LayerMask floorMask = 1;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        public bool IsBurning { get; private set; }
        public bool IsBurnt { get; private set; }

        // A spell went off at the point: a fire spell sets alight the crates in its splash (or the one it hit)
        public static void IgniteAround(SpellData spell, Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, Mathf.Max(spell.SplashRadius, 0.5f), overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var crate = overlapBuffer[i].GetComponentInParent<BurnableCrate>();
                if (crate != null && spell.Tags.Contains(crate.fireTag)) crate.Ignite();
            }
        }

        private void Awake()
        {
            renderers = (model != null ? model : transform).GetComponentsInChildren<Renderer>();
        }

        public void Ignite()
        {
            if (IsBurning) return;
            IsBurning = true;
            GameObject fire = fireVfx != null ? Instantiate(fireVfx, transform.position + fireOffset, fireVfx.transform.rotation, transform) : null;
            Tween.Custom(this, 0f, 1f, burnTime, (crate, t) => crate.SetColors(Color.Lerp(Color.white, crate.charColor, t), crate.emberColor * t));
            Burn(fire, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid Burn(GameObject fire, CancellationToken token)
        {
            float spread = Random.Range(spreadDelay.x, spreadDelay.y);
            await UniTask.Delay(TimeSpan.FromSeconds(spread), cancellationToken: token);
            Spread();
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, burnTime - spread)), cancellationToken: token);
            Collapse(fire);
        }

        private void Collapse(GameObject fire)
        {
            IsBurnt = true;
            GetComponent<Collider>().enabled = false;
            if (fire != null)
            {
                fire.transform.SetParent(null);
                foreach (var particles in fire.GetComponentsInChildren<ParticleSystem>())
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                Destroy(fire, 2f);
            }

            // Down onto the floor, flat, a little scattered, then smouldering
            Vector2 scatter = Random.insideUnitCircle * 0.3f;
            Vector3 floor = FloorBelow() + new Vector3(scatter.x, 0f, scatter.y);
            Tween.Position(transform, floor, 0.35f, Ease.InQuad);
            Tween.Rotation(transform, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 0.35f);
            if (model != null) Tween.Scale(model, Vector3.Scale(model.localScale, new Vector3(1.15f, 0.15f, 1.15f)), 0.35f, Ease.InQuad);
            Tween.Custom(this, 0.6f, 0.25f, 1.5f, (crate, glow) => crate.SetColors(crate.charColor, crate.emberColor * glow),
                Ease.InOutSine, -1, CycleMode.Yoyo, startDelay: 0.35f);
        }

        private void Spread()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, spreadRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var crate = overlapBuffer[i].GetComponentInParent<BurnableCrate>();
                if (crate != null) crate.Ignite();
            }
        }

        // Where the crate's center ends up lying on the floor (the center is half a crate above its bottom)
        private Vector3 FloorBelow()
        {
            Vector3 position = transform.position;
            float nearest = float.MaxValue;
            int count = Physics.RaycastNonAlloc(position, Vector3.down, rayBuffer, 20f, floorMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = rayBuffer[i];
                if (hit.distance >= nearest || hit.collider.GetComponentInParent<BurnableCrate>() != null) continue;
                nearest = hit.distance;
                position = hit.point + Vector3.up * 0.5f;
            }
            return position;
        }

        private void SetColors(Color baseColor, Color emission)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(EmissionId, emission);
            foreach (var r in renderers) r.SetPropertyBlock(block);
        }
    }
}
