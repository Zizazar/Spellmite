using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // Half-Life squad slots: only a few enemies attack at once, the rest reposition. Enemies the player sees ask for
    // tokens every frame they want to cast; once a frame the free tokens go to the ones closest to the screen center.
    // A token comes back TokenHoldTime after the cast, or at once when its holder dies, leaves the screen or is possessed.
    // Also keeps the alarm: a wave opens with the enemies unaware of the player, and the first thing that gives the player
    // away alerts the whole wave until it ends. Outside waves (tutorial, boss floor) the alarm is always up.
    public class AttackDirector : MonoBehaviour
    {
        private class Token
        {
            public EnemyBrain Holder; // null once released
            public int Cost;
            public float FreeTime;    // back in the pool at this time
        }

        // A granted token nobody casts with goes back after this
        private const float UnclaimedTime = 0.3f;

        private static readonly System.Comparison<(EnemyBrain enemy, int cost)> ByScreenOffset =
            (a, b) => a.enemy.ScreenOffset.CompareTo(b.enemy.ScreenOffset);

        public static AttackDirector Instance { get; private set; }

        [SerializeField] private DifficultySettings settings;

        private readonly List<Token> tokens = new();
        private readonly List<(EnemyBrain enemy, int cost)> requests = new();
        private readonly Dictionary<EnemyBrain, float> readyTimes = new();
        private ParasiteBody player;
        private HostBody lastHost;
        private float graceUntil;
        private float possessedAt = float.PositiveInfinity; // infinity once this body has given itself away
        private float alarmTimeoutAt = float.PositiveInfinity;

        public DifficultySettings Settings => settings;
        public bool IsAlarmed { get; private set; } = true;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable() => HostBody.AnyDamaged += OnHostDamaged;
        private void OnDisable() => HostBody.AnyDamaged -= OnHostDamaged;

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
        }

        // A wave starts: nobody knows about the player yet
        public void BeginStealth()
        {
            IsAlarmed = false;
            alarmTimeoutAt = settings.AlarmTimeout > 0f ? Time.time + settings.AlarmTimeout : float.PositiveInfinity;
        }

        // Reinforcements read IsAlarmed too, so they arrive alerted. Enemies get ReactionDelay to turn, and the "!" time to read
        public void RaiseAlarm()
        {
            if (IsAlarmed) return;
            IsAlarmed = true;
            graceUntil = Mathf.Max(graceUntil, Time.time + settings.ReactionDelay);
            foreach (var enemy in EnemyBrain.All) enemy.ShowAlarm();
        }

        private void OnHostDamaged(HostBody host, float amount)
        {
            if (!host.IsPossessed) RaiseAlarm();
        }

        // Seen long enough, and the player isn't just settling into a new body. Melee needs only this, spells also a token.
        public bool MayAttack(EnemyBrain enemy) => enemy.HasNoticedPlayer && Time.time >= graceUntil;

        public bool HoldsToken(EnemyBrain enemy) => Find(enemy) != null;

        // true = the enemy has a token and casts now. Otherwise its request competes in this frame's handout.
        public bool TryAcquire(EnemyBrain enemy, int cost)
        {
            var token = Find(enemy);
            if (token != null)
            {
                token.FreeTime = float.MaxValue; // in use until Release
                return true;
            }

            if (MayAttack(enemy) && Time.time >= ReadyTime(enemy) && !IsRequesting(enemy))
                requests.Add((enemy, Mathf.Min(cost, settings.MaxTokens)));
            return false;
        }

        // The cast is over. The token comes back a bit later so attacks come in waves, not as a machine gun.
        public void Release(EnemyBrain enemy)
        {
            var token = Find(enemy);
            if (token == null) return;
            token.Holder = null;
            token.FreeTime = Time.time + settings.TokenHoldTime;
            readyTimes[enemy] = Time.time + settings.EnemyTokenCooldown;
        }

        private void LateUpdate()
        {
            HostBody host = player != null ? player.Host : null;
            if (host != lastHost)
            {
                lastHost = host;
                if (host != null)
                {
                    tokens.Clear();
                    graceUntil = Time.time + settings.PossessionGrace;
                    possessedAt = Time.time;
                }
            }

            // The body's first cast or swing gives it away: the grace ends, so chained hops aren't free
            if (host != null && host.LastActionTime >= possessedAt)
            {
                possessedAt = float.PositiveInfinity;
                graceUntil = Mathf.Min(graceUntil, Time.time);
                RaiseAlarm();
            }

            if (Time.time >= alarmTimeoutAt) RaiseAlarm();

            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                var token = tokens[i];
                bool lostHolder = token.Holder is not null && !CanHold(token.Holder);
                if (lostHolder || Time.time >= token.FreeTime) tokens.RemoveAt(i);
            }

            if (Time.time >= graceUntil)
            {
                requests.Sort(ByScreenOffset);
                int free = FreeTokens();
                foreach (var (enemy, cost) in requests)
                {
                    // The best candidate waits for enough tokens instead of being skipped
                    if (cost > free) break;
                    tokens.Add(new Token { Holder = enemy, Cost = cost, FreeTime = Time.time + UnclaimedTime });
                    free -= cost;
                }
            }
            requests.Clear();
        }

        private static bool CanHold(EnemyBrain enemy) => enemy != null && enemy.IsThinking && enemy.IsSeenByPlayer;

        private int FreeTokens()
        {
            int free = settings.MaxTokens;
            foreach (var token in tokens) free -= token.Cost;
            return free;
        }

        private Token Find(EnemyBrain enemy)
        {
            foreach (var token in tokens)
                if (token.Holder == enemy) return token;
            return null;
        }

        private bool IsRequesting(EnemyBrain enemy)
        {
            foreach (var request in requests)
                if (request.enemy == enemy) return true;
            return false;
        }

        private float ReadyTime(EnemyBrain enemy) => readyTimes.TryGetValue(enemy, out float time) ? time : 0f;
    }
}
