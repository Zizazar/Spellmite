using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Spellmite
{
    // Lift in the middle of the tower. The floor's wave starts when the player steps off; the lift takes them up one
    // floor once the wave is over and every enemy on the floor is dead. Falling off a floor respawns the player on the lift, never on a lower floor. Only the floor the player is on is lit:
    // the WebGL quality level gives each object 4 lights, and the floors above and below would take them.
    public class TowerElevator : MonoBehaviour
    {
        [SerializeField] private Transform platform;
        [SerializeField, Tooltip("Floor surface heights from the bottom up, relative to this object")] private float[] floorHeights = { 0f, 6f, 12f, 18f, 24f };
        [SerializeField] private float platformRadius = 2.9f;
        [SerializeField, Tooltip("Below the floor's surface by this much counts as falling off: the player respawns on the lift")] private float fallDepth = 3f;
        [SerializeField] private float speed = 2.5f;
        [SerializeField, Tooltip("Seconds the player stands on the platform before it moves")] private float departDelay = 1f;
        [SerializeField, Tooltip("Empty = no waves, only the enemies placed in the scene")] private WaveSpawner waves;

        [Header("Per floor, same order as the heights")]
        [SerializeField, Tooltip("Torches and other lights. Only the player's floor is on")] private GameObject[] floorLights;
        [SerializeField, Tooltip("Invisible lids over the shaft hole in each floor, so the parasite can't crawl up the shaft. Element 0 stays empty")] private Collider[] shaftSeals;

        [Header("Status")]
        [SerializeField] private Light statusLight;
        [SerializeField] private Color lockedColor = new(0.9f, 0.15f, 0.08f);
        [SerializeField] private Color readyColor = new(0.35f, 0.6f, 1f);

        private ParasiteBody player;
        private int currentFloor;
        private float standTime;
        private bool moving;

        // Read by the HUD
        public int CurrentFloor => currentFloor;
        public int FloorCount => floorHeights.Length;
        public bool IsCurrentFloorCleared => IsFloorCleared(currentFloor);

        private void Start()
        {
            player = FindAnyObjectByType<ParasiteBody>();
#if UNITY_EDITOR
            currentFloor = Mathf.Clamp(Cheats.StartFloor, 0, floorHeights.Length - 1);
#endif
            SetPlatformHeight(floorHeights[currentFloor]);
            UpdateFloors(currentFloor, currentFloor);
#if UNITY_EDITOR
            if (currentFloor > 0 && player != null) PutPlayerOnPlatform();
#endif
        }

#if UNITY_EDITOR
        // Start floor cheat: drop the player onto the deck, it respawns there too
        private void PutPlayerOnPlatform()
        {
            Transform body = player.ActiveBody;
            Vector3 position = platform.position + transform.up;
            if (body.TryGetComponent(out CharacterController controller))
            {
                controller.enabled = false;
                body.position = position;
                controller.enabled = true;
            }
            else body.position = position;
            player.SetSpawnPoint(position);
            Physics.SyncTransforms();
        }
#endif

        private void Update()
        {
            if (moving || player == null) return;

            CheckFall();

            bool cleared = IsFloorCleared(currentFloor);
            if (statusLight != null) statusLight.color = cleared ? readyColor : lockedColor;

            Transform body = player.ActiveBody;
            if (!IsOnPlatform(body))
            {
                standTime = 0f;
                int playerFloor = FloorOf(body.position.y);
                // Stepped off onto this floor: its wave begins (once)
                if (playerFloor == currentFloor && waves != null) waves.StartWave(currentFloor);
                return;
            }

            if (!cleared || currentFloor >= floorHeights.Length - 1)
            {
                standTime = 0f;
                return;
            }

            standTime += Time.deltaTime;
            if (standTime >= departDelay) RideTo(currentFloor + 1).Forget();
        }

        // Fell off the floor (the shaft, over the edge): the parasite comes back on the lift of this floor, never on a lower one.
        // In a host, the host dies first; the parasite is thrown out below the floor and dies next, which respawns it
        private void CheckFall()
        {
            Health parasiteHealth = player.Health;
            if (parasiteHealth.IsDead) return;

            Transform body = player.ActiveBody;
            if (body.position.y > transform.position.y + floorHeights[currentFloor] - fallDepth) return;

            player.SetSpawnPoint(platform.position + transform.up);
            if (player.IsHosted && player.Host != null)
            {
                if (!player.Host.Health.IsDead) Kill(player.Host.Health);
            }
            else Kill(parasiteHealth);
        }

        private static void Kill(Health health)
        {
            health.Invulnerable = false;
            health.TakeDamage(float.MaxValue, Vector3.zero);
        }

        private async UniTaskVoid RideTo(int floor)
        {
            moving = true;
            standTime = 0f;
            UpdateFloors(currentFloor, floor);

            float start = platform.localPosition.y;
            float end = floorHeights[floor];
            float duration = Mathf.Max(0.1f, Mathf.Abs(end - start) / speed);
            for (float t = 0f; t < 1f;)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
                t = Mathf.Min(1f, t + Time.deltaTime / duration);
                float y = Mathf.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                float delta = y - platform.localPosition.y;
                // Going up, lift the rider first so it never ends up inside the deck
                bool carry = IsOnPlatform(player.ActiveBody);
                if (carry && delta > 0f) Carry(delta);
                SetPlatformHeight(y);
                if (carry && delta < 0f) Carry(delta);
            }

            currentFloor = floor;
            UpdateFloors(floor, floor);
            // Dying on this floor must not send the player back down to the first one
            player.SetSpawnPoint(platform.position + transform.up);
            moving = false;
        }

        private void Carry(float delta)
        {
            Transform body = player.ActiveBody;
            Vector3 move = transform.TransformVector(0f, delta, 0f);
            if (body.TryGetComponent(out CharacterController controller)) controller.Move(move);
            else body.position += move;
        }

        private void SetPlatformHeight(float y)
        {
            Vector3 p = platform.localPosition;
            platform.localPosition = new Vector3(p.x, y, p.z);
            // The parasite raycasts the deck right away, it must see where the deck is now
            Physics.SyncTransforms();
        }

        // Light the floors the lift is between, open the shaft only up to the higher one
        private void UpdateFloors(int a, int b)
        {
            int top = Mathf.Max(a, b);
            for (int i = 0; i < floorLights.Length; i++)
            {
                if (floorLights[i] != null) floorLights[i].SetActive(i == a || i == b);
            }
            for (int i = 0; i < shaftSeals.Length; i++)
            {
                if (shaftSeals[i] != null) shaftSeals[i].enabled = i > top;
            }
        }

        private bool IsFloorCleared(int floor)
        {
#if UNITY_EDITOR
            if (Cheats.NoWaves) return true;
#endif
            if (waves != null && !waves.IsWaveDone(floor)) return false;
            foreach (var enemy in EnemyBrain.All)
            {
                if (enemy.IsThinking && FloorOf(enemy.transform.position.y) == floor) return false;
            }
            return true;
        }

        private bool IsOnPlatform(Transform body)
        {
            Vector3 local = platform.InverseTransformPoint(body.position);
            return new Vector2(local.x, local.z).magnitude < platformRadius && local.y > -0.5f && local.y < 2.5f;
        }

        private int FloorOf(float worldY)
        {
            float y = worldY - transform.position.y + 0.5f;
            int floor = 0;
            for (int i = 1; i < floorHeights.Length; i++)
            {
                if (y >= floorHeights[i]) floor = i;
            }
            return floor;
        }
    }
}
