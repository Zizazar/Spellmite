using Cysharp.Threading.Tasks;
using GameJam.Localization;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Spellmite
{
    // The end of the run, faded in over the boss's burning hall until it hides it and its sound: the run time, the best
    // one (saved) or "new record", the run's stats (counting up one row after another once the screen is in), and Restart,
    // which replays the tower from the start. BossEnding shows it.
    public class EndScreen : MonoBehaviour
    {
        private const string BestTimeKey = "run.best_time";

        [SerializeField, Tooltip("The whole screen, hidden until shown")] private CanvasGroup root;
        [SerializeField, Tooltip("Grows a little while it fades in")] private RectTransform panel;
        [SerializeField, Tooltip("Key end.time")] private LocText timeText;
        [SerializeField, Tooltip("Key end.best. Hidden when this run is the record")] private LocText bestText;
        [SerializeField, Tooltip("Shown when this run beat the best time")] private GameObject newRecord;
        [SerializeField] private Button restartButton;
        [SerializeField] private float fadeTime = 1.5f;

        [Header("Stats")]
        [SerializeField, Tooltip("Keys end.kills, end.deaths, end.possessions, end.chain, in this order")] private LocText[] statRows;
        [SerializeField, Tooltip("Seconds between rows")] private float rowDelay = 0.2f;
        [SerializeField, Tooltip("Seconds a row counts up to its number")] private float rowCountTime = 0.4f;
        [SerializeField] private float rowPunch = 0.15f;

        private void Awake()
        {
            root.alpha = 0f;
            root.interactable = root.blocksRaycasts = false;
            restartButton.onClick.AddListener(Restart);
        }

        // The returned tween ends once the screen is fully in and hides the hall
        public Tween Show(float time)
        {
            float best = PlayerPrefs.GetFloat(BestTimeKey, float.MaxValue);
            bool record = time < best;
            if (record)
            {
                PlayerPrefs.SetFloat(BestTimeKey, time);
                PlayerPrefs.Save();
            }
            timeText.SetArgs(FormatTime(time));
            if (!record) bestText.SetArgs(FormatTime(best));
            bestText.gameObject.SetActive(!record);
            newRecord.SetActive(record);
            foreach (LocText row in statRows)
            {
                row.SetArgs(0);
                row.GetComponent<TMP_Text>().alpha = 0f;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // The tower has no event system of its own: the main menu's stays in the tutorial
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            _ = Tween.Scale(panel, 0.9f, 1f, fadeTime, Ease.OutCubic);
            // All the sound goes with the hall: music, explosions, fire
            _ = Tween.Custom(this, AudioListener.volume, 0f, fadeTime, (_, volume) => AudioListener.volume = volume);
            // Clickable only once it's in, so a key still held from the fight can't restart right away
            return Tween.Alpha(root, 1f, fadeTime).OnComplete(this, screen => screen.Activate());
        }

        private void Activate()
        {
            root.interactable = root.blocksRaycasts = true;
            // Keyboard and gamepad start on the button
            EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            RevealStats().Forget();
        }

        private async UniTaskVoid RevealStats()
        {
            RunStats stats = RunStats.Instance;
            if (stats == null) return;
            int[] values = { stats.Kills, stats.Deaths, stats.Possessions, stats.BestChain };
            for (int i = 0; i < statRows.Length && i < values.Length; i++)
            {
                LocText row = statRows[i];
                _ = Tween.Alpha(row.GetComponent<TMP_Text>(), 1f, 0.15f);
                _ = Tween.PunchScale(row.transform, Vector3.one * rowPunch, 0.3f);
                if (values[i] > 0)
                {
                    await Tween.Custom(row, 0f, values[i], rowCountTime, (r, v) => r.SetArgs(Mathf.RoundToInt(v)), Ease.OutCubic)
                        .ToUniTask().AttachExternalCancellation(destroyCancellationToken);
                }
                await UniTask.Delay(System.TimeSpan.FromSeconds(rowDelay), cancellationToken: destroyCancellationToken);
            }
        }

        private static void Restart()
        {
            // Both outlive the scene
            Time.timeScale = 1f;
            AudioListener.volume = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static string FormatTime(float t) =>
            $"{Mathf.Floor(t / 60f):00}:{Mathf.Floor(t % 60f):00}.{Mathf.Floor(t * 100f % 100f):00}"; // noloc
    }
}
