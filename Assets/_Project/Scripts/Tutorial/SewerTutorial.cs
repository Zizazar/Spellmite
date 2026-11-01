using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameJam.Localization;
using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Spellmite
{
    // The sewer tutorial before the tower, step by step: possess the standing body, burn the crate heap with its
    // Fireball, leap to a body across the chasm, beat the room that wakes up, crawl into the drain to the tower.
    // A fall into the chasm puts the player back into the first body at the edge; dying in the room puts it back to
    // sleep. The objective goes into the HUD's floor panel, which only the tower uses otherwise.
    [DefaultExecutionOrder(100)] // Start after GameHud's, which hides the floor panel without a lift
    public class SewerTutorial : MonoBehaviour
    {
        private enum Step { Possess, BurnCrates, CrossChasm, Fight, Drain }

        [SerializeField] private ParasiteBody parasite;

        [Header("Corridor")]
        [SerializeField, Tooltip("Stands without a brain; possessing it never runs out")] private HostBody corridorBody;
        [SerializeField, Tooltip("Parent of the crates blocking the corridor")] private Transform crateHeap;
        [SerializeField, Tooltip("A fall into the chasm puts the corridor body here, with the player inside")] private Transform chasmRespawn;
        [SerializeField, Tooltip("Below this height the player has fallen into the chasm")] private float fallHeight = -1.5f;
        [SerializeField, Tooltip("Its children are the waypoints the corridor body walks off along once the player is across")] private Transform exitPath;

        [Header("Room")]
        [SerializeField, Tooltip("Asleep (no brain, can't be hurt) until one of them is possessed")] private EnemyBrain[] roomBodies;
        [SerializeField, Tooltip("Spawned asleep in their places when the room starts over")] private EnemyBrain roomBodyPrefab;
        [SerializeField, Tooltip("Where the parasite comes back after dying in the room")] private Transform roomRespawn;

        [Header("Drain")]
        [SerializeField, Tooltip("Slides up into the wall once the room is cleared")] private Transform drainGrate;
        [SerializeField] private float grateLift = 0.7f;
        [SerializeField, Tooltip("The free parasite this close to the point has gone down the drain")] private Transform drainEnd;
        [SerializeField] private float drainRadius = 0.7f;
        [SerializeField] private string nextScene = "Tower";

        [Header("HUD (the tower's floor panel)")]
        [SerializeField] private GameObject objectivePanel;
        [SerializeField] private LocText titleText;
        [SerializeField, Tooltip("The run timer, hidden here")] private GameObject timer;
        [SerializeField] private LocText objectiveText;
        [SerializeField] private Graphic objectiveCheck;
        [SerializeField] private GameObject progressRoot;
        [SerializeField, Tooltip("Image Type = Filled")] private Image progressFill;

        [Header("Resistance hint")]
        [SerializeField, Tooltip("Shown with slow motion the first time a room body is about to throw the parasite off")] private LocText resistanceHint;
        [SerializeField, Range(0f, 1f)] private float hintResistance = 0.3f;
        [SerializeField] private float slowMotionScale = 0.2f;
        [SerializeField, Tooltip("Real seconds, shorter if the player leaps")] private float slowMotionTime = 1.5f;
        [SerializeField, Tooltip("Real seconds the hint stays after the slow motion")] private float hintTime = 2f;

        private Step step;
        private BurnableCrate[] crates;
        private Pose[] roomPoses;
        private Transform roomParent;
        private string leapKey;
        private string castKey;
        private bool hintShown;
        private int shownDone = -1;
        private bool? shownLeave;

        private void Start()
        {
            crates = crateHeap.GetComponentsInChildren<BurnableCrate>();
            corridorBody.EndlessPossession = true;
            roomParent = roomBodies[0].transform.parent;
            roomPoses = new Pose[roomBodies.Length];
            for (int i = 0; i < roomBodies.Length; i++)
            {
                roomPoses[i] = new Pose(roomBodies[i].transform.position, roomBodies[i].transform.rotation);
                Sleep(roomBodies[i]);
            }
            parasite.Health.Died += OnPlayerDied;
            leapKey = KeyName("Player/Leap"); // noloc
            castKey = KeyName("Player/Cast1"); // noloc

            objectivePanel.SetActive(true);
            timer.SetActive(false);
            titleText.SetKey("tutorial.title");
            resistanceHint.gameObject.SetActive(false);
            SetStep(Step.Possess);
        }

        private void OnDestroy()
        {
            if (parasite != null) parasite.Health.Died -= OnPlayerDied;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            CheckFall();
            HostBody host = parasite.IsHosted ? parasite.Host : null;

            // A room body taken over starts the fight from any earlier step, even if the crates were somehow skipped
            if (step < Step.Fight && IsRoomBody(host)) StartFight();

            switch (step)
            {
                case Step.Possess:
                    if (host != null && host == corridorBody) SetStep(Step.BurnCrates);
                    break;
                case Step.BurnCrates:
                    if (AllCratesBurnt()) SetStep(Step.CrossChasm);
                    break;
                case Step.Fight:
                    UpdateFight();
                    break;
                case Step.Drain:
                    UpdateDrain(host);
                    break;
            }
        }

        private void SetStep(Step value)
        {
            step = value;
            shownDone = -1;
            shownLeave = null;
            objectiveCheck.enabled = step == Step.Drain;
            progressRoot.SetActive(false);
            switch (step)
            {
                case Step.Possess: objectiveText.SetKey("tutorial.possess"); break;
                case Step.BurnCrates: objectiveText.SetKey("tutorial.burn", castKey); break;
                case Step.CrossChasm: objectiveText.SetKey("tutorial.chasm", leapKey); break;
            }
        }

        private bool AllCratesBurnt()
        {
            foreach (var crate in crates)
                if (crate != null && !crate.IsBurnt) return false; // burnt ones are destroyed
            return true;
        }

        // ---------- Chasm ----------

        private void CheckFall()
        {
            if (parasite.Health.IsDead || parasite.ActiveBody.position.y > fallHeight) return;

            if (corridorBody != null)
            {
                // Before the fight: back at the edge inside the corridor body, whatever the player was
                corridorBody.Teleport(chasmRespawn.position, chasmRespawn.rotation);
                if (parasite.Host != corridorBody) parasite.Possess(corridorBody);
                return;
            }

            // The corridor body left when the fight began: now a fall is a death (and the room starts over while it's on)
            Kill(parasite.IsHosted && parasite.Host != null ? parasite.Host.Health : parasite.Health);
        }

        private static void Kill(Health health)
        {
            health.Invulnerable = false;
            health.TakeDamage(float.MaxValue, Vector3.zero);
        }

        // ---------- Room ----------

        // Asleep: no brain, and nothing hurts it, so it can't be fought from across the chasm
        private static void Sleep(EnemyBrain body)
        {
            body.enabled = false;
            body.GetComponent<HostBody>().Health.Invulnerable = true;
        }

        private bool IsRoomBody(HostBody host)
        {
            if (host == null) return false;
            foreach (var body in roomBodies)
                if (body != null && body.gameObject == host.gameObject) return true;
            return false;
        }

        private void StartFight()
        {
            foreach (var body in roomBodies)
            {
                body.enabled = true;
                body.GetComponent<HostBody>().Health.Invulnerable = false;
            }
            parasite.SetSpawnPoint(roomRespawn.position);
            if (corridorBody != null)
            {
                WalkAway(corridorBody, destroyCancellationToken).Forget();
                corridorBody = null;
            }
            SetStep(Step.Fight);
        }

        // Like a tower wave: possessed counts as beaten
        private void UpdateFight()
        {
            int free = 0;
            foreach (var body in roomBodies)
                if (body != null && body.IsThinking) free++;

            int done = roomBodies.Length - free;
            if (done != shownDone)
            {
                shownDone = done;
                objectiveText.SetKey("hud.objective.wave", done, roomBodies.Length);
                progressRoot.SetActive(true);
                progressFill.fillAmount = (float)done / roomBodies.Length;
            }

            if (free == 0) ClearRoom();
            else CheckResistanceHint();
        }

        private void ClearRoom()
        {
            // A body the player still sits in dies once they leave it, as in the tower
            foreach (var body in roomBodies)
                if (body != null && body.TryGetComponent(out HostBody host) && host.IsPossessed)
                    WaveSpawner.KillOnRelease(host, destroyCancellationToken).Forget();

            Tween.PositionY(drainGrate, drainGrate.position.y + grateLift, 0.8f, Ease.InOutSine);
            SetStep(Step.Drain);
        }

        // Dying in the room puts it back to sleep; the parasite comes back at its entrance (set when the fight began)
        private void OnPlayerDied()
        {
            if (step != Step.Fight) return;
            for (int i = 0; i < roomBodies.Length; i++)
            {
                if (roomBodies[i] != null) Destroy(roomBodies[i].gameObject);
                roomBodies[i] = Instantiate(roomBodyPrefab, roomPoses[i].position, roomPoses[i].rotation, roomParent);
                Sleep(roomBodies[i]);
            }
            SetStep(Step.Possess);
        }

        // The corridor body isn't needed any more: it walks off into the dark side passage and is gone
        private async UniTaskVoid WalkAway(HostBody body, CancellationToken token)
        {
            float giveUpTime = Time.time + 20f;
            foreach (Transform point in exitPath)
            {
                while (body != null && Time.time < giveUpTime)
                {
                    Vector3 to = Vector3.ProjectOnPlane(point.position - body.transform.position, Vector3.up);
                    if (to.sqrMagnitude < 0.25f) break;
                    if (!body.IsPossessed) body.SetInput(new ControlInput { Move = to.normalized, Aim = to.normalized });
                    await UniTask.NextFrame(token);
                }
            }
            if (body != null && !body.IsPossessed) Destroy(body.gameObject);
        }

        // The first time a room body is about to throw the parasite off: slow motion and a hint to leap on
        private void CheckResistanceHint()
        {
            if (hintShown || parasite.State != ParasiteState.Controlling || parasite.Host == null
                || parasite.Host.Resistance01 > hintResistance) return;
            hintShown = true;
            ShowResistanceHint(parasite.Host, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid ShowResistanceHint(HostBody host, CancellationToken token)
        {
            resistanceHint.SetKey("tutorial.hint.resist", leapKey);
            resistanceHint.gameObject.SetActive(true);
            Time.timeScale = slowMotionScale;
            float end = Time.unscaledTime + slowMotionTime;
            await UniTask.WaitUntil(() => Time.unscaledTime >= end || parasite.Host != host, cancellationToken: token);
            Time.timeScale = 1f;
            await UniTask.Delay(TimeSpan.FromSeconds(hintTime), ignoreTimeScale: true, cancellationToken: token);
            resistanceHint.gameObject.SetActive(false);
        }

        // ---------- Drain ----------

        private void UpdateDrain(HostBody host)
        {
            bool leave = host != null;
            if (leave != shownLeave)
            {
                shownLeave = leave;
                objectiveText.SetKey(leave ? "tutorial.drain.leave" : "tutorial.drain", leapKey);
            }

            if (!leave && parasite.State == ParasiteState.Free
                && (parasite.transform.position - drainEnd.position).sqrMagnitude <= drainRadius * drainRadius)
            {
                enabled = false;
                Time.timeScale = 1f;
                SceneManager.LoadScene(nextScene);
            }
        }

        private static string KeyName(string actionName)
        {
            var action = InputSystem.actions.FindAction(actionName);
            return action != null ? action.GetBindingDisplayString(group: "Keyboard&Mouse") : ""; // noloc
        }
    }
}
