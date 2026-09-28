using DG.Tweening;
using MangraGames;
using Shared.GameFlow;
using Shared.PlayerData;
using PlayerDataModel = Shared.PlayerData.PlayerData;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using NUnit.Framework;
using System.Collections.Generic;
using Shared.Vibration;
using System.Linq;

namespace Shared.Settings
{
    public sealed class SettingsPopupController : MonoBehaviour
    {
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private PopupScaleAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button homeButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button feedbackButton;
        [SerializeField] private Button privacyPolicyButton;
        [SerializeField] private Button termsButton;
        // Optional - re-runs InAppManager.RestorePurchases on demand (same call BootLoader already
        // makes automatically at app startup) so a player whose purchase succeeded but was never
        // granted in-game (see InAppManager.UnattendedPurchaseGranted) can recover it without
        // waiting for their next app launch. Left unassigned on any prefab that doesn't have this
        // button yet - SettingsPopupController must stay usable without it.
        [SerializeField] private Button restorePurchasesButton;
        [SerializeField] private AnimatedToggleUI soundToggle;
        [SerializeField] private AnimatedToggleUI musicToggle;
        [SerializeField] private AnimatedToggleUI hapticToggle;
        [SerializeField] private TMP_Text buildVersionLabel;
        [SerializeField] private TMP_Text userIdLabel;
        [SerializeField] private string buildNumber = "1";
        //[SerializeField] private GameObject GameSceneButtons;
        [SerializeField] private List<GameObject> GameSceneButtons = new List<GameObject>();
        [SerializeField] private List<GameObject> MenuSceneButtons = new List<GameObject>();
        [SerializeField] private List<GameObject> AnyMenuPanel= new List<GameObject>();
        bool isMenuPanelOpen => AnyMenuPanel.Any(p => p.activeSelf);

        [Header("Background Image")]
        [SerializeField] private RectTransform BGImage;

        // Collapsed state (Screenshot 1): Pos Y = -0.6899, Height = 922.3313
        private const float BGImageCollapsedPosY = 0f;
        private const float BGImageCollapsedHeight = 922.3313f;

        // Expanded state (Screenshot 2): Pos Y = 80.18222, Height = 1084.075
        private const float BGImageExpandedPosY = 80.18222f;
        private const float BGImageExpandedHeight = 1084.075f;

        [Header("Button Click Animation")]
        [SerializeField] private float clickPunchScale = 0.12f;
        [SerializeField] private float clickAnimationDuration = 0.2f;
        [SerializeField] private int clickAnimationVibrato = 6;

        private readonly PlayerDataSaveSystem playerDataSaveSystem = new PlayerDataSaveSystem();
        private bool soundEnabled = true;
        private bool musicEnabled = true;
        private bool hapticEnabled = true;
        private bool gameplayInputWasEnabled;

        public UnityEvent PopupOpened = new UnityEvent();
        public UnityEvent PopupClosed = new UnityEvent();
        public UnityEvent HomeRequested = new UnityEvent();
        public UnityEvent QuitRequested = new UnityEvent();

        private void Awake()
        {
            ResolveAnimator();
            LoadSettings();
            RefreshLabels();
            SetVisible(false);
        }

        private void OnEnable()
        {
            closeButton?.onClick.AddListener(Close);
            homeButton?.onClick.AddListener(RequestHome);
            quitButton?.onClick.AddListener(RequestQuit);
            feedbackButton?.onClick.AddListener(OnFeedbackClicked);
            privacyPolicyButton?.onClick.AddListener(OnPrivacyPolicyClicked);
            termsButton?.onClick.AddListener(OnTermsClicked);
            restorePurchasesButton?.onClick.AddListener(OnRestorePurchasesClicked);
            soundToggle?.Toggle.onValueChanged.AddListener(OnSoundToggled);
            musicToggle?.Toggle.onValueChanged.AddListener(OnMusicToggled);
            hapticToggle?.Toggle.onValueChanged.AddListener(OnHapticToggled);
        }

        private void OnDisable()
        {
            closeButton?.onClick.RemoveListener(Close);
            homeButton?.onClick.RemoveListener(RequestHome);
            quitButton?.onClick.RemoveListener(RequestQuit);
            feedbackButton?.onClick.RemoveListener(OnFeedbackClicked);
            privacyPolicyButton?.onClick.RemoveListener(OnPrivacyPolicyClicked);
            termsButton?.onClick.RemoveListener(OnTermsClicked);
            restorePurchasesButton?.onClick.RemoveListener(OnRestorePurchasesClicked);
            soundToggle?.Toggle.onValueChanged.RemoveListener(OnSoundToggled);
            musicToggle?.Toggle.onValueChanged.RemoveListener(OnMusicToggled);
            hapticToggle?.Toggle.onValueChanged.RemoveListener(OnHapticToggled);
        }

        /// <summary>
        /// Hides/shows the Quit ("Restart") button - Cat Flap hides it while a Daily Challenge
        /// level is active, since restarting from Settings there discards the attempt (see
        /// CatFlapGameplayManager.SettingsQuit/PauseForSettingsPopup).
        /// </summary>
        public void SetQuitButtonVisible(bool visible)
        {
            quitButton?.gameObject.SetActive(visible);
        }

        public void Open()
        {
            SetBGImageState(isMenuPanelOpen);
            gameObject.SetActive(true);
            ResolveAnimator();
            gameplayInputWasEnabled = Shared.GameFlow.InputManager.GamePlayInputEnabled;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            Shared.GameFlow.InputManager.BlockAllButtonsExcept(popupPanel);
            RefreshLabels();
            SetVisible(true);
            if (popupPanel != null)
            {
                popupPanel.localScale = Vector3.zero;
            }

            PopupOpened.Invoke();
            PlayPopupOpenSound();
            PlayOpen(null);
        }

        public void Close()
        {
            PlayButtonClickAnimation(closeButton, () =>
            {
                PlayClose(() =>
                {
                    SetVisible(false);
                    PopupClosed.Invoke();
                    Shared.GameFlow.InputManager.GamePlayInputEnabled = gameplayInputWasEnabled;
                    Shared.GameFlow.InputManager.UnblockAllButtons();
                });
            },ImpactFeedbackStyle.Soft, "ButtonBack");
        }

        private void RequestHome()
        {
            PlayButtonClickAnimation(homeButton, () =>
            {
                PlayClose(() =>
                {
                    SetVisible(false);
                    Shared.GameFlow.InputManager.UnblockAllButtons();
                    HomeRequested.Invoke();
                });
            }, ImpactFeedbackStyle.Soft);
        }

        private void RequestQuit()
        {
            PlayButtonClickAnimation(quitButton, () =>
            {
                PlayClose(() =>
                {
                    SetVisible(false);
                    Shared.GameFlow.InputManager.UnblockAllButtons();
                    QuitRequested.Invoke();
                });
            }, ImpactFeedbackStyle.Soft);
        }

        private const string FeedbackRecipient = "cs@mangragames.com";

        private void OnFeedbackClicked()
        {
            PlayButtonClickAnimation(feedbackButton, () =>
            {
                string subject = $"Cat Flap ({Application.version}, {buildNumber}) feedback";
                string body = "version: " + Application.version + "\n" +
                    "version code: " + buildNumber + "\n" +
                    "OS: " + SystemInfo.operatingSystem + "\n" +
                    "device: " + SystemInfo.deviceModel + "\n" +
                    "firebaseID: " + PlayerDataModel.Instance.PlayerId;

                // mailto's subject/body must be percent-encoded (spaces, newlines, punctuation) -
                // Uri.EscapeDataString handles that; a plain concatenated mailto: URL breaks on
                // the newlines/spaces here.
                string mailtoUrl = $"mailto:{FeedbackRecipient}?subject={System.Uri.EscapeDataString(subject)}&body={System.Uri.EscapeDataString(body)}";
                Application.OpenURL(mailtoUrl);
            }, ImpactFeedbackStyle.Soft);
        }

        private const string PrivacyPolicyUrl = "https://boombit.com/privacy-policy/";
        private const string TermsOfServiceUrl = "https://boombit.com/terms-of-service/";

        private void OnPrivacyPolicyClicked()
        {
            PlayButtonClickAnimation(privacyPolicyButton, () =>
            {
                Application.OpenURL(PrivacyPolicyUrl);
            }, ImpactFeedbackStyle.Soft);
        }

        private void OnTermsClicked()
        {
            PlayButtonClickAnimation(termsButton, () =>
            {
                Application.OpenURL(TermsOfServiceUrl);
            }, ImpactFeedbackStyle.Soft);
        }

        private bool restoreInProgress;

        // Re-runs the same recovery InAppManager.RestorePurchases already performs automatically
        // at app startup (see BootLoader) - lets a player trigger it on demand right after a
        // purchase that succeeded but was never granted in-game, instead of having to relaunch the
        // app and wait for the automatic startup call. Any reward found is granted the same way
        // the startup call grants one - see InAppManager.UnattendedPurchaseGranted/
        // CatFlapGameplayManager.HandleUnattendedPurchaseGranted.
        private void OnRestorePurchasesClicked()
        {
            if (restoreInProgress)
            {
                return;
            }

            PlayButtonClickAnimation(restorePurchasesButton, () =>
            {
                restoreInProgress = true;
                InAppManager.Instance.RestorePurchases(_ => restoreInProgress = false);
            }, ImpactFeedbackStyle.Soft);
        }

        private void OnSoundToggled(bool isOn)
        {
            soundEnabled = isOn;
            SaveSettings();
            RefreshAudioSettings();
        }

        private void OnMusicToggled(bool isOn)
        {
            musicEnabled = isOn;
            SaveSettings();
            RefreshAudioSettings();

            // RefreshAudioSettings only unmutes the AudioMixer's MusicVolume - if musicSource was
            // left stopped (AudioManager.PlayAudio skips PlayBgMusic while music is off), turning
            // the mixer volume back up alone has nothing audible to play until playback actually
            // resumes here. Without this, music only came back after a full app relaunch (the only
            // other place PlayBgMusic gets called).
            if (isOn && global::AudioManager.IsInstanceExist())
            {
                global::AudioManager.Instance.PauseMusicAction?.Invoke(false);
            }
        }

        private void OnHapticToggled(bool isOn)
        {
            hapticEnabled = isOn;
            SaveSettings();
        }

        private void LoadSettings()
        {
            PlayerDataModel data = PlayerDataModel.Instance;
            soundEnabled = data.IsSoundOn;
            musicEnabled = data.IsMusicOn;
            hapticEnabled = data.IsHapticOn;
        }

        private void SaveSettings()
        {
            PlayerDataModel data = PlayerDataModel.Instance;
            data.IsSoundOn = soundEnabled;
            data.IsMusicOn = musicEnabled;
            data.IsHapticOn = hapticEnabled;
            data.Touch();

            string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
                ? PlayerDataDefaults.FileName
                : PlayerDataRuntimeContext.FileName;
            playerDataSaveSystem.Save(fileName, data);
        }

        private static void RefreshAudioSettings()
        {
            if (global::AudioManager.IsInstanceExist())
            {
                global::AudioManager.Instance.AudionSettingAction?.Invoke();
            }
        }

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.PopupOpen);
            // DOVirtual.DelayedCall(0.3f, () =>
            // { 
            // });
        }

        private void RefreshLabels()
        {
            // Sync toggle visuals to current state without re-triggering listeners or animating.
            soundToggle?.SetState(soundEnabled, false);
            musicToggle?.SetState(musicEnabled, false);
            hapticToggle?.SetState(hapticEnabled, false);

            SetText(buildVersionLabel, $"{Application.version}_vc{buildNumber}");
            string playerId = PlayerDataModel.Instance.PlayerId;
            SetText(userIdLabel, playerId);
        }

        private void SetVisible(bool visible)
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(visible);
            }

            if (popupPanel != null)
            {
                popupPanel.localScale = visible ? popupPanel.localScale : Vector3.zero;
            }
        }

        private void PlayOpen(UnityAction onComplete)
        {
            ResolveAnimator();
            if (popupAnimator != null)
            {
                popupAnimator.PlayOpen(onComplete);
                return;
            }

            if (popupPanel != null)
            {
                popupPanel.localScale = Vector3.one;
            }

            onComplete?.Invoke();
        }

        private void PlayClose(UnityAction onComplete)
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            ResolveAnimator();
            if (popupAnimator != null)
            {
                popupAnimator.PlayClose(onComplete);
                return;
            }

            if (popupPanel != null)
            {
                popupPanel.localScale = Vector3.zero;
            }

            onComplete?.Invoke();
        }

        private void ResolveAnimator()
        {
            if (popupPanel == null && popupRoot != null)
            {
                Transform panel = popupRoot.transform.Find("PopupPanel");
                popupPanel = panel as RectTransform;
            }

            if (popupAnimator == null && popupPanel != null)
            {
                popupAnimator = popupPanel.GetComponent<PopupScaleAnimator>();
                if (popupAnimator == null)
                {
                    popupAnimator = popupPanel.gameObject.AddComponent<PopupScaleAnimator>();
                }
            }

            if (popupAnimator != null && popupPanel != null)
            {
                popupAnimator.SetTarget(popupPanel);
            }
        }

        /// <summary>
        /// Switches BGImage's RectTransform between the collapsed and expanded presets.
        /// Only Pos Y and Height are changed since the RectTransform stretches horizontally.
        /// </summary>
        /// <param name="isExpanded">True = expanded state, False = collapsed state.</param>
        public void SetBGImageState(bool isExpanded)
        {
            //return;
            if (BGImage == null)
            {
                return;
            }
            GameSceneButtons.ForEach(x => x.SetActive(!isExpanded));
            MenuSceneButtons.ForEach(x => x.SetActive(isExpanded));
            return;
            float posY = isExpanded ? BGImageExpandedPosY : BGImageCollapsedPosY;
            float height = isExpanded ? BGImageExpandedHeight : BGImageCollapsedHeight;

            BGImage.anchoredPosition = new Vector2(BGImage.anchoredPosition.x, posY);
            BGImage.sizeDelta = new Vector2(BGImage.sizeDelta.x, height);
        }

        /// <summary>
        /// Plays a quick punch-scale click feedback animation on a button's RectTransform,
        /// then invokes onComplete once the animation finishes.
        /// </summary>
        // AudioName is only passed for buttons whose click should sound different from the
        // generic click (e.g. Close's "ButtonBack") - every button here also has a
        // ButtonPressScaleFeedback component, which already plays SoundName.ClickSound on every
        // click ("the single place the click sound plays", per its own doc comment). Passing
        // "ClickSound" again here would just play that same clip a second time - so null/omit
        // AudioName for any button that should just get the generic click sound.
        private void PlayButtonClickAnimation(Button button, UnityAction onComplete, ImpactFeedbackStyle impactFeedbackStyle, string AudioName = null)
        {
            if (button == null)
            {
                onComplete?.Invoke();
                return;
            }

            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform == null)
            {
                onComplete?.Invoke();
                return;
            }

            rectTransform.DOKill();
            rectTransform.localScale = Vector3.one;
            rectTransform
                .DOPunchScale(Vector3.one * clickPunchScale, clickAnimationDuration, clickAnimationVibrato)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    VibrationManager.Instance.PlayVibration(impactFeedbackStyle);
                    if (!string.IsNullOrEmpty(AudioName))
                    {
                        global::AudioManager.Instance.PlaySoundAction?.Invoke(AudioName);
                    }
                    onComplete?.Invoke();
                });
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }
    }
}