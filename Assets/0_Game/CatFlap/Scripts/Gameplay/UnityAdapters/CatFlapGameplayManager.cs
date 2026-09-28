using System.Collections;
using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Levels;
using CatFlap.Models;
using CatFlap.Runtime;
using CatFlap.Validation;
using CatFlap.Views;
using Shared.Analytics;
using Shared.Boot;
using Shared.Boosters;
using Shared.CheatUI;
using Shared.Ftue;
using Shared.GameConfig;
using Shared.GameFlow;
using Shared.PlayerData;
using MangraGames;
using Shared.Notes;
using Shared.RemoteConfig;
using Shared.Vibration;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using System;

namespace CatFlap.UnityAdapters
{
    public partial class CatFlapGameplayManager : MonoBehaviour
    {
        [SerializeField] private TextAsset levelJson;
        [SerializeField] private TextAsset[] levelSequence = new TextAsset[0];
        [FormerlySerializedAs("gameConfig")]
        [SerializeField] private GlobalConfigAsset globalConfig;
        [SerializeField] private GameRulesConfigAsset gameRulesConfig;
        [SerializeField] private StoreConfigAsset storeConfigAsset;
        [SerializeField] private Shared.Store.StoreManager storeManager;
        [SerializeField] private Shared.Store.UnityAdapters.ShopPageController shopPage;
        [SerializeField] private MonoBehaviour boardView;
        [SerializeField] private CatFlapStatusViewAdapter statusView;
        [SerializeField] private CatFlapHintBannerController hintBannerController;

        // Shown instead of uiFlowManager.OutOfLivesPopup while isDailyChallengeSession is true -
        // see PlayHeartBreakThenShowOutOfLivesPopup. Leave unassigned to keep using the shared
        // popup for Daily Challenge too (e.g. before this prefab exists in a given scene).
        [SerializeField] private CatFlapDailyChallengeFailPopupController dailyChallengeOutOfLivesPopup;
        [SerializeField] private CatFlapHintSpotlightController hintSpotlightController;
        [SerializeField] private Button hintApplyButton;
        [SerializeField] private CanvasGroup boosterPanelCanvasGroup;
        [SerializeField] private CatFlapUIFlowManager uiFlowManager;
        // Credited on level win (see HandleLevelWon): one point per heart the player finished
        // with, 1:1. Assign in the Inspector - there is no singleton. Left unassigned it is looked
        // up in the scene once, because a silently unscored win is very hard to notice.
        [SerializeField] private Shared.Leaderboard.LeaderboardManager leaderboardManager;
        // Shown between the win celebration and the win popup: the board opens on the standing the
        // player STARTED the level with, then the hearts they finished on are added and their row
        // climbs. Leave it unassigned and the points are credited silently, exactly as before.
        [SerializeField] private Shared.Leaderboard.LeaderboardPanelController leaderboardPanel;
        // Shown every time a normal-progression level listed in GlobalConfig.HardLevels finishes
        // its entrance animation - restarts and repeat visits included (see LoadAssignedLevel/
        // FinishLevelStartEntrance - this manager owns that whole decision; the popup itself is
        // purely presentational). The HUD's own hard-level badge/label do NOT wait on this popup;
        // they turn hard back in LoadAssignedLevel. Left unassigned disables the popup entirely.
        [SerializeField] private Shared.GameFlow.HardLevelPopupController hardLevelPopupController;

        // Set in LoadAssignedLevel (before the HUD/coin entrance animation starts), consumed by
        // FinishLevelStartEntrance once that entrance finishes - true for any normal-progression
        // level that's hard.
        private bool pendingHardLevelIntroThisLoad;

        // What HandleLevelWon actually credited for the level just won (LevelWinCoins or
        // HardLevelWinCoins) - the win popup's display and its 2x ad grant both run after
        // currentLevelIndex has already advanced, so neither can re-derive it from
        // CurrentLevelNumber without reading the NEXT level's hardness instead.
        private int lastLevelWinCoinReward;

        // Leaderboard points awarded for the level just won (hearts remaining, 1:1), cached for
        // the same reason as lastLevelWinCoinReward above: the win popup shows it after
        // currentLevelIndex has already moved on to the next level. 0 for Daily Challenge and for
        // levels below GlobalConfig.LevelAttemptUnlockLevel - see the award in HandleLevelWon.
        private int lastLevelEarnedPoints;
        // Assign the same SettingsPopupRoot instance the Settings button opens - used to hide its
        // Quit ("Restart") button while a Daily Challenge level is active (see
        // PauseForSettingsPopup): restarting from Settings there would discard the attempt.
        [SerializeField] private Shared.Settings.SettingsPopupController settingsPopupController;
        [SerializeField] private CatFlapFtueDriver ftueDriver;
        // Only shown while FTUE is active (see ApplyFtueState) - wire its Button.onClick to
        // SkipFtueTutorial in the prefab. Left unassigned, the feature simply has no visible
        // button yet but SkipFtueTutorial itself still works if called directly.
        [SerializeField] private GameObject skipTutorialButtonRoot;
        // Designer-authored FTUE text lives in FtueStepsAsset ScriptableObjects (Shared.Ftue),
        // not in level JSON - this just maps a 1-based level number to its asset.
        [SerializeField] private CatFlapFtueLevelStepsEntry[] ftueLevelSteps = new CatFlapFtueLevelStepsEntry[0];

        [Header("Level Attempt one-time tip - shown once ever, first level Level Attempt turns on")]
        [SerializeField] private string levelAttemptTipDescription = "Lives are lost on wrong moves";

        public UnityEngine.Events.UnityEvent ReturnedToMainMenu = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// Fired only for a deliberate "abandon this attempt" action (Settings Quit) - NOT for
        /// Home (a pause, which must stay resumable/Continue-able) and NOT for Give Up (which
        /// already resolves a real Lost result through the normal LevelFailed path). The Daily
        /// Challenge bridge listens for this specifically instead of the generic
        /// ReturnedToMainMenu, so a Home pause never gets mistaken for an interrupted attempt.
        /// </summary>
        public UnityEngine.Events.UnityEvent DailyChallengeAttemptAbandoned = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// Fired right after RestartLevel() reloads the same Daily Challenge sub-level in place
        /// (Settings Quit already fired DailyChallengeAttemptAbandoned for the attempt being
        /// discarded, which made the bridge unbind its LevelWon/LevelFailed listeners). Without
        /// this, the freshly-restarted attempt's eventual win/lose would never reach
        /// DailyChallengeController/the result popup. The bridge listens for this specifically to
        /// re-bind and mark itself active again for the new attempt.
        /// </summary>
        public UnityEngine.Events.UnityEvent DailyChallengeSessionRestarted = new UnityEngine.Events.UnityEvent();
        [SerializeField] private Shared.Boosters.BoosterManager sharedBoosterManager;
        [SerializeField] private CatFlapHintHighlightAnimator hintHighlightAnimator;
        [SerializeField] private CatFlapAnimationViewAdapter animationView;
        [SerializeField] private CatFlapGameProgressSaveManager progressSaveManager;
        [SerializeField] private CatFlapCurrentLevelBoardSaveManager boardSaveManager;

        // Same boardSaveManager instance/file as regular levels use - this is just the
        // progressionId "row" passed to its progressionIdOverride-aware overloads, so a Daily
        // Challenge board save can never collide with (or wipe, via RemoveAllExcept) the regular
        // campaign level's saved board.
        private const string DailyChallengeProgressionId = "CatFlapDailyChallenge";
        // Free Revive on the Out of Lives popup is a lifetime one-shot - once used, this flag is
        // set forever and the popup falls back to its normal coin/ad revive choice from then on.
        private const string FreeReviveUsedKey = "OutOfLives.FreeReviveUsed";
        // Persisted (survives app restart) UTC timestamp (ISO-8601, same format as
        // PlayerData.CreatedUtc/LastUpdatedUtc) of the last time a level-complete interstitial was
        // shown - see TryShowLevelCompleteInterstitial/IsInterstitialCooldownElapsed. Also updated
        // whenever a rewarded ad is watched successfully (see HandleRewardedAdCompleted), so
        // watching one doesn't get immediately followed by an interstitial too.
        private const string LastInterstitialShownUtcKey = "Ads.LastInterstitialShownUtc";
        [SerializeField] private CatFlapBoosterManager boosterManager;
        [SerializeField] private NotesManager notesManager;
        [SerializeField] private BoosterConfig boosterEconomy = new BoosterConfig();
        // Two genuinely separate checkpoints within ValidateLineAfterDelay's single coroutine (see
        // that method) - correct resolves early, wrong/error always waits the full delay.
        [SerializeField] private float wrongValidationDelaySeconds = 0.75f;
        [SerializeField] private float correctValidationDelaySeconds = 0.4f;
        [SerializeField] private float autosaveIntervalSeconds = 5f;
        [SerializeField] private bool loadOnStart = true;

        private CatFlapRuntimeController controller;
        private AdsManager subscribedAdsManager;
        private InAppManager subscribedInAppManager;
        private GlobalConfig activeConfig = new GlobalConfig().CloneNormalized();
        private GameRulesConfig activeRulesConfig = new GameRulesConfig().CloneNormalized();
        // Keyed per-row and per-column (not per-cell) - a tap starts one independent timer for its
        // row and one for its column, and only a later tap sharing that exact row/column index
        // cancels/restarts its one timer, leaving every other in-flight row/column feedback delay
        // untouched. See CatFlapGameplayManager.Validation.cs.
        private readonly Dictionary<int, Coroutine> rowValidations = new Dictionary<int, Coroutine>();
        private readonly Dictionary<int, Coroutine> columnValidations = new Dictionary<int, Coroutine>();
        private int currentLevelIndex;
        // In-memory cache of the persisted loop-level sequence (see
        // CatFlapGameProgressSaveManager.LoadLoopLevelSequence/ResolveLoopLevelNumber below) -
        // loaded lazily the first time a loop index is actually resolved, since most sessions
        // never play past GlobalConfig.TotalLevels at all.
        private List<int> loopLevelSequence;
        // See LoadedLevelNumber. Set every time GetCurrentLevelAsset resolves an asset.
        private int lastLoadedActualLevelNumber;
        // Per-level-attempt analytics counters (see LogLevelFail/LogOutOfLives's reviveCount/
        // retryCount) - how many times the player has revived/restarted THIS level number since
        // first entering it. Reset only when a genuinely different level number loads (see
        // EnsureAttemptCountersForLevel), never by a restart of the same level, so they keep
        // accumulating across repeated attempts at one stubborn level.
        private int trackedAttemptLevelNumber = -1;
        private int levelReviveCount;
        private int levelRetryCount;
        // Per-attempt analytics counters for level_complete/level_fail (see
        // LevelResultEventData) - unlike levelReviveCount/levelRetryCount above, these reset on
        // EVERY LoadAssignedLevel call (including a same-level restart), not just a genuinely
        // different level - see ResetPerAttemptAnalyticsCounters. Each only ever increments from
        // its own real trigger point (UseHint/UseUndo success, notes usage, a newly-charged rule
        // violation, a newly-detected wrong-sequence line, a tap on a given/locked cell) - see
        // BuildLevelResultEventData for how they're all read back into one event.
        private int attemptHintUsedCount;
        private int attemptUndoUsedCount;
        private int attemptTotalNotesUsed;
        private int attemptTotalNotesApply;
        private int attemptNotesButtonTapCount;
        private int attemptWrongSolutionCount;
        private int attemptRule1BreakCount;
        private int attemptRule2BreakCount;
        private int attemptPrePlacedTileTap;
        // Cumulative mistakes spent THIS ATTEMPT - deliberately NOT derived from
        // (MaxMistakes - RemainingMistakes), since a revive/refill (RefillLevelMistakes) restores
        // RemainingMistakes back up, which would otherwise make lives_used/lives_lost visibly
        // drop back down right after a revive instead of keeping the true running total.
        // Incremented once per HandleMistakeSpent, same as the other attemptXxx counters.
        private int attemptLivesUsedTotal;
        // Set once per LoadAssignedLevel call (see there) - read back by BuildLevelResultEventData
        // for every level_start/level_complete/level_fail/home/restart level event's hard_level.
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
        private bool isApplyingNotes;
        private bool isDailyChallengeSession;
        // Captured in HandleLevelWon at the moment a Daily Challenge sub-level's coin reward is
        // granted, so CatFlapDailyChallengeBridge can feed the real pre-win/reward amounts into
        // CatFlapDailyChallengeWinPopupController.SetCurrentCoin regardless of event subscriber
        // order between this class and the bridge (both react to the same LevelWon event).
        private int dailyChallengeCoinsBeforeWin;
        private int dailyChallengeCoinsWon;
        private int dailyChallengePuzzleIndex = -1;
        private int dailyChallengeSubLevelIndex = -1;
        private System.DateTime? dailyChallengeRestartDate;

        // Caps how many taps an FTUE tile can take before further taps are ignored - 1 on
        // levels 1-2 (empty -> Fish, stop), 2 on level 3 (empty -> Fish -> Cat, stop), 0 (no
        // cap) everywhere else. Set once at load, not tied to ftueDriver.IsActive - see the tap
        // guard in HandleCellSelected.
        private int ftueMaxTapsPerTile;

        // Set true only by SkipFtueTutorial, right before it calls controller.ForceWin() -
        // checked (and reset) at the top of PlayLevelWinCelebrationThenShowPopup so a skip goes
        // STRAIGHT into the next board (or the win popup, for the last FTUE level) with no
        // celebration beat at all, instead of playing the full board-wide win animation
        // (Happy mood, particles, background pulse) first the way a real completion does.
        private bool skippingFtueTutorial;

        public CatFlapRuntimeController Controller => controller;
        public GlobalConfig ActiveConfig => activeConfig;
        public bool NotesMode => notesManager != null && notesManager.IsNotesActive;
        public int CurrentLevelIndex => currentLevelIndex;
        public int CurrentLevelNumber => currentLevelIndex + 1;
        // The actual underlying level number (1-100) GetCurrentLevelAsset last resolved to - equal
        // to CurrentLevelNumber for every level before the loop-level pack kicks in, and the
        // looped/reshuffled level number afterward (see CatFlapLoopLevelSequencer). Surfaced to the
        // cheat panel (CatFlapGameplayManager.Cheat.cs) so "Level 101" can show which real level it
        // actually loaded.
        public int LoadedLevelNumber => lastLoadedActualLevelNumber;
        public int LevelCount => GetLevelCount();
        public bool HasNextLevel => currentLevelIndex + 1 < GetLevelCount();

        // Mirrors the exact condition Start() uses to decide whether to call StartGame()
        // directly instead of ShowMainMenu() - MainMenuHandler.OnEnable() (which runs before
        // ANY Start(), including this one) reads this to skip showing itself in that case,
        // since only its own RequestPlay() click handler normally hides mainMenuUIRoot; going
        // straight to gameplay bypasses that entirely and would otherwise leave Home and
        // Gameplay UI visible/overlapping at once.
        public bool SkipMainMenuOnLaunch => loadOnStart || currentLevelIndex <= 0;

        protected virtual void Awake()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            EnsureButtonClickSfxBinder();
            ResolveNotesManager();
            ApplyGlobalConfig();
            ApplyGameRulesConfig();
            ApplyRulesConfigToViews();

            // Resolved here (Awake) rather than Start() so CurrentLevelNumber is already correct
            // before any other component's OnEnable runs - Unity runs Awake for every object in
            // the scene before OnEnable for any of them, and MainMenuHandler.BindButtons() (which
            // hands CurrentLevelNumber to the Spin Wheel/Daily Reward/Daily Challenge widgets)
            // fires from OnEnable. Resolving late (in Start()) let those widgets read a
            // stale/default level number if their OnEnable ran first. Always resolved now
            // (previously skipped when loadOnStart was true) since Start() also needs the real
            // value below to decide whether to skip Home entirely for a player who hasn't
            // finished Level 1 yet.
            ResolveCurrentLevelIndexFromSave();
            ICatFlapBoardView board = boardView as ICatFlapBoardView;
            controller = new CatFlapRuntimeController(
                new CatFlapLevelRepository(),
                new CatFlapRuntimeSession(),
                board,
                statusView,
                uiFlowManager,
                animationView);
            controller.LevelWon += HandleLevelWon;
            controller.LevelFailed += HandleLevelFailed;
            controller.OutOfMistakes += HandleOutOfMistakes;
            controller.MistakeSpent += HandleMistakeSpent;
            controller.WrongSolutionDetected += HandleWrongSolutionDetected;
            controller.RuleViolationCharged += HandleRuleViolationCharged;
            // Cached so OnDestroy unsubscribes from this EXACT instance via Unity's own
            // destroyed-object null check, rather than re-querying AdsManager.Instance there -
            // that getter lazily creates a brand new AdsManager if none is found, which would be
            // wasteful (and pointless) to trigger during teardown.
            subscribedAdsManager = AdsManager.Instance;
            subscribedAdsManager.RewardedAdCompleted += HandleRewardedAdCompletedForInterstitialCooldown;
            // Same cached-instance reasoning as subscribedAdsManager above.
            subscribedInAppManager = InAppManager.Instance;
            subscribedInAppManager.UnattendedPurchaseGranted += HandleUnattendedPurchaseGranted;
            ConfigureStore();
            BindCheatPanel();
            BindSharedBoosterEvents();
            BindOutOfLivesPopupEvents();

            if (boardView is CatFlapBoardViewAdapter uiBoardView)
            {
                uiBoardView.CellSelected += HandleCellSelected;
            }
            else if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                spriteBoardView.CellSelected += HandleCellSelected;
            }
        }

        // Coins granted for winning levelNumber: a Daily Challenge sub-level uses its own config
        // value, a hard level (GlobalConfig.HardLevels) pays EconomyConfigController.HardLevelWinCoins,
        // everything else pays EconomyConfigController.LevelWinCoins (both Remote Config-backed,
        // see Shared.RemoteConfig).
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

        private void PlayBoardEnterAnimation(System.Action onComplete, bool skipHudPopIn = false)
        {
            // Every regular level load/restart and Daily Challenge load/restart funnels through
            // this one method, so the HUD's top-bar pop-in replays alongside the board every time,
            // not just once per session. FTUE levels pass skipHudPopIn: true since Settings/
            // Info/Coins/Rules are already hidden for the whole level (see ApplyFtueState) - only
            // the board's own tile pop-in wave below should play for them.
            if (!skipHudPopIn)
            {
                statusView?.PlayEntryAnimation();
            }

            if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                spriteBoardView.PlayEnterAnimation(onComplete);
                return;
            }

            onComplete?.Invoke();
        }

        [Header("Level Start Entrance Animation")]
        // Shared head-start before ANY in-game HUD element (top bar, life panel, rules, booster
        // panel) starts moving - the board's own entrance animation (PlayBoardEnterAnimation)
        // deliberately ignores this and starts immediately, see PlayLevelStartEntranceSequence.
        [SerializeField] private float hudEntranceInitialDelaySeconds = 0.15f;
        [SerializeField] private float boosterPanelSlideDurationSeconds = 0.3f;
        [SerializeField] private float boosterPanelSlideDistance = 220f;

        // Slides the booster panel up from below its resting position, continuing the same
        // "top HUD -> life -> rules -> booster" start-time stagger CatFlapStatusViewAdapter uses
        // for its own three groups (booster is the 4th, so its start delay is 3 gaps in) - a
        // no-op (completes immediately) when it's hidden entirely (see ApplyFtueState, which
        // deactivates it for the whole of an FTUE level), so this never plays for a panel nobody
        // can see anyway.
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
            // Positioned off-screen immediately, before the stagger delay, so it never flashes at
            // its resting position while waiting for its turn to start.
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

        // Kicks off the level-start entrance beat: top HUD, then life panel, then rules, then
        // booster panel each START in that order with a small stagger gap between them (see
        // CatFlapStatusViewAdapter.PlayHudEntranceAnimation/EntranceGroupStaggerSeconds and
        // PlayBoosterPanelEntranceAnimation above) - none of them wait for an earlier one to
        // FINISH before starting. The board plays its own entrance animation independently
        // alongside all of this. Only once every group has completed does an FTUE level's
        // tutorial actually pop in (a non-FTUE level's one-time "Beware!" hearts tip, if due,
        // shows instead) and gameplay input turns back on - otherwise a player could start
        // tapping tiles mid-animation, or the FTUE spotlight could appear pointing at a HUD
        // element that's still sliding into place.
        private void PlayLevelStartEntranceSequence(bool ftueActive, List<FtueStepData> ftueSteps, int ftueWalkthroughStepCount)
        {
            // FTUE levels skip only the HUD-side entrance animation (top bar slide, life panel
            // slide, rules panel scale, booster panel slide) - Settings/Info/Coins/Rules and the
            // booster panel are already hidden outright for these levels (see ApplyFtueState), so
            // animating them (or the still-visible level-number/hearts group) in is pure
            // cosmetic noise, and would otherwise visibly collide with the FTUE board-slide
            // transition, which reloads the next level while the board sits off-screen mid-slide
            // (see PlayFtueLevelTransition). The board's OWN entrance animation (tile pop-in
            // wave) still plays regardless - only the HUD is skipped.
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
                    FinishLevelStartEntrance(ftueActive, ftueSteps, ftueWalkthroughStepCount);
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

            // Deliberately not delayed by hudEntranceInitialDelaySeconds - the board animates in
            // immediately, independent of the HUD's own head-start.
            PlayBoardEnterAnimation(GroupComplete, skipHudPopIn: ftueActive);
        }

        private void FinishLevelStartEntrance(bool ftueActive, List<FtueStepData> ftueSteps, int ftueWalkthroughStepCount)
        {
            // The one place a booster's unlock celebration is allowed to play - only now, once
            // this level's whole entrance sequence (board startup animation included) has actually
            // finished. currentLevelIndex advances the instant a win is recorded (HandleLevelWon),
            // well before the next level starts, so every OTHER RefreshAllBoosterUI() call in
            // between (level win HUD refresh, hint/fix/undo, popups closing, etc.) must not
            // trigger it - see BoosterButtonUI.Render's allowUnlockAnimation.
            sharedBoosterManager?.RefreshAllBoosterUI(allowUnlockAnimation: true);

            if (ftueActive)
            {
                // ApplyFtueState already configured everything else (Settings button/booster
                // panel visibility) synchronously at level load - only the FTUE module's actual
                // spotlight/hand pop-in waits for the entrance animation to finish.
                ftueDriver?.Configure(true, ftueSteps, controller?.Session, ftueWalkthroughStepCount,
                    ResolveFtueRulesWalkthrough(CurrentLevelNumber), statusView);
            }
            else
            {
                // Only shown when the board-cell tutorial isn't also running this level - they'd
                // otherwise fight over the same shared spotlight/banner.
                ShowLevelAttemptTipIfNeeded();
            }

            // pendingHardLevelIntroThisLoad was decided back in LoadAssignedLevel, before the HUD/
            // coin entrance animation even started - true for any hard, non-Daily-Challenge level.
            // The popup plays on EVERY entry to one (restarts and repeat visits included); the HUD
            // badge/label were already turned hard back in LoadAssignedLevel either way.
            if (pendingHardLevelIntroThisLoad && hardLevelPopupController != null)
            {
                // pendingOnComplete fires exactly once the full open->wait->close animation
                // actually finishes, so gameplay input only enables once the popup is truly done.
                hardLevelPopupController.pendingOnComplete = () => StartCoroutine(EnableGameplayInputNextFrame());
                hardLevelPopupController.Show();
            }
            else
            {
                StartCoroutine(EnableGameplayInputNextFrame());
            }
        }

        private void ConfigureStore()
        {
            storeManager?.Configure(
                storeConfigAsset != null ? storeConfigAsset.ToConfig() : new StoreConfig(),
                GrantStoreCoins,
                () => controller.Session.BoosterWallet?.CurrentCoins ?? 0);
            uiFlowManager?.StorePopup?.Configure(storeManager);
            shopPage?.Configure(storeManager);

            // Unlike StorePopupController (bound on every OpenStorePopup - see BindStorePopupEvents),
            // ShopPageController isn't a popup that opens/closes, so its PurchaseRequested only
            // needs binding once, here, right after Configure. Previously this was never subscribed
            // to at all, so tapping Buy on the Shop page fired the event into the void - the store
            // popup granted coins, the Shop page silently did nothing.
            if (shopPage != null)
            {
                shopPage.PurchaseRequested.RemoveListener(HandleStorePurchaseRequested);
                shopPage.PurchaseRequested.AddListener(HandleStorePurchaseRequested);
            }
        }

        protected virtual void Start()
        {
            // Played unconditionally, before the branch below decides Home vs straight-to-gameplay,
            // so it's already playing "from the start" no matter which screen actually ends up
            // showing first - one single loop for the whole game (Menu and Gameplay alike), never
            // restarted again. StartGame() deliberately does NOT call PlaySound(GamePlayBg) itself
            // any more - pressing Play from the Menu just lets this same already-running loop carry
            // straight into gameplay instead of cutting and restarting it. AudioManager.PlayAudio
            // no-ops this like any other sound while PlayerData.IsMusicOn is off.
            PlaySound(global::SoundName.GamePlayBg);

            // A first-time player (or anyone who hasn't finished Level 1 yet -
            // currentLevelIndex only advances past 0 the instant a win is recorded, see
            // HandleLevelWon/SaveProgress) drops straight into gameplay instead of Home,
            // regardless of the loadOnStart Inspector toggle - Home only makes sense once
            // there's at least one completed level's worth of progress/economy to show off.
            if (loadOnStart || currentLevelIndex <= 0)
            {
                StartGame();
            }
            else
            {
                // Already resolved in Awake() - see the comment there.
                ShowMainMenu();
            }
        }

        protected virtual void Update()
        {
            // Also gated on GameTransitionOverlay.IsTransitioning - without it, GameState.Current
            // (e.g. Won, while the win/out-of-lives popup's own flag isn't set for this state)
            // keeps mapping to the same ShowState(...) call every frame for as long as the
            // overlay covers the screen, re-activating whatever page it swaps to and undoing a
            // transition's own hideOldPage step before revealNewPage ever runs.
            if (!settingsPopupOpen && !outOfLivesPopupOpen && !buyBoosterPopupOpen && !storePopupOpen &&
                !Shared.GameFlow.GameTransitionOverlay.IsTransitioning)
            {
                controller?.Tick(Time.deltaTime);
            }

            TickAutosave();
        }

        protected virtual void OnDestroy()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            StopDelayedValidation();
            SaveAll();
            if (boardView is CatFlapBoardViewAdapter uiBoardView)
            {
                uiBoardView.CellSelected -= HandleCellSelected;
            }
            else if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                spriteBoardView.CellSelected -= HandleCellSelected;
            }

            if (controller != null)
            {
                controller.LevelWon -= HandleLevelWon;
                controller.LevelFailed -= HandleLevelFailed;
                controller.OutOfMistakes -= HandleOutOfMistakes;
                controller.MistakeSpent -= HandleMistakeSpent;
                controller.WrongSolutionDetected -= HandleWrongSolutionDetected;
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

            if (notesManager != null)
            {
                notesManager.NotesModeChanged.RemoveListener(HandleNotesModeChanged);
                notesManager.ApplyAllRequested.RemoveListener(ApplyAllNotes);
            }

            UnbindCheatPanel();
            UnbindSharedBoosterEvents();
            controller = null;
        }

        protected virtual void OnApplicationQuit()
        {
            StopDelayedValidation();
            controller?.CancelActiveSequence();
            SaveAll();
        }

        protected virtual void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                StopDelayedValidation();
                controller?.CancelActiveSequence();
                SaveAll();
            }
        }

        // isContinueFromWin: true only when reached via LoadNextLevel (the win popup's Continue
        // button) - reported as LevelStart's IsContinue param, distinguishing "kept playing
        // forward through progression" from every other entry (Home Play button, a restart).
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

            TextAsset selectedLevel = GetCurrentLevelAsset();
            cheatPanel?.SetLoopLevelReadout(CurrentLevelNumber, lastLoadedActualLevelNumber);
            // Set BEFORE LoadLevel below (whose RenderStatus fires the first SetLevel) so the HUD
            // never briefly shows the loaded asset's own number. Once loop levels start,
            // lastLoadedActualLevelNumber replays 1-100 while CurrentLevelNumber keeps climbing -
            // the player must see the latter.
            statusView?.SetDisplayLevelNumber(CurrentLevelNumber);
            if (selectedLevel == null)
            {
                statusView?.ShowLoadErrors(new[] { "No Cat Flap level JSON or level sequence assigned." });
                return;
            }

            CatFlapRuntimeLevelLoadResult result = controller.ParseLevelJson(selectedLevel.text);
            if (!result.IsLoaded)
            {
                statusView?.ShowLoadErrors(result.Errors);
                RefreshLevelFlowControls();
                return;
            }

            // Resets the per-attempt revive/retry counters only when this is genuinely a
            // different level than last tracked - a restart of the SAME level (RestartLevel also
            // funnels through here) must NOT reset them, or LogLevelFail/LogOutOfLives would
            // always report 0 even on someone's fifth straight retry of a hard level.
            EnsureAttemptCountersForLevel(CurrentLevelNumber);
            // Unlike revive/retry above, these DO reset on every load, including a same-level
            // restart - see the fields' own comment.
            ResetPerAttemptAnalyticsCounters();

            ApplySavedCoinBalance(boosterEconomy);
            ApplyRulesConfigToViews();
            int levelMistakeLimit = ResolveLevelMistakeLimit(result.Level);
            // Set before LoadLevel (not after) so the very first RenderStatus it triggers
            // internally already reflects this level's Level Attempt state - otherwise the
            // hearts HUD's first layout pass still reflects the PREVIOUS level, and reading its
            // on-screen rect for the "Beware!" tip spotlight (right below) picks up a stale
            // position/size.
            controller.LevelAttemptFeatureEnabled = IsLevelAttemptFeatureUnlocked(CurrentLevelNumber);
            // Same "set before LoadLevel" reasoning - suppresses red rule-error rendering
            // board-wide (see CatFlapRuntimeController.SuppressWrongIndication) for the whole of
            // levels 1-3, not just the tapped cell, so a rule that goes transiently wrong
            // mid-way through a guided multi-tap FTUE cell never paints red.
            controller.SuppressWrongIndication = CurrentLevelNumber <= 3;
            // Same reasoning, for the whole FTUE range (1..FtueEnabledUntilLevel, today 1-4) -
            // the Appreciation Text callout popup is an unrelated distraction during the guided
            // tutorial; it first appears starting the level right after FTUE ends (today,
            // level 5).
            controller.SuppressAppreciationText = IsFtueLevel(CurrentLevelNumber);
            controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
            ApplyCompletedLineFlipTuning();
            List<FtueStepData> ftueSteps = ResolveFtueSteps(CurrentLevelNumber);
            bool ftueActive = IsFtueLevel(CurrentLevelNumber) && ftueSteps != null && ftueSteps.Count > 0;
            ApplyFtueState(ftueActive, ftueSteps);
            statusView?.SetFtueLevelIndicator(CurrentLevelNumber, ftueActive);
            ftueMaxTapsPerTile = ftueActive ? ResolveFtueMaxTapsPerTile(CurrentLevelNumber) : 0;
            int ftueWalkthroughStepCount = ResolveFtueWalkthroughStepCount(CurrentLevelNumber);
            // Notes Mode (and its Apply All availability) always resets to OFF on a fresh level.
            notesManager?.SetNotesActive(false);
            RefreshApplyAllAvailability();
            if (result.IsLoaded)
            {
                progressSaveManager?.ApplySavedBoosterState(controller.Session.BoosterWallet);

                // Cached (not just used inline here) so BuildLevelResultEventData can reuse it
                // later for level_complete/level_fail/home/restart level, which now share the
                // exact same "hard_level" field as level_start.
                currentLevelIsHard = string.Equals(result.Level.Difficulty, "Hard", System.StringComparison.OrdinalIgnoreCase);

                // Single choke point for every regular-level load (fresh start from Home, restart,
                // and Continue from the win popup all funnel through here) - was previously only
                // logged from StartGame(), so every level reached via LoadNextLevel/RestartLevel
                // (i.e. every level after the very first one in a session) never logged a
                // LevelStart at all despite each one logging its own LevelComplete. Placed here,
                // not earlier in this method, so the coin balance and hint booster charge count
                // it reads are both already fully restored for THIS level.
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

                // A save made while Out of Lives was showing (0 remaining mistakes) but never
                // resolved (Refill/Watch Ad/Give Up) - the player just quit instead. Treat that
                // exactly like Give Up would have: wipe the stale save and reload this level
                // fresh, rather than resuming with 0 lives and live input still enabled. Only
                // applies when the Level Attempt feature is actually unlocked - that popup can
                // never have shown otherwise, so 0 remaining mistakes here is not "stale".
                if (restoredBoard && controller.LevelAttemptFeatureEnabled &&
                    controller.Session.LevelMistakeLimit != null &&
                    controller.Session.LevelMistakeLimit.RemainingMistakes <= 0)
                {
                    boardSaveManager.Clear(controller.Session);
                    controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
                    restoredBoard = false;
                }

                if (restoredBoard)
                {
                    controller.RefreshValidationVisuals(suppressValueChangeAnimation: true);
                    // A restored board can bring back tentative notes, so Apply All availability
                    // needs to be recomputed from the restored tentative count.
                    RefreshApplyAllAvailability();
                    // Undo history is never persisted - it only lives in boosterManager's
                    // in-memory stack, which is empty on any fresh load (a full Editor Play-mode
                    // restart, not just Home->Play). Rebuild it from the just-restored board so
                    // previously-placed cells (including wrong ones from notes/Apply All) are
                    // undoable again, then re-render: Configure() above already rendered the
                    // booster buttons once, but that ran before the board was restored, so
                    // Undo's availability was evaluated against the still-blank fresh-loaded
                    // board and never re-rendered.
                    boosterManager?.RebuildUndoHistoryFromBoard();
                    sharedBoosterManager?.RefreshAllBoosterUI();
                }

                controller.RefreshViews(suppressValueChangeAnimation: true);
                SaveProgress();

                // Decided here, BEFORE the HUD/coin entrance animation below starts, not inside
                // FinishLevelStartEntrance (which only runs once that whole animation finishes).
                // Daily Challenge sub-levels never touch CurrentLevelNumber/HardLevels progression -
                // same exclusion used elsewhere in this class (LevelAttemptFeatureEnabled,
                // SuppressWrongIndication, interstitial gating).
                bool isHardLevel = !isDailyChallengeSession && IsHardLevel(CurrentLevelNumber);
                if (isHardLevel)
                {
                    // The HUD badge/label turn hard right here, from the start of every hard
                    // level - deliberately NOT deferred to the popup's own OnMoveToTargetArrived,
                    // so they pop in together with the coin/HUD entrance animation right below and
                    // are correct even while the popup is still playing over them.
                    statusView?.EnableHardLevelUI();
                }

                pendingHardLevelIntroThisLoad = isHardLevel;

                PlayLevelStartEntranceSequence(ftueActive, ftueSteps, ftueWalkthroughStepCount);
            }

            RefreshLevelFlowControls();
        }

        /// <summary>
        /// Reads a Daily Challenge sub-level's saved board (if any) and returns how far the
        /// player got through it last time, as a percent of its empty (non-clue) cells correctly
        /// filled in - see CatFlapLevelProgressCalculator. Read-only: parses the level JSON and
        /// reads the save file directly, without loading the level into the runtime session, so
        /// it's safe to call for a day/sub-level that isn't the one currently being played (e.g.
        /// every button in the Daily Challenge calendar). Returns false (0%) when never attempted
        /// - callers should show "not started" rather than a stuck 0%.
        /// </summary>
        public bool TryGetDailyChallengeProgressPercent(int puzzleIndex, int subLevelIndex, out float progressPercent)
        {
            progressPercent = 0f;
            if (boardSaveManager == null)
            {
                return false;
            }

            string levelName = $"dc_{puzzleIndex + 1}_{subLevelIndex + 1}";
            TextAsset levelAsset = Resources.Load<TextAsset>($"DailyChallengeLevels/{levelName}");
            if (levelAsset == null)
            {
                return false;
            }

            CatFlapRuntimeLevelLoadResult result = new CatFlapLevelRepository().LoadFromJson(levelAsset.text);
            if (!result.IsLoaded)
            {
                return false;
            }

            if (!boardSaveManager.TryReadSavedBoardValues(result.Level.Id, DailyChallengeProgressionId, out CatFlapCurrentLevelBoardSaveManager.SavedBoardValues saved))
            {
                return false;
            }

            progressPercent = CatFlapLevelProgressCalculator.Calculate(result.Level, saved.Size, saved.Values, saved.Notes).ProgressPercent;
            return true;
        }

        /// <summary>
        /// Loads a Daily Challenge level: resolves (puzzleIndex, subLevelIndex) - both 0-based, as
        /// handed over by Shared.DailyChallenges.DailyChallengeController/DailyChallengeData - to
        /// a level JSON named "dc_{puzzleIndex + 1}_{subLevelIndex + 1}" (e.g. "dc_1_1", "dc_1_2",
        /// "dc_1_3" for the first day's three sub-levels) under
        /// Assets/Shared/Features/DailyChallenges/Resources/DailyChallengeLevels/, loaded directly
        /// via Resources.Load at runtime - no array to pre-populate in the Inspector, same as any
        /// other Resources-based content. Plays it through the normal Cat Flap runtime. Unlike
        /// <see cref="LoadAssignedLevel"/>, this does not touch the regular per-level board
        /// save/restore or currentLevelIndex - Daily Challenge progress (win/lose per sub-level)
        /// is tracked entirely by Shared.DailyChallenges.DailyChallengeController, not by this
        /// game's normal save slot.
        /// </summary>
        public bool LoadDailyChallengeLevel(int puzzleIndex, int subLevelIndex, System.DateTime? challengeDate = null)
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (controller == null)
            {
                Debug.LogWarning("CatFlapGameplayManager.LoadDailyChallengeLevel: controller is null (Awake hasn't run yet?).");
                return false;
            }

            string levelName = $"dc_{puzzleIndex + 1}_{subLevelIndex + 1}";
            TextAsset levelAsset = Resources.Load<TextAsset>($"DailyChallengeLevels/{levelName}");
            if (levelAsset == null)
            {
                Debug.LogWarning($"CatFlapGameplayManager.LoadDailyChallengeLevel: Resources.Load(\"DailyChallengeLevels/{levelName}\") returned null - check the file is under a Resources folder and named exactly '{levelName}.json'.");
                statusView?.ShowLoadErrors(new[] { $"Daily Challenge level '{levelName}' was not found." });
                return false;
            }

            CatFlapRuntimeLevelLoadResult result = controller.ParseLevelJson(levelAsset.text);
            if (!result.IsLoaded)
            {
                Debug.LogWarning($"CatFlapGameplayManager.LoadDailyChallengeLevel: ParseLevelJson failed for '{levelName}': {string.Join(", ", result.Errors ?? new string[0])}");
                statusView?.ShowLoadErrors(result.Errors);
                return false;
            }

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
            // Daily Challenge levels aren't part of normal level progression, so the Level Attempt
            // feature always behaves as fully unlocked here rather than being gated by
            // CurrentLevelNumber. Set before LoadLevel for the same reason as the regular level
            // path above - so the first RenderStatus/HUD layout it triggers is already correct.
            controller.LevelAttemptFeatureEnabled = true;
            // Same reasoning - FTUE (and its wrong-answer suppression, plus the Appreciation Text
            // suppression) never applies to Daily Challenge levels.
            controller.SuppressWrongIndication = false;
            controller.SuppressAppreciationText = false;
            controller.LoadLevel(result.Level, boosterEconomy, levelMistakeLimit);
            ApplyCompletedLineFlipTuning();
            ApplyFtueState(false, null);
            ftueMaxTapsPerTile = 0;
            notesManager?.SetNotesActive(false);
            RefreshApplyAllAvailability();
            progressSaveManager?.ApplySavedBoosterState(controller.Session.BoosterWallet);

            // The undo stack is a persistent in-memory Stack that otherwise survives across
            // whatever was previously loaded - without this, a stale entry from a
            // differently-sized board (e.g. arriving here straight from a regular level) throws
            // when its row/column is checked against this level's board bounds.
            boosterManager?.ClearUndoHistory();
            boosterManager?.Initialize(controller, () => CurrentLevelNumber);
            sharedBoosterManager?.SetUndoAvailabilityProvider(() => boosterManager != null && boosterManager.CanUndo());
            BindSharedBoosterEvents();
            sharedBoosterManager?.ApplyRulesConfig(activeRulesConfig);
            sharedBoosterManager?.Configure(controller.Session.BoosterWallet, () => CurrentLevelNumber);
            ConfigureHintHighlight();

            // Same Level Progression mechanism regular levels use, just a separate save-manager
            // instance/row so an in-progress Daily Challenge board never collides with the
            // regular campaign level's saved board - restores this sub-level's board if the
            // player quit mid-puzzle and is now resuming the same (puzzleIndex, subLevelIndex).
            bool restoredBoard = boardSaveManager != null && boardSaveManager.TryRestore(controller.Session, DailyChallengeProgressionId);
            if (restoredBoard)
            {
                controller.RefreshValidationVisuals(suppressValueChangeAnimation: true);
                RefreshApplyAllAvailability();

                // Undo history was just cleared above - rebuild it from the just-restored board
                // so previously-placed cells are undoable again (same as the regular resume path).
                boosterManager?.RebuildUndoHistoryFromBoard();
                sharedBoosterManager?.RefreshAllBoosterUI();
            }

            controller.RefreshViews(suppressValueChangeAnimation: true);
            // Daily Challenge sub-levels never touch CurrentLevelNumber/HardLevels progression -
            // see the matching check in LoadAssignedLevel.
            pendingHardLevelIntroThisLoad = false;
            PlayLevelStartEntranceSequence(ftueActive: false, ftueSteps: null, ftueWalkthroughStepCount: 0);
            return true;
        }

        public void ShowMainMenu()
        {
            ReturnToMainMenu();
        }

        public void SaveAndShowMainMenu()
        {
            // Home button click - also reused by CatFlapDailyChallengeBridge's own back-navigation
            // (not a fail), so this only actually logs LevelFail when the level genuinely failed or
            // ran out of mistakes (IsCurrentLevelFailedOrOutOfLives already excludes DC entirely).
            if (IsCurrentLevelFailedOrOutOfLives())
            {
                AnalyticsManager.Instance.LogLevelFail(BuildLevelResultEventData());
            }

            SaveAll();
            ReturnToMainMenu();
        }

        public void PauseForSettingsPopup()
        {
            StopDelayedValidation();
            settingsPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            bool inNormalLevelGameplay = !isDailyChallengeSession &&
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Playing;
            settingsPopupController?.SetQuitButtonVisible(inNormalLevelGameplay);
            SaveAll();
        }

        public void ResumeFromSettingsPopup()
        {
            settingsPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Playing;

            // Wired to the Settings popup's own PopupClosed event (plain Close/X, not the
            // separate Home/Quit exits, which already log their own distinct events) - reads the
            // toggle states straight back from PlayerData, which SettingsPopupController.
            // SaveSettings already kept in sync with whatever the player just changed.
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            AnalyticsManager.Instance.LogSettingUi(CurrentLevelNumber, data.IsMusicOn, data.IsSoundOn, data.IsHapticOn);
        }

        // Called by LevelWinPopupController.OnHomeClicked - kept as a wrapper (rather than making
        // BuildLevelResultEventData itself public) so the shared popup script never needs to know
        // how that snapshot is actually built.
        public void LogHomeFromWinPopup()
        {
            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: false, levelWin: true, placement: AnalyticsEvents.HomePlacementValues.Win);
        }

        public void SettingsHome()
        {
            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: true, levelWin: false, placement: AnalyticsEvents.HomePlacementValues.InGame);
            SaveAll();

            // Home is a pause, not an abandon - the board (regular or Daily Challenge) must stay
            // resumable, same as it already was for regular levels. Only the explicit "abandon
            // this attempt" actions (Settings Quit, Give Up) clear the saved board. FTUE levels
            // 1-4 are the one exception to "resumable" - see SaveCurrentBoard, which clears
            // rather than persists their board on every save (Home included), so an in-progress
            // guided tap sequence is never re-entered partway through on next entry. Doesn't
            // extend to level 8 (Level Attempt tutorial highlight) or any other level.

            // The undo stack is a persistent in-memory Stack (CatFlapBoosterController) that
            // otherwise survives across whatever gets loaded next - stale entries referencing a
            // differently-sized board (e.g. leaving a regular level to play Daily Challenge, or
            // vice versa) would otherwise throw when checked against the new board's bounds.
            boosterManager?.ClearUndoHistory();

            // SettingsPopupController already hides itself (SetVisible(false)) before invoking
            // HomeRequested/QuitRequested, so there's no separate popup to close here - the
            // overlay's fade-in still covers the otherwise-uncovered board while it clears and
            // the main menu's own entry animation plays underneath as the overlay fades back out.
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => controller?.ClearBoardView(),
                revealNewPage: ReturnToMainMenu);
        }

        // Wired to the Settings popup's "Quit" button (QuitRequested UnityEvent) - now restarts
        // the current level in place instead of abandoning it back to the main menu.
        // RestartLevel() already clears the board save, undo history, and saves progress.
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
            StopDelayedValidation();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
            // GamePlayBg is already playing from Start() (one single loop for Menu and Gameplay
            // alike) - not re-triggered here, or pressing Play from the Menu would cut and restart
            // the already-running loop instead of carrying it straight through.
            ResolveCurrentLevelIndexFromSave();
            // LogLevelStart now fires from inside LoadAssignedLevel itself (see there) - covers
            // this fresh-from-Home entry plus Restart/Continue, instead of only this one.
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
            StopDelayedValidation();
            settingsPopupOpen = false;
            outOfLivesPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (controller == null)
            {
                return;
            }

            controller.ShowMainMenu();
            ReturnedToMainMenu.Invoke();
            RefreshLevelFlowControls();
        }

        public void ApplyAllNotes()
        {
            if (isApplyingNotes ||
                !Shared.GameFlow.InputManager.GamePlayInputEnabled ||
                controller == null ||
                !controller.Session.HasLevel ||
                controller.IsSequencing)
            {
                return;
            }

            // Guard against duplicate execution from rapid repeated presses - Apply All must run
            // as a single atomic batch, never overlap with another in-flight batch.
            isApplyingNotes = true;
            bool moveChanged = controller.ApplyAllNotes(out IReadOnlyList<CatFlapCellPosition> appliedPositions);

            if (!moveChanged)
            {
                isApplyingNotes = false;
                return;
            }

            attemptTotalNotesApply += appliedPositions.Count;

            if (boosterManager != null)
            {
                // Apply All only increments the shared move counter once for the whole batch, so
                // only the first recorded position is flagged as move-counted. Pushing RecordMove
                // once per position IN SELECTION ORDER makes the undo stack (which pops
                // last-pushed-first) reverse in last-selected-first order with zero changes to
                // UseUndo()/CanUndo().
                for (int index = 0; index < appliedPositions.Count; index++)
                {
                    CatFlapCellPosition position = appliedPositions[index];
                    boosterManager.RecordMove(position.Row, position.Column, index == 0);
                }
            }

            // Same immediate placement feedback a normal tap plays right after committing (see
            // HandleCellSelected) - one shared cue for the whole batch, never once per tile. Each
            // affected row/column's own eventual Complete/Rule Break/Wrong Solution outcome is
            // decided independently below by StartDelayedValidation, exactly like a real tap -
            // Apply All no longer decides that outcome itself.
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Light);
            PlayCellTapSound(appliedPositions[0].Row, appliedPositions[0].Column);

            SaveAll();
            sharedBoosterManager?.RefreshAllBoosterUI();
            RefreshApplyAllAvailability();
            // Apply All commits every tentative note in one action - notes mode has nothing left
            // to do afterward, so it turns itself off rather than leaving the player in notes mode
            // with an empty board of notes.
            notesManager?.SetNotesActive(false);
            ftueDriver?.Refresh();

            // Starts the exact same per-row/per-column delayed-validation timer a real tap starts
            // (see StartDelayedValidation) for every cell this batch committed, in the same order
            // they were noted - a later position sharing an earlier one's row/column simply
            // restarts that line's still-pending timer, identical to tapping each cell in this
            // same order for real. This is what makes Apply All's eventual outcome (Complete/Rule
            // Break/Wrong Solution, life cost, sound, reveal animation) resolve through precisely
            // the same code path and timing as a normal tap, instead of a separate batch-only rule.
            for (int index = 0; index < appliedPositions.Count; index++)
            {
                CatFlapCellPosition position = appliedPositions[index];
                StartDelayedValidation(position.Row, position.Column);
            }

            isApplyingNotes = false;
        }

        /// <summary>
        /// While a hint is active, only the hint's Apply button and the hinted cell itself should
        /// take input - every other booster/notes button (Undo, Fix, Hint, Notes toggle, Apply
        /// All) lives under this CanvasGroup, so blocking it here covers all of them without
        /// touching their individual scripts.
        /// </summary>
        private void SetBoosterPanelInputEnabled(bool enabled)
        {
            if (boosterPanelCanvasGroup == null)
            {
                return;
            }

            boosterPanelCanvasGroup.interactable = enabled;
            boosterPanelCanvasGroup.blocksRaycasts = enabled;
        }

        public void ApplyActiveHint()
        {
            sharedBoosterManager?.HideBoosterInfo();
            hintBannerController?.Hide();
            hintSpotlightController?.Hide();
            hintApplyButton?.gameObject.SetActive(false);
            SetBoosterPanelInputEnabled(true);

            if (controller == null || !controller.ApplyHint(out CatFlapCellPosition position))
            {
                return;
            }

            boosterManager?.RecordMove(position.Row, position.Column, true);
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Rigid);
            PlayCellTapSound(position.Row, position.Column);
            if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                spriteBoardView.PlayHintParticle(position.Row, position.Column);
            }

            SaveAll();
            sharedBoosterManager?.RefreshAllBoosterUI();
            RefreshApplyAllAvailability();
            StartDelayedValidation(position.Row, position.Column);
        }

        private void RefreshApplyAllAvailability()
        {
            notesManager?.SetApplyAllAvailable(
                controller != null && controller.Session.HasLevel && controller.Session.TentativeNoteCount >= 1);
        }

        public void SetNotesMode(bool enabled)
        {
            notesManager?.SetNotesActive(enabled);
        }

        public void ToggleNotesMode()
        {
            attemptNotesButtonTapCount++;
            notesManager?.ToggleNotes();
        }

        public void ClearBoard()
        {
            StopDelayedValidation();
            boardSaveManager?.Clear(controller?.Session);
            boosterManager?.ClearUndoHistory();
            controller?.Clear();
            notesManager?.SetNotesActive(false);
            RefreshApplyAllAvailability();
            SaveAll();
        }

        public void RestartLevel()
        {
            StopDelayedValidation();
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

            // Restart button click (Level Fail popup, or Out-of-Lives popup via
            // PerformOutOfLivesRestart) - only actually logs LevelFail when the level was in a
            // fail/out-of-mistakes state, so a normal Settings-popup Quit mid-play (which also
            // routes through here) never fires it. levelRetryCount itself still increments on
            // EVERY restart regardless, as a plain "how many times has this level been retried"
            // counter - EnsureAttemptCountersForLevel (inside LoadAssignedLevel below) never resets
            // it for a restart of this same level number.
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
            StopDelayedValidation();

            // currentLevelIndex was already advanced past the just-won level in HandleLevelWon,
            // so this just needs to check that index is still a valid level to load - not
            // HasNextLevel, which would be asking whether a level exists past this one.
            if (currentLevelIndex >= GetLevelCount())
            {
                statusView?.ShowLoadErrors(new[] { "No next Cat Flap level is available." });
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

        public void ValidateNow()
        {
            StopDelayedValidation();
            controller?.ValidateBoard();
            SaveAll();
        }

        public void UseHint()
        {
            if (IsDuplicateBoosterUse(BoosterType.Hint))
            {
                return;
            }

            StopDelayedValidation();
            CatFlapCellRevealResult? result = controller?.RevealHint();
            if (result.HasValue && result.Value.Success)
            {
                attemptHintUsedCount++;
                // Highlight-only: the hint never places/mutates the target cell, so there is
                // nothing to validate yet - the player still has to tap it themselves, which
                // will run through the normal SelectCell -> StartDelayedValidation path.
                VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Rigid);
                PlaySound(global::SoundName.HintUsed);
                statusView?.ShowHintResult(result.Value, controller.Session.LastHintStars, controller.Session.LastHintDifficultyRemoved);
                hintBannerController?.Show(result.Value.Value, result.Value.ReasonKey);
                hintSpotlightController?.Show(result.Value.Row, result.Value.Column);
                hintApplyButton?.gameObject.SetActive(true);
                SetBoosterPanelInputEnabled(false);
                SaveAll();
            }
            else if (result.HasValue)
            {
                // No charge deducted for a failed hint (no candidate found, or a correct-note
                // "commit it" case) - still surface the message so the player isn't left guessing.
                statusView?.ShowHintResult(result.Value, 0, 0);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveCurrentBoard();
        }

        public void UseUndo()
        {
            if (IsDuplicateBoosterUse(BoosterType.Undo))
            {
                return;
            }

            StopDelayedValidation();
            if (boosterManager == null)
            {
                statusView?.ShowLoadErrors(new[] { "BoosterManager is not assigned." });
                SaveAll();
                return;
            }

            sharedBoosterManager?.HideBoosterInfo();
            hintBannerController?.Hide();
            hintSpotlightController?.Hide();
            hintApplyButton?.gameObject.SetActive(false);
            SetBoosterPanelInputEnabled(true);
            // Undo before the player ever tapped the hinted cell cancels the hint outright -
            // refund the spent charge, not just clear the highlight (normal dismissal elsewhere
            // still never refunds).
            controller?.DismissActiveHint(refundCharge: true);
            BoosterUseResult result = boosterManager.UseUndo();
            statusView?.ShowBoosterResult(result);
            if (result.Success)
            {
                attemptUndoUsedCount++;
                PlaySound(global::SoundName.UndoUsed);
                controller?.RefreshValidationVisuals();
                SaveAll();
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            RefreshApplyAllAvailability();
            SaveCurrentBoard();
        }

        public void UseFix()
        {
            if (IsDuplicateBoosterUse(BoosterType.Fix))
            {
                return;
            }

            StopDelayedValidation();
            CatFlapCellRevealResult? result = controller?.UseFix();
            if (result.HasValue && result.Value.Success)
            {
                PlaySound(global::SoundName.SolveStep);
                SaveAll();
                StartDelayedValidation(result.Value.Row, result.Value.Column);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveCurrentBoard();
        }

        private bool IsDuplicateBoosterUse(BoosterType boosterType)
        {
            if (lastBoosterUseFrame == Time.frameCount && lastUsedBoosterType == boosterType)
            {
                return true;
            }

            lastBoosterUseFrame = Time.frameCount;
            lastUsedBoosterType = boosterType;
            return false;
        }

        public void SaveProgress()
        {
            progressSaveManager?.SaveProgress(currentLevelIndex, controller?.Session);
            MainMenuHandler?.RefreshCoinDisplay();
        }

        public void SaveCurrentBoard()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            // A level that already ended (Won/Failed) has nothing meaningful left to persist -
            // saving it here would just re-write the ended board back over a Clear that already
            // ran for it (e.g. SaveAndShowMainMenu()'s unconditional SaveAll() firing right as the
            // player backs out of a Daily Challenge fail popup).
            if (controller.Session.GameState.Current != CatFlap.Models.CatFlapGameState.Playing)
            {
                return;
            }

            // FTUE levels 1-4 never persist an in-progress board, from ANY save call (Home,
            // autosave, app pause/quit, OnDestroy) - a mid-level quit always restarts the guided
            // tap sequence from the very first tile rather than resuming it partway through.
            // Doesn't extend to level 8 (Level Attempt tutorial highlight) or any other level -
            // IsFtueLevel only covers 1..GlobalConfig.FtueEnabledUntilLevel (4). Daily Challenge
            // is never an FTUE level, so this only ever takes the regular-campaign Clear branch.
            if (!isDailyChallengeSession && IsFtueLevel(CurrentLevelNumber))
            {
                boardSaveManager?.Clear(controller.Session);
                return;
            }

            // Both go through the same Level Progression save system/file - only the
            // save-manager instance (and therefore its progressionId "row") differs, so a
            // Daily Challenge save never collides with/overwrites the regular campaign save.
            if (isDailyChallengeSession)
            {
                boardSaveManager?.Save(controller.Session, -1, DailyChallengeProgressionId);
            }
            else
            {
                boardSaveManager?.Save(controller?.Session, currentLevelIndex);
            }
        }

        /// <summary>
        /// Clears the saved board for the current Daily Challenge sub-level - called on a Lost
        /// result so the next attempt (whether via Retry or coming back later) starts from a
        /// fresh empty board instead of resuming the failed one.
        /// </summary>
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

        private void ResolveNotesManager()
        {
            if (notesManager == null)
            {
                notesManager = transform.root.GetComponentInChildren<NotesManager>(true);
            }

            if (notesManager == null)
            {
                return;
            }

            notesManager.NotesModeChanged.RemoveListener(HandleNotesModeChanged);
            notesManager.NotesModeChanged.AddListener(HandleNotesModeChanged);
            notesManager.ApplyAllRequested.RemoveListener(ApplyAllNotes);
            notesManager.ApplyAllRequested.AddListener(ApplyAllNotes);
            // Sync UI to current state without treating this initial sync as a real toggle -
            // only HandleNotesModeChanged (the real listener path) plays the toggle sound.
            statusView?.SetNotesMode(notesManager.IsNotesActive);
        }

        private void HandleNotesModeChanged(bool active)
        {
            statusView?.SetNotesMode(active);
            if (active)
            {
                // PlaySound(global::SoundName.NotesUsed);
            }
        }

        private void HandleCellSelected(int row, int column)
        {
            // Any board tap postpones the Hint idle-attention highlight (see
            // CatFlapHintHighlightAnimator) regardless of what this tap actually ends up doing
            // below - a tap that gets rejected (FTUE tip up, input disabled, etc.) is still real
            // player activity on this screen.
            hintHighlightAnimator?.ResetTimer();

            // The "Beware!" hearts tip isn't pointing at a tappable board cell - while it's up, a
            // tap anywhere should only dismiss it (handled by FtueController's own full-screen
            // catcher), never also place a tile underneath.
            if (ftueDriver != null && ftueDriver.IsShowingUiTip)
            {
                return;
            }

            // The rule-break mistake tip (see CatFlapFtueDriver.ShowMistakeTip) uses plain Show(),
            // not ShowDismissOnTap, so it has no dismiss-catcher of its own - this tap must
            // explicitly hide it. Same "consume the tap, don't also act on the board underneath"
            // reasoning as the UI tip above.
            if (ftueDriver != null && ftueDriver.IsShowingMistakeTip)
            {
                ftueDriver.DismissMistakeTip();
                return;
            }

            if (!Shared.GameFlow.InputManager.GamePlayInputEnabled)
            {
                // Distinguish "blocked because out of hearts" from every other reason input can
                // be disabled (popups open, mid-sequence, level not loaded yet) - only the former
                // gets the action-blocked cue, since a popup is already covering the board for
                // the rest.
                if (controller != null && controller.Session.HasLevel && controller.Session.LevelMistakeLimit.IsOutOfMistakes)
                {
                    PlaySound(global::SoundName.ActionBlocked);
                }

                return;
            }

            if (controller == null || !controller.Session.HasLevel || controller.IsSequencing)
            {
                return;
            }

            // Any genuine board tap dismisses an already-finished Wrong Solution blink's HUD text
            // (see CatFlapRuntimeController.ArmWrongSolutionTextDismiss/
            // ConsumeWrongSolutionTextDismissIfArmed) - regardless of which cell was tapped or
            // whether this tap goes on to fix the wrong line below. A no-op unless a dismiss is
            // actually armed.
            controller.ConsumeWrongSolutionTextDismissIfArmed();

            // While a hint is active, only the hinted cell itself takes input - tapping the
            // hinted cell commits its value directly (no empty->fish->cat tap-cycle for that tap),
            // and taps on every other cell are ignored outright (no dismiss, no placement).
            CatFlapCellPosition? activeHintCell = controller.ActiveHintCell;
            if (activeHintCell.HasValue)
            {
                if (activeHintCell.Value.Row == row && activeHintCell.Value.Column == column)
                {
                    ApplyActiveHint();
                }

                return;
            }

            // While FTUE is active, only the currently-highlighted cell takes input at all -
            // mirrors the ActiveHintCell behavior above (taps on every other cell are ignored
            // outright, no sound/feedback of any kind).
            if (ftueDriver != null && ftueDriver.IsActive)
            {
                (int Row, int Column)? ftueTarget = ftueDriver.CurrentTarget;
                if (!ftueTarget.HasValue || ftueTarget.Value.Row != row || ftueTarget.Value.Column != column)
                {
                    return;
                }
            }

            // FTUE tiles cap out after a fixed number of taps (1 on levels 1-2 - empty -> Fish
            // and stop; 2 on level 3 - empty -> Fish -> Cat and stop) rather than the normal
            // empty -> Fish -> Cat -> empty cycle continuing indefinitely. This holds for the
            // whole level, not just while the tutorial overlay is still on screen - it stays
            // true even after the last tile is filled and ftueDriver has hidden itself, so a
            // stray extra tap can never undo a completed FTUE board.
            if (ftueMaxTapsPerTile > 0 &&
                CountFtueTapsConsumed(controller.Session.Board.GetCell(row, column).Value) >= ftueMaxTapsPerTile)
            {
                return;
            }

            bool notesActive = notesManager != null && notesManager.IsNotesActive;
            bool moveChanged = controller.SelectCell(row, column, notesActive);
            if (moveChanged)
            {
                boosterManager?.RecordMove(row, column, !notesActive);
                ftueDriver?.Refresh();
                if (notesActive)
                {
                    attemptTotalNotesUsed++;
                }
            }

            if (!moveChanged)
            {
                CatFlapCellModel tappedCell = controller.Session.Board.GetCell(row, column);
                if (tappedCell.IsLocked)
                {
                    PlaySound(global::SoundName.CellLockedTap);

                    // Extra negative feedback only for a pre-placed clue cell (IsGiven - defined in
                    // the level JSON, never true for a cell restored from a "continue" save) that
                    // hasn't since become part of a completed/correct row or column - once it's
                    // green, tapping it is no longer "wrong", so none of this fires.
                    if (tappedCell.IsGiven && !controller.IsCellPartOfCompletedLine(row, column))
                    {
                        attemptPrePlacedTileTap++;
                        VibrationManager.Instance.PlayVibration(global::NotificationFeedbackStyle.Error);
                        PlaySound(global::SoundName.CellWrongTap);
                        if (boardView is ICatFlapBoardView typedBoardView)
                        {
                            typedBoardView.PlayLockedTapShake(row, column);
                        }
                    }
                }

                return;
            }

            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Light);
            PlayCellTapSound(row, column);
            SaveAll();
            sharedBoosterManager?.RefreshAllBoosterUI();
            RefreshApplyAllAvailability();
            if (!notesActive)
            {
                StartDelayedValidation(row, column);
            }
        }

        private IEnumerator EnableGameplayInputNextFrame()
        {
            yield return null;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = controller != null && controller.Session.HasLevel;
        }
        // Completed-line tile-flip cascade timing (stagger between each tile starting, how much
        // faster each successive tile plays, capped at the max multiplier - see
        // CatFlapRuntimeController.CompletedLineFlipStaggerSeconds/
        // CompletedLineFlipSpeedIncreasePerStep/CompletedLineFlipMaxSpeedMultiplier). Split into
        // two tunable sets by board size - applied per level load (see ApplyCompletedLineFlipTuning)
        // once the board's actual row/column count is known, not once in Awake, since different
        // levels can use either size class.
        [Header("Completed-Line Flip Cascade - Set 1 (below 6x6 boards)")]
        [SerializeField] private float smallBoardFlipStaggerSeconds = 0.05f;
        [SerializeField] private float smallBoardFlipSpeedIncreasePerStep = 0.2f;
        [SerializeField] private float smallBoardFlipMaxSpeedMultiplier = 4f;

        [Header("Completed-Line Flip Cascade - Set 2 (6x6 and above boards)")]
        [SerializeField] private float largeBoardFlipStaggerSeconds = 0.05f;
        [SerializeField] private float largeBoardFlipSpeedIncreasePerStep = 0.2f;
        [SerializeField] private float largeBoardFlipMaxSpeedMultiplier = 4f;

        // 6x6 and above uses Set 2; anything smaller (in either dimension) uses Set 1.
        private const int LargeBoardSizeThreshold = 6;

        private void ApplyCompletedLineFlipTuning()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            bool isLargeBoard = controller.Session.Board.RowCount >= LargeBoardSizeThreshold &&
                controller.Session.Board.ColumnCount >= LargeBoardSizeThreshold;

            controller.CompletedLineFlipStaggerSeconds = isLargeBoard ? largeBoardFlipStaggerSeconds : smallBoardFlipStaggerSeconds;
            controller.CompletedLineFlipSpeedIncreasePerStep = isLargeBoard ? largeBoardFlipSpeedIncreasePerStep : smallBoardFlipSpeedIncreasePerStep;
            controller.CompletedLineFlipMaxSpeedMultiplier = isLargeBoard ? largeBoardFlipMaxSpeedMultiplier : smallBoardFlipMaxSpeedMultiplier;
        }

        /// <summary>
        /// Fires once PlayLevelWinCelebrationThenShowPopup's celebration beat (tile-flip wait +
        /// happy mood/board pulse/confetti, waited out until every tile's own win-pulse has
        /// actually finished playing) has finished - for BOTH normal and Daily Challenge sessions,
        /// since that beat always plays regardless of session type. CatFlapDailyChallengeBridge
        /// waits for this before reporting a Win, so the DC win popup (like the normal one) only
        /// appears once the board's own scale-up celebration has visibly finished.
        /// </summary>
        public event System.Action LevelWinCelebrationFinished;

        public MainMenuHandler MainMenuHandler;
        /// <summary>
        /// The leaderboard to credit, falling back to a scene lookup when the Inspector reference
        /// is empty.
        ///
        /// Worth the fallback because the failure mode is invisible: an unassigned reference does
        /// not error, it just means every win scores nothing, and the board looks like it is broken
        /// rather than unwired. Resolved once and cached.
        /// </summary>
        private Shared.Leaderboard.LeaderboardManager ResolveLeaderboardManager()
        {
            if (leaderboardManager == null)
            {
                // Include inactive: the board lives on a UI root that is switched off whenever the
                // menu is not up, which is most of the time a level is being played.
                leaderboardManager = FindFirstObjectByType<Shared.Leaderboard.LeaderboardManager>(FindObjectsInactive.Include);

                if (leaderboardManager == null)
                {
                    WarnMissingLeaderboard();
                }
            }

            return leaderboardManager;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnMissingLeaderboard()
        {
            Debug.LogWarning(
                "[Leaderboard] No LeaderboardManager in the scene - level wins will score nothing. "
                + "Assign it on CatFlapGameplayManager.",
                this);
        }

        private void HandleLevelWon(object sender, System.EventArgs eventArgs)
        {
            // Freeze booster button state right here, before currentLevelIndex advances below -
            // CurrentLevelNumber (read by sharedBoosterManager's level-number provider) points at
            // the NEXT level from that point on, well before it actually loads, so nothing about
            // the boosters must change again until then (see BoosterManager.SuppressUntilNextConfigure).
            sharedBoosterManager?.SuppressUntilNextConfigure();

            // Disabled the instant a win is recorded, before the celebration/coin/popup animations
            // below even start - it must never be tappable through any of them.
            statusView?.DisableInfoButton();

            MainMenuHandler.OnLevelWinSpinTheWheel();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LevelComplete);

            // Daily Challenge sub-levels are rewarded from their own config value
            // (GlobalConfig.DailyChallenge.LevelWinCoins), separate from the main game's
            // LevelWinCoins/HardLevelWinCoins - captured here (before AddCoins changes the wallet)
            // so CatFlapDailyChallengeBridge can feed the real amounts into
            // CatFlapDailyChallengeWinPopupController.SetCurrentCoin once its own result-resolved
            // handler runs (see GetDailyChallengeLevelWinCoinInfo).
            // Resolved while CurrentLevelNumber is still the level just won - currentLevelIndex
            // only advances further below - and cached in lastLevelWinCoinReward, since the win
            // popup and the 2x ad grant both need this exact amount after that point, when
            // CurrentLevelNumber already points at the NEXT level and so can no longer be asked
            // whether the WON level was hard.
            int levelWinCoinReward = ResolveLevelWinCoinReward(CurrentLevelNumber);
            lastLevelWinCoinReward = levelWinCoinReward;
            if (isDailyChallengeSession)
            {
                dailyChallengeCoinsBeforeWin = Mathf.Max(0, controller.Session.BoosterWallet.CurrentCoins);
                dailyChallengeCoinsWon = levelWinCoinReward;
            }

            controller?.Session.AddCoins(levelWinCoinReward);
            if (controller != null)
            {
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    levelWinCoinReward,
                    isDailyChallengeSession ? "daily_challenge_win" : "level_win",
                    controller.Session.BoosterWallet.CurrentCoins);
            }

            // Hearts left over become leaderboard points, 1:1. Read here, before currentLevelIndex
            // advances below, while Session.LevelMistakeLimit is still this level's - it is only
            // rebuilt by the next LoadAssignedLevel.
            //
            // Gated on LevelAttemptFeatureEnabled so wins below GlobalConfig.LevelAttemptUnlockLevel
            // score nothing: hearts ARE still spent on those levels, but the player is never shown
            // them, and charging for invisible mistakes isn't defensible. Daily Challenge is
            // excluded outright - it has its own reward track, doesn't touch normal progression,
            // and force-enables that same flag.
            lastLevelEarnedPoints = 0;
            if (!isDailyChallengeSession && controller != null && controller.LevelAttemptFeatureEnabled)
            {
                // Hearts left over ARE the points - one heart, one point, no conversion. Losing
                // a heart costs a point, so a flawless level is worth the full mistake limit.
                lastLevelEarnedPoints = Mathf.Max(0, controller.Session.LevelMistakeLimit.RemainingMistakes);

                // NOT credited here when the board is going to show it happening. The whole point
                // of that sequence is that the player sees their OLD standing first and watches
                // the points land on it - crediting now would open the board on the result and
                // animate a climb that had already happened.
                //
                // Exactly one of these two paths runs. Granting in both would double every win.
                if (leaderboardPanel == null)
                {
                    Shared.Leaderboard.LeaderboardManager board = ResolveLeaderboardManager();
                    if (board != null)
                    {
                        board.AddWinPoints(lastLevelEarnedPoints);
                    }
                }
            }

            // A different cell's own delayed-validation coroutine (see StartDelayedValidation -
            // each cell's wrong/correct feedback runs independently and isn't cancelled by another
            // cell completing a line) may still be counting down when the winning tap lands. Left
            // running, it can fire ResolveMistake/ValidateBoard after the board has already won and
            // locked, painting a stale red rule-violation highlight onto an otherwise-completed
            // board. Every other level-ending path (restart/undo/quit/load) already cancels these;
            // winning must too.
            StopDelayedValidation();

            // Every cell's Happy mood plays first, board-wide, with input still blocked - only
            // once that celebration beat finishes does the LevelWinUIRoot popup itself appear.
            // SuppressWinFailPages holds off RefreshStatusOnly()'s own automatic ShowState(Win)
            // call (CatFlapRuntimeController.RenderStatus fires it unconditionally whenever
            // session.GameState.Current is Won) so the HUD can still be refreshed (coins, hearts,
            // etc.) immediately without the popup jumping in ahead of the celebration. A Daily
            // Challenge session already holds this true for its whole attempt (see
            // LoadDailyChallengeLevel) and shows its own result popup instead - never clear it
            // back to false for that case.
            if (uiFlowManager != null && !isDailyChallengeSession)
            {
                uiFlowManager.SuppressWinFailPages = true;
            }

            // Status/HUD only, not RefreshViews' full board repaint - the winning line's own
            // reveal cascade (kicked off moments earlier, inside ValidateBoard's Won branch) is
            // still mid-flip right now, and a full repaint here would immediately force-stop it.
            controller?.RefreshStatusOnly();
            boardSaveManager?.Clear(controller?.Session);

            // Captured before currentLevelIndex advances below - this is the level number the
            // player actually just won, which is what the ads-button gate (and any other
            // this-level-specific popup content) must be evaluated against.
            int wonLevelNumber = CurrentLevelNumber;

            AnalyticsManager.Instance.LogLevelComplete(BuildLevelResultEventData(wonLevelNumber));

            // Progression advances the instant the win is recorded, not only when the player
            // presses Continue (LoadNextLevel) - otherwise closing the win popup or quitting from
            // it saves the level as never-won, and the home screen keeps showing the old level
            // number. LoadNextLevel no longer advances currentLevelIndex itself; it just loads
            // whatever this already-advanced index now points to.
            currentLevelIndex++;
            SaveProgress();

            StartCoroutine(PlayLevelWinCelebrationThenShowPopup(wonLevelNumber));
        }

        private IEnumerator PlayLevelWinCelebrationThenShowPopup(int wonLevelNumber)
        {
            // Skip Tutorial's own completion - never plays the board-wide win celebration at
            // all, straight into the next board (or the win popup, for the last FTUE level)
            // below instead. Reset immediately so it never leaks into the NEXT real completion.
            bool skipCelebration = skippingFtueTutorial;
            skippingFtueTutorial = false;

            if (!skipCelebration && boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                // The winning tap's own object-entry animation (tile flip/pop) may still be
                // mid-play at this point - RefreshViews() above renders instantly, but the flip
                // that placed this value plays independently on its own timer. Let it actually
                // land before the Happy celebration takes over that same icon renderer, or the
                // two visibly collide.
                while (spriteBoardView.IsAnyEntryAnimationPlaying())
                {
                    yield return null;
                }

                // No post-flip pop wait/hand-off delay here - the winning line's own reveal
                // (see CatFlapRuntimeController.PlayWinningLineReveal) suppresses that pop
                // entirely, so the flip goes directly into the board-wide win celebration the
                // instant it settles.
                //
                // FTUE levels skip the win particles (confetti + each cell's own sparkle burst) -
                // the celebration still plays (mood animation, background pulse), just without
                // the particle flourish, since these levels move straight into the next level's
                // board rather than a result screen.
                bool playWinParticles = !IsFtueLevel(wonLevelNumber);
                spriteBoardView.PlayHappyMoodOnAllCells(playWinParticles);
                spriteBoardView.PlayLevelWinBoardAnimation(playWinParticles);

                // Wait for every tile's own win-pulse (wave delay + scale up + settle) to
                // actually finish playing, not just be triggered - the result popup should only
                // appear once the whole board's scale-up celebration has visibly finished.
                while (spriteBoardView.IsAnyLevelWinPulsePlaying())
                {
                    yield return null;
                }
            }

            LevelWinCelebrationFinished?.Invoke();

            if (isDailyChallengeSession)
            {
                // Daily Challenge owns its own result popup (CatFlapDailyChallengeBridge) and keeps
                // SuppressWinFailPages true for the whole attempt - the celebration still plays,
                // but the regular LevelWinUIRoot must never show underneath/alongside it.
                yield break;
            }

            // FTUE levels 1..(FtueEnabledUntilLevel - 1) skip the win popup entirely - slide the
            // board off-screen and load the next level into it in its place instead, so guided
            // progression never gets interrupted by a popup. The last FTUE level
            // (FtueEnabledUntilLevel, level 4 today) is deliberately excluded here and falls
            // through to the normal popup below, same as level 5+.
            if (IsFtueLevel(wonLevelNumber) && wonLevelNumber < activeConfig.FtueEnabledUntilLevel)
            {
                // Extra beat on top of the wait above (pulse-only, see there) so the board-slide
                // doesn't cut off the character's own win-celebration mood animation, which runs
                // independently of the pulse and isn't otherwise waited on.
                yield return new WaitForSeconds(Mathf.Max(0f, ftueLevelWinExtraDelaySeconds));
                yield return PlayFtueLevelTransition();
                yield break;
            }

            // The board goes first: it opens over the finished level showing where the player
            // stood BEFORE this win, adds the hearts they earned, and holds until they are done
            // with it. Only then does the win popup appear.
            yield return PlayLeaderboardWinFlow();

            if (uiFlowManager != null)
            {
                uiFlowManager.SuppressWinFailPages = false;
            }

            UpdateLevelWinPopup(wonLevelNumber);
            uiFlowManager?.ShowWin();

            // GamePlayInputEnabled stays false here, same as before this celebration beat existed -
            // the win popup now owns input until Continue/Home loads the next screen and its own
            // flow re-enables gameplay input.
        }

        /// <summary>
        /// Opens the leaderboard on the pre-win standing, adds this level's points, and waits for
        /// the player to dismiss it.
        ///
        /// Waits on a flag rather than a fixed duration because there is no single length to wait
        /// for: the player may sit on the board, or press Collect and go through the whole reward
        /// screen first. Either way the win popup must not appear underneath.
        /// </summary>
        private IEnumerator PlayLeaderboardWinFlow()
        {
            if (leaderboardPanel == null)
            {
                yield break;
            }

            // A board the player cannot open from the menu must not be forced on them here. The
            // points are still credited - they just accrue quietly until it unlocks.
            Shared.Leaderboard.LeaderboardManager board = ResolveLeaderboardManager();
            if (board == null || !board.IsFeatureUnlocked)
            {
                board?.AddWinPoints(lastLevelEarnedPoints);
                yield break;
            }

            // The reward screen is part of this sequence, not a separate thing that happens
            // afterwards: pressing Collect on the board CLOSES the board and opens the popup, so
            // watching the board alone would call the flow finished the moment it handed over.
            Shared.Leaderboard.LeaderboardRewardPopupController rewardPopup =
                FindFirstObjectByType<Shared.Leaderboard.LeaderboardRewardPopupController>(FindObjectsInactive.Include);

            bool finished = false;
            void OnFinished() => finished = true;

            leaderboardPanel.WinFlowContinued += OnFinished;
            if (rewardPopup != null)
            {
                rewardPopup.Completed += OnFinished;
            }

            try
            {
                leaderboardPanel.OpenWithScoreGain(lastLevelEarnedPoints);

                // Held while EITHER is up. The last exit condition also covers the board being
                // dismissed some other way - the back button - which would otherwise leave the win
                // popup waiting forever.
                while (!finished
                    && (leaderboardPanel.IsOpen || (rewardPopup != null && rewardPopup.IsOpen)))
                {
                    yield return null;
                }
            }
            finally
            {
                leaderboardPanel.WinFlowContinued -= OnFinished;
                if (rewardPopup != null)
                {
                    rewardPopup.Completed -= OnFinished;
                }
            }
        }

        [SerializeField] private float ftueBoardSlideDurationSeconds = 0.4f;
        // FTUE levels 1-3 only - see PlayLevelWinCelebrationThenShowPopup's own call site.
        [SerializeField] private float ftueLevelWinExtraDelaySeconds = 0.25f;

        // FTUE levels 1..3's own "no popup" completion: slides the whole board (with its base -
        // see CatFlapSpriteBoardViewAdapter.SlideLocalPosition, which moves the board's root
        // transform, never touched by Build/individual cell positioning) fully off-screen to the
        // left, then teleports it off-screen to the right and loads the next level into it while
        // nothing is visible, then slides it back in to its normal centered position - reusing
        // the SAME board instance throughout rather than juggling two live boards at once.
        // Also used by the Skip Tutorial flow (see SkipFtueTutorial) after auto-solving the
        // current board, so both a real completion and a skip land on the exact same transition.
        private IEnumerator PlayFtueLevelTransition()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;

            // Disabled for the WHOLE transition (both legs of the slide, and the invisible
            // reload in between) - re-enabled only once the board has actually finished sliding
            // back into view, below. LoadAssignedLevel's own ApplyFtueState call (for the level
            // being loaded mid-transition) would otherwise re-show/re-enable this the instant it
            // runs, while the board is still off-screen.
            if (skipTutorialButtonRoot != null)
            {
                skipTutorialButtonRoot.SetActive(false);
            }

            if (boardView is not CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                // No sprite board to animate (e.g. the plain UI board adapter, used only for
                // cheat/test scenes) - just load the next level normally.
                LoadAssignedLevel(isContinueFromWin: true);
                Shared.GameFlow.InputManager.GamePlayInputEnabled = true;
                yield break;
            }

            Vector3 originalPosition = spriteBoardView.transform.localPosition;
            float offscreenDistance = spriteBoardView.GetOffscreenSlideDistance();

            yield return spriteBoardView.SlideLocalPosition(
                originalPosition + new Vector3(-offscreenDistance, 0f, 0f),
                ftueBoardSlideDurationSeconds);

            // Fully off-screen now - this jump to the opposite side, and the level load/rebuild
            // that follows, are both invisible from here.
            spriteBoardView.transform.localPosition = originalPosition + new Vector3(offscreenDistance, 0f, 0f);
            LoadAssignedLevel(isContinueFromWin: true);
            if (skipTutorialButtonRoot != null)
            {
                skipTutorialButtonRoot.SetActive(false);
            }

            yield return spriteBoardView.SlideLocalPosition(originalPosition, ftueBoardSlideDurationSeconds);

            if (skipTutorialButtonRoot != null && IsFtueLevel(CurrentLevelNumber))
            {
                UpdateSkipTutorialButtonPosition();
                skipTutorialButtonRoot.SetActive(true);
            }

            // Overrides whatever LoadAssignedLevel's own entrance sequence already did to this
            // flag internally - guarantees input is only actually usable once the board has
            // genuinely finished sliding back into view, not whenever that sequence happens to
            // re-enable it relative to this animation.
            Shared.GameFlow.InputManager.GamePlayInputEnabled = true;
        }

        private void UpdateLevelWinPopup(int wonLevelNumber)
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            Shared.GameFlow.LevelWinPopupController popup = uiFlowManager?.GetOrCreateLevelWinPopup();
            // AddCoins(lastLevelWinCoinReward) already ran back in HandleLevelWon, so
            // BoosterWallet.CurrentCoins is already the post-win total here - but the popup's own
            // open animation (PlayOpenSequence) does currentCoin += winCoinAmount internally to
            // count up on screen. Feeding it the already-incremented total would double-count the
            // reward visually, so hand it the pre-win balance instead and let its own animation
            // add the reward once.
            int coinsBeforeWin = Mathf.Max(0, controller.Session.BoosterWallet.CurrentCoins - lastLevelWinCoinReward);
            popup?.SetCurrentCoin(coinsBeforeWin, lastLevelWinCoinReward);
            popup?.SetStats(
                controller.Session.Progress.Moves,
                controller.Session.Progress.ElapsedSeconds);
            popup?.SetAdsButtonVisible(wonLevelNumber > activeConfig.NoAdsBeforeLevel);
            // Hidden for the first 3 levels - a brand new player can only go forward (Next) out of
            // the win popup, not back out to the main menu, on levels 1-3.
            popup?.SetHomeButtonVisible(wonLevelNumber > 3);
            popup?.SetLevelResultText(wonLevelNumber);
            if (popup != null)
            {
                popup.WatchAdRequested.RemoveListener(HandleLevelWinWatchAd);
                popup.WatchAdRequested.AddListener(HandleLevelWinWatchAd);
            }
        }

        /// <summary>
        /// The pre-win wallet balance and coin reward for the Daily Challenge sub-level just won -
        /// captured in HandleLevelWon (see dailyChallengeCoinsBeforeWin/dailyChallengeCoinsWon).
        /// CatFlapDailyChallengeBridge calls this from its own DailyChallengeResultResolved handler
        /// to feed real values into CatFlapDailyChallengeWinPopupController.SetCurrentCoin instead
        /// of that popup's old placeholder amount.
        /// </summary>
        public void GetDailyChallengeLevelWinCoinInfo(out int coinsBeforeWin, out int coinsWon)
        {
            coinsBeforeWin = dailyChallengeCoinsBeforeWin;
            coinsWon = dailyChallengeCoinsWon;
        }

        /// <summary>
        /// Grants the Daily Challenge win reward a second time - called by
        /// CatFlapDailyChallengeBridge.HandleWinWatchAd once its rewarded ad actually succeeds.
        /// HandleLevelWon already credited the wallet with the single (non-doubled) reward before
        /// this popup even opened, and CatFlapDailyChallengeWinPopupController.
        /// DoubleWinCoinsAndContinue only doubles what the popup DISPLAYS - without this, the
        /// player would see "2x" on screen but their actual coin balance would only ever reflect
        /// the original single reward.
        /// </summary>
        public void GrantDailyChallengeWatchAdBonus()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            controller.Session.AddCoins(dailyChallengeCoinsWon);
            SaveProgress();
        }

        // Routes through the shared AdsManager - once actually rewarded, doubles the level-win
        // coin reward and continues to the next level, same as tapping Continue/Next.
        private void HandleLevelWinWatchAd()
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DoubleCoinNormalLevel, success =>
            {
                if (success)
                {
                    GrantLevelWinWatchAdReward();
                }
                else
                {
                    uiFlowManager?.GetOrCreateLevelWinPopup()?.OnWatchAdDeclined();
                }
            });
        }

        private void GrantLevelWinWatchAdReward()
        {
            // DoubleWinCoinsAndContinue below only doubles what the popup DISPLAYS
            // (winCoin/winCoinText) - the wallet was already credited with the single
            // (non-doubled) reward back in HandleLevelWon, before this popup even opened. Grant
            // the same amount again here so the player's actual coin balance matches what they now
            // see on screen instead of silently falling short of the "2x" they were promised.
            if (controller != null && controller.Session.HasLevel)
            {
                controller.Session.AddCoins(lastLevelWinCoinReward);
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    lastLevelWinCoinReward,
                    "watch_ad_double",
                    controller.Session.BoosterWallet.CurrentCoins);
                SaveProgress();
            }

            uiFlowManager?.GetOrCreateLevelWinPopup()?.DoubleWinCoinsAndContinue();
        }

        private void HandleLevelFailed(object sender, System.EventArgs eventArgs)
        {
            // Same "disabled the instant it's recorded" reasoning as HandleLevelWon.
            statusView?.DisableInfoButton();

            outOfLivesPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LevelFail);
            // LevelFail analytics no longer logs here (the game-over transition itself) - it now
            // fires specifically when the player clicks Home or Restart on the resulting popup
            // (see RestartLevel/SaveAndShowMainMenu/AbandonLevelToMainMenu), since simply reaching
            // this state isn't itself the event the requirement wants tracked.
            uiFlowManager?.GetOrCreateLevelFailPopup()?.SetLevelResultText(CurrentLevelNumber);
            SaveAll();
        }

        // Fires once per mistake that actually spends a life - see CatFlapRuntimeController.
        // MistakeSpent/ResolveMistake. Independent of HandleOutOfMistakes below, which only fires
        // once the LAST mistake is spent - this fires on every single one, including that last.
        private void HandleMistakeSpent(object sender, System.EventArgs eventArgs)
        {
            attemptLivesUsedTotal++;
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            // progression_lives_lost is BOARD progress (how much of the puzzle is already
            // correctly solved), not lives/mistake progress - i.e. "the player lost a life when
            // the board was X% done", not "X% of their mistake allowance is gone".
            AnalyticsManager.Instance.LogLivesLost(CurrentLevelNumber, ComputeBoardProgressPercent());
        }

        // Live equivalent of CatFlapLevelProgressCalculator's saved-board reading path (see
        // CatFlapCurrentLevelBoardSaveManager.BoardSaveData.FromSession) - built directly from the
        // current in-memory board instead of a persisted save file.
        private float ComputeBoardProgressPercent()
        {
            CatFlapRuntimeSession session = controller.Session;
            int size = session.Board.RowCount;
            var values = new int[size * size];
            var notes = new bool[size * size];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int index = row * size + column;
                    CatFlapCellModel cell = session.Board.GetCell(row, column);
                    values[index] = (int)cell.Value;
                    notes[index] = cell.IsNote;
                }
            }

            return CatFlapLevelProgressCalculator.Calculate(session.Level, size, values, notes).ProgressPercent;
        }

        // Feeds level_complete/level_fail's wrong_solution_count - see
        // CatFlapRuntimeController.WrongSolutionDetected.
        private void HandleWrongSolutionDetected(object sender, System.EventArgs eventArgs)
        {
            attemptWrongSolutionCount++;
        }

        // Feeds level_complete/level_fail's rule_1_break_count/rule_2_break_count - see
        // CatFlapRuntimeController.RuleViolationCharged. Only these 2 of the game's 3 rules are
        // tracked, matching the tracking spec's own 2 slots.
        private void HandleRuleViolationCharged(CatFlapRule rule)
        {
            if (rule == CatFlapRule.MaxTwoAdjacent)
            {
                attemptRule1BreakCount++;
            }
            else if (rule == CatFlapRule.EqualCounts)
            {
                attemptRule2BreakCount++;
            }
        }

        [SerializeField] private float outOfLivesHeartBreakDelaySeconds = 0.5f;

        private void HandleOutOfMistakes(object sender, System.EventArgs eventArgs)
        {
            // The Level Attempt feature (hearts/mistake-limit) isn't unlocked yet for this level -
            // running out of mistakes should have no visible effect at all, so skip the Out of
            // Lives popup/input-block entirely rather than only hiding its heart-break FX.
            if (controller == null || !controller.LevelAttemptFeatureEnabled)
            {
                return;
            }

            // This fires synchronously from inside ResolveMistake, before the HUD heart-break FX
            // (RenderStatus(animateHeartLoss: true) / boardView.PlayHeartBreak, a few lines further
            // down the same call stack) has even started - so input must be blocked here right
            // away, but the popup itself has to wait for that FX to actually finish playing,
            // mirroring PlayLevelWinCelebrationThenShowPopup's "celebration beat, then popup"
            // sequencing for the win path.
            StopDelayedValidation();
            outOfLivesPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LifeEmpty);
            // LevelFail itself is timed to the popup's own cat entry animation instead (see
            // OutOfLivesPopupController.PlayCatOpenAnimation) - this fires well before the popup
            // even shows (still mid heart-break FX delay), which was too early for it.
            AnalyticsManager.Instance.LogOutOfLives(CurrentLevelNumber, isDailyChallengeSession, levelReviveCount, levelRetryCount);
            StartCoroutine(PlayHeartBreakThenShowOutOfLivesPopup());
        }

        private IEnumerator PlayHeartBreakThenShowOutOfLivesPopup()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, outOfLivesHeartBreakDelaySeconds));

            int refillCost = EconomyConfigController.OutOfLivesRefillCoinCost;
            bool canAffordRefill = controller != null &&
                controller.Session.HasLevel &&
                controller.Session.BoosterWallet.CurrentCoins >= refillCost;
            bool freeReviveAvailable = !HasUsedFreeRevive();

            if (isDailyChallengeSession && dailyChallengeOutOfLivesPopup != null)
            {
                BindDailyChallengeOutOfLivesPopupEvents();
                dailyChallengeOutOfLivesPopup.SetRefillCoinCost(refillCost);
                dailyChallengeOutOfLivesPopup.SetAffordableRefill(canAffordRefill);
                dailyChallengeOutOfLivesPopup.SetFreeReviveAvailable(freeReviveAvailable);
                dailyChallengeOutOfLivesPopup.Show(
                    title: null,
                    progressText: string.Empty,
                    showBack: true,
                    primaryAction: HandleOutOfLivesRestart,
                    backAction: HandleOutOfLivesGiveUp);

                // GamePlayInputEnabled stays false here, same as before this delay existed - the
                // out-of-lives popup now owns input until it closes.
                yield break;
            }

            BindOutOfLivesPopupEvents();
            uiFlowManager?.SetCurrentCoinOutOfLife(controller.Session.BoosterWallet.CurrentCoins);
            uiFlowManager?.ShowOutOfLivesPopup(refillCost);

            OutOfLivesPopupController popup = uiFlowManager != null ? uiFlowManager.OutOfLivesPopup : null;
            popup?.SetAffordableRefill(canAffordRefill);
            popup?.SetFreeReviveAvailable(freeReviveAvailable);
            popup?.SetLevelResultText(CurrentLevelNumber);

            // GamePlayInputEnabled stays false here, same as before this delay existed - the
            // out-of-lives popup now owns input until it closes.
        }

        private void HandleOutOfLivesWatchAd()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            string placement = isDailyChallengeSession
                ? AdsManager.RewardedPlacement.ReviveDcLevel
                : AdsManager.RewardedPlacement.ReviveLevel;

            AdsManager.Instance.ShowRewarded(placement, success =>
            {
                if (success)
                {
                    GrantOutOfLivesWatchAdReward();
                }
            });
        }

        private void GrantOutOfLivesWatchAdReward()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            controller.Session.AddLevelMistakes(1);
            levelReviveCount++;
            ContinueAfterOutOfLivesPopup();
        }

        // Free Revive: same refill-to-full effect as the coin revive button, just with no coin
        // cost and no ad, and works exactly once per player lifetime (see FreeReviveUsedKey). The
        // popup only shows this option at all when HasUsedFreeRevive() was false at Show() time
        // (see PlayHeartBreakThenShowOutOfLivesPopup), but the flag is still re-checked here so a
        // stale/already-open popup can never grant it twice.
        private void HandleOutOfLivesFreeRevive()
        {
            if (controller == null || !controller.Session.HasLevel || HasUsedFreeRevive())
            {
                return;
            }

            Shared.PlayerData.PlayerData.Instance.SetInt(FreeReviveUsedKey, 1);
            SaveAll();

            controller.Session.RefillLevelMistakes();
            levelReviveCount++;
            ContinueAfterOutOfLivesPopup();
        }

        private static bool HasUsedFreeRevive()
        {
            return Shared.PlayerData.PlayerData.Instance.TryGetInt(FreeReviveUsedKey, out int used) && used != 0;
        }

        private void HandleOutOfLivesRefill()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            int cost = Mathf.Max(0, EconomyConfigController.OutOfLivesRefillCoinCost);
            if (!controller.Session.TrySpendCoins(cost))
            {
                Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
                CloseActiveOutOfLivesPopup(() =>
                {
                    outOfLivesPopupOpen = false;
                    OpenStorePopup();
                });
                return;
            }

            AnalyticsManager.Instance.LogCurrencySink(
                AnalyticsEvents.CurrencyTypeValues.Coins,
                cost,
                "out_of_lives_refill",
                1,
                controller.Session.BoosterWallet.CurrentCoins);
            levelReviveCount++;
            controller.Session.RefillLevelMistakes();
            ContinueAfterOutOfLivesPopup();
        }

        // Give Up (the Out of Lives popup's "Home" button) abandons this attempt straight to the
        // main menu. NOTE: this used to call SettingsQuit(), but SettingsQuit() was later
        // repurposed to restart the level in place for the Settings popup's own Quit button -
        // Give Up was never updated to match, so it silently inherited "restart" instead of
        // "go home". Use AbandonLevelToMainMenu() here instead, which clears the saved
        // board/progress like a restart would, but returns to the main menu rather than
        // reloading this level in place.
        private void HandleOutOfLivesGiveUp()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            outOfLivesPopupOpen = false;

            // Fade in first, THEN close the Out of Lives popup (once fully covered), THEN abandon
            // to the main menu - whose own entry animation plays underneath while the overlay
            // fades back out.
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => CloseActiveOutOfLivesPopup(null),
                revealNewPage: AbandonLevelToMainMenu);
        }

        // Abandons the current attempt back to the main menu: clears the saved board for this
        // level (so it starts fresh next time, same as RestartLevel()'s clearing) but goes home
        // instead of reloading the level in place.
        private void AbandonLevelToMainMenu()
        {
            // Out-of-Lives popup's "Give Up"/Home click - this method is only ever reached from an
            // actual out-of-mistakes state, but route through the same shared guard as
            // SaveAndShowMainMenu/RestartLevel for consistency.
            if (IsCurrentLevelFailedOrOutOfLives())
            {
                AnalyticsManager.Instance.LogLevelFail(BuildLevelResultEventData());
            }

            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: false, levelWin: false, placement: AnalyticsEvents.HomePlacementValues.OutOfLives);

            if (isDailyChallengeSession)
            {
                DailyChallengeAttemptAbandoned.Invoke();
            }

            StopDelayedValidation();
            boardSaveManager?.Clear(controller?.Session);
            boosterManager?.ClearUndoHistory();
            SaveProgress();
            ReturnToMainMenu();
        }

        private void HandleOutOfLivesRestart()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            outOfLivesPopupOpen = false;

            // A Daily Challenge restart from Out of Lives requires watching an ad first - a
            // regular level's restart stays free/instant. Shown on top of the still-open popup,
            // same as the other AdsManager.ShowRewarded(...) call sites (HandleLevelWinWatchAd,
            // HandleOutOfLivesWatchAd) - only once rewarded does the popup actually close/restart.
            if (isDailyChallengeSession)
            {
                AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DailyChallengeRestart, success =>
                {
                    if (success)
                    {
                        PerformOutOfLivesRestart();
                    }
                });
                return;
            }

            PerformOutOfLivesRestart();
        }

        private void PerformOutOfLivesRestart()
        {
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => CloseActiveOutOfLivesPopup(null),
                revealNewPage: RestartLevel);
        }

        private void ContinueAfterOutOfLivesPopup()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            CloseActiveOutOfLivesPopup(ResumeAfterOutOfLivesPopupClosed);
        }

        // Closes whichever out-of-lives popup is actually open - the shared
        // Shared.GameFlow.OutOfLivesPopupController, or dailyChallengeOutOfLivesPopup during a
        // Daily Challenge session (see PlayHeartBreakThenShowOutOfLivesPopup) - so
        // HandleOutOfLivesRefill/GiveUp/Restart/ContinueAfterOutOfLivesPopup don't each need to
        // know which one is showing.
        private void CloseActiveOutOfLivesPopup(UnityEngine.Events.UnityAction onClosed)
        {
            if (isDailyChallengeSession && dailyChallengeOutOfLivesPopup != null)
            {
                dailyChallengeOutOfLivesPopup.Hide();
                onClosed?.Invoke();
                return;
            }

            if (uiFlowManager != null)
            {
                uiFlowManager.CloseOutOfLivesPopup(onClosed);
                return;
            }

            onClosed?.Invoke();
        }

        private void ResumeAfterOutOfLivesPopupClosed()
        {
            outOfLivesPopupOpen = false;

            // Every path that reaches here (watch-ad +1, coin refill, free revive) just restored
            // at least one attempt - the pips visibly fill back up to the real post-grant
            // remaining count (not always to max - the ad grant only restores 1) once the popup
            // is out of the way, instead of instantly snapping behind it. Played before
            // RefreshViews() below, not instead of it - RenderAttempts only ever touches each
            // pip's sprite/active-state/label, never fillAmount, so it can't stomp this animation
            // while it's still running.
            if (controller != null && controller.Session.HasLevel)
            {
                statusView?.PlayReviveAnimation(controller.Session.LevelMistakeLimit.RemainingMistakes, controller.Session.LevelMistakeLimit.MaxMistakes);
            }

            controller?.RefreshViews();
            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveAll();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Playing;
        }

        private void BindOutOfLivesPopupEvents()
        {
            OutOfLivesPopupController popup = uiFlowManager != null ? uiFlowManager.OutOfLivesPopup : null;
            if (popup == null)
            {
                return;
            }

            popup.WatchAdRequested.RemoveListener(HandleOutOfLivesWatchAd);
            popup.RefillRequested.RemoveListener(HandleOutOfLivesRefill);
            popup.GiveUpRequested.RemoveListener(HandleOutOfLivesGiveUp);
            popup.RestartRequested.RemoveListener(HandleOutOfLivesRestart);
            popup.FreeReviveRequested.RemoveListener(HandleOutOfLivesFreeRevive);
            popup.WatchAdRequested.AddListener(HandleOutOfLivesWatchAd);
            popup.RefillRequested.AddListener(HandleOutOfLivesRefill);
            popup.GiveUpRequested.AddListener(HandleOutOfLivesGiveUp);
            popup.RestartRequested.AddListener(HandleOutOfLivesRestart);
            popup.FreeReviveRequested.AddListener(HandleOutOfLivesFreeRevive);
        }

        // Only the three revive options are UnityEvents here - Restart/Give Up are wired directly
        // as dailyChallengeOutOfLivesPopup.Show(...)'s primaryAction/backAction parameters instead
        // (see PlayHeartBreakThenShowOutOfLivesPopup), since that popup's API takes them as plain
        // Actions rather than UnityEvents.
        private void BindDailyChallengeOutOfLivesPopupEvents()
        {
            if (dailyChallengeOutOfLivesPopup == null)
            {
                return;
            }

            dailyChallengeOutOfLivesPopup.WatchAdRequested.RemoveListener(HandleOutOfLivesWatchAd);
            dailyChallengeOutOfLivesPopup.RefillRequested.RemoveListener(HandleOutOfLivesRefill);
            dailyChallengeOutOfLivesPopup.FreeReviveRequested.RemoveListener(HandleOutOfLivesFreeRevive);
            dailyChallengeOutOfLivesPopup.WatchAdRequested.AddListener(HandleOutOfLivesWatchAd);
            dailyChallengeOutOfLivesPopup.RefillRequested.AddListener(HandleOutOfLivesRefill);
            dailyChallengeOutOfLivesPopup.FreeReviveRequested.AddListener(HandleOutOfLivesFreeRevive);
        }

        private void TickAutosave()
        {
            if (autosaveIntervalSeconds <= 0f || controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            // Once a level has ended (Won/Failed), the session's board is a stale/failed record,
            // not something to keep re-persisting - without this, any autosave tick that happens
            // to land between Give Up/Fail and the next level actually loading silently re-saves
            // that stale board, undoing whatever just cleared it.
            if (controller.Session.GameState.Current != CatFlap.Models.CatFlapGameState.Playing)
            {
                return;
            }

            autosaveTimer += Time.deltaTime;
            if (autosaveTimer < autosaveIntervalSeconds)
            {
                return;
            }

            SaveAll();
        }

        private TextAsset GetCurrentLevelAsset()
        {
            if (levelSequence != null && levelSequence.Length > 0)
            {
                if (currentLevelIndex < 0)
                {
                    currentLevelIndex = 0;
                }

                // The normal (non-loop) pack is whichever is smaller of the assigned TextAssets
                // and TotalLevels - same effective count GetLevelCount() already uses below.
                int normalLevelCount = System.Math.Min(levelSequence.Length, activeConfig.TotalLevels);
                if (currentLevelIndex < normalLevelCount)
                {
                    lastLoadedActualLevelNumber = currentLevelIndex + 1;
                    return levelSequence[currentLevelIndex];
                }

                // Past the normal pack - GlobalConfig.LoopLevels replays the SAME 100 level assets
                // in a curated, reshuffled order (see CatFlapLoopLevelSequencer) instead of needing
                // new content. currentLevelIndex keeps counting up forever; loopIndex is its
                // 0-based position within that endless loop content.
                int loopLevelNumber = ResolveLoopLevelNumber(currentLevelIndex - normalLevelCount);
                if (loopLevelNumber >= 1 && loopLevelNumber <= levelSequence.Length)
                {
                    lastLoadedActualLevelNumber = loopLevelNumber;
                    return levelSequence[loopLevelNumber - 1];
                }

                // No loop levels configured (or a misconfigured pool) - fall back to replaying the
                // last level of the normal pack, matching the old clamp-at-the-end behavior.
                currentLevelIndex = normalLevelCount - 1;
                lastLoadedActualLevelNumber = currentLevelIndex + 1;
                return levelSequence[System.Math.Max(0, currentLevelIndex)];
            }

            lastLoadedActualLevelNumber = CurrentLevelNumber;
            return levelJson;
        }

        // Maps a 0-based position within the endless loop content (past TotalLevels) to a concrete
        // level number (1-100), by extending - and persisting - a growing resolved sequence one
        // freshly shuffled "set" (see CatFlapLoopLevelSequencer.GenerateSet) at a time. Pure
        // function of loopIndex given the persisted sequence: replaying/resuming the exact same
        // loopIndex always returns the exact same level, and a brand-new set is only shuffled in
        // once the previous one has actually been fully consumed. Returns 0 if no loop levels are
        // configured at all (or the configured pools can't produce even one full pattern cycle).
        private int ResolveLoopLevelNumber(int loopIndex)
        {
            if (loopIndex < 0)
            {
                loopIndex = 0;
            }

            if (!CatFlapLoopLevelSequencer.HasAnyLevels(activeConfig.LoopLevels))
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
                List<int> set = CatFlapLoopLevelSequencer.GenerateSet(activeConfig.LoopLevels);
                if (set.Count == 0)
                {
                    // Configured pools can't produce even one full pattern cycle (e.g. a pool the
                    // pattern needs is empty) - stop rather than spinning forever.
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

            // Loop levels replay the same assets endlessly once the normal pack is cleared - report
            // an effectively unbounded count so HasNextLevel/LoadNextLevel never stop the player at
            // TotalLevels. GetCurrentLevelAsset (and ResolveLoopLevelNumber's own empty-set guard)
            // still fall back safely if the configured pools turn out unable to produce a level.
            if (levelSequence != null && levelSequence.Length > 0 && CatFlapLoopLevelSequencer.HasAnyLevels(activeConfig.LoopLevels))
            {
                return int.MaxValue;
            }

            return normalLevelCount;
        }

        private int ResolveLevelMistakeLimit(CatFlap.Levels.CatFlapLevelData level)
        {
            return Mathf.Max(1, level?.LevelMistakeLimit ?? activeRulesConfig.LevelMistakeLimit);
        }

        // Same -1/0/positive convention as BoosterConfig.IsActive/GetUnlockLevel: here 0 means
        // always unlocked (the current default), a positive unlock level gates the heart break FX
        // and attempt-pips HUD until the player reaches it. Read from GlobalConfig (not
        // GameRulesConfig) since it's set per-project alongside the other level-milestone gates
        // like NoAdsBeforeLevel and the booster UnLockLevels.
        private bool IsLevelAttemptFeatureUnlocked(int levelNumber)
        {
            int unlockLevel = activeConfig.LevelAttemptUnlockLevel;
            return unlockLevel <= 0 || levelNumber >= unlockLevel;
        }

        // Gates the level-complete interstitial (both normal AND Daily Challenge) on normal-level
        // progression - currentLevelIndex is never touched by a Daily Challenge session (see
        // LoadDailyChallengeLevel), so reading it here works unchanged for both call sites. Also
        // numerically equals "levels completed so far": HandleLevelWon increments it BEFORE
        // LoadNextLevel runs, so by the time LoadNextLevel calls this, currentLevelIndex already
        // equals the level number that was just won.
        private bool IsInterstitialLevelUnlocked()
        {
            if (!AdsConfigController.InterstitialAdsEnabled)
            {
                return false;
            }

            int unlockLevel = AdsConfigController.InterstitialStartLevel;
            return unlockLevel <= 0 || currentLevelIndex >= unlockLevel;
        }

        private bool IsInterstitialCooldownElapsed()
        {
            if (!Shared.PlayerData.PlayerData.Instance.TryGetString(LastInterstitialShownUtcKey, out string rawLastShown) ||
                !System.DateTime.TryParse(rawLastShown, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime lastShownUtc))
            {
                return true;
            }

            double secondsSinceLastShown = (System.DateTime.UtcNow - lastShownUtc).TotalSeconds;
            return secondsSinceLastShown >= Mathf.Max(0f, AdsConfigController.InterstitialDelayBetweenAds);
        }

        // Marks "now" as the last interstitial shown time - also called from
        // HandleRewardedAdCompletedForInterstitialCooldown to re-arm the same cooldown after a
        // rewarded ad, so a player who just watched one isn't shown an interstitial right after.
        private static void MarkInterstitialCooldownStartNow()
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.SetString(LastInterstitialShownUtcKey, System.DateTime.UtcNow.ToString("O"));
            data.Touch();

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }

        private void HandleRewardedAdCompletedForInterstitialCooldown()
        {
            MarkInterstitialCooldownStartNow();
        }

        /// <summary>
        /// Shows the level-complete interstitial (normal or Daily Challenge, per isDailyChallenge)
        /// and only then invokes proceed - unless it's not unlocked yet for this level, or still on
        /// cooldown from the last one, in which case proceed runs immediately with no ad. Callers:
        /// LoadNextLevel (normal) and CatFlapDailyChallengeBridge (DC's own Continue action).
        /// </summary>
        public void TryShowLevelCompleteInterstitial(bool isDailyChallenge, System.Action proceed)
        {
            if (!IsInterstitialLevelUnlocked() || !IsInterstitialCooldownElapsed())
            {
                proceed?.Invoke();
                return;
            }

            MarkInterstitialCooldownStartNow();

            System.Action<bool> onComplete = _ => proceed?.Invoke();
            if (isDailyChallenge)
            {
                AdsManager.Instance.ShowLevelCompleteDcInterstitial(onComplete);
            }
            else
            {
                AdsManager.Instance.ShowLevelCompleteNormalInterstitial(onComplete);
            }
        }

        private bool IsFtueLevel(int levelNumber)
        {
            int untilLevel = activeConfig.FtueEnabledUntilLevel;
            return untilLevel > 0 && levelNumber <= untilLevel;
        }

        // See levelReviveCount/levelRetryCount's own comment. Called from LoadAssignedLevel on
        // every load - a no-op unless levelNumber genuinely differs from what was last tracked.
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

        // See the attemptXxx fields' own comment - always resets, unlike
        // EnsureAttemptCountersForLevel above, since a retry starts these back at 0.
        private void ResetPerAttemptAnalyticsCounters()
        {
            attemptHintUsedCount = 0;
            attemptUndoUsedCount = 0;
            attemptTotalNotesUsed = 0;
            attemptTotalNotesApply = 0;
            attemptNotesButtonTapCount = 0;
            attemptWrongSolutionCount = 0;
            attemptRule1BreakCount = 0;
            attemptRule2BreakCount = 0;
            attemptPrePlacedTileTap = 0;
            attemptLivesUsedTotal = 0;
        }

        // Reads every per-attempt analytics counter/field back into one LevelResultEventData -
        // shared by every level_start/level_complete/level_fail/home/restart level call site so
        // they never drift apart. levelNumberOverride: HandleLevelWon calls this AFTER
        // currentLevelIndex has already advanced past the level that was just won - CurrentLevelNumber
        // would otherwise report the NEXT level instead of the one this event is actually about.
        private LevelResultEventData BuildLevelResultEventData(int? levelNumberOverride = null)
        {
            int levelNumber = levelNumberOverride ?? CurrentLevelNumber;
            int remainingMistakes = controller?.Session.LevelMistakeLimit?.RemainingMistakes ?? 0;
            return new LevelResultEventData
            {
                LevelNumber = levelNumber,
                FtueLevel = IsFtueLevel(levelNumber),
                HardLevel = currentLevelIsHard,
                LevelTime = controller?.Session.Progress.ElapsedSeconds ?? 0f,
                ReviveCount = levelReviveCount,
                RetryCount = levelRetryCount,
                CoinCount = controller?.Session.BoosterWallet?.CurrentCoins ?? 0,
                HintBoosterCount = controller?.Session.BoosterWallet?.GetCount(BoosterType.Hint) ?? 0,
                HintBoosterUsed = attemptHintUsedCount,
                UndoUsed = attemptUndoUsedCount,
                TotalNotesUsed = attemptTotalNotesUsed,
                TotalNotesApply = attemptTotalNotesApply,
                NotesButtonTapCount = attemptNotesButtonTapCount,
                // Cumulative, not (MaxMistakes - RemainingMistakes) - a revive/refill restores
                // RemainingMistakes, which would otherwise make this drop back down instead of
                // keeping the true running total for the whole attempt. See attemptLivesUsedTotal.
                LivesUsed = attemptLivesUsedTotal,
                LivesLeft = remainingMistakes,
                WrongSolutionCount = attemptWrongSolutionCount,
                Rule1BreakCount = attemptRule1BreakCount,
                Rule2BreakCount = attemptRule2BreakCount,
                // 1-based, matching CurrentLevelNumber's own convention - 0 outside a DC attempt.
                DcId = isDailyChallengeSession ? dailyChallengeSubLevelIndex + 1 : -1,
                PrePlacedTileTap = attemptPrePlacedTileTap,
            };
        }

        // Gates the NEW LogLevelFail call sites (Home/Restart clicks - see RestartLevel,
        // SaveAndShowMainMenu, AbandonLevelToMainMenu) so the event only fires when the level
        // actually failed or ran out of mistakes, never for an ordinary Settings-popup Quit/Home
        // during normal active play (both of which route through the same shared methods).
        // Daily Challenge is excluded entirely - CurrentLevelNumber doesn't identify a DC level
        // (see IsFtueLevel's own DC caveat elsewhere), so a DC fail can't be logged accurately here.
        private bool IsCurrentLevelFailedOrOutOfLives()
        {
            return !isDailyChallengeSession &&
                controller != null &&
                controller.Session.HasLevel &&
                (controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Failed ||
                 controller.Session.LevelMistakeLimit.IsOutOfMistakes);
        }

        // Levels 1-2 teach the single-tap mechanic (stop at Fish); level 3 teaches the two-tap
        // cycle to Cat (stop at Cat); every other FTUE level plays with the normal, uncapped tap
        // cycle.
        private static int ResolveFtueMaxTapsPerTile(int levelNumber)
        {
            if (levelNumber == 1 || levelNumber == 2)
            {
                return 1;
            }

            if (levelNumber == 3)
            {
                return 2;
            }

            return 0;
        }

        // How many taps (from Empty) it takes to reach this value, given the board's own
        // Empty -> Fish -> Cat -> Empty cycle (CatFlapRuntimeSession.NextValue).
        private static int CountFtueTapsConsumed(CatFlapCellValue value)
        {
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return 1;
                case CatFlapCellValue.Cat:
                    return 2;
                default:
                    return 0;
            }
        }

        // Designer-authored FTUE text lives in FtueStepsAsset ScriptableObjects (assigned in the
        // Inspector via ftueLevelSteps), not in level JSON.
        private List<FtueStepData> ResolveFtueSteps(int levelNumber)
        {
            if (ftueLevelSteps == null)
            {
                return null;
            }

            for (int index = 0; index < ftueLevelSteps.Length; index++)
            {
                CatFlapFtueLevelStepsEntry entry = ftueLevelSteps[index];
                if (entry != null && entry.LevelNumber == levelNumber)
                {
                    return entry.Steps != null ? entry.Steps.Steps : null;
                }
            }

            return null;
        }

        // 0 (the default on every entry except where explicitly authored otherwise) means "walk
        // through every step" - unchanged behaviour. See CatFlapFtueLevelStepsEntry.WalkthroughStepCount/
        // CatFlapFtueDriver.Configure for what a positive value does.
        private int ResolveFtueWalkthroughStepCount(int levelNumber)
        {
            if (ftueLevelSteps == null)
            {
                return 0;
            }

            for (int index = 0; index < ftueLevelSteps.Length; index++)
            {
                CatFlapFtueLevelStepsEntry entry = ftueLevelSteps[index];
                if (entry != null && entry.LevelNumber == levelNumber)
                {
                    return entry.WalkthroughStepCount;
                }
            }

            return 0;
        }

        // See CatFlapFtueLevelStepsEntry.EnableRulesWalkthrough - true only for the one level
        // entry authored with it (Level 4).
        private bool ResolveFtueRulesWalkthrough(int levelNumber)
        {
            if (ftueLevelSteps == null)
            {
                return false;
            }

            for (int index = 0; index < ftueLevelSteps.Length; index++)
            {
                CatFlapFtueLevelStepsEntry entry = ftueLevelSteps[index];
                if (entry != null && entry.LevelNumber == levelNumber)
                {
                    return entry.EnableRulesWalkthrough;
                }
            }

            return false;
        }

        // Drives the standalone FTUE module (CatFlapFtueDriver -> Shared.Ftue.FtueController)
        // and hides the Settings button/booster panel while it's active - coins and the level
        // number stay visible, only Settings is off-limits during the tutorial. All FTUE
        // "Skip Tutorial" button - only meaningful during an actual FTUE level (1..
        // FtueEnabledUntilLevel). Turns the guided walkthrough off immediately (same as a normal
        // FTUE-end - see ApplyFtueState), then auto-solves the board via the same ForceWin path
        // the cheat panel's Win Level button already uses. Unlike a real completion, this skips
        // the board-wide win celebration entirely (see skippingFtueTutorial/
        // PlayLevelWinCelebrationThenShowPopup) and lands directly on either the board-slide-to-
        // next-level transition (levels 1..3) or the normal win popup (level 4).
        public void SkipFtueTutorial()
        {
            if (controller == null || !controller.Session.HasLevel || !IsFtueLevel(CurrentLevelNumber))
            {
                return;
            }

            // ftue_id: 1-based, which step of THIS level's walkthrough the player was on when
            // they skipped - see CatFlapFtueDriver.CurrentStepNumber.
            AnalyticsManager.Instance.LogFtueLevelSkip(CurrentLevelNumber, ftueDriver?.CurrentStepNumber ?? 1);

            // Instant, not the normal animated hide - the equivalent through Configure/Refresh ->
            // FtueController.Hide() has a fade-out that takes real time and would otherwise
            // visibly hang in its old on-screen position (it's a screen-anchored UI element, not
            // a child of the world-space board) while ForceWin's board-slide transition is
            // already sliding the board out from under it.
            ftueDriver?.ForceHideImmediate();

            // Deliberately just the driver, NOT the full ApplyFtueState(false, ...) - that call
            // also flips the Rules panel/Booster panel/Coins HUD/Settings+Info buttons straight
            // back to visible, which a REAL completion never does mid-transition (those only
            // ever come back once LoadAssignedLevel runs ApplyFtueState fresh for whatever level
            // loads next, staying hidden if that's also an FTUE level). Skipping must match that
            // exactly, not flash the normal HUD on over the still-showing FTUE board first.
            ftueDriver?.Configure(false, null, controller?.Session);
            // Own button included - ApplyFtueState would normally hide this too, and
            // PlayFtueLevelTransition (levels 1..3) hides it again anyway, but the level 4 case
            // falls straight through to the win popup instead, which never calls that.
            skipTutorialButtonRoot?.SetActive(false);

            // Levels 1..(FtueEnabledUntilLevel - 1): must land on the exact same "no popup,
            // straight into the next board" transition a real completion gets - forced here
            // explicitly, rather than trusting ForceWin -> LevelWon -> HandleLevelWon's own
            // SuppressWinFailPages=true (set later, inside that handler) to always win the race
            // against ForceWin's own board-wide repaint. The last FTUE level (today: level 4)
            // deliberately does NOT set this - skipping it must show the normal level-complete
            // popup, exactly like actually finishing it would.
            if (uiFlowManager != null && CurrentLevelNumber < activeConfig.FtueEnabledUntilLevel)
            {
                uiFlowManager.SuppressWinFailPages = true;
            }

            skippingFtueTutorial = true;
            controller.ForceWin();
        }

        // gameplay-specific decisions (which levels, which steps) live here - the shared module
        // itself has no opinion on any of this.
        private void ApplyFtueState(bool active, List<FtueStepData> steps)
        {
            // Turning FTUE OFF (or off for good, e.g. into a Daily Challenge/non-FTUE level) must
            // clear any stale target/highlight from whatever was previously active immediately -
            // this can't wait for the entrance animation, since input stays gated on
            // ftueDriver.IsActive/CurrentTarget (see HandleCellSelected). Turning it ON is
            // deferred instead - see PlayLevelStartEntranceSequence/FinishLevelStartEntrance,
            // which calls Configure(true, ...) only once every entrance animation has finished.
            if (!active)
            {
                ftueDriver?.Configure(false, null, controller?.Session);
            }

            // Level 4's rules walkthrough (see CatFlapFtueDriver.ConfigureRulesWalkthrough) teaches
            // Equal Counts/Max Two Adjacent by revealing each rule row on the panel itself, so the
            // panel CONTAINER must stay visible there (unlike every other FTUE level, which hides
            // it outright) while every individual row starts hidden until the driver reveals it.
            bool isRulesWalkthroughLevel = active && ResolveFtueRulesWalkthrough(CurrentLevelNumber);
            statusView?.SetSettingsButtonVisible(!active);
            statusView?.SetRulesPanelVisible(isRulesWalkthroughLevel || !active);
            // The Rules panel's own overriding Canvas (see SetRulesPanelOverlaySortingActive) only
            // needs to sort above everything else while Level 4's rules walkthrough is actively
            // dimming the screen - everywhere else (levels 1-3, and every level once FTUE ends) it
            // must go back to normal sibling-order sorting, or it bleeds on top of other popups
            // (level win/fail, settings, out-of-lives) that don't carry a matching override.
            statusView?.SetRulesPanelOverlaySortingActive(isRulesWalkthroughLevel);
            statusView?.SetInfoButtonVisible(!active);
            // Coins HUD stays hidden for every FTUE level (1-4), including level 4's rules
            // walkthrough - it only starts showing once gameplay HUD returns to normal (this level
            // or a later one, non-FTUE). Level 4's win popup shows its coin reward regardless (see
            // LevelWinPopupController), independent of this top-HUD toggle.
            statusView?.SetCoinsHudVisible(!active);

            if (isRulesWalkthroughLevel)
            {
                statusView?.SetRuleRowVisible(CatFlapRule.EqualCounts, false);
                statusView?.SetRuleRowVisible(CatFlapRule.MaxTwoAdjacent, false);
                statusView?.SetRuleRowVisible(CatFlapRule.UniqueLines, false);
            }
            else
            {
                // Leaving FTUE (or any non-rules-walkthrough level) - make sure the two rules the
                // game actually enforces are back to visible in case a previous Level 4 session
                // left them hidden. UniqueLines is never shown here (or anywhere) - only
                // EqualCounts and MaxTwoAdjacent are real, validated rules (see
                // CatFlapRuleValidator.Validate/ValidateLines, which never call
                // ValidateUniqueLines) - the game only has 2 rules.
                statusView?.SetRuleRowVisible(CatFlapRule.EqualCounts, true);
                statusView?.SetRuleRowVisible(CatFlapRule.MaxTwoAdjacent, true);
                statusView?.SetRuleRowVisible(CatFlapRule.UniqueLines, false);
            }
            if (boosterPanelCanvasGroup != null)
            {
                boosterPanelCanvasGroup.gameObject.SetActive(!active);
            }

            if (skipTutorialButtonRoot != null)
            {
                UpdateSkipTutorialButtonPosition();
                skipTutorialButtonRoot.SetActive(active);
            }
        }

        // Levels 1-3 have a Rules panel row below the board's own frame, but level 4's rules
        // walkthrough keeps that panel visible/active throughout (see isRulesWalkthroughLevel in
        // ApplyFtueState above), which needs the button to sit higher (closer to the board) so it
        // doesn't overlap that panel.
        private static readonly Vector2 SkipTutorialButtonPositionLevels1To3 = new Vector2(-220f, -100f);
        private static readonly Vector2 SkipTutorialButtonPositionLevel4 = new Vector2(-220f, 0f);

        private void UpdateSkipTutorialButtonPosition()
        {
            if (skipTutorialButtonRoot == null || skipTutorialButtonRoot.transform is not RectTransform rect)
            {
                return;
            }

            rect.anchoredPosition = CurrentLevelNumber >= activeConfig.FtueEnabledUntilLevel
                ? SkipTutorialButtonPositionLevel4
                : SkipTutorialButtonPositionLevels1To3;
        }

        // Key in PlayerData.Variables (see Shared.PlayerData.PlayerData) marking the "Lives are
        // lost on wrong moves" hearts tip as already seen - persisted so it never shows again
        // once dismissed, even across relaunches.
        private const string LevelAttemptTipSeenKey = "Ftue.SeenLevelAttemptTip";

        // Shows the one-time "Lives are lost on wrong moves" tip pointing at the hearts HUD the
        // first time Level Attempt turns on for this player, ever. No-ops (and never shows again)
        // once already seen, or if Level Attempt isn't actually active for this level.
        private void ShowLevelAttemptTipIfNeeded()
        {
            if (controller == null || !controller.LevelAttemptFeatureEnabled || statusView == null)
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

        private void RefreshLevelFlowControls()
        {
            uiFlowManager?.SetHasNextLevel(HasNextLevel);
        }

        private void BindSharedBoosterEvents()
        {
            ResolveSharedBoosterManager();
            if (sharedBoosterManager == null)
            {
                return;
            }

            sharedBoosterManager.ApplyRulesConfig(activeRulesConfig);
            sharedBoosterManager.HintImplementationRequested.RemoveListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.RemoveListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.RemoveListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.RemoveListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.RemoveListener(HandleBoosterImplementationStarted);
            sharedBoosterManager.HintImplementationRequested.AddListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.AddListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.AddListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.AddListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.AddListener(HandleBoosterImplementationStarted);
        }

        private void UnbindSharedBoosterEvents()
        {
            if (sharedBoosterManager == null)
            {
                return;
            }

            sharedBoosterManager.HintImplementationRequested.RemoveListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.RemoveListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.RemoveListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.RemoveListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.RemoveListener(HandleBoosterImplementationStarted);
        }

        // Hint's idle-attention highlight (see CatFlapHintHighlightAnimator) is postponed the
        // instant the player actually uses ANY booster while it's implemented as "the hint booster
        // specifically was used" - Fix/Undo don't touch it, only Hint's own implementation start
        // matters here.
        private void HandleBoosterImplementationStarted(BoosterType boosterType)
        {
            if (boosterType == BoosterType.Hint)
            {
                hintHighlightAnimator?.ResetTimer();
            }
        }

        // (Re)points the Hint highlight at the Hint button's own RectTransform and its live
        // availability check - called anywhere sharedBoosterManager.Configure(...) is (a fresh
        // level load, restart, or resume), since GetBoosterButton/IsBoosterAvailable both need the
        // wallet/level-number provider Configure just set up.
        private void ConfigureHintHighlight()
        {
            if (hintHighlightAnimator == null || sharedBoosterManager == null)
            {
                return;
            }

            BoosterButtonUI hintButton = sharedBoosterManager.GetBoosterButton(BoosterType.Hint);
            RectTransform hintTransform = hintButton != null ? hintButton.transform as RectTransform : null;
            hintHighlightAnimator.Configure(
                hintTransform,
                activeRulesConfig.HintHighlightIdleSeconds,
                () => sharedBoosterManager != null && sharedBoosterManager.IsBoosterAvailable(BoosterType.Hint));
        }

        private void HandleBoosterPurchaseRequested(BoosterType boosterType)
        {
            if (controller == null || !controller.Session.HasLevel || controller.Session.BoosterWallet == null)
            {
                return;
            }

            pendingBuyBoosterType = boosterType;
            buyBoosterPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            BindBuyBoosterPopupEvents();

            BoosterWallet wallet = controller.Session.BoosterWallet;
            int buyCount = wallet.Config.GetBuyCount(boosterType);
            int adBuyCount = wallet.Config.GetAdBuyCount(boosterType);
            int coinCost = wallet.Config.GetCost(boosterType);
            uiFlowManager?.ShowBuyBoosterPopup(boosterType, buyCount, adBuyCount, coinCost);
        }

        private void BindBuyBoosterPopupEvents()
        {
            BuyBoosterPopupController popup = uiFlowManager != null ? uiFlowManager.BuyBoosterPopup : null;
            if (popup == null)
            {
                return;
            }

            popup.BuyRequested.RemoveListener(HandleBuyBoosterConfirm);
            popup.CancelRequested.RemoveListener(HandleBuyBoosterCancel);
            popup.WatchAdRequested.RemoveListener(HandleBuyBoosterWatchAd);
            popup.BuyRequested.AddListener(HandleBuyBoosterConfirm);
            popup.CancelRequested.AddListener(HandleBuyBoosterCancel);
            popup.WatchAdRequested.AddListener(HandleBuyBoosterWatchAd);
        }

        // Routes through the shared AdsManager - once actually rewarded, grants one free charge
        // of the pending booster type without spending coins.
        private void HandleBuyBoosterWatchAd()
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.BuyHintBooster, success =>
            {
                if (success)
                {
                    GrantBuyBoosterWatchAdReward();
                }
            });
        }

        private void GrantBuyBoosterWatchAdReward()
        {
            if (controller != null && controller.Session.HasLevel && controller.Session.BoosterWallet != null)
            {
                controller.Session.BoosterWallet.RefundCharge(pendingBuyBoosterType);
                SaveAll();
            }

            if (pendingBuyBoosterType == BoosterType.Hint)
            {
                AnalyticsManager.Instance.LogHintPurchasedPopup(CurrentLevelNumber, hintPurchasedCoin: 0, hintPurchasedAd: 1);
            }

            ContinueAfterBuyBoosterPopup();
        }

        private void HandleBuyBoosterConfirm()
        {
            if (controller != null && controller.Session.HasLevel && controller.Session.BoosterWallet != null)
            {
                BoosterWallet wallet = controller.Session.BoosterWallet;
                int boosterCost = wallet.Config.GetCost(pendingBuyBoosterType);
                if (wallet.TryPurchaseCharge(pendingBuyBoosterType))
                {
                    AnalyticsManager.Instance.LogBoosterPurchased(pendingBuyBoosterType.ToString(), CurrentLevelNumber);
                    AnalyticsManager.Instance.LogCurrencySink(
                        AnalyticsEvents.CurrencyTypeValues.Coins,
                        boosterCost,
                        "booster_" + pendingBuyBoosterType.ToString().ToLowerInvariant(),
                        wallet.Config.GetBuyCount(pendingBuyBoosterType),
                        wallet.CurrentCoins);
                    if (pendingBuyBoosterType == BoosterType.Hint)
                    {
                        AnalyticsManager.Instance.LogHintPurchasedPopup(CurrentLevelNumber, hintPurchasedCoin: boosterCost, hintPurchasedAd: 0);
                    }

                    SaveAll();
                    ContinueAfterBuyBoosterPopup();
                    return;
                }
            }

            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseBuyBoosterPopup(() =>
                {
                    buyBoosterPopupOpen = false;
                    OpenStorePopup();
                });
                return;
            }

            buyBoosterPopupOpen = false;
            OpenStorePopup();
        }

        private void HandleBuyBoosterCancel()
        {
            ContinueAfterBuyBoosterPopup();
        }

        private void ContinueAfterBuyBoosterPopup()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseBuyBoosterPopup(ResumeAfterBuyBoosterPopupClosed);
                return;
            }

            ResumeAfterBuyBoosterPopupClosed();
        }

        private void ResumeAfterBuyBoosterPopupClosed()
        {
            buyBoosterPopupOpen = false;
            sharedBoosterManager?.RefreshAllBoosterUI();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Playing;
        }

        public void OpenStorePopup()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            storePopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            BindStorePopupEvents();
            uiFlowManager?.ShowStorePopup();
            AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, string.Empty, Shared.PlayerData.PlayerData.Instance.Analytics.IapPackPurchaseCount, shopOpen: true);
        }

        private void BindStorePopupEvents()
        {
            Shared.Store.UnityAdapters.StorePopupController popup = uiFlowManager != null ? uiFlowManager.StorePopup : null;
            if (popup == null)
            {
                return;
            }

            popup.CloseRequested.RemoveListener(HandleStoreCloseRequested);
            popup.PurchaseRequested.RemoveListener(HandleStorePurchaseRequested);
            popup.CloseRequested.AddListener(HandleStoreCloseRequested);
            popup.PurchaseRequested.AddListener(HandleStorePurchaseRequested);
        }

        private void HandleStoreCloseRequested()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseStorePopup(ResumeAfterStorePopupClosed);
                return;
            }

            ResumeAfterStorePopupClosed();
        }

        private void ResumeAfterStorePopupClosed()
        {
            storePopupOpen = false;
            sharedBoosterManager?.RefreshAllBoosterUI();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.GameState.Current == CatFlap.Models.CatFlapGameState.Playing;
        }

        // Grants the reward for a purchase InAppManager just recovered OUTSIDE the normal
        // Purchase()-request flow - see InAppManager.UnattendedPurchaseGranted's own comment.
        // This is exactly what closes the "paid but never got the reward, then can't re-purchase
        // because the store still thinks it's owned" gap: RestorePurchases (run once at app
        // startup by BootLoader, and again on demand from Settings) re-validates any purchase
        // still sitting unconsumed and finalizes it, and this is where that finalized purchase's
        // coins/unlock actually reaches the player.
        private void HandleUnattendedPurchaseGranted(string productId)
        {
            Shared.Store.StoreItemDefinition item = storeManager?.FindItemByProductId(productId);
            if (item == null)
            {
                Debug.LogWarning($"{nameof(CatFlapGameplayManager)}: recovered a purchase for product '{productId}' but no StoreItemDefinition in storeConfigAsset has that ProductId - reward could not be granted.");
                return;
            }

            storeManager?.CompletePurchase(item.ItemId);
            Shared.PlayerData.PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
            analytics.IapPackPurchaseCount++;
            AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, item.ItemId, analytics.IapPackPurchaseCount, shopOpen: false);
            sharedBoosterManager?.RefreshAllBoosterUI();
        }

        private void HandleStorePurchaseRequested(string itemId)
        {
            Shared.Store.StoreItemDefinition item = storeManager?.FindItem(itemId);
            if (item == null)
            {
                // Surfaced rather than silently doing nothing - a UI button firing an itemId that
                // doesn't exist in storeConfigAsset means the button's binding and the config data
                // it was bound from have drifted apart.
                Debug.LogWarning($"{nameof(CatFlapGameplayManager)}: no StoreItemDefinition found for itemId '{itemId}' - check storeConfigAsset.");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.ProductId))
            {
                Debug.LogWarning($"{nameof(CatFlapGameplayManager)}: StoreItemDefinition '{itemId}' has no ProductId set - purchase will still simulate in the Editor, but will always fail on a real device build.");
            }

            InAppManager.Instance.Purchase(item.ProductId, success =>
            {
                if (success)
                {
                    storeManager?.CompletePurchase(itemId);
                    Shared.PlayerData.PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
                    analytics.IapPackPurchaseCount++;
                    AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, itemId, analytics.IapPackPurchaseCount, shopOpen: false);
                }

                sharedBoosterManager?.RefreshAllBoosterUI();
            }, item.CoinAmount, item.Type == Shared.Store.StoreItemType.CoinPack, item.IconKey);
        }

        private void GrantStoreCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (controller != null && controller.Session.HasLevel)
            {
                controller.Session.AddCoins(amount);
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    amount,
                    "iap_coin_pack",
                    controller.Session.BoosterWallet.CurrentCoins);
                SaveAll();
            }
            else
            {
                // No level loaded (e.g. purchased from the Main Menu/Home shop, not mid-gameplay) -
                // there's no active Session.BoosterWallet to credit and SaveAll() no-ops without a
                // level too (see SaveAll's own HasLevel guard), so this would otherwise silently
                // drop the purchase: the popup still shows and the coin-fly animation still plays,
                // but PlayerData.TotalCoins - and everything reading it (HUD, shop) - never actually
                // changes. Grant straight to the persisted total instead.
                GrantStoreCoinsWithoutSession(amount);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
        }

        private static void GrantStoreCoinsWithoutSession(int amount)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.TotalCoins = Mathf.Max(0, data.TotalCoins + amount);
            data.Touch();

            AnalyticsManager.Instance.LogCurrencySource(
                AnalyticsEvents.CurrencyTypeValues.Coins,
                amount,
                "iap_coin_pack",
                data.TotalCoins);

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }

        private void ResolveSharedBoosterManager()
        {
            if (sharedBoosterManager != null)
            {
                return;
            }

            sharedBoosterManager = transform.root.GetComponentInChildren<Shared.Boosters.BoosterManager>(true);
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

        // Plays the per-value board_tile_* cue for whatever value is now sitting in (row, column)
        // after a tap/hint placement - Fish, Cat, or back to Empty.
        private void PlayCellTapSound(int row, int column)
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            switch (controller.Session.Board.GetEffectiveValue(row, column))
            {
                case CatFlapCellValue.Fish:
                    PlaySound(global::SoundName.TapFish);
                    break;
                case CatFlapCellValue.Cat:
                    PlaySound(global::SoundName.TapCat);
                    break;
                default:
                    PlaySound(global::SoundName.TapEmpty);
                    break;
            }
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
            if (boardView is CatFlapBoardViewAdapter uiBoardView)
            {
                uiBoardView.ApplyRulesConfig(activeRulesConfig);
            }
            else if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                spriteBoardView.ApplyRulesConfig(activeRulesConfig);
            }

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
                int targetCoins = Mathf.Max(0, PlayerData.Instance.TotalCoins);
                controller.Session.BoosterWallet.SetCoins(targetCoins);
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
