using System;
using UnityEngine;

namespace Spellmite
{
    // The spell the parasite carries between bodies. Once a wave or the tutorial room is won, the last body the player
    // sits in can be eaten: LMB bites it, the cast keys choose the spell. The bite that kills it steals that spell into
    // the Q slot. Eating is optional, the body can still ride the lift. The slot's cooldown lives here, so hopping to a
    // fresh body doesn't reset it, and it casts only from inside a body.
    [DefaultExecutionOrder(-20)] // Before ParasiteBody, which calls Devour and CastStolen from its Update
    [RequireComponent(typeof(ParasiteBody))]
    public class SpellThief : MonoBehaviour
    {
        // Carried from the tutorial into the tower. Restarting the tower goes back to this
        private static SpellData carried;

        [Header("Devour")]
        [SerializeField, Range(0.05f, 1f), Tooltip("Fraction of the body's max health one press eats")] private float biteFraction = 0.2f;
        [SerializeField, Tooltip("Presses faster than this are ignored")] private float biteInterval = 0.12f;
        [SerializeField, Tooltip("Burst on every bite")] private GameObject biteVfx;
        [SerializeField, Min(1), Tooltip("Bursts per bite: one at the latch point, the rest around the body")] private int biteVfxCount = 3;
        [SerializeField, Tooltip("All of these play at the body on the bite that kills it")] private GameObject[] finishVfx;

        [Header("Stolen spell")]
        [SerializeField, Tooltip("The slot's cooldown is the spell's cooldown times this")] private float cooldownMultiplier = 2f;
        [SerializeField, Tooltip("Seconds, so a fast spell isn't spammed")] private float minCooldown = 5f;
        [SerializeField, Tooltip("A press while busy or on cooldown still fires within this many seconds")] private float inputBuffer = 0.15f;

        private ParasiteBody parasite;
        private int chosenSlot;
        private float nextBiteTime;
        private float readyTime;
        private float castBufferedUntil = -1f;

        public SpellData StolenSpell { get; private set; }
        // The body that can be eaten right now, null when there is none
        public HostBody DevourTarget { get; private set; }
        public SpellCaster DevourCaster { get; private set; }
        public int ChosenSlot => chosenSlot;
        public float Cooldown => StolenSpell != null ? Mathf.Max(minCooldown, StolenSpell.Cooldown * cooldownMultiplier) : 0f;
        public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);

        // For ParasiteAudio
        public event Action Bitten;
        public event Action SpellStolen;

        // Static fields survive entering Play Mode when domain reload is off
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCarried() => carried = null;

        private void Awake()
        {
            parasite = GetComponent<ParasiteBody>();
            StolenSpell = carried;
        }

        public void CarryToNextScene() => carried = StolenSpell;

        private void Update()
        {
            HostBody host = parasite.State == ParasiteState.Controlling ? parasite.Host : null;
            if (host != null && (!host.Devourable || host.Health.IsDead)) host = null;
            if (host == DevourTarget) return;

            DevourCaster = host != null ? host.GetComponent<SpellCaster>() : null;
            chosenSlot = FirstSpellSlot(DevourCaster);
            DevourTarget = chosenSlot >= 0 ? host : null;
        }

        private static int FirstSpellSlot(SpellCaster caster)
        {
            if (caster == null) return -1;
            for (int i = 0; i < caster.SlotCount; i++)
                if (caster.GetSpell(i) != null) return i;
            return -1;
        }

        // ParasiteBody's input for the host: while it can be eaten, LMB bites and the cast keys choose instead of acting
        public void Devour(ref ControlInput input)
        {
            if (DevourTarget == null) return;

            if (input.CastSlot > 0 && DevourCaster.GetSpell(input.CastSlot - 1) != null) chosenSlot = input.CastSlot - 1;
            if (input.AttackPressed && Time.time >= nextBiteTime) Bite(DevourTarget);
            input.AttackPressed = false;
            input.CastSlot = 0;
        }

        private void Bite(HostBody host)
        {
            nextBiteTime = Time.time + biteInterval;
            SpellData spell = DevourCaster.GetSpell(chosenSlot);
            host.Health.Consume(host.Health.Max * biteFraction);

            for (int i = 0; i < biteVfxCount; i++)
            {
                Vector3 point = i == 0
                    ? host.LatchPoint.position
                    : host.Center + Vector3.Scale(UnityEngine.Random.insideUnitSphere, host.Bounds.extents);
                PlayVfx(biteVfx, point);
            }
            BodyCamera.ShakeMeleeHit();
            Bitten?.Invoke();
            if (!host.Health.IsDead) return;

            StolenSpell = spell;
            readyTime = 0f;
            foreach (var vfx in finishVfx) PlayVfx(vfx, host.Center);
            BodyCamera.ShakeThrownOff();
            SpellStolen?.Invoke();
        }

        // The blood bursts come with a sound of their own: ParasiteAudio plays the bites instead of a stack of them
        private static void PlayVfx(GameObject prefab, Vector3 point)
        {
            if (prefab == null) return;
            var vfx = Instantiate(prefab, point, UnityEngine.Random.rotation);
            foreach (var source in vfx.GetComponentsInChildren<AudioSource>()) source.Stop();
            Destroy(vfx, 3f);
        }

        // After ParasiteBody gave the host its input: the spell leaves along the current aim
        public void CastStolen(HostBody host, in ControlInput input)
        {
            if (input.StolenCastPressed) castBufferedUntil = Time.time + inputBuffer;
            if (StolenSpell == null || Time.time > castBufferedUntil || Time.time < readyTime) return;
            if (!host.TryCastStolen(StolenSpell, input.CastTimeScale)) return;

            castBufferedUntil = -1f;
            readyTime = Time.time + Cooldown;
        }
    }
}
