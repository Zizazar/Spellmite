using GameJam.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spellmite
{
    // Risk of Rain style HUD: health of the body the player is in (bottom left), skills (bottom right),
    // floor, run timer and objective (top right). The crosshair and the resistance bar are ParasiteHud's.
    public class GameHud : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("Empty = no floor panel")] private TowerElevator elevator;
        [SerializeField] private WaveSpawner waves;

        [Header("Health")]
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

        [Header("Floor")]
        [SerializeField] private GameObject floorPanel;
        [SerializeField, Tooltip("Key hud.floor")] private LocText floorText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private LocText objectiveText;
        [SerializeField, Tooltip("Tick in the objective box, on once the floor is cleared")] private Graphic objectiveCheck;

        private Health shownHealth;
        private float trailHoldUntil;
        private HostBody casterHost;
        private SpellCaster caster;
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
            Health health = parasite.IsHosted && parasite.Host != null ? parasite.Host.Health : parasite.Health;
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
            leapSlot.Show(leapIcon, 0f, 0f, host != null || parasite.AimedHost != null);

            for (int i = 0; i < spellSlots.Length; i++)
            {
                SpellData spell = caster != null ? caster.GetSpell(i) : null;
                if (spell == null) spellSlots[i].Show(null, 0f, 0f, false);
                else spellSlots[i].Show(spell.Icon, caster.CooldownRemaining(i), spell.Cooldown, true);
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

            bool cleared = elevator.IsCurrentFloorCleared;
            string key;
            int done = 0, total = 0;
            if (cleared)
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
        }
    }
}
