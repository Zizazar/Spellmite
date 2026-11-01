using GameJam.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // Risk of Rain style HUD: name and health of the body the player is in (bottom left), skills (bottom right),
    // floor, run timer and objective (top right). The crosshair and the control bar are ParasiteHud's.
    public class GameHud : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("Empty = no floor panel")] private TowerElevator elevator;
        [SerializeField] private WaveSpawner waves;
        [SerializeField, Tooltip("Boss on the top floor. Empty = none")] private TreeBoss boss;

        [Header("Health")]
        [SerializeField, Tooltip("Name of the body: the host's NameKey, or parasiteNameKey when free")] private LocText bodyName;
        [SerializeField] private string parasiteNameKey = "body.parasite";
        [SerializeField, Tooltip("Image Type = Filled, Horizontal")] private Image healthFill;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal. Light bar that lags behind after a hit")] private Image healthTrail;
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private Color healthColor = new(0.45f, 0.8f, 0.25f);
        [SerializeField] private Color lowHealthColor = new(0.85f, 0.2f, 0.15f);
        [SerializeField, Range(0f, 1f)] private float lowHealth = 0.25f;
        [SerializeField, Tooltip("Seconds the trail waits before it drains")] private float trailDelay = 0.4f;
        [SerializeField, Tooltip("Bar fractions per second")] private float trailSpeed = 0.8f;

        [Header("Skills")]
        [SerializeField] private SkillSlot attackSlot;
        [SerializeField] private SkillSlot leapSlot;
        [SerializeField, Tooltip("Cast1..Cast4")] private SkillSlot[] spellSlots;
        [SerializeField] private Sprite biteIcon;
        [SerializeField] private Sprite hostAttackIcon;
        [SerializeField] private Sprite leapIcon;
        [SerializeField, Tooltip("Spell slots and their separator, hidden while the parasite is free")] private GameObject spellsRoot;
        [SerializeField, Tooltip("\"Possess an enemy to cast spells\", shown instead of the spells while free")] private GameObject freeHint;
        [SerializeField, Tooltip("While the host's control is low the leap slot punches this often (twice as often when critical)")] private float leapPulseInterval = 0.6f;
        [SerializeField] private float leapPulseStrength = 0.2f;

        [Header("Floor")]
        [SerializeField] private GameObject floorPanel;
        [SerializeField, Tooltip("Key hud.floor")] private LocText floorText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private LocText objectiveText;
        [SerializeField, Tooltip("Tick in the objective box, on once the floor is cleared")] private Graphic objectiveCheck;
        [SerializeField, Tooltip("Shown while a wave is on")] private GameObject waveProgressRoot;
        [SerializeField, Tooltip("Image Type = Filled, Horizontal. Enemies defeated / wave size")] private Image waveProgressFill;

        private Health shownHealth;
        private string shownNameKey;
        private float trailHoldUntil;
        private HostBody casterHost;
        private SpellCaster caster;
        private float nextLeapPulse;
        private int shownFloor = -1;
        private string objectiveKey;
        private int objectiveDone = -1;
        private int objectiveTotal = -1;

        private void Start()
        {
            if (floorPanel != null) floorPanel.SetActive(elevator != null);
        }

        private void Update()
        {
            UpdateHealth();
            UpdateSkills();
            if (elevator != null) UpdateFloor();
        }

        private void UpdateHealth()
        {
            bool hosted = parasite.IsHosted && parasite.Host != null;
            Health health = hosted ? parasite.Host.Health : parasite.Health;
            string nameKey = hosted ? parasite.Host.NameKey : parasiteNameKey;
            if (nameKey != shownNameKey)
            {
                shownNameKey = nameKey;
                bodyName.SetKey(nameKey);
            }
            float fill = health.Max > 0f ? health.Current / health.Max : 0f;

            // Another body: its bar starts without a trail
            if (health != shownHealth)
            {
                shownHealth = health;
                healthTrail.fillAmount = fill;
            }
            else if (fill < healthFill.fillAmount - 1e-4f)
            {
                trailHoldUntil = Time.time + trailDelay;
            }

            healthFill.fillAmount = fill;
            healthFill.color = fill <= lowHealth ? lowHealthColor : healthColor;
            if (healthTrail.fillAmount <= fill) healthTrail.fillAmount = fill;
            else if (Time.time >= trailHoldUntil) healthTrail.fillAmount = Mathf.MoveTowards(healthTrail.fillAmount, fill, trailSpeed * Time.deltaTime);

            healthText.SetText("{0} / {1}", Mathf.Ceil(health.Current), Mathf.Ceil(health.Max));
        }

        // Free, the parasite only bites and leaps; in a host LMB swings, RMB leaps out and 1-4 are the host's spells
        private void UpdateSkills()
        {
            HostBody host = parasite.State == ParasiteState.Controlling ? parasite.Host : null;
            if (host != casterHost)
            {
                casterHost = host;
                caster = host != null ? host.GetComponent<SpellCaster>() : null;
            }

            attackSlot.Show(host != null ? hostAttackIcon : biteIcon, 0f, 0f, true);
            // Control running out: the leap slot is the way out, so it lights up and keeps punching
            bool low = parasite.IsControlLow;
            leapSlot.Show(leapIcon, 0f, 0f, host != null || parasite.AimedHost != null, low);
            if (low && Time.time >= nextLeapPulse)
            {
                leapSlot.Punch(leapPulseStrength);
                nextLeapPulse = Time.time + leapPulseInterval * (parasite.IsControlCritical ? 0.5f : 1f);
            }

            // Free there is nothing to cast: the slots make way for a hint on how to get spells
            spellsRoot.SetActive(host != null);
            freeHint.SetActive(host == null);
            if (host == null) return;

            for (int i = 0; i < spellSlots.Length; i++)
            {
                SpellData spell = caster != null ? caster.GetSpell(i) : null;
                if (spell == null) spellSlots[i].Show(null, 0f, 0f, false);
                else spellSlots[i].Show(spell.Icon, caster.CooldownRemaining(i), spell.Cooldown, true, spell.ResistanceCost >= host.Resistance01);
            }
        }

        private void UpdateFloor()
        {
            float t = Time.timeSinceLevelLoad;
            timerText.SetText("{0:00}:{1:00}<size=60%>.{2:00}</size>", Mathf.Floor(t / 60f), Mathf.Floor(t % 60f), Mathf.Floor(t * 100f % 100f)); // noloc

            int floor = elevator.CurrentFloor;
            if (floor != shownFloor)
            {
                shownFloor = floor;
                floorText.SetArgs(floor + 1);
            }

            // The boss floor isn't cleared while the boss lives, even before the fight starts
            bool bossAlive = boss != null && !boss.IsDefeated && floor >= elevator.FloorCount - 1;
            bool cleared = elevator.IsCurrentFloorCleared && !bossAlive;
            string key;
            int done = 0, total = 0;
            if (bossAlive)
            {
                key = "hud.objective.boss";
            }
            else if (cleared)
            {
                key = floor >= elevator.FloorCount - 1 ? "hud.objective.top" : "hud.objective.lift";
            }
            else if (waves != null && waves.ActiveFloor == floor && waves.TotalCount > 0)
            {
                // Possessed enemies count as defeated, same as for the wave
                key = "hud.objective.wave";
                done = waves.SpawnedCount - waves.AliveCount;
                total = waves.TotalCount;
            }
            else
            {
                key = "hud.objective.clear";
            }

            if (key == objectiveKey && done == objectiveDone && total == objectiveTotal) return;
            objectiveKey = key;
            objectiveDone = done;
            objectiveTotal = total;
            objectiveText.SetKey(key, done, total);
            objectiveCheck.enabled = cleared;
            waveProgressRoot.SetActive(total > 0);
            if (total > 0) waveProgressFill.fillAmount = (float)done / total;
        }
    }
}
