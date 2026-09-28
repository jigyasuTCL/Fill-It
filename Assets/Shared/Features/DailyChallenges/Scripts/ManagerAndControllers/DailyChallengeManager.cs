using System;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using MangraGames;
using Shared.Currency;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    /// <summary>
    /// Composition root for the Daily Challenge popup. All cross-component wiring - controller
    /// event subscriptions, button.onClick listeners - lives here in Awake/OnDestroy. The popup's
    /// child views (DailyChallengePlayButtonController, DailyChallengeRewardTrackController,
    /// DailyChallengeNextDayPromptController) are deliberately dumb: they expose public
    /// Refresh/Show/Hide methods and have no OnEnable/OnDisable of their own, because nothing in
    /// their behavior depends on their own GameObject's active state - Manager already knows
    /// exactly when the popup opens and closes and drives them explicitly from ShowPopup()/
    /// ClosePopup() instead of letting each one react independently.
    ///
    /// The one exception, by design, is DailyChallengeDateButton: each calendar cell's countdown
    /// coroutine is tied to *that individual cell's* own active state (cells are instantiated/
    /// destroyed per month by DailyChallengeController, and Unity gives Manager no hook into an
    /// individual child's enable/disable), so that component keeps its own OnEnable/OnDisable.
    /// Same reasoning applies to the Main Menu's DailyChallengeButtonUI, which isn't owned by
    /// this popup at all.
    /// </summary>
    public sealed class DailyChallengeManager : MonoBehaviour
    {
        [SerializeField]
        private DailyChallengeController challengeController = default;

        [SerializeField]
        private int year = 0;

        [SerializeField]
        private int month = 0;

        public GameObject GamePlayPanel = default;

        [Header("Popup")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private Button closeButton;

        [Header("Popup Locked State (optional - shown instead of the calendar when DC itself is locked)")]
        [SerializeField] private GameObject lockedRoot;
        [SerializeField] private GameObject calendarRoot;
        [SerializeField] private TMPro.TMP_Text lockedMessageLabel;

        [Header("Popup Child Views - wired here, not self-wired (see class summary)")]
        [SerializeField] private DailyChallengePlayButtonController playButtonController;
        [SerializeField] private DailyChallengeRewardTrackController rewardTrackController;
        [SerializeField] private DailyChallengeNextDayPromptController nextDayPromptController;

        [Header("Coin Total")]
        [Tooltip("Shows the player's current coin total on this popup. Refreshed every time the popup opens and whenever a reward tier is claimed.")]
        [SerializeField] private CoinContainerView coinContainer;

        [Header("Entry Animation")]
        [Tooltip("Popup elements that pop in one after another when ShowPopup() runs, in this " +
                 "exact order: TopHUD -> ProgressArea -> Date -> PlayButton. Leave an entry " +
                 "unassigned to skip it.")]
        [SerializeField] private List<GameObject> entryAnimationTargets = new List<GameObject>();
        [Tooltip("Delay added per target index in the sequence - e.g. the 3rd target (Date) " +
                 "starts entryAnimationStaggerSeconds * 2 after the 1st (TopHUD).")]
        [SerializeField] private float entryAnimationStaggerSeconds = 0.08f;
        [SerializeField] private float entryAnimationDurationSeconds = 0.25f;
        [SerializeField] private Ease entryAnimationEase = Ease.OutBack;

        private Coroutine entryAnimationRoutine;

        [Header("Cheat - Date Jump")]
        [Tooltip("How many days jumpDateButton jumps forward when tapped (negative = backward). " +
                 "Read fresh from the input every tap, so leaving it blank falls back to 1.")]
        [SerializeField] private TMPro.TMP_InputField jumpDaysInput;
        [SerializeField] private Button jumpDateButton;
        [SerializeField] private Button resetDateButton;

        public DailyChallengeController Controller => challengeController;

        [Header("Puzzle Naming")]
        [SerializeField] private string puzzleNamePrefix = "Puzzle";
        [SerializeField] private string puzzleNameMiddle = "_";

        public string PuzzleNamePrefix => puzzleNamePrefix;
        public string PuzzleNameMiddle => puzzleNameMiddle;

        private void Awake()
        {
            if (challengeController == null)
            {
                Debug.LogWarning("DailyChallengeManager requires a DailyChallengeController reference.");
                return;
            }

            if (year <= 0 || month <= 0)
            {
                challengeController.InitializeCurrentMonth();
            }
            else
            {
                challengeController.Initialize(year, month);
            }

            challengeController.DebugPrintData();

            closeButton?.onClick.AddListener(ClosePopup);
            jumpDateButton?.onClick.AddListener(HandleJumpDateClicked);
            resetDateButton?.onClick.AddListener(HandleResetDateClicked);

            WireChildViews();

            // Reflect whatever's already selected (SelectDefaultDay ran inside Initialize above)
            // before the popup is ever shown, so the first ShowPopup() doesn't show stale/empty
            // child views for a frame.
            RefreshPopupChildren();
        }

        private void OnDestroy()
        {
            closeButton?.onClick.RemoveListener(ClosePopup);
            jumpDateButton?.onClick.RemoveListener(HandleJumpDateClicked);
            resetDateButton?.onClick.RemoveListener(HandleResetDateClicked);

            UnwireChildViews();
        }

        // -----------------------------------------------------------------
        // CHILD VIEW WIRING
        // -----------------------------------------------------------------

        private void WireChildViews()
        {
            if (challengeController == null)
            {
                return;
            }

            if (playButtonController != null)
            {
                challengeController.SelectedDayChanged += playButtonController.Refresh;

                if (playButtonController.Button != null)
                {
                    playButtonController.Button.onClick.AddListener(HandlePlayButtonClicked);
                }
            }

            if (nextDayPromptController != null)
            {
                challengeController.SelectedDayChanged += nextDayPromptController.HandleSelectedDayChanged;
                challengeController.DailyChallengeResultResolved += nextDayPromptController.HandleResultResolved;
            }

            if (rewardTrackController != null)
            {
                challengeController.RewardTierClaimed += HandleRewardTierClaimedForTrack;
            }
        }

        private void UnwireChildViews()
        {
            if (challengeController == null)
            {
                return;
            }

            if (playButtonController != null)
            {
                challengeController.SelectedDayChanged -= playButtonController.Refresh;

                if (playButtonController.Button != null)
                {
                    playButtonController.Button.onClick.RemoveListener(HandlePlayButtonClicked);
                }
            }

            if (nextDayPromptController != null)
            {
                challengeController.SelectedDayChanged -= nextDayPromptController.HandleSelectedDayChanged;
                challengeController.DailyChallengeResultResolved -= nextDayPromptController.HandleResultResolved;
            }

            if (rewardTrackController != null)
            {
                challengeController.RewardTierClaimed -= HandleRewardTierClaimedForTrack;
            }
        }

        private void HandlePlayButtonClicked()
        {
            challengeController?.PlayButtonClick();
        }

        private void HandleRewardTierClaimedForTrack(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            // Slots only, not Refresh() - claiming doesn't move the completed-day count the
            // progress bar reflects, so a full Refresh() here would replay the bar's fill tween a
            // second time for no visual change (see DailyChallengeRewardTrackController.RefreshSlotsOnly).
            rewardTrackController?.RefreshSlotsOnly();
            coinContainer?.Refresh();
        }

        // Pulls the current selection into every child view - SelectedDayChanged only fires on
        // the NEXT selection, so this covers "popup opens/re-opens with a selection that was
        // already made" (same reasoning each child previously handled itself in OnEnable).
        private void RefreshPopupChildren()
        {
            DailyChallengeData selected = challengeController != null ? challengeController.SelectedData : null;
            playButtonController?.Refresh(selected);
            nextDayPromptController?.HandleSelectedDayChanged(selected);
        }

        // -----------------------------------------------------------------
        // CHEAT - DATE JUMP
        // -----------------------------------------------------------------

        private void HandleJumpDateClicked()
        {
            if (challengeController == null)
            {
                return;
            }

            int days = 1;
            if (jumpDaysInput != null && !string.IsNullOrWhiteSpace(jumpDaysInput.text))
            {
                int.TryParse(jumpDaysInput.text, out days);
            }

            if (days == 0)
            {
                days = 1;
            }

            challengeController.CheatJumpDays(days);
            RefreshPopupChildren();
        }

        private void HandleResetDateClicked()
        {
            challengeController?.CheatResetToRealDate();
            RefreshPopupChildren();
        }

        /// <summary>
        /// Most mobile OSes pause the app rather than kill it, so a day boundary can pass while
        /// still resident - re-check unlock state as soon as the app comes back to the
        /// foreground so the Main Menu Daily Challenge widget doesn't stay stuck showing
        /// yesterday's "Completed" state (see DailyChallengeController.RefreshForPossibleDayChange).
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                challengeController?.RefreshForPossibleDayChange();
                RefreshPopupChildren();
            }
        }

        /// <summary>
        /// Fired at the end of ShowPopup() (never from ShowLockedPopup()) - lets callers outside
        /// this Shared feature that don't themselves open this popup (e.g. Cat Flap's
        /// MainMenuHandler, which owns the bottom-nav Daily Challenges toggle) stay in sync
        /// regardless of who actually triggered it - the Main Menu widget, the popup's own Back
        /// button, or UnlockManager auto-opening it right after a fresh unlock.
        /// </summary>
        public event Action PopupShown;

        public void ShowPopup()
        {
            ShowPopup(null);
        }

        // onComplete fires as soon as the popup is set active and its child views refreshed -
        // it does NOT wait for PlayEntranceAnimation() below to finish, so callers (e.g. the
        // post-unlock reveal flow) can rely on it firing synchronously rather than assuming
        // it means the entry animation has completed.
        public void ShowPopup(Action onComplete)
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(true);
            }

            SetLockedVisual(false, null);

            // Manager drives the popup's child views explicitly instead of each one reacting to
            // its own OnEnable (see class summary) - this is the "popup just became visible"
            // trigger point.
            RefreshPopupChildren();
            rewardTrackController?.Show();
            coinContainer?.Refresh();
            PlayEntranceAnimation();

            PopupShown?.Invoke();
            onComplete?.Invoke();
        }

        // Pops entryAnimationTargets into view one after another (zero -> full scale, ease back
        // out) in the exact order they're assigned - TopHUD -> ProgressArea -> Date -> PlayButton.
        // Only called from ShowPopup(): RefreshPopupChildren() also runs from other triggers
        // (date-jump cheats, app refocus) that must not re-play this every time.
        private void PlayEntranceAnimation()
        {
            if (entryAnimationRoutine != null)
            {
                StopCoroutine(entryAnimationRoutine);
                entryAnimationRoutine = null;
            }

            for (int index = 0; index < entryAnimationTargets.Count; index++)
            {
                GameObject target = entryAnimationTargets[index];
                if (target != null)
                {
                    target.transform.DOKill();
                    target.transform.localScale = Vector3.zero;
                }
            }

            entryAnimationRoutine = StartCoroutine(PlayEntranceAnimationNextFrame());
        }

        // Deferred by one frame so each target's layout group has had a chance to position it
        // before it starts animating - same historical fix ShopPageController.PlayEntranceAnimation
        // relies on for content not being fully settled yet on the very first show.
        private IEnumerator PlayEntranceAnimationNextFrame()
        {
            yield return null;

            for (int index = 0; index < entryAnimationTargets.Count; index++)
            {
                GameObject target = entryAnimationTargets[index];
                if (target == null)
                {
                    continue;
                }

                target.transform.DOScale(Vector3.one, entryAnimationDurationSeconds)
                    .SetEase(entryAnimationEase)
                    .SetDelay(index * entryAnimationStaggerSeconds)
                    .SetUpdate(true);
            }

            entryAnimationRoutine = null;
        }

        /// <summary>
        /// Opens the popup showing its locked state instead of the calendar - used when the
        /// player taps the Daily Challenge area while the feature itself is still locked (GDD
        /// section 1: tapping while locked opens the popup showing the locked state, it must not
        /// load gameplay). Falls back to a plain ShowPopup() if lockedRoot/calendarRoot haven't
        /// been assigned in the Inspector yet.
        /// </summary>
        public void ShowLockedPopup(string message)
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(true);
            }

            SetLockedVisual(true, message);
        }

        private void SetLockedVisual(bool locked, string message)
        {
            if (lockedRoot != null)
            {
                lockedRoot.SetActive(locked);
            }

            if (calendarRoot != null)
            {
                calendarRoot.SetActive(!locked);
            }

            if (locked && lockedMessageLabel != null && !string.IsNullOrEmpty(message))
            {
                lockedMessageLabel.text = message;
            }
        }

        public void ClosePopup()
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(false);
            }

            // Explicit cleanup in place of the OnDisable each child used to have - Manager is the
            // only place that knows the popup just closed.
            rewardTrackController?.Hide();
            nextDayPromptController?.Hide();
        }

        public void ResetAllDailyChallengeStates()
        {
            if (challengeController == null)
            {
                Debug.LogWarning("DailyChallengeManager requires a DailyChallengeController reference.");
                return;
            }

            challengeController.ResetAllStates();
            RefreshPopupChildren();
        }

        /// <summary>
        /// Debug-only: fakes one more completed day this month and re-drives the reward track the
        /// same way ShowPopup() would - so a tier that just became claimable auto-claims and plays
        /// its collect animation, letting the whole reward progression be tested without actually
        /// winning puzzles.
        /// </summary>
        public void DebugAddRewardProgressDay()
        {
            if (challengeController == null)
            {
                Debug.LogWarning("DailyChallengeManager requires a DailyChallengeController reference.");
                return;
            }

            challengeController.DebugAddCompletedDayThisMonth();
            rewardTrackController?.Show();
            coinContainer?.Refresh();
        }

        /// <summary>Debug-only: clears this month's reward progress/claims back to empty.</summary>
        public void DebugResetRewardProgress()
        {
            if (challengeController == null)
            {
                Debug.LogWarning("DailyChallengeManager requires a DailyChallengeController reference.");
                return;
            }

            challengeController.DebugResetRewardProgress();
            rewardTrackController?.Refresh();
            coinContainer?.Refresh();
        }

        /// <summary>
        /// Debug-only: simulates fully winning the current day (see
        /// DailyChallengeController.DebugSimulateFullDayWin) and shows this popup right after, so
        /// the reward track's auto-claim/collect animation plays exactly as it would after a real
        /// win. If CatFlapDailyChallengeBridge is active in the scene, its own win result popup
        /// also shows first (from the same DailyChallengeResultResolved event this fires) before
        /// this popup takes over - same chain a real win produces.
        /// </summary>
        public void DebugSimulateFullDayWinAndShowRewardScreen()
        {
            if (challengeController == null)
            {
                Debug.LogWarning("DailyChallengeManager requires a DailyChallengeController reference.");
                return;
            }

            challengeController.DebugSimulateFullDayWin();
            ShowPopup();
        }

        /// <summary>
        /// Routes through the shared AdsManager (RV_DailyChallengePlay) and invokes callback only
        /// if the ad was actually rewarded.
        /// </summary>
        public void WatchAdForDailyChallenge(Action callback)
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DailyChallengePlay, success =>
            {
                if (success)
                {
                    callback?.Invoke();
                }
            });
        }

        public void ShowGamePlayPanel()
        {
            if (GamePlayPanel != null)
            {
                GamePlayPanel.SetActive(true);
            }
        }

        public void HideGamePlayPanel()
        {
            if (GamePlayPanel != null)
            {
                GamePlayPanel.SetActive(false);
            }

            // Coming back from gameplay is another "state may have changed" point - a win may
            // have just been reported, which SelectedDayChanged/DailyChallengeResultResolved
            // already push to the child views, but this covers the panel toggle itself too.
            RefreshPopupChildren();
        }
    }
}