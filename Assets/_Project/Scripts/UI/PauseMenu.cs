using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Spellmite
{
    // Esc (Start on a gamepad) during play: stops time and sound (the music plays on), frees the cursor and shows the
    // settings (look sensitivity, music and sound volume, saved) with Resume and Restart. On WebGL the browser takes
    // Esc to release the cursor lock and the game may never see the key, so losing the lock opens the menu as well
    public class PauseMenu : MonoBehaviour
    {
        private const string SensitivityKey = "settings.sensitivity";
        private const string MusicKey = "settings.music";
        private const string SfxKey = "settings.sfx";
        private const string MusicParameter = "MusicVolume";
        private const string SfxParameter = "SfxVolume";

        // Multiplies the camera's look speed, mouse and stick
        public static float LookSensitivity { get; private set; } = 1f;
        public static bool IsPaused { get; private set; }

        [SerializeField, Tooltip("Pausing works only while it's enabled: not in the main menu or during the boss ending")] private PlayerBrain playerBrain;
        [SerializeField, Tooltip("Exposes MusicVolume and SfxVolume")] private AudioMixer mixer;
        [SerializeField, Tooltip("The whole menu, hidden until paused")] private CanvasGroup root;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private TMP_Text sensitivityValue;
        [SerializeField] private TMP_Text musicValue;
        [SerializeField] private TMP_Text sfxValue;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;

        private float timeScaleBefore = 1f;
        private bool wasLocked;

        private void Awake()
        {
            // Statics outlive a scene reload
            IsPaused = false;
            LookSensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
            Show(false);
        }

        private void Start()
        {
            // The mixer ignores SetFloat before Start
            float music = PlayerPrefs.GetFloat(MusicKey, 1f);
            float sfx = PlayerPrefs.GetFloat(SfxKey, 1f);
            SetVolume(MusicParameter, music);
            SetVolume(SfxParameter, sfx);

            sensitivitySlider.SetValueWithoutNotify(LookSensitivity);
            musicSlider.SetValueWithoutNotify(music);
            sfxSlider.SetValueWithoutNotify(sfx);
            UpdateValues();

            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
        }

        private void Update()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (IsPaused)
            {
                if (PausePressed()) Resume();
            }
            else if (playerBrain.enabled && (PausePressed() || (wasLocked && !locked)))
            {
                Pause();
            }
            wasLocked = Cursor.lockState == CursorLockMode.Locked;
        }

        private static bool PausePressed() =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        private void Pause()
        {
            IsPaused = true;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true; // the music sources ignore it
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // The tower has no event system of its own: the main menu's stays in the tutorial
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Show(true);
            // Keyboard and gamepad start on the button
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        private void Resume()
        {
            PlayerPrefs.Save();
            IsPaused = false;
            Time.timeScale = timeScaleBefore;
            AudioListener.pause = false;
            Show(false);
            EventSystem.current.SetSelectedGameObject(null);
            // A click or a key press is a user input event, so this works on WebGL too (or the next click in game locks it)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Restart()
        {
            PlayerPrefs.Save();
            // Both outlive the scene
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnSensitivityChanged(float value)
        {
            LookSensitivity = value;
            PlayerPrefs.SetFloat(SensitivityKey, value);
            UpdateValues();
        }

        private void OnMusicChanged(float value)
        {
            SetVolume(MusicParameter, value);
            PlayerPrefs.SetFloat(MusicKey, value);
            UpdateValues();
        }

        private void OnSfxChanged(float value)
        {
            SetVolume(SfxParameter, value);
            PlayerPrefs.SetFloat(SfxKey, value);
            UpdateValues();
        }

        // Slider 0..1 to decibels: the ear hears loudness logarithmically
        private void SetVolume(string parameter, float value) =>
            mixer.SetFloat(parameter, value > 0.0001f ? Mathf.Log10(value) * 20f : -80f);

        private void UpdateValues()
        {
            sensitivityValue.text = $"{sensitivitySlider.value:0.0}x"; // noloc
            musicValue.text = $"{Mathf.RoundToInt(musicSlider.value * 100f)}%"; // noloc
            sfxValue.text = $"{Mathf.RoundToInt(sfxSlider.value * 100f)}%"; // noloc
        }

        private void Show(bool show)
        {
            root.alpha = show ? 1f : 0f;
            root.interactable = root.blocksRaycasts = show;
        }
    }
}
