using UnityEngine;

namespace Spellmite
{
    // The tower run's numbers for the EndScreen, counted from the scene's load like the timer: bodies the player's damage
    // finished off, the parasite's deaths, possessions, and the longest chain of possessions without the parasite touching
    // a floor (walls don't break it). Sits on the parasite, which every scene makes anew.
    [RequireComponent(typeof(ParasiteBody))]
    public class RunStats : MonoBehaviour
    {
        private ParasiteBody parasite;
        private int chain;

        public static RunStats Instance { get; private set; }

        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Possessions { get; private set; }
        public int BestChain { get; private set; }

        private void Awake()
        {
            Instance = this;
            parasite = GetComponent<ParasiteBody>();
        }

        // Start, not OnEnable: the parasite's Health is set in its Awake
        private void Start()
        {
            Health.AnyKilledByPlayer += OnKilled;
            parasite.Latched += OnLatched;
            parasite.Health.Died += OnDied;
        }

        private void OnDestroy()
        {
            Health.AnyKilledByPlayer -= OnKilled;
            parasite.Latched -= OnLatched;
            if (parasite.Health != null) parasite.Health.Died -= OnDied;
        }

        private void Update()
        {
            if (parasite.IsOnFloor) chain = 0;
        }

        private void OnKilled(Health _) => Kills++;

        private void OnDied() => Deaths++;

        private void OnLatched()
        {
            Possessions++;
            chain++;
            BestChain = Mathf.Max(BestChain, chain);
        }
    }
}
