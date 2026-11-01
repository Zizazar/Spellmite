using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using Unity.Cinemachine;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Spellmite
{
    // The end of the game, once the tree boss dies. The player's control and the HUD go, and the view flies over to a
    // camera on the tree. The demon screams and stiffens while fire spreads over the tree, chars it black and burns out,
    // leaving a smoking husk. Meanwhile the hall goes up in explosions, thick at first and calmer later, each one
    // shaking the camera; the end screen with the run time fades in over it all, and once it hides the hall the
    // explosions stop.
    public class BossEnding : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RoarHash = Animator.StringToHash("Roar");
        private const float HotPart = 0.25f;

        [SerializeField] private TreeBoss boss;
        [SerializeField] private EndScreen endScreen;
        [SerializeField, Tooltip("Seconds from the boss's death until the end screen starts fading in")] private float screenDelay = 3.5f;
        [SerializeField, Tooltip("Seconds the HUD takes to fade out")] private float hudFadeTime = 0.5f;

        [Header("Camera")]
        [SerializeField, Tooltip("Keep its GameObject inactive: it's switched on at the end. Priority above the player's camera")] private CinemachineCamera endingCamera;
        [SerializeField, Tooltip("On the ending camera; its amplitude gain is the shake")] private CinemachineBasicMultiChannelPerlin shakeNoise;
        [SerializeField, Tooltip("From the player's camera to the ending one")] private CinemachineBlendDefinition flyBlend = new(CinemachineBlendDefinition.Styles.EaseInOut, 2f);
        [SerializeField, Tooltip("Meters the camera slowly moves in along its view")] private float pushIn = 3f;
        [SerializeField] private float pushInTime = 12f;
        [SerializeField, Tooltip("Noise gain the camera shakes with all the time")] private float rumble = 0.4f;
        [SerializeField, Tooltip("Noise gain of an explosion right at the camera, less further away")] private float explosionShake = 2f;
        [SerializeField, Tooltip("Explosions further than this from the camera don't shake it and sound quieter")] private float shakeRange = 30f;
        [SerializeField] private float maxShake = 4f;
        [SerializeField, Tooltip("Noise gain lost per second")] private float shakeDecay = 6f;

        [Header("Burning")]
        [SerializeField, Tooltip("Everything under it is charred and its animators slow to a stop")] private Transform model;
        [SerializeField, Tooltip("Screams when it dies. Trigger Roar")] private Animator demonAnimator;
        [SerializeField, Tooltip("Seconds the animations take to stop")] private float stiffenTime = 3f;
        [SerializeField, Tooltip("Fires and the explosions on the tree sit on these meshes' bones (trunk and demon)")] private SkinnedMeshRenderer[] burningMeshes;
        [SerializeField, Tooltip("Looping fire (Retro Arsenal Environment/Fire)")] private GameObject fireVfx;
        [SerializeField, Min(1)] private int fireCount = 12;
        [SerializeField, Tooltip("Seconds the fire takes to spread over the whole tree")] private float spreadTime = 2f;
        [SerializeField, Tooltip("Seconds from the death to a charred husk; then the fires go out")] private float burnTime = 7f;
        [SerializeField, ColorUsage(false, true), Tooltip("The tree glows like this early in the burn")] private Color hotColor = new(2.2f, 0.9f, 0.35f);
        [SerializeField] private Color charColor = new(0.06f, 0.05f, 0.045f);
        [SerializeField, Tooltip("Rises from the husk once the fires are out (Retro Arsenal Environment/Smoke)")] private GameObject smokeVfx;
        [SerializeField, Min(0)] private int smokeCount = 4;
        [SerializeField, Tooltip("Turns fire coloured and flickers while the tree burns")] private Light glowLight;
        [SerializeField] private Color glowColor = new(1f, 0.45f, 0.15f);
        [SerializeField] private float glowIntensity = 4f;

        [Header("Explosions")]
        [SerializeField, Tooltip("A random one each time")] private GameObject[] explosionVfx;
        [SerializeField, Tooltip("2D. Taken in turn, so overlapping explosions keep their own pitch")] private AudioSource[] explosionSources;
        [SerializeField] private AudioClip[] explosionSounds;
        [SerializeField] private Vector2 explosionPitch = new(0.75f, 1.1f);
        [SerializeField, Tooltip("Inside the hall: explosions go off where random rays from here hit something")] private Transform explosionCenter;
        [SerializeField] private float explosionRange = 25f;
        [SerializeField] private LayerMask explosionMask = 1;
        [SerializeField, Range(0f, 1f), Tooltip("Share of the explosions on the tree itself")] private float treeExplosionShare = 0.3f;
        [SerializeField, Tooltip("Seconds between explosions (Y) over seconds since the death (X). The last key holds")]
        private AnimationCurve explosionInterval = new(new Keyframe(0f, 0.4f), new Keyframe(4f, 0.1f), new Keyframe(12f, 0.8f));

        private readonly List<Transform[]> meshBones = new();
        private readonly List<(Transform vfx, Transform bone)> fires = new();
        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Animator[] animators;
        private int nextSource;
        private float shake;
        private float glow;
        private bool playing;

        private void Start()
        {
            boss.Health.Died += OnBossDied;
        }

        private void LateUpdate()
        {
            if (!playing) return;
            // The fires stay on their bones while the demon still moves
            foreach (var (vfx, bone) in fires)
                if (vfx != null) vfx.position = bone.position;

            shake = Mathf.MoveTowards(shake, 0f, shakeDecay * Time.deltaTime);
            shakeNoise.AmplitudeGain = rumble + shake;
            if (glowLight != null) glowLight.intensity = glowIntensity * glow * (0.7f + 0.3f * Mathf.PerlinNoise(Time.time * 10f, 0f));
        }

        private void OnBossDied()
        {
            playing = true;
            foreach (var mesh in burningMeshes)
                if (mesh != null && mesh.bones.Length > 0) meshBones.Add(mesh.bones);

            var token = destroyCancellationToken;
            TakeControl();
            FlyCamera();
            Burn(token).Forget();
            Explode(token).Forget();
            ShowScreen(token).Forget();
        }

        // The player's body stops where it is and can't be hurt any more (orbs may still be flying); the HUD fades out
        private void TakeControl()
        {
            var brain = FindAnyObjectByType<PlayerBrain>();
            if (brain != null) brain.enabled = false;
            var parasite = FindAnyObjectByType<ParasiteBody>();
            if (parasite != null)
            {
                Transform body = parasite.ActiveBody;
                parasite.SetInput(new ControlInput { Aim = body.forward, AimOrigin = body.position });
                parasite.Health.Invulnerable = true;
                if (parasite.Host != null) parasite.Host.Health.Invulnerable = true;
            }

            var hud = FindAnyObjectByType<GameHud>();
            if (hud == null) return;
            if (!hud.TryGetComponent(out CanvasGroup group)) group = hud.gameObject.AddComponent<CanvasGroup>();
            Tween.Alpha(group, 0f, hudFadeTime);
        }

        private void FlyCamera()
        {
            if (Camera.main != null && Camera.main.TryGetComponent(out CinemachineBrain brain)) brain.DefaultBlend = flyBlend;
            endingCamera.gameObject.SetActive(true);
            Transform cam = endingCamera.transform;
            Tween.Position(cam, cam.position + cam.forward * pushIn, pushInTime, Ease.OutSine);
        }

        private async UniTaskVoid ShowScreen(CancellationToken token)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(screenDelay), cancellationToken: token);
            await endScreen.Show(boss.DefeatTime).ToUniTask().AttachExternalCancellation(token);
            // The hall is hidden now: no more explosions or shake
            playing = false;
            shakeNoise.AmplitudeGain = 0f;
        }

        // ---------- Burning ----------

        private async UniTaskVoid Burn(CancellationToken token)
        {
            if (demonAnimator != null) demonAnimator.SetTrigger(RoarHash);
            animators = model.GetComponentsInChildren<Animator>();
            _ = Tween.Custom(this, 1f, 0f, stiffenTime, (ending, speed) => ending.SetAnimatorSpeed(speed), Ease.InQuad);
            renderers = model.GetComponentsInChildren<Renderer>();
            _ = Tween.Custom(this, 0f, 1f, burnTime, (ending, t) => ending.SetBurn(t));
            if (glowLight != null) glowLight.color = glowColor;
            _ = Tween.Custom(this, 0f, 1f, 0.5f, (ending, value) => ending.glow = value);

            // Catches fire bit by bit
            for (int i = 0; i < fireCount; i++)
            {
                Transform bone = RandomBone();
                if (fireVfx != null && bone != null) fires.Add((Instantiate(fireVfx, bone.position, fireVfx.transform.rotation, transform).transform, bone));
                await UniTask.Delay(TimeSpan.FromSeconds(spreadTime / fireCount), cancellationToken: token);
            }
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, burnTime - spreadTime)), cancellationToken: token);

            // Burnt out: the fires die down and the husk smokes
            _ = Tween.Custom(this, glow, 0f, 2f, (ending, value) => ending.glow = value);
            for (int i = 0; i < fires.Count; i++)
            {
                var (vfx, bone) = fires[i];
                foreach (var particles in vfx.GetComponentsInChildren<ParticleSystem>())
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(vfx.gameObject, 3f);
                if (smokeVfx != null && i < smokeCount) Instantiate(smokeVfx, bone.position, smokeVfx.transform.rotation, transform);
            }
            fires.Clear();
        }

        private void SetAnimatorSpeed(float speed)
        {
            foreach (var animator in animators)
                if (animator != null) animator.speed = speed;
        }

        // 0 = as it was, glowing hot early on, 1 = charred black
        private void SetBurn(float t)
        {
            Color color = t < HotPart
                ? Color.LerpUnclamped(Color.white, hotColor, t / HotPart)
                : Color.LerpUnclamped(hotColor, charColor, (t - HotPart) / (1f - HotPart));
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(block);
        }

        // Every mesh as likely as the other, however many bones it has (the demon's fingers alone outnumber the trunk's)
        private Transform RandomBone()
        {
            if (meshBones.Count == 0) return null;
            Transform[] bones = meshBones[Random.Range(0, meshBones.Count)];
            return bones[Random.Range(0, bones.Length)];
        }

        // ---------- Explosions ----------

        private async UniTaskVoid Explode(CancellationToken token)
        {
            float start = Time.time;
            while (playing)
            {
                ExplodeOnce();
                float interval = Mathf.Max(0.02f, explosionInterval.Evaluate(Time.time - start));
                await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: token);
            }
        }

        private void ExplodeOnce()
        {
            if (explosionVfx.Length == 0) return;
            Transform bone = Random.value < treeExplosionShare ? RandomBone() : null;
            Vector3 point;
            if (bone != null) point = bone.position;
            else if (!HallPoint(out point)) return;

            GameObject prefab = explosionVfx[Random.Range(0, explosionVfx.Length)];
            if (prefab != null) Destroy(Instantiate(prefab, point, prefab.transform.rotation), 5f);

            // Closer to the camera: shakier and louder
            float near = Mathf.Clamp01(1f - Vector3.Distance(point, endingCamera.transform.position) / shakeRange);
            shake = Mathf.Min(maxShake, shake + explosionShake * near);
            if (explosionSources.Length == 0) return;
            AudioSource source = explosionSources[nextSource++ % explosionSources.Length];
            source.PlayRandom(explosionSounds, explosionPitch, Mathf.Lerp(0.3f, 1f, near));
        }

        // Where a random ray from the middle of the hall hits a wall, the floor, a pier or the ceiling
        private bool HallPoint(out Vector3 point)
        {
            bool hit = Physics.Raycast(explosionCenter.position, Random.onUnitSphere, out var info, explosionRange, explosionMask, QueryTriggerInteraction.Ignore);
            point = hit ? info.point + info.normal * 0.3f : default;
            return hit;
        }

        private void OnDrawGizmosSelected()
        {
            if (explosionCenter == null) return;
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawWireSphere(explosionCenter.position, explosionRange);
        }
    }
}
