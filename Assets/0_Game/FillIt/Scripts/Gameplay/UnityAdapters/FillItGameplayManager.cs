using System.Collections;
using System.Collections.Generic;
using FillIt.Levels;
using FillIt.Models;
using FillIt.Runtime;
using MangraGames;
using Shared.Analytics;
using Shared.Boot;
using Shared.Boosters;
using Shared.CheatUI;
using Shared.Ftue;
using Shared.GameConfig;
using Shared.GameFlow;
using Shared.Notes;
using Shared.PlayerData;
using Shared.RemoteConfig;
using Shared.Vibration;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Scene-level orchestrator. Owns level loading (campaign, loop levels and Daily Challenge),
    /// wires every shared feature (lives/revive, boosters, store, ads, analytics, leaderboard,
    /// FTUE, hard levels, win/fail popups, autosave) around the Fillomino core
    /// (<see cref="FillItRuntimeController"/>), and turns board gestures into moves.
    /// Split across partials: .Input (gestures, number pad, boosters), .Validation (life charges),
    /// .Results (win/fail/out-of-lives/store flows), .Cheat.
    /// </summary>
    public partial class FillItGameplayManager : MonoBehaviour
    {
        [SerializeField] private TextAsset levelJson;
        [SerializeField] private TextAsset[] levelSequence = new TextAsset[0];
        [FormerlySerializedAs("gameConfig")]
        [SerializeField] private GlobalConfigAsset globalConfig;
        [SerializeField] private GameRulesConfigAsset gameRulesConfig;
        [SerializeField] private StoreConfigAsset storeConfigAsset;
        [SerializeField] private Shared.Store.StoreManager storeManager;
        [SerializeField] private Shared.Store.UnityAdapters.ShopPageController shopPage;
        [SerializeField] private FillItBoardView boardView;
        [SerializeField] private FillItStatusViewAdapter statusView;
        [SerializeField] private FillItNumberPadView numberPad;
        [SerializeField] private FillItHintBannerController hintBannerController;

        // Shown instead of uiFlowManager.OutOfLivesPopup while isDailyChallengeSession is true.
        // Leave unassigned to keep using the shared popup for Daily Challenge too.
        [SerializeField] private FillItDailyChallengeFailPopupController dailyChallengeOutOfLivesPopup;
        [SerializeField] private FillItHintSpotlightController hintSpotlightController;
        [SerializeField] private Button hintApplyButton;
        [SerializeField] private CanvasGroup boosterPanelCanvasGroup;
        [SerializeField] private FillItUIFlowManager uiFlowManager;
        // Credited on level win: one point per heart the player finished with, 1:1. Left
        // unassigned it is looked up in the scene once - a silently unscored win is hard to notice.
        [SerializeField] private Shared.Leaderboard.LeaderboardManager leaderboardManager;
        // Shown between the win celebration and the win popup (old standing, then the climb).
        // Leave unassigned and the points are credited silently.
        [SerializeField] private Shared.Leaderboard.LeaderboardPanelController leaderboardPanel;
        // Shown every time a normal-progression level listed in GlobalConfig.HardLevels finishes
        // its entrance animation. Left unassigned disables the popup entirely.
        [SerializeField] private Shared.GameFlow.HardLevelPopupController hardLevelPopupController;

        // Set in LoadAssignedLevel, consumed by FinishLevelStartEntrance.
        private bool pendingHardLevelIntroThisLoad;

        // What HandleLevelWon actually credited - the win popup reads it after currentLevelIndex
        // has already advanced to the next level.
        private int lastLevelWinCoinReward;

        // Leaderboard points for the level just won (hearts remaining), cached for the same reason.
        private int lastLevelEarnedPoints;
        // Same Settings popup the Settings button opens - its Quit ("Restart") button is hidden
        // while a Daily Challenge level is active.
        [SerializeField] private Shared.Settings.SettingsPopupController settingsPopupController;
        [SerializeField] private FillItFtueDriver ftueDriver;
        // Only shown while FTUE is active - wire its Button.onClick to SkipFtueTutorial.
        [SerializeField] private GameObject skipTutorialButtonRoot;
        // FTUE text lives in FtueStepsAsset ScriptableObjects; each entry maps a level number to
        // its asset plus the guided drag paths.
        [SerializeField] private FillItFtueLevelStepsEntry[] ftueLevelSteps = new FillItFtueLevelStepsEntry[0];

        [Header("Level Attempt one-time tip - shown once ever, first level Level Attempt turns on")]
        [SerializeField] private string levelAttemptTipDescription = "Lives are lost on wrong moves";

        public UnityEngine.Events.UnityEvent ReturnedToMainMenu = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// Fired only for a deliberate "abandon this attempt" action (Settings Quit) - NOT for Home
        /// (a resumable pause) and NOT for Give Up (already a real Lost result). The Daily
        /// Challenge bridge listens for this instead of ReturnedToMainMenu.
        /// </summary>
        public UnityEngine.Events.UnityEvent DailyChallengeAttemptAbandoned = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// Fired right after RestartLevel() reloads the same Daily Challenge sub-level in place, so
        /// the bridge re-binds its LevelWon/LevelFailed listeners for the new attempt.
        /// </summary>
        public UnityEngine.Events.UnityEvent DailyChallengeSessionRestarted = new UnityEngine.Events.UnityEvent();
        [SerializeField] private Shared.Boosters.BoosterManager sharedBoosterManager;
        [SerializeField] private FillItHintHighlightAnimator hintHighlightAnimator;
        [SerializeField] private FillItAnimationViewAdapter animationView;
        [SerializeField] private FillItGameProgressSaveManager progressSaveManager;
        [SerializeField] private FillItCurrentLevelBoardSaveManager boardSaveManager;

        // Same boardSaveManager instance/file as regular levels - just a separate progressionId
        // "row", so a Daily Challenge board save never collides with the campaign's.
        private const string DailyChallengeProgressionId = "FillItDailyChallenge";
        // Free Revive on the Out of Lives popup is a lifetime one-shot.
        private const string FreeReviveUsedKey = "OutOfLives.FreeReviveUsed";
        // Persisted UTC timestamp of the last interstitial (or rewarded ad) - interstitial cooldown.
        private const string LastInterstitialShownUtcKey = "Ads.LastInterstitialShownUtc";
        [SerializeField] private FillItBoosterManager boosterManager;
        // Fillomino has no notes mode - kept only to switch the shared Notes UI off.
        [SerializeField] private NotesManager notesManager;
        [SerializeField] private BoosterConfig boosterEconomy = new BoosterConfig();
        // A move that breaks a region is charged a life only after this grace period, and only if
        // the region is still broken - a mistake fixed within the window is free.
        [SerializeField] private float wrongValidationDelaySeconds = 0.75f;
        [SerializeField] private float correctValidationDelaySeconds = 0.4f;
        [SerializeField] private float autosaveIntervalSeconds = 5f;
        [SerializeField] private bool loadOnStart = true;
        [Tooltip("Show the Appreciation Text popups (\"Nice!\", \"Great!\"...) when regions are completed. See SetAppreciationTextEnabled.")]
        [SerializeField] private bool appreciationTextEnabled = true;

        private FillItRuntimeController controller;
        private AdsManager subscribedAdsManager;
        private InAppManager subscribedInAppManager;
        private GlobalConfig activeConfig = new GlobalConfig().CloneNormalized();
        private GameRulesConfig activeRulesConfig = new GameRulesConfig().CloneNormalized();
        private int currentLevelIndex;
        // Lazily loaded persisted loop-level sequence (levels past GlobalConfig.TotalLevels).
        private List<int> loopLevelSequence;
        // See LoadedLevelNumber.
        private int lastLoadedActualLevelNumber;
        // Revive/retry counters for the current level number - reset only when a different level
        // loads, never on a restart of the same one.
        private int trackedAttemptLevelNumber = -1;
        private int levelReviveCount;
        private int levelRetryCount;
        // Per-attempt analytics counters - reset on every load, including a restart.
        private int attemptHintUsedCount;
        private int attemptUndoUsedCount;
        private int attemptRegionTooBigCount;
        private int attemptRegionTooSmallCount;
        private int attemptPrePlacedTileTap;
        // Cumulative lives spent this attempt - not (Max - Remaining), which drops after a revive.
        private int attemptLivesUsedTotal;
        private bool currentLevelIsHard;
        private float autosaveTimer;
        private bool settingsPopupOpen;
        private bool outOfLivesPopupOpen;
        private bool buyBoosterPopupOpen;
        private bool storePopupOpen;
        private BoosterType pendingBuyBoosterType;
        private CheatPanelController cheatPanel;
        private BoosterType lastUsedBoosterType;
        private int lastBoosterUseFrame = -1;
        private bool isDailyChallengeSession;
        // Captured in HandleLevelWon when a Daily Challenge sub-level's coins are granted - see
        // GetDailyChallengeLevelWinCoinInfo.
        private int dailyChallengeCoinsBeforeWin;
        private int dailyChallengeCoinsWon;
        private int dailyChallengePuzzleIndex = -1;
        private int dailyChallengeSubLevelIndex = -1;
        private System.DateTime? dailyChallengeRestartDate;

        // Set only by SkipFtueTutorial right before ForceWin - skips the win celebration beat.
        private bool skippingFtueTutorial;

        public FillItRuntimeController Controller => controller;
        public GlobalConfig ActiveConfig => activeConfig;
        public int CurrentLevelIndex => currentLevelIndex;
        public int CurrentLevelNumber => currentLevelIndex + 1;
        // The underlying level asset number last loaded - differs from CurrentLevelNumber once
        // loop levels replay the pack (surfaced to the cheat panel).
        public int LoadedLevelNumber => lastLoadedActualLevelNumber;
        public int LevelCount => GetLevelCount();
        public bool HasNextLevel => currentLevelIndex + 1 < GetLevelCount();

        // Mirrors Start()'s "go straight to gameplay" condition - MainMenuHandler.OnEnable reads it
        // to skip showing itself in that case.
        public bool SkipMainMenuOnLaunch => loadOnStart || currentLevelIndex <= 0;

        protected virtual void Awake()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            EnsureButtonClickSfxBinder();
            ResolveNotesManager();
            ApplyGlobalConfig();
            ApplyGameRulesConfig();
            ApplyRulesConfigToViews();

            // Resolved in Awake (before any OnEnable) so MainMenuHandler's widgets read the right
            // CurrentLevelNumber.
            ResolveCurrentLevelIndexFromSave();
            controller = new FillItRuntimeController(
                new FillItLevelRepository(),
                new FillItRuntimeSession(),
                boardView,
                statusView,
                uiFlowManager);
            controller.AppreciationTextEnabled = appreciationTextEnabled;
            controller.LevelWon += HandleLevelWon;
            controller.LevelFailed += HandleLevelFailed;
            controller.OutOfMistakes += HandleOutOfMistakes;
            controller.TimeUp += HandleTimeUp;
            controller.MistakeSpent += HandleMistakeSpent;
            controller.RuleViolationCharged += HandleRuleViolationCharged;
            // Cached so OnDestroy unsubscribes from this exact instance (AdsManager.Instance
            // lazily creates one, which would be pointless during teardown).
            subscribedAdsManager = AdsManager.Instance;
            subscribedAdsManager.RewardedAdCompleted += HandleRewardedAdCompletedForInterstitialCooldown;
            subscribedInAppManager = InAppManager.Instance;
            subscribedInAppManager.UnattendedPurchaseGranted += HandleUnattendedPurchaseGranted;
            ConfigureStore();
            BindCheatPanel();
            BindSharedBoosterEvents();
            BindOutOfLivesPopupEvents();
            BindBoardInput();
        }

        protected virtual void Start()
        {
            // One single music loop for Menu and Gameplay alike, started before deciding which
            // screen shows first.
            PlaySound(global::SoundName.GamePlayBg);

            // A player who hasn't finished Level 1 drops straight into gameplay.
            if (loadOnStart || currentLevelIndex <= 0)
            {
                StartGame();
            }
            else
            {
                ShowMainMenu();
            }
        }

        protected virtual void Update()
        {
            // Also gated on GameTransitionOverlay.IsTransitioning - otherwise the per-frame status
            // render keeps re-showing the page a transition is trying to swap away from.
            if (!settingsPopupOpen && !outOfLivesPopupOpen && !buyBoosterPopupOpen && !storePopupOpen &&
                !Shared.GameFlow.GameTransitionOverlay.IsTransitioning)
            {
                // The countdown only runs while the player can actually act - not during the level
                // entrance, popups, tips or the win celebration.
                if (Shared.GameFlow.InputManager.GamePlayInputEnabled && (ftueDriver == null || !ftueDriver.IsShowingUiTip))
                {
                    controller?.TickTimer(Time.deltaTime);
                }

                controller?.Tick(Time.deltaTime);
            }

            TickKeyboardInput();
            TickAutosave();
        }

        protected virtual void OnDestroy()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            StopPendingMistake();
            SaveAll();
            UnbindBoardInput();

            if (controller != null)
            {
                controller.LevelWon -= HandleLevelWon;
                controller.LevelFailed -= HandleLevelFailed;
                controller.OutOfMistakes -= HandleOutOfMistakes;
                controller.TimeUp -= HandleTimeUp;
                controller.MistakeSpent -= HandleMistakeSpent;
                controller.RuleViolationCharged -= HandleRuleViolationCharged;
                controller.Dispose();
            }

            if (subscribedAdsManager)
            {
                subscribedAdsManager.RewardedAdCompleted -= HandleRewardedAdCompletedForInterstitialCooldown;
            }

            if (subscribedInAppManager)
            {
                subscribedInAppManager.UnattendedPurchaseGranted -= HandleUnattendedPurchaseGranted;
            }

            UnbindCheatPanel();
            UnbindSharedBoosterEvents();
            controller = null;
        }

        protected virtual void OnApplicationQuit()
        {
            StopPendingMistake();
            controller?.CancelDrag();
            SaveAll();
        }

        protected virtual void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                StopPendingMistake();
                controller?.CancelDrag();
                SaveAll();
            }
        }

        // ----- level loading -----

        // isContinueFromWin: true only via LoadNextLevel (win popup Continue) - LevelStart's IsContinue.
        public void LoadAssignedLevel(bool isContinueFromWin = false)
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            isDailyChallengeSession = false;
            statusView?.SetDailyChallengeDate(null);
            if (uiFlowManager != null)
            {
                uiFlowManager.SuppressWinFailPages = false;
            }

            if (controller == null)
            {
                return;
            }

            StopPendingMistake();
            TextAsset selectedLevel = GetCurrentLevelAsset();
            cheatPanel?.SetLoopLevelReadout(CurrentLevelNumber, lastLoadedActualLevelNumber);
            // Before LoadLevel's first render, so the HUD never shows the looped asset's own number.
            statusView?.SetDisplayLevelNumber(CurrentLevelNumber);
            if (selectedLevel == null)
            {
                statusView?.ShowLoadErrors(new[] { "No Fill It level JSON or level sequence assigned." });
                return;
            }

            FillItLevelLoadResult result = controller.ParseLevelJson(selectedLevel.text);
            if (!result.IsLoaded)
            {
                Debug.LogError($"FillItGameplayManager: level '{selectedLevel.name}' failed to load: {string.Join(" | ", result.Errors)}", selectedLevel);
                statusView?.ShowLoadErrors(result.Errors);
                RefreshLevelFlowControls();
                return;
            }

            EnsureAttemptCountersForLevel(CurrentLevelNumber);
            ResetPerAttemptAnalyticsCounters();

            ApplySavedCoinBalance(boosterEconomy);
            ApplyRulesConfigToViews();
            int levelMistakeLimit = ResolveLevelMistakeLimit(result.Level);
            bool ftueLevel = IsFtueLevel(CurrentLevelNumber);
            // Set before LoadLevel so its first render already reflects this level's rules.
            controller.LevelAttemptFeatureEnabled = IsLevelAttemptFeatureUnlocked(CurrentLevelNumber);
            controller.SuppressWrongIndication = ftueLevel;
            controller.SuppressAppreciationText = ftueLevel;
            // Timer mode times every level except the FTUE tutorials.
            ConfigureTimerForLevel(result.Level, !ftueLevel);
            controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
            List<FtueStepData> ftueSteps = ResolveFtueSteps(CurrentLevelNumber);
            FillItFtueMove[] ftueMoves = ResolveFtueMoves(CurrentLevelNumber);
            bool ftueActive = ftueLevel && ftueMoves != null && ftueMoves.Length > 0;
            ApplyFtueState(ftueActive);
            statusView?.SetFtueLevelIndicator(CurrentLevelNumber, ftueActive);
            progressSaveManager?.ApplySavedBoosterState(controller.Session.BoosterWallet);

            currentLevelIsHard = string.Equals(result.Level.Difficulty, "Hard", System.StringComparison.OrdinalIgnoreCase);
            // Single choke point for every regular-level load (Home Play, restart, Continue).
            AnalyticsManager.Instance.LogLevelStart(BuildLevelResultEventData());

            boosterManager?.Initialize(controller, () => CurrentLevelNumber);
            sharedBoosterManager?.SetUndoAvailabilityProvider(() => boosterManager != null && boosterManager.CanUndo());
            BindSharedBoosterEvents();
            sharedBoosterManager?.ApplyRulesConfig(activeRulesConfig);
            sharedBoosterManager?.Configure(controller.Session.BoosterWallet, () => CurrentLevelNumber);
            ConfigureHintHighlight();
            ConfigureStore();
            bool restoredBoard = activeConfig.RestoreCurrentBoardOnLoad &&
                                 boardSaveManager != null &&
                                 boardSaveManager.TryRestore(controller.Session);

            // A save made while Out of Lives / Time's Up was showing but never resolved (the player
            // quit) - treat it like Give Up: wipe it and start fresh rather than resume with 0
            // lives or 0 seconds.
            if (restoredBoard &&
                (controller.Session.IsOutOfTime ||
                 (controller.LevelAttemptFeatureEnabled && !controller.Session.TimerEnabled &&
                  controller.Session.LevelMistakeLimit != null &&
                  controller.Session.LevelMistakeLimit.RemainingMistakes <= 0)))
            {
                boardSaveManager.Clear(controller.Session);
                controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
                restoredBoard = false;
            }

            controller.RefreshViews();
            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveProgress();

            // Daily Challenge never touches HardLevels progression.
            bool isHardLevel = !isDailyChallengeSession && IsHardLevel(CurrentLevelNumber);
            if (isHardLevel)
            {
                statusView?.EnableHardLevelUI();
            }

            pendingHardLevelIntroThisLoad = isHardLevel;
            PlayLevelStartEntranceSequence(ftueActive, ftueSteps, ftueMoves);
            RefreshLevelFlowControls();
        }

        /// <summary>
        /// How far the player got through a Daily Challenge sub-level last time, as a percent of
        /// its empty cells correctly filled. Read-only - never loads the level into the runtime, so
        /// it's safe for every calendar button. False (0%) when never attempted.
        /// </summary>
        public bool TryGetDailyChallengeProgressPercent(int puzzleIndex, int subLevelIndex, out float progressPercent)
        {
            progressPercent = 0f;
            if (boardSaveManager == null)
            {
                return false;
            }

            TextAsset levelAsset = LoadDailyChallengeAsset(puzzleIndex, subLevelIndex, out _);
            if (levelAsset == null)
            {
                return false;
            }

            FillItLevelLoadResult result = new FillItLevelRepository().LoadFromJson(levelAsset.text);
            if (!result.IsLoaded ||
                !boardSaveManager.TryReadSavedBoardValues(result.Level.Id, DailyChallengeProgressionId, out int size, out int[] values) ||
                size != result.Level.Size)
            {
                return false;
            }

            progressPercent = FillItLevelProgressCalculator.CalculatePercent(result.Level, values);
            return true;
        }

        /// <summary>
        /// Loads Daily Challenge level "dc_{puzzleIndex + 1}_{subLevelIndex + 1}" from
        /// Resources/DailyChallengeLevels (both indices 0-based, as handed over by
        /// DailyChallengeController). Doesn't touch currentLevelIndex or the campaign board save.
        /// </summary>
        public bool LoadDailyChallengeLevel(int puzzleIndex, int subLevelIndex, System.DateTime? challengeDate = null)
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (controller == null)
            {
                Debug.LogWarning("FillItGameplayManager.LoadDailyChallengeLevel: controller is null (Awake hasn't run yet?).");
                return false;
            }

            TextAsset levelAsset = LoadDailyChallengeAsset(puzzleIndex, subLevelIndex, out string levelName);
            if (levelAsset == null)
            {
                Debug.LogWarning($"FillItGameplayManager.LoadDailyChallengeLevel: Resources.Load(\"DailyChallengeLevels/{levelName}\") returned null - check the file is under a Resources folder and named exactly '{levelName}.json'.");
                statusView?.ShowLoadErrors(new[] { $"Daily Challenge level '{levelName}' was not found." });
                return false;
            }

            FillItLevelLoadResult result = controller.ParseLevelJson(levelAsset.text);
            if (!result.IsLoaded)
            {
                Debug.LogWarning($"FillItGameplayManager.LoadDailyChallengeLevel: '{levelName}' failed to load: {string.Join(", ", result.Errors)}");
                statusView?.ShowLoadErrors(result.Errors);
                return false;
            }

            StopPendingMistake();
            isDailyChallengeSession = true;
            dailyChallengePuzzleIndex = puzzleIndex;
            dailyChallengeSubLevelIndex = subLevelIndex;
            dailyChallengeRestartDate = challengeDate;
            statusView?.SetDailyChallengeDate(challengeDate);
            if (uiFlowManager != null)
            {
                uiFlowManager.SuppressWinFailPages = true;
            }

            ApplySavedCoinBalance(boosterEconomy);
            ApplyRulesConfigToViews();
            int levelMistakeLimit = ResolveLevelMistakeLimit(result.Level);
            // Daily Challenge isn't normal progression: Level Attempt always on, never FTUE.
            controller.LevelAttemptFeatureEnabled = true;
            controller.SuppressWrongIndication = false;
            controller.SuppressAppreciationText = false;
            ConfigureTimerForLevel(result.Level, allowTimer: true);
            controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
            ApplyFtueState(false);
            progressSaveManager?.ApplySavedBoosterState(controller.Session.BoosterWallet);

            boosterManager?.Initialize(controller, () => CurrentLevelNumber);
            sharedBoosterManager?.SetUndoAvailabilityProvider(() => boosterManager != null && boosterManager.CanUndo());
            BindSharedBoosterEvents();
            sharedBoosterManager?.ApplyRulesConfig(activeRulesConfig);
            sharedBoosterManager?.Configure(controller.Session.BoosterWallet, () => CurrentLevelNumber);
            ConfigureHintHighlight();

            // Resume this sub-level's board if the player quit mid-puzzle.
            boardSaveManager?.TryRestore(controller.Session, DailyChallengeProgressionId);
            controller.RefreshViews();
            sharedBoosterManager?.RefreshAllBoosterUI();
            pendingHardLevelIntroThisLoad = false;
            PlayLevelStartEntranceSequence(ftueActive: false, ftueSteps: null, ftueMoves: null);
            return true;
        }

        private static TextAsset LoadDailyChallengeAsset(int puzzleIndex, int subLevelIndex, out string levelName)
        {
            levelName = $"dc_{puzzleIndex + 1}_{subLevelIndex + 1}";
            return Resources.Load<TextAsset>($"DailyChallengeLevels/{levelName}");
        }

        // ----- gameplay mode (Hearts / Timer) -----

        // Set by SetGameplayMode; otherwise GlobalConfig.GameplayMode decides.
        private GameplayMode? gameplayModeOverride;

        /// <summary>The mode the next level loads with: SetGameplayMode's choice, else GlobalConfig.GameplayMode.</summary>
        public GameplayMode CurrentGameplayMode => gameplayModeOverride ?? activeConfig.GameplayMode;

        /// <summary>True while the level being played is running a countdown.</summary>
        public bool IsTimerModeActive => controller != null && controller.Session.HasLevel && controller.Session.TimerEnabled;

        /// <summary>
        /// Switches between Hearts (wrong moves cost lives) and Timer (a countdown per level) for
        /// this session, overriding GlobalConfig.GameplayMode. Takes effect from the next level
        /// load (Play, Restart, Next) - the level currently on screen keeps its mode.
        /// </summary>
        public void SetGameplayMode(GameplayMode mode)
        {
            gameplayModeOverride = mode;
        }

        /// <summary>Convenience for a UI Toggle / UnityEvent: true = Timer, false = Hearts.</summary>
        public void SetTimerMode(bool timer)
        {
            SetGameplayMode(timer ? GameplayMode.Timer : GameplayMode.Hearts);
        }

        // Called right before every LoadLevel. The modes are exclusive: Timer mode = a countdown
        // and no hearts, Hearts mode = hearts and no countdown. allowTimer is false for FTUE
        // levels (tutorials are never timed). The limit comes from the level's
        // "timeLimitSeconds"; levels without one fall back to the GameRulesConfig estimate.
        private void ConfigureTimerForLevel(FillItLevelData level, bool allowTimer)
        {
            bool timer = allowTimer && level != null && CurrentGameplayMode == GameplayMode.Timer;
            float limit = 0f;
            if (timer)
            {
                limit = level.TimeLimitSeconds ??
                        level.EstimateTimeLimitSeconds(activeRulesConfig.TimerSecondsPerEmptyCell, activeRulesConfig.TimerMinimumSeconds);
            }

            controller.Session.ConfigureTimer(timer, limit);
        }

        public bool IsAppreciationTextEnabled => appreciationTextEnabled;

        /// <summary>
        /// Turns the Appreciation Text (gratification) popups on or off for the rest of the session.
        /// Also callable from a UnityEvent (e.g. a settings Toggle's onValueChanged).
        /// </summary>
        public void SetAppreciationTextEnabled(bool enabled)
        {
            appreciationTextEnabled = enabled;
            if (controller != null)
            {
                controller.AppreciationTextEnabled = enabled;
            }
        }

        public void ShowMainMenu()
        {
            ReturnToMainMenu();
        }

        public void SaveAndShowMainMenu()
        {
            // Home click - only logs LevelFail when the level genuinely failed/ran out of lives.
            if (IsCurrentLevelFailedOrOutOfLives())
            {
                AnalyticsManager.Instance.LogLevelFail(BuildLevelResultEventData());
            }

            SaveAll();
            ReturnToMainMenu();
        }

        public void PauseForSettingsPopup()
        {
            PausePendingMistake();
            controller?.CancelDrag();
            settingsPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            bool inNormalLevelGameplay = !isDailyChallengeSession && controller != null && controller.Session.IsPlaying;
            settingsPopupController?.SetQuitButtonVisible(inNormalLevelGameplay);
            SaveAll();
        }

        public void ResumeFromSettingsPopup()
        {
            settingsPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = controller != null && controller.Session.IsPlaying;
            // A wrong number left pending when Settings opened is re-checked now.
            RestartMistakeTimer();

            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            AnalyticsManager.Instance.LogSettingUi(CurrentLevelNumber, data.IsMusicOn, data.IsSoundOn, data.IsHapticOn);
        }

        // Called by LevelWinPopupController.OnHomeClicked.
        public void LogHomeFromWinPopup()
        {
            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: false, levelWin: true, placement: AnalyticsEvents.HomePlacementValues.Win);
        }

        public void SettingsHome()
        {
            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: true, levelWin: false, placement: AnalyticsEvents.HomePlacementValues.InGame);
            // Home is a pause, not an abandon - the board stays resumable (FTUE levels excepted,
            // see SaveCurrentBoard).
            SaveAll();
            boosterManager?.ClearUndoHistory();
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => controller?.ClearBoardView(),
                revealNewPage: ReturnToMainMenu);
        }

        // Settings popup "Quit" - restarts the current level in place.
        public void SettingsQuit()
        {
            if (isDailyChallengeSession)
            {
                DailyChallengeAttemptAbandoned.Invoke();
            }

            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: null,
                revealNewPage: RestartLevel);
        }

        public void StartGame()
        {
            StopPendingMistake();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
            ResolveCurrentLevelIndexFromSave();
            LoadAssignedLevel();
        }

        private void ResolveCurrentLevelIndexFromSave()
        {
            EnsurePlayerDataInstance();
            currentLevelIndex = activeConfig.ResumeSavedLevelOnPlay
                ? progressSaveManager?.LoadCurrentLevelIndex(0, GetLevelCount()) ?? 0
                : 0;
        }

        private void ReturnToMainMenu()
        {
            StopPendingMistake();
            settingsPopupOpen = false;
            outOfLivesPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (controller == null)
            {
                return;
            }

            ftueDriver?.Configure(false, null, null, controller);
            controller.ShowMainMenu();
            ReturnedToMainMenu.Invoke();
            RefreshLevelFlowControls();
        }

        public void ClearBoard()
        {
            StopPendingMistake();
            boardSaveManager?.Clear(controller?.Session);
            controller?.Clear();
            SaveAll();
        }

        public void RestartLevel()
        {
            StopPendingMistake();
            boosterManager?.ClearUndoHistory();

            if (isDailyChallengeSession)
            {
                AnalyticsManager.Instance.LogRestartLevel(BuildLevelResultEventData(), AnalyticsEvents.RestartPlacementValues.DailyChallenge);
                ClearDailyChallengeBoard();
                if (LoadDailyChallengeLevel(dailyChallengePuzzleIndex, dailyChallengeSubLevelIndex, dailyChallengeRestartDate))
                {
                    DailyChallengeSessionRestarted.Invoke();
                }

                return;
            }

            // Only logs LevelFail when the level really failed - a Settings Quit mid-play routes
            // here too. levelRetryCount counts every restart regardless.
            if (IsCurrentLevelFailedOrOutOfLives())
            {
                AnalyticsManager.Instance.LogLevelFail(BuildLevelResultEventData());
            }

            AnalyticsManager.Instance.LogRestartLevel(BuildLevelResultEventData(), AnalyticsEvents.RestartPlacementValues.InGame);
            levelRetryCount++;
            boardSaveManager?.Clear(controller?.Session);
            SaveProgress();
            LoadAssignedLevel();
        }

        public void LoadNextLevel()
        {
            StopPendingMistake();

            // currentLevelIndex already advanced past the won level in HandleLevelWon.
            if (currentLevelIndex >= GetLevelCount())
            {
                statusView?.ShowLoadErrors(new[] { "No next Fill It level is available." });
                return;
            }

            TryShowLevelCompleteInterstitial(isDailyChallenge: false, () =>
            {
                boardSaveManager?.Clear(controller?.Session);
                boosterManager?.ClearUndoHistory();
                SaveAll();
                LoadAssignedLevel(isContinueFromWin: true);
            });
        }

        // ----- saving -----

        public void SaveProgress()
        {
            progressSaveManager?.SaveProgress(currentLevelIndex, controller != null && controller.Session.HasLevel ? controller.Session.BoosterWallet : null);
            MainMenuHandler?.RefreshCoinDisplay();
        }

        public void SaveCurrentBoard()
        {
            // An ended level (Won/Failed) has nothing left to persist - saving would re-write it
            // over a Clear that already ran.
            if (controller == null || !controller.Session.IsPlaying)
            {
                return;
            }

            // FTUE levels never persist an in-progress board - a mid-level quit restarts the
            // guided sequence from its first step.
            if (!isDailyChallengeSession && IsFtueLevel(CurrentLevelNumber))
            {
                boardSaveManager?.Clear(controller.Session);
                return;
            }

            if (isDailyChallengeSession)
            {
                boardSaveManager?.Save(controller.Session, -1, DailyChallengeProgressionId);
            }
            else
            {
                boardSaveManager?.Save(controller.Session, currentLevelIndex);
            }
        }

        /// <summary>Clears the current Daily Challenge sub-level's saved board (a Lost result starts fresh).</summary>
        public void ClearDailyChallengeBoard()
        {
            boardSaveManager?.Clear(controller?.Session, DailyChallengeProgressionId);
        }

        public void SaveAll()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            SaveCurrentBoard();
            SaveProgress();
            autosaveTimer = 0f;
        }

        private void TickAutosave()
        {
            if (autosaveIntervalSeconds <= 0f || controller == null || !controller.Session.IsPlaying)
            {
                return;
            }

            autosaveTimer += Time.deltaTime;
            if (autosaveTimer >= autosaveIntervalSeconds)
            {
                SaveAll();
            }
        }

        // ----- level entrance -----

        [Header("Level Start Entrance Animation")]
        // Head-start before any HUD element moves - the board's own entrance ignores this.
        [SerializeField] private float hudEntranceInitialDelaySeconds = 0.15f;
        [SerializeField] private float boosterPanelSlideDurationSeconds = 0.3f;
        [SerializeField] private float boosterPanelSlideDistance = 220f;

        private void PlayBoardEnterAnimation(System.Action onComplete, bool skipHudPopIn = false)
        {
            if (!skipHudPopIn)
            {
                statusView?.PlayEntryAnimation();
            }

            if (boardView != null)
            {
                boardView.PlayEnterAnimation(onComplete);
                return;
            }

            onComplete?.Invoke();
        }

        // Continues the status view's "top HUD -> life -> rules" start stagger for the booster panel.
        private void PlayBoosterPanelEntranceAnimation(System.Action onComplete)
        {
            if (boosterPanelCanvasGroup == null ||
                !boosterPanelCanvasGroup.gameObject.activeInHierarchy ||
                boosterPanelCanvasGroup.transform is not RectTransform boosterRect)
            {
                onComplete?.Invoke();
                return;
            }

            float staggerDelay = statusView != null ? statusView.EntranceGroupStaggerSeconds * 3f : 0f;
            float startDelay = Mathf.Max(0f, hudEntranceInitialDelaySeconds) + staggerDelay;
            StartCoroutine(SlideBoosterPanelRoutine(boosterRect, startDelay, onComplete));
        }

        private IEnumerator SlideBoosterPanelRoutine(RectTransform target, float startDelay, System.Action onComplete)
        {
            Vector2 restPosition = target.anchoredPosition;
            Vector2 startPosition = restPosition - new Vector2(0f, boosterPanelSlideDistance);
            target.anchoredPosition = startPosition;

            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            float duration = Mathf.Max(0.01f, boosterPanelSlideDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.anchoredPosition = Vector2.LerpUnclamped(startPosition, restPosition, t);
                yield return null;
            }

            target.anchoredPosition = restPosition;
            onComplete?.Invoke();
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }

        // Top HUD, life panel, rules and booster panel each START in turn with a small stagger;
        // the board animates in independently. Only once every group is done does the FTUE (or
        // the one-time hearts tip) pop in and gameplay input turn on.
        private void PlayLevelStartEntranceSequence(bool ftueActive, List<FtueStepData> ftueSteps, FillItFtueMove[] ftueMoves)
        {
            bool playHudEntrance = !ftueActive;
            int remaining = playHudEntrance ? 3 : 1;
            bool finished = false;
            sharedBoosterManager?.RefreshAllBoosterUI(allowUnlockAnimation: true);

            void GroupComplete()
            {
                remaining--;
                if (remaining <= 0 && !finished)
                {
                    finished = true;
                    FinishLevelStartEntrance(ftueActive, ftueSteps, ftueMoves);
                }
            }

            if (playHudEntrance)
            {
                if (statusView != null)
                {
                    statusView.PlayHudEntranceAnimation(hudEntranceInitialDelaySeconds, GroupComplete);
                }
                else
                {
                    GroupComplete();
                }

                PlayBoosterPanelEntranceAnimation(GroupComplete);
            }

            PlayBoardEnterAnimation(GroupComplete, skipHudPopIn: ftueActive);
        }

        private void FinishLevelStartEntrance(bool ftueActive, List<FtueStepData> ftueSteps, FillItFtueMove[] ftueMoves)
        {
            // The one place a booster's unlock celebration may play.
            sharedBoosterManager?.RefreshAllBoosterUI(allowUnlockAnimation: true);

            if (ftueActive)
            {
                ftueDriver?.Configure(true, ftueSteps, ftueMoves, controller);
            }
            else
            {
                ShowLevelAttemptTipIfNeeded();
            }

            if (pendingHardLevelIntroThisLoad && hardLevelPopupController != null)
            {
                hardLevelPopupController.pendingOnComplete = () => StartCoroutine(EnableGameplayInputNextFrame());
                hardLevelPopupController.Show();
            }
            else
            {
                StartCoroutine(EnableGameplayInputNextFrame());
            }
        }

        private IEnumerator EnableGameplayInputNextFrame()
        {
            yield return null;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = controller != null && controller.Session.HasLevel;
        }

        // ----- FTUE -----

        private bool IsFtueLevel(int levelNumber)
        {
            int untilLevel = activeConfig.FtueEnabledUntilLevel;
            return untilLevel > 0 && levelNumber <= untilLevel;
        }

        private FillItFtueLevelStepsEntry FindFtueEntry(int levelNumber)
        {
            if (ftueLevelSteps == null)
            {
                return null;
            }

            for (int index = 0; index < ftueLevelSteps.Length; index++)
            {
                if (ftueLevelSteps[index] != null && ftueLevelSteps[index].LevelNumber == levelNumber)
                {
                    return ftueLevelSteps[index];
                }
            }

            return null;
        }

        private List<FtueStepData> ResolveFtueSteps(int levelNumber)
        {
            FillItFtueLevelStepsEntry entry = FindFtueEntry(levelNumber);
            return entry != null && entry.Steps != null ? entry.Steps.Steps : null;
        }

        private FillItFtueMove[] ResolveFtueMoves(int levelNumber)
        {
            return FindFtueEntry(levelNumber)?.Moves;
        }

        /// <summary>
        /// "Skip Tutorial": ends the walkthrough immediately and auto-solves the board. Skips the
        /// win celebration and lands on the same transition (or win popup, last FTUE level) a real
        /// completion would.
        /// </summary>
        public void SkipFtueTutorial()
        {
            if (controller == null || !controller.Session.HasLevel || !IsFtueLevel(CurrentLevelNumber))
            {
                return;
            }

            AnalyticsManager.Instance.LogFtueLevelSkip(CurrentLevelNumber, ftueDriver?.CurrentStepNumber ?? 1);
            ftueDriver?.ForceHideImmediate();
            ftueDriver?.Configure(false, null, null, controller);
            skipTutorialButtonRoot?.SetActive(false);

            if (uiFlowManager != null && CurrentLevelNumber < activeConfig.FtueEnabledUntilLevel)
            {
                uiFlowManager.SuppressWinFailPages = true;
            }

            skippingFtueTutorial = true;
            StopPendingMistake();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            controller.ForceWin();
        }

        // Hides Settings/Info/Coins/Rules, the booster panel and the number pad while FTUE is active.
        private void ApplyFtueState(bool active)
        {
            if (!active)
            {
                ftueDriver?.Configure(false, null, null, controller);
            }

            statusView?.SetSettingsButtonVisible(!active);
            statusView?.SetRulesPanelVisible(!active);
            statusView?.SetRulesPanelOverlaySortingActive(false);
            statusView?.SetInfoButtonVisible(!active);
            statusView?.SetCoinsHudVisible(!active);
            statusView?.SetRuleRowVisible(FillIt.Rules.FillItRule.RegionTooBig, true);
            statusView?.SetRuleRowVisible(FillIt.Rules.FillItRule.RegionTooSmall, true);

            if (boosterPanelCanvasGroup != null)
            {
                boosterPanelCanvasGroup.gameObject.SetActive(!active);
            }

            numberPad?.SetVisible(!active);

            if (skipTutorialButtonRoot != null)
            {
                skipTutorialButtonRoot.SetActive(active);
            }
        }

        // Key in PlayerData marking the "Lives are lost on wrong moves" tip as seen.
        private const string LevelAttemptTipSeenKey = "Ftue.SeenLevelAttemptTip";

        // One-time tip pointing at the hearts HUD, the first time Level Attempt turns on.
        private void ShowLevelAttemptTipIfNeeded()
        {
            // The tip is about hearts - Timer mode has none.
            if (controller == null || !controller.LevelAttemptFeatureEnabled || statusView == null || IsTimerModeActive)
            {
                return;
            }

            Shared.PlayerData.PlayerData playerData = Shared.PlayerData.PlayerData.Instance;
            if (playerData.TryGetInt(LevelAttemptTipSeenKey, out int seen) && seen != 0)
            {
                return;
            }

            RectTransform target = statusView.AttemptsHudRectTransform;
            if (target == null)
            {
                return;
            }

            ftueDriver?.ShowUiTip(target, new FtueStepData
            {
                Description = levelAttemptTipDescription
            });

            playerData.SetInt(LevelAttemptTipSeenKey, 1);
            SaveAll();
        }

        // ----- level sequence -----

        private TextAsset GetCurrentLevelAsset()
        {
            if (levelSequence != null && levelSequence.Length > 0)
            {
                if (currentLevelIndex < 0)
                {
                    currentLevelIndex = 0;
                }

                int normalLevelCount = System.Math.Min(levelSequence.Length, activeConfig.TotalLevels);
                if (currentLevelIndex < normalLevelCount)
                {
                    lastLoadedActualLevelNumber = currentLevelIndex + 1;
                    return levelSequence[currentLevelIndex];
                }

                // Past the normal pack, GlobalConfig.LoopLevels replays the same assets in a
                // curated, reshuffled order.
                int loopLevelNumber = ResolveLoopLevelNumber(currentLevelIndex - normalLevelCount);
                if (loopLevelNumber >= 1 && loopLevelNumber <= levelSequence.Length)
                {
                    lastLoadedActualLevelNumber = loopLevelNumber;
                    return levelSequence[loopLevelNumber - 1];
                }

                // No loop levels configured - replay the last level of the normal pack.
                currentLevelIndex = normalLevelCount - 1;
                lastLoadedActualLevelNumber = currentLevelIndex + 1;
                return levelSequence[System.Math.Max(0, currentLevelIndex)];
            }

            lastLoadedActualLevelNumber = CurrentLevelNumber;
            return levelJson;
        }

        // Maps a 0-based loop position to a level number by extending (and persisting) the resolved
        // sequence one shuffled set at a time. 0 when no loop levels are configured.
        private int ResolveLoopLevelNumber(int loopIndex)
        {
            if (loopIndex < 0)
            {
                loopIndex = 0;
            }

            if (!FillItLoopLevelSequencer.HasAnyLevels(activeConfig.LoopLevels))
            {
                return 0;
            }

            if (loopLevelSequence == null)
            {
                loopLevelSequence = progressSaveManager?.LoadLoopLevelSequence() ?? new List<int>();
            }

            bool extended = false;
            while (loopLevelSequence.Count <= loopIndex)
            {
                List<int> set = FillItLoopLevelSequencer.GenerateSet(activeConfig.LoopLevels);
                if (set.Count == 0)
                {
                    break;
                }

                loopLevelSequence.AddRange(set);
                extended = true;
            }

            if (extended)
            {
                progressSaveManager?.SaveLoopLevelSequence(loopLevelSequence);
            }

            return loopIndex < loopLevelSequence.Count ? loopLevelSequence[loopIndex] : 0;
        }

        private int GetLevelCount()
        {
            int availableLevels = levelSequence != null && levelSequence.Length > 0 ? levelSequence.Length : (levelJson == null ? 0 : 1);
            int normalLevelCount = activeConfig.TotalLevels <= 0 ? availableLevels : System.Math.Min(availableLevels, activeConfig.TotalLevels);

            // Loop levels replay endlessly - report an effectively unbounded count.
            if (levelSequence != null && levelSequence.Length > 0 && FillItLoopLevelSequencer.HasAnyLevels(activeConfig.LoopLevels))
            {
                return int.MaxValue;
            }

            return normalLevelCount;
        }

        private int ResolveLevelMistakeLimit(FillItLevelData level)
        {
            return Mathf.Max(1, level?.LevelMistakeLimit ?? activeRulesConfig.LevelMistakeLimit);
        }

        // 0 = always unlocked; a positive level gates the hearts HUD / heart break FX until reached.
        private bool IsLevelAttemptFeatureUnlocked(int levelNumber)
        {
            int unlockLevel = activeConfig.LevelAttemptUnlockLevel;
            return unlockLevel <= 0 || levelNumber >= unlockLevel;
        }

        // Coins for winning levelNumber: a Daily Challenge sub-level uses its own config value, a
        // hard level pays HardLevelWinCoins, everything else LevelWinCoins (Remote Config-backed).
        private int ResolveLevelWinCoinReward(int levelNumber)
        {
            if (isDailyChallengeSession)
            {
                return activeConfig.DailyChallenge.LevelWinCoins;
            }

            return IsHardLevel(levelNumber) ? EconomyConfigController.HardLevelWinCoins : EconomyConfigController.LevelWinCoins;
        }

        private static bool IsHardLevel(int levelNumber)
        {
            Shared.GameConfig.GlobalConfig config = Shared.Boot.GlobalConfigRuntimeContext.Config;
            if (config == null || config.HardLevels == null)
            {
                return false;
            }

            return System.Array.IndexOf(config.HardLevels, levelNumber) >= 0;
        }

        private void RefreshLevelFlowControls()
        {
            uiFlowManager?.SetHasNextLevel(HasNextLevel);
        }

        // ----- analytics -----

        private void EnsureAttemptCountersForLevel(int levelNumber)
        {
            if (trackedAttemptLevelNumber == levelNumber)
            {
                return;
            }

            trackedAttemptLevelNumber = levelNumber;
            levelReviveCount = 0;
            levelRetryCount = 0;
        }

        private void ResetPerAttemptAnalyticsCounters()
        {
            attemptHintUsedCount = 0;
            attemptUndoUsedCount = 0;
            attemptRegionTooBigCount = 0;
            attemptRegionTooSmallCount = 0;
            attemptPrePlacedTileTap = 0;
            attemptLivesUsedTotal = 0;
        }

        // levelNumberOverride: HandleLevelWon calls this after currentLevelIndex already advanced.
        private LevelResultEventData BuildLevelResultEventData(int? levelNumberOverride = null)
        {
            int levelNumber = levelNumberOverride ?? CurrentLevelNumber;
            bool hasLevel = controller != null && controller.Session.HasLevel;
            int remainingMistakes = hasLevel ? controller.Session.LevelMistakeLimit?.RemainingMistakes ?? 0 : 0;
            return new LevelResultEventData
            {
                LevelNumber = levelNumber,
                FtueLevel = IsFtueLevel(levelNumber),
                HardLevel = currentLevelIsHard,
                LevelTime = hasLevel ? controller.Session.Progress.ElapsedSeconds : 0f,
                ReviveCount = levelReviveCount,
                RetryCount = levelRetryCount,
                CoinCount = hasLevel ? controller.Session.BoosterWallet?.CurrentCoins ?? 0 : 0,
                HintBoosterCount = hasLevel ? controller.Session.BoosterWallet?.GetCount(BoosterType.Hint) ?? 0 : 0,
                HintBoosterUsed = attemptHintUsedCount,
                UndoUsed = attemptUndoUsedCount,
                TotalNotesUsed = 0,
                TotalNotesApply = 0,
                NotesButtonTapCount = 0,
                LivesUsed = attemptLivesUsedTotal,
                LivesLeft = remainingMistakes,
                WrongSolutionCount = 0,
                // rule_1 = a region grew bigger than its number, rule_2 = a region got boxed in too small.
                Rule1BreakCount = attemptRegionTooBigCount,
                Rule2BreakCount = attemptRegionTooSmallCount,
                DcId = isDailyChallengeSession ? dailyChallengeSubLevelIndex + 1 : -1,
                PrePlacedTileTap = attemptPrePlacedTileTap,
            };
        }

        // Daily Challenge excluded - CurrentLevelNumber doesn't identify a DC level.
        private bool IsCurrentLevelFailedOrOutOfLives()
        {
            return !isDailyChallengeSession &&
                controller != null &&
                controller.Session.HasLevel &&
                (controller.Session.GameState.Current == FillItGameState.Failed ||
                 controller.Session.IsOutOfTime ||
                 (!controller.Session.TimerEnabled && controller.Session.LevelMistakeLimit.IsOutOfMistakes));
        }

        // ----- config & shared plumbing -----

        private void ConfigureStore()
        {
            storeManager?.Configure(
                storeConfigAsset != null ? storeConfigAsset.ToConfig() : new StoreConfig(),
                GrantStoreCoins,
                () => controller != null && controller.Session.HasLevel ? controller.Session.BoosterWallet?.CurrentCoins ?? 0 : Shared.PlayerData.PlayerData.Instance.TotalCoins);
            uiFlowManager?.StorePopup?.Configure(storeManager);
            shopPage?.Configure(storeManager);

            if (shopPage != null)
            {
                shopPage.PurchaseRequested.RemoveListener(HandleStorePurchaseRequested);
                shopPage.PurchaseRequested.AddListener(HandleStorePurchaseRequested);
            }
        }

        private void ResolveNotesManager()
        {
            if (notesManager == null)
            {
                notesManager = transform.root.GetComponentInChildren<NotesManager>(true);
            }

            // Fillomino has no notes mode.
            notesManager?.SetNotesActive(false);
            notesManager?.SetApplyAllAvailable(false);
        }

        private void ResolveSharedBoosterManager()
        {
            if (sharedBoosterManager == null)
            {
                sharedBoosterManager = transform.root.GetComponentInChildren<Shared.Boosters.BoosterManager>(true);
            }
        }

        private void EnsureButtonClickSfxBinder()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            global::ButtonClickSfxBinder binder = canvas.GetComponent<global::ButtonClickSfxBinder>();
            if (binder == null)
            {
                binder = canvas.gameObject.AddComponent<global::ButtonClickSfxBinder>();
            }

            binder.Rebind();
        }

        private static void PlaySound(string soundName)
        {
            if (string.IsNullOrWhiteSpace(soundName))
            {
                return;
            }

            global::AudioManager.Instance.PlaySoundAction?.Invoke(soundName);
        }

        private void ApplyGlobalConfig()
        {
            activeConfig = GlobalConfigRuntimeContext.HasConfig
                ? GlobalConfigRuntimeContext.Config.CloneNormalized()
                : new GlobalConfig().CloneNormalized();

            if (globalConfig != null)
            {
                activeConfig = globalConfig.ToConfig();
            }

            boosterEconomy = CreateBoosterEconomyConfig(activeConfig);
            progressSaveManager?.ApplySaveKey(activeConfig.CurrentLevelIndexSaveKey);
            boardSaveManager?.ApplySaveKeyPrefix(activeConfig.BoardStateSaveKeyPrefix);
        }

        private void ApplyGameRulesConfig()
        {
            activeRulesConfig = gameRulesConfig != null
                ? gameRulesConfig.ToConfig()
                : new GameRulesConfig
                {
                    WrongValidationDelaySeconds = wrongValidationDelaySeconds,
                    CorrectValidationDelaySeconds = correctValidationDelaySeconds,
                    AutosaveIntervalSeconds = autosaveIntervalSeconds
                }.CloneNormalized();

            wrongValidationDelaySeconds = activeRulesConfig.WrongValidationDelaySeconds;
            correctValidationDelaySeconds = activeRulesConfig.CorrectValidationDelaySeconds;
            autosaveIntervalSeconds = activeRulesConfig.AutosaveIntervalSeconds;
        }

        private void ApplyRulesConfigToViews()
        {
            sharedBoosterManager?.ApplyRulesConfig(activeRulesConfig);
        }

        private void EnsurePlayerDataInstance()
        {
            if (!PlayerDataRuntimeContext.HasData)
            {
                progressSaveManager?.LoadTotalCoins(activeConfig.InitialCoins);
            }

            _ = PlayerData.Instance;
            ApplySavedCoinBalance(boosterEconomy);
        }

        private void ApplySavedCoinBalance(BoosterConfig economyConfig)
        {
            if (economyConfig == null)
            {
                return;
            }

            if (!PlayerDataRuntimeContext.HasData)
            {
                progressSaveManager?.LoadTotalCoins(economyConfig.InitialCoins);
            }

            economyConfig.InitialCoins = Mathf.Max(0, PlayerData.Instance.TotalCoins);
        }

        private void SyncSessionEconomyFromPlayerData()
        {
            if (controller == null || !controller.Session.HasLevel || controller.Session.BoosterWallet == null)
            {
                return;
            }

            if (PlayerData.Instance != null)
            {
                controller.Session.BoosterWallet.SetCoins(Mathf.Max(0, PlayerData.Instance.TotalCoins));
            }

            progressSaveManager?.ApplySavedBoosterState(controller.Session.BoosterWallet);
        }

        private static BoosterConfig CreateBoosterEconomyConfig(GlobalConfig config)
        {
            GlobalConfig normalized = (config ?? new GlobalConfig()).CloneNormalized();
            return new BoosterConfig
            {
                InitialCoins = normalized.InitialCoins,
                BoosterConfigList = CreateBoosterDefinitions(normalized.BoosterConfigList)
            };
        }

        private static BoosterDefinition[] CreateBoosterDefinitions(BoosterConfigData[] configList)
        {
            if (configList == null)
            {
                return new BoosterDefinition[0];
            }

            var definitions = new BoosterDefinition[configList.Length];
            for (int index = 0; index < configList.Length; index++)
            {
                BoosterConfigData config = configList[index] ?? new BoosterConfigData();
                definitions[index] = new BoosterDefinition
                {
                    UnLockLevel = config.UnLockLevel,
                    UnLockCount = config.UnLockCount,
                    BuyCoinCost = config.BuyCoinCost,
                    BuyCount = config.BuyCount,
                    AdBuyCount = config.AdBuyCount
                };
            }

            return definitions;
        }
    }
}
