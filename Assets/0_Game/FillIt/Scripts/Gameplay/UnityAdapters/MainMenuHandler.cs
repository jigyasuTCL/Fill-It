using UnityEngine;
using UnityEngine.UI;
using Shared.Store.UnityAdapters;
using Shared.GameFlow;

namespace FillIt.UnityAdapters
{
    public sealed class MainMenuHandler : MonoBehaviour
    {
        [SerializeField] private FillItGameplayManager gameplayManager;

        [Header("Bottom Buttons")]
        [SerializeField] private HSScreenButtonView homeScreenButton;
        [SerializeField] private HSScreenButtonView shopScreenButton;
        [SerializeField] private HSScreenButtonView DailyChallengesScreenButton;

        [Header("Home")]
        [SerializeField] private GameObject mainMenuUIRoot;
        [Tooltip("Sits on normalLevelButtonRoot (MainMenuHomeAnimator) - only one of normalPlayButton/hardPlayButton is ever visible at a time, but both trigger the same RequestPlay flow.")]
        [SerializeField] private Button normalPlayButton;
        [Tooltip("Sits on hardLevelButtonRoot (MainMenuHomeAnimator) - only one of normalPlayButton/hardPlayButton is ever visible at a time, but both trigger the same RequestPlay flow.")]
        [SerializeField] private Button hardPlayButton;
        [SerializeField] private MainMenuHomeAnimator homeAnimator;

        [Header("Home Extra Popups")]
        [SerializeField] private Button dailyChallengeButton;
        [SerializeField] private DailyChallenges.DailyChallengeButtonUI dailyChallengeButtonUI;
        [SerializeField] private DailyChallenges.DailyChallengeManager dailyChallengeManager;
        [SerializeField] private Game.SpinWheel.SpinWheelMenuButtonUIController spinWheelMenuButtonUIController;
        [SerializeField] private DailyRewardMainMenuButton dailyRewardMainMenuButton;
        //[SerializeField] private GameObject spinWheelPopupRoot;
        [SerializeField] private Button dailyRewardButton;
        [SerializeField] private GameObject dailyRewardPopupRoot;

        [Header("Shop")]
        [SerializeField] private ShopPageController shopPage;

        [Header("Coming Soon")]
        [SerializeField] private GameObject DailyChallengePopUpRoot;

        [Header("Bottom Buttons Bar")]
        [SerializeField] private MainMenuBottomButtonsAnimator bottomButtonsAnimator;

        [Header("Feature Unlock")]
        [SerializeField] UnlockManager UnlockManager;

        // Which real tab (Home/Shop) was last actually shown - used only to restore the bottom
        // nav's ToggleGroup selection when a tap on the Daily Challenges icon turns out to be
        // locked (see HandleDailyChallengeToggle), never to gate whether Show* runs.
        private bool lastRealTabWasShop;

        // Guards RequestPlay against a double-tap: the Home tab/bottom bar animate out and the
        // transition overlay covers the screen for up to ~1s before Gameplay actually swaps in,
        // during which playButton would otherwise still be clickable and re-enter StartGame().
        private bool playRequested;

        private void OnEnable()
        {
            playRequested = false;

            // Daily Reward / Spin Wheel popups grant coins without this handler ever being
            // re-enabled (they sit on top of the still-active Home tab), so RefreshCoinDisplay()
            // needs its own push from that flow instead of only running on ShowHome/SaveProgress.
            CollectScreenController.OnCoinRewardGranted += RefreshCoinDisplay;

            BindButtons();
            ActivateHomeTab(false);

            // FillItGameplayManager.Start() (which runs after every OnEnable, including this
            // one) skips Home entirely and calls StartGame() directly for a first-time player -
            // normally only RequestPlay()'s click handler hides mainMenuUIRoot, so without this
            // ActivateHomeTab's own SetActive(mainMenuUIRoot, true) above would be left showing
            // underneath/alongside the Gameplay UI once StartGame() loads the level.
            if (gameplayManager != null && gameplayManager.SkipMainMenuOnLaunch)
            {
                SetActive(mainMenuUIRoot, false);
            }

            bottomButtonsAnimator?.PlaySlideIn(false);
            CheckForFeatureUnlock();
            RefreshDailyChallengeLockVisual();
        }
        void CheckForFeatureUnlock() {
            // CurrentLevelNumber (1-based) - matches the level numbering UnlockLevel is
            // authored in, and what SpinWheel/DailyChallenge/DailyReward already check
            // against. CurrentLevelIndex (0-based) made every unlock fire one level late.
            UnlockManager.CheckUnlocks(gameplayManager.CurrentLevelNumber);
        }

        /// <summary>
        /// The single unlock check for Daily Challenge - every entry point (bottom-nav icon,
        /// Home tab's widget/button, the popup-reopen path) reads this instead of each
        /// re-deriving it, so "is it unlocked" can never disagree between them.
        /// </summary>
        private bool IsDailyChallengeUnlocked(out int unlockLevel)
        {
            Shared.GameConfig.GlobalConfig config = Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config
                : null;

            unlockLevel = config != null ? config.DailyChallenge.UnlockLevel : 1;
            return config == null || (config.DailyChallenge.Enabled && GetCurrentLevelNumber() >= config.DailyChallenge.UnlockLevel);
        }

        /// <summary>Pushes current unlock state onto the bottom-nav icon's own locked visual -
        /// called on enable and every time OpenDailyChallenge runs, so the icon reflects reality
        /// even before it's ever tapped.</summary>
        private void RefreshDailyChallengeLockVisual()
        {
            DailyChallengesScreenButton?.SetLocked(!IsDailyChallengeUnlocked(out _));
        }

        public void ShowBottomButtons(bool immediate = false)
        {
            bottomButtonsAnimator?.PlaySlideIn(immediate);
        }

        private void OnDisable()
        {
            CollectScreenController.OnCoinRewardGranted -= RefreshCoinDisplay;

            UnbindButtons();
        }

        public void OnLevelWinSpinTheWheel() {

            spinWheelMenuButtonUIController.LevelWin();
        }
        private void BindButtons()
        {
            UnbindButtons();
            BindToggle(homeScreenButton, HandleHomeToggle);
            BindToggle(shopScreenButton, HandleShopToggle);
            BindToggle(DailyChallengesScreenButton, HandleDailyChallengeToggle);
            normalPlayButton?.onClick.AddListener(RequestPlay);
            hardPlayButton?.onClick.AddListener(RequestPlay);
            dailyChallengeButton?.onClick.AddListener(OpenDailyChallenge);
            //spinWheelButton?.onClick.AddListener(OpenSpinWheel);
            dailyRewardButton?.onClick.AddListener(OpenDailyReward);
            gameplayManager?.ReturnedToMainMenu.AddListener(HandleReturnedToMainMenu);
            if (dailyChallengeButtonUI != null)
            {
                dailyChallengeButtonUI.PlayRequested -= PlayDailyChallengeDirectly;
                dailyChallengeButtonUI.PlayRequested += PlayDailyChallengeDirectly;
                dailyChallengeButtonUI.Configure(GetCurrentLevelNumber, dailyChallengeManager != null ? dailyChallengeManager.Controller : null);
            }
            if (dailyChallengeManager != null)
            {
                dailyChallengeManager.PopupShown -= HandleDailyChallengePopupShown;
                dailyChallengeManager.PopupShown += HandleDailyChallengePopupShown;
            }
            if (spinWheelMenuButtonUIController != null) {
                spinWheelMenuButtonUIController.SetLevel(GetCurrentLevelNumber);
            }
            if (dailyRewardMainMenuButton != null)
            {
                dailyRewardMainMenuButton.LoadData(GetCurrentLevelNumber);
            }
        }

        private int GetCurrentLevelNumber()
        {
            return gameplayManager != null ? gameplayManager.CurrentLevelNumber : 1;
        }

        private void UnbindButtons()
        {
            UnbindToggle(homeScreenButton, HandleHomeToggle);
            UnbindToggle(shopScreenButton, HandleShopToggle);
            UnbindToggle(DailyChallengesScreenButton, HandleDailyChallengeToggle);
            normalPlayButton?.onClick.RemoveListener(RequestPlay);
            hardPlayButton?.onClick.RemoveListener(RequestPlay);
            dailyChallengeButton?.onClick.RemoveListener(OpenDailyChallenge);
            //spinWheelButton?.onClick.RemoveListener(OpenSpinWheel);
            dailyRewardButton?.onClick.RemoveListener(OpenDailyReward);
            gameplayManager?.ReturnedToMainMenu.RemoveListener(HandleReturnedToMainMenu);
            if (dailyChallengeButtonUI != null)
            {
                dailyChallengeButtonUI.PlayRequested -= PlayDailyChallengeDirectly;
            }
            if (dailyChallengeManager != null)
            {
                dailyChallengeManager.PopupShown -= HandleDailyChallengePopupShown;
            }
        }

        /// <summary>
        /// DailyChallengeManager.PopupShown fires whenever its popup actually opens, no matter
        /// who triggered it - covers callers that don't (and, being Shared code, can't) know
        /// about this Fill It-specific bottom-nav toggle, e.g. UnlockManager auto-opening the
        /// popup right after the player crosses the Daily Challenge unlock level. Skips the
        /// toggle re-select when it's already correctly selected (the direct-tap path already
        /// selected it itself via HandleDailyChallengeToggle's own OnToggle call, with its click
        /// sound/slide-in animation) so this doesn't restart that animation a second time.
        /// </summary>
        private void HandleDailyChallengePopupShown()
        {
            DailyChallengesScreenButton?.SetLocked(false);

            bool alreadySelected = DailyChallengesScreenButton != null
                && DailyChallengesScreenButton.baseToggle != null
                && DailyChallengesScreenButton.baseToggle.isOn;
            if (alreadySelected)
            {
                return;
            }

            SetToggleOnSilently(homeScreenButton, false);
            SetToggleOnSilently(shopScreenButton, false);
            SetToggleOnSilently(DailyChallengesScreenButton, true);
        }

        /// <summary>
        /// Tapping the Daily Challenge area (not the Play CTA inside it) always opens the popup -
        /// showing its locked state if the feature itself isn't unlocked yet, never loading
        /// gameplay directly (see GDD section 1/3).
        /// </summary>
        private void OpenDailyChallenge()
        {
            bool unlocked = IsDailyChallengeUnlocked(out int unlockLevel);
            DailyChallengesScreenButton?.SetLocked(!unlocked);

            if (!unlocked)
            {
                dailyChallengeManager?.ShowLockedPopup($"Unlocks at level {unlockLevel}");
                return;
            }

            dailyChallengeManager?.ShowPopup();

            Shared.PlayerData.PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
            analytics.DailyChallengeOpenCount++;
            Shared.Analytics.AnalyticsManager.Instance.LogDailyChallenge(
                GetCurrentLevelNumber(),
                analytics.DailyChallengeOpenCount,
                currentLevelPlay: false,
                analytics.DailyChallengeTotalLevelsPlayed,
                analytics.DailyChallengeTotalLevelsPlayed);
        }

        /// <summary>
        /// Same as tapping the bottom-nav Daily Challenges icon (OpenDailyChallenge's unlock
        /// check + modal popup), but also snaps that icon's own toggle to "selected" first -
        /// only when actually unlocked, so a caller can never mark a locked icon "selected".
        /// Needed when something else already forced Home/Shop to look selected right before
        /// this runs - e.g. FillItDailyChallengeBridge's BackToDailyChallengePopup calls
        /// gameplayManager.SaveAndShowMainMenu() (which fires ReturnedToMainMenu -&gt;
        /// ActivateHomeTab, forcing Home's toggle on) immediately before reopening this popup, so
        /// without this the bottom nav was showing Home as active while the Daily Challenge popup
        /// was the thing actually on screen.
        /// </summary>
        public void OpenDailyChallengePopup()
        {
            if (IsDailyChallengeUnlocked(out _))
            {
                SetToggleOnSilently(homeScreenButton, false);
                SetToggleOnSilently(shopScreenButton, false);
                SetToggleOnSilently(DailyChallengesScreenButton, true);
            }

            OpenDailyChallenge();
        }

        /// <summary>
        /// The Daily Challenge widget's own Play CTA - loads today's still-fresh challenge
        /// directly, bypassing the popup entirely (GDD section 3/8). The widget only shows this
        /// button when today is actually directly-playable, but if that assumption is somehow
        /// stale by the time it's tapped, fall back to opening the popup rather than doing
        /// nothing.
        /// </summary>
        private void PlayDailyChallengeDirectly()
        {
            DailyChallenges.DailyChallengeController controller = dailyChallengeManager != null ? dailyChallengeManager.Controller : null;
            if (controller == null)
            {
                return;
            }

            if (controller.GetTodaysDirectPlayChallenge() == null)
            {
                OpenDailyChallenge();
                return;
            }

            SetActive(mainMenuUIRoot, false);

            if (bottomButtonsAnimator == null)
            {
                controller.PlayCurrentChallengeDirectly();
                return;
            }

            bottomButtonsAnimator.PlaySlideOut(false, () => controller.PlayCurrentChallengeDirectly());
        }

        /// <summary>
        /// Called by FillItDailyChallengeBridge right after a Daily Challenge level has actually
        /// loaded, whichever CTA started the flow (this widget's own PLAY, or the popup's
        /// Play/Watch Ad/Continue button) - the popup's path never went through
        /// PlayDailyChallengeDirectly above, so nothing else hides the Main Menu/closes the popup
        /// for it.
        /// </summary>
        public void HideForDailyChallengeGameplay()
        {
            SetActive(mainMenuUIRoot, false);
            dailyChallengeManager?.ClosePopup();
        }

        private void OpenSpinWheel()
        {
            //SetActive(spinWheelPopupRoot, true);
        }

        private void OpenDailyReward()
        {
            SetActive(dailyRewardPopupRoot, true);
        }

        private void HandleReturnedToMainMenu()
        {
            ActivateHomeTab(false);
            ShowBottomButtons();
        }

        public void ShowHomeTab()
        {
            ActivateHomeTab(false);
        }

        private void ActivateHomeTab(bool immediate)
        {
            SetToggleOnSilently(homeScreenButton, true);
            SetToggleOnSilently(shopScreenButton, false);
            SetToggleOnSilently(DailyChallengesScreenButton, false);
            ShowHome(immediate);
        }

        private static void SetToggleOnSilently(HSScreenButtonView view, bool isOn)
        {
            if (view == null)
            {
                return;
            }

            view.baseToggle?.SetIsOnWithoutNotify(isOn);
            view.OnBaseToggle(isOn);
        }

        private static void BindToggle(HSScreenButtonView view, UnityEngine.Events.UnityAction<bool> handler)
        {
            if (view?.baseToggle == null)
            {
                return;
            }

            view.baseToggle.onValueChanged.RemoveListener(handler);
            view.baseToggle.onValueChanged.AddListener(handler);
        }

        private static void UnbindToggle(HSScreenButtonView view, UnityEngine.Events.UnityAction<bool> handler)
        {
            if (view?.baseToggle != null)
            {
                view.baseToggle.onValueChanged.RemoveListener(handler);
            }
        }

        private void RequestPlay()
        {
            if (playRequested)
            {
                return;
            }

            playRequested = true;
            // AudioManager.Instance
                // .PlaySoundAction?
                // .Invoke(global::SoundName.ClickSound);
            // Soft is the lowest-intensity ImpactFeedbackStyle - the generic button-click tap.
            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
            // Fade in first, THEN hide the Home page (once fully covered), THEN start the game -
            // whose own board-enter animation plays underneath while the overlay fades back out.
            GameTransitionOverlay.Instance.Play(
                hideOldPage: () =>
                {
                    SetActive(mainMenuUIRoot, false);
                    bottomButtonsAnimator?.PlaySlideOut(true);
                },
                revealNewPage: () => gameplayManager?.StartGame());
        }

        private void HandleHomeToggle(bool isOn)
        {
            homeScreenButton?.OnToggle(isOn);
            // Acts on isOn directly rather than tracking/checking "is this already the active
            // tab" - Daily Challenges shares this same ToggleGroup but only opens a popup on top
            // of whatever's already showing, so it silently flips this toggle off without this
            // handler ever finding out. A stale "already active" guard here previously swallowed
            // the next tap. Unity's Toggle won't refire onValueChanged(true) for a toggle that's
            // already on, so acting unconditionally here can't loop.
            if (isOn)
            {
                ShowHome(false);
            }
        }

        private void HandleShopToggle(bool isOn)
        {
            shopScreenButton?.OnToggle(isOn);
            if (isOn)
            {
                ShowShop();
            }
        }

        private void HandleDailyChallengeToggle(bool isOn)
        {
            if (!isOn)
            {
                DailyChallengesScreenButton?.OnToggle(false);
                return;
            }

            if (!IsDailyChallengeUnlocked(out int unlockLevel))
            {
                // Unity's ToggleGroup already flipped this on (and Home/Shop off) before this
                // handler ran - undo that silently rather than letting a locked tap register as
                // "selected", or a second tap later would do nothing (Toggle only refires
                // onValueChanged on an actual isOn change).
                SetToggleOnSilently(DailyChallengesScreenButton, false);
                SetToggleOnSilently(homeScreenButton, !lastRealTabWasShop);
                SetToggleOnSilently(shopScreenButton, lastRealTabWasShop);
                DailyChallengesScreenButton?.ShowLockedToast($"Unlocks at level {unlockLevel}");
                return;
            }

            DailyChallengesScreenButton?.OnToggle(true);
            DailyChallengesOpen();
        }

        private void ShowShop()
        {
            lastRealTabWasShop = true;
            SetActive(mainMenuUIRoot, false);
            SetActive(DailyChallengePopUpRoot, false);
            // The Daily Challenge tab opens dailyChallengeManager's OWN popup (see
            // OpenDailyChallenge), not DailyChallengePopUpRoot above - without this it stays on
            // screen on top of the Shop page when the player switches tabs.
            dailyChallengeManager?.ClosePopup();
            SetActive(shopPage != null ? shopPage.gameObject : null, true);
        }

        private void DailyChallengesOpen()
        {
            // The Daily Challenge popup opens on top of whatever page is already showing, so
            // arriving from the Shop tab left the Shop page visible behind it. Swap the backdrop
            // to Home first, so the popup always opens over the same thing no matter which tab
            // it came from.
            //
            // Home specifically, not just "hide Shop": the popup's close button goes straight to
            // DailyChallengeManager.ClosePopup(), which only hides the popup and tells this
            // handler nothing - so whatever is left behind IS where the player lands. With both
            // pages hidden that would be an empty screen.
            if (lastRealTabWasShop)
            {
                lastRealTabWasShop = false;
                SetActive(shopPage != null ? shopPage.gameObject : null, false);
                SetActive(mainMenuUIRoot, true);
            }

            OpenDailyChallenge();
        }

        private void ShowHome(bool immediate)
        {
            lastRealTabWasShop = false;
            SetActive(shopPage != null ? shopPage.gameObject : null, false);
            SetActive(DailyChallengePopUpRoot, false);
            // Same reason as in ShowShop - closes the real Daily Challenge popup, which
            // DailyChallengePopUpRoot above is not.
            dailyChallengeManager?.ClosePopup();
            SetActive(mainMenuUIRoot, true);

            homeAnimator?.PlayEntry(immediate);
            homeAnimator?.RefreshLevelTypeButtons(GetCurrentLevelNumber());
            RefreshCoinDisplay();
        }

        /// <summary>
        /// Pushes the player's current coin total to the home tab's coin text. Called whenever the
        /// coin total actually changes (level win/spend saved via FillItGameplayManager.SaveProgress,
        /// cheat "Add Coins") as well as whenever the home tab is shown, so the display never goes
        /// stale while the main menu is up.
        /// </summary>
        public void RefreshCoinDisplay()
        {
            homeAnimator?.CallCoinUpdate?.Invoke(Shared.PlayerData.PlayerData.Instance.TotalCoins);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }
    }
}
