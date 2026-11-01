using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameJam.Localization;
using PrimeTween;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Spellmite
{
    // The game opens straight in the tutorial, the camera on the parasite and the menu over it. The first launch asks
    // for the language (saved, never asked again), then "press any key": the camera flies from the parasite to its usual
    // place behind it, the HUD fades in on the way, and the player gets control once the camera is there.
    public class MainMenu : MonoBehaviour
    {
        // Loc saves the chosen language under this key: no key = never chosen
        private const string LanguagePrefsKey = "loc.language";

        [Header("Game")]
        [SerializeField, Tooltip("Off until the camera has arrived")] private PlayerBrain playerBrain;
        [SerializeField, Tooltip("Hidden in the menu, fades in during the flight")] private CanvasGroup hud;
        [SerializeField] private CinemachineBrain cameraBrain;
        [SerializeField, Tooltip("Looks at the parasite. Priority above the player's camera while the menu is on")] private CinemachineCamera menuCamera;
        [SerializeField, Tooltip("From the menu camera to the player's")] private CinemachineBlendDefinition flyBlend = new(CinemachineBlendDefinition.Styles.EaseInOut, 2.5f);

        [Header("Menu")]
        [SerializeField, Tooltip("The whole menu, fades out when the game starts")] private CanvasGroup root;
        [SerializeField] private CanvasGroup languagePanel;
        [SerializeField, Tooltip("Copied for every language, then hidden")] private Button languageButtonTemplate;
        [SerializeField, Tooltip("\"Press any key\" and the language button")] private CanvasGroup startPanel;
        [SerializeField] private Graphic pressAnyKey;
        [SerializeField] private Button changeLanguageButton;
        [SerializeField] private float fadeTime = 0.4f;

        private readonly Dictionary<string, Button> languageButtons = new();
        private InputActionMap playerActions;
        private bool waitingForKey;
        private float acceptKeysTime;

        private void Start()
        {
            // Nothing moves the parasite or turns the camera until the flight is over
            playerBrain.enabled = false;
            playerActions = InputSystem.actions.FindActionMap("Player", true);
            playerActions.Disable();
            hud.alpha = 0f;

            foreach (string code in Loc.Languages)
            {
                Button button = Instantiate(languageButtonTemplate, languageButtonTemplate.transform.parent);
                button.GetComponentInChildren<TMP_Text>().text = Loc.DisplayName(code); // noloc
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => ChooseLanguage(code));
                languageButtons[code] = button;
            }
            languageButtonTemplate.gameObject.SetActive(false);
            changeLanguageButton.onClick.AddListener(ShowLanguages);
            Tween.Alpha(pressAnyKey, 0.25f, 0.9f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);

            languagePanel.alpha = startPanel.alpha = 0f;
            if (PlayerPrefs.HasKey(LanguagePrefsKey)) ShowStart();
            else ShowLanguages();
        }

        private void Update()
        {
            if (waitingForKey && Time.unscaledTime >= acceptKeysTime && AnyKeyPressed())
                StartGame(destroyCancellationToken).Forget();
        }

        private void ShowLanguages()
        {
            waitingForKey = false;
            Fade(startPanel, false);
            Fade(languagePanel, true);
            // Keyboard and gamepad start on the current language
            EventSystem.current.SetSelectedGameObject(languageButtons.TryGetValue(Loc.Language, out Button button) ? button.gameObject : null);
        }

        private void ChooseLanguage(string code)
        {
            Loc.Language = code; // saved to PlayerPrefs
            ShowStart();
        }

        private void ShowStart()
        {
            Fade(languagePanel, false);
            Fade(startPanel, true);
            EventSystem.current.SetSelectedGameObject(null);
            // The key that picked the language mustn't start the game as well
            waitingForKey = true;
            acceptKeysTime = Time.unscaledTime + fadeTime;
        }

        private static bool AnyKeyPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame)) return true;
            // A click on the language button isn't "any key"
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !EventSystem.current.IsPointerOverGameObject();
        }

        private async UniTaskVoid StartGame(CancellationToken token)
        {
            waitingForKey = false;
            // Cursor lock only works from a user input event on WebGL, and this is one
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Fade(root, false);

            cameraBrain.DefaultBlend = flyBlend;
            menuCamera.Priority = -1; // below the player's camera: the brain flies over to it
            float half = flyBlend.Time * 0.5f;
            _ = Tween.Alpha(hud, 1f, half, startDelay: half);
            await UniTask.Delay(TimeSpan.FromSeconds(flyBlend.Time), cancellationToken: token);

            playerActions.Enable();
            playerBrain.enabled = true;
            Destroy(gameObject);
        }

        private void Fade(CanvasGroup group, bool show)
        {
            Tween.StopAll(group);
            group.interactable = group.blocksRaycasts = show;
            Tween.Alpha(group, show ? 1f : 0f, fadeTime);
        }
    }
}
