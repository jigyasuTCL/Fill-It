using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Shared.Boot;
using Shared.GameConfig;
using Shared.GameFlow;

namespace DailyChallenges
{
    public sealed class DailyChallengeButtonUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject lockRoot;
        [SerializeField] private TMP_Text lockLabel;
        [SerializeField] private GameObject timerRoot;
        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private TMP_Text dateLabel;

        [Header("Play CTA - tapping this loads gameplay directly, it must NOT also open the popup")]
        [SerializeField] private Button playButton;
        [SerializeField] private GameObject completedRoot;
        [SerializeField] private TMP_Text completedLabel;
        [SerializeField] private string completedText = "Completed";

        [SerializeField] private string dateFormat = "dd MMM";
        [SerializeField] private float refreshIntervalSeconds = 1f;

        [Header("Unlock Flow")]
        [Tooltip("Used by UnlockTheFeatureFinal to open the same calendar popup ShowPopup() " +
                 "opens, once this feature's unlock animation sequence completes.")]
        [SerializeField] private DailyChallengeManager dailyChallengeManager;

        [Header("Unlock Reveal - hidden by OnUnlockStart, grows/moves into place in UnlockTheFeatureFinal")]
        [SerializeField] private GameObject unlockRevealObject;
        [SerializeField] private Vector3 unlockStartPosition;
        [SerializeField] private Vector3 unlockStartScale;
        [SerializeField] private Vector3 unlockEndPosition;
        [SerializeField] private Vector3 unlockEndScale;
        [SerializeField] private float unlockRevealDuration = 0.4f;

        /// <summary>Fired when the Play CTA is tapped - Main Menu wires this to
        /// DailyChallengeController.PlayCurrentChallengeDirectly, never to ShowPopup.</summary>
        public event Action PlayRequested;

        private Func<int> currentLevelNumberProvider;
        private DailyChallengeController controller;
        private Coroutine refreshRoutine;

        public void Configure(Func<int> currentLevelNumberProvider, DailyChallengeController controller)
        {
            this.currentLevelNumberProvider = currentLevelNumberProvider;
            this.controller = controller;
            Refresh();
        }

        private void OnEnable()
        {
            playButton?.onClick.AddListener(HandlePlayClicked);

            // Refresh immediately on enable - even before Configure() has supplied a real
            // controller, Refresh() now defaults every indeterminate case to Play rather than a
            // false "Completed" (see the confirmedWin check below), so there's no flicker risk in
            // refreshing early. Waiting for the RefreshLoop coroutine's first tick instead would
            // leave the button unrefreshed for up to refreshIntervalSeconds every time it enables.
            Refresh();

            StopRefreshLoop();
            refreshRoutine = StartCoroutine(RefreshLoop());
        }

        private void OnDisable()
        {
            playButton?.onClick.RemoveListener(HandlePlayClicked);
            StopRefreshLoop();
        }

        public void HandlePlayClicked()
        {
            PlayRequested?.Invoke();
        }

        /// <summary>Called when this feature's unlock flow begins (see
        /// UnlockManager.ShowNextInQueue) - hides unlockRevealObject (if assigned) so it's not on
        /// screen until UnlockTheFeatureFinal reveals it, and refreshes this widget's own state
        /// so it reads correctly by the time the unlock popup shows.</summary>
        public void OnUnlockStart()
        {
            if (unlockRevealObject != null)
            {
                unlockRevealObject.SetActive(false);
            }

            Refresh();
        }

        /// <summary>Final call once the unlock flow (popup reveal + fly-to-target) completes -
        /// plays unlockRevealObject's own reveal (grows/moves from unlockStartPosition/
        /// unlockStartScale to unlockEndPosition/unlockEndScale) and only opens the same calendar
        /// popup a normal tap on the Daily Challenge area would (see
        /// DailyChallengeManager.ShowPopup) once that finishes. Falls back to opening immediately
        /// if unlockRevealObject isn't assigned.</summary>
        public void UnlockTheFeatureFinal(Action onComplete = null)
        {
            if (unlockRevealObject == null)
            {
                if (dailyChallengeManager != null)
                {
                    dailyChallengeManager.ShowPopup(onComplete);
                }
                else
                {
                    onComplete?.Invoke();
                }
                return;
            }

            unlockRevealObject.SetActive(true);
            unlockRevealObject.transform.localPosition = unlockStartPosition;
            unlockRevealObject.transform.localScale = unlockStartScale;

            unlockRevealObject.transform.DOKill();

            Sequence sequence = DOTween.Sequence();
            sequence.Join(unlockRevealObject.transform.DOLocalMove(unlockEndPosition, unlockRevealDuration).SetEase(Ease.OutBack));
            sequence.Join(unlockRevealObject.transform.DOScale(unlockEndScale, unlockRevealDuration).SetEase(Ease.OutBack));
            sequence.OnComplete(() =>
            {
                if (dailyChallengeManager != null)
                {
                    dailyChallengeManager.ShowPopup(onComplete);
                }
                else
                {
                    onComplete?.Invoke();
                }
            });
        }

        private void StopRefreshLoop()
        {
            if (refreshRoutine != null)
            {
                StopCoroutine(refreshRoutine);
                refreshRoutine = null;
            }
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, refreshIntervalSeconds));
                Refresh();
            }
        }

        public void Refresh()
        {
            SetText(dateLabel, DailyChallengeClock.Now.ToString(dateFormat));

            GlobalConfig config = GlobalConfigRuntimeContext.HasConfig ? GlobalConfigRuntimeContext.Config : null;

            if (config != null && !config.DailyChallenge.Enabled)
            {
                gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
            {
                // gameObject.SetActive(true);
            }

            int unlockLevel = config?.DailyChallenge.UnlockLevel ?? 1;
            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            bool comingSoon = unlockLevel == -1;
            bool unlocked = !comingSoon && (unlockLevel <= 1 || currentLevelNumber >= unlockLevel);

            SetInteractable(unlocked);

            if (!unlocked)
            {
                SetVisible(lockRoot, true);
                SetVisible(timerRoot, false);
                SetVisible(completedRoot, false);
                SetPlayVisible(false);
                SetText(lockLabel, comingSoon ? "Coming Soon" : $"Unlocks at level {unlockLevel}");
                return;
            }

            SetVisible(lockRoot, false);

            // Self-heals the day boundary before reading any state below - most mobile OSes
            // pause rather than kill the app, so a day can roll over while this widget is still
            // showing yesterday's stale "Completed" state (see
            // DailyChallengeController.RefreshForPossibleDayChange).
            controller?.RefreshForPossibleDayChange();

            // The widget's PLAY CTA jumps into today's challenge as long as it isn't fully won -
            // a Hard fail still leaves easier sub-levels replayable, so it keeps showing Play
            // too. Only a confirmed full win routes through the popup instead (see
            // DailyChallengeController.GetTodaysDirectPlayChallenge). Note this only reflects
            // "confirmed won" - any indeterminate case (controller not wired up yet, today's
            // entry not resolved yet) must default to showing Play, never "Completed": a false
            // Completed reads as a fresh player's day being stolen from them, whereas a false
            // Play at worst falls back to opening the popup (see
            // MainMenuHandler.PlayDailyChallengeDirectly) when tapped before data is ready.
            bool confirmedWin = controller?.GetTodaysChallenge()?.HasWin ?? false;
            bool canPlayToday = !confirmedWin;
            SetVisible(completedRoot, !canPlayToday);
            SetText(completedLabel, completedText);
            SetPlayVisible(canPlayToday);

            // Next-day timer button: only makes sense once there's nothing left to play today -
            // Play CTA disabled (canPlayToday false) or today's challenge already completed. It
            // stays hidden the rest of the time so it never overlaps the Play CTA.
            SetVisible(timerRoot, !canPlayToday);
            if (!canPlayToday)
            {
                SetText(timerLabel, CountdownTimeFormatter.Format(GetTimeUntilNextLocalMidnight()));
            }
        }

        /// <summary>
        /// The single "time until the next Daily Challenge release" calculation - anchored to
        /// local midnight, not decremented purely via Time.deltaTime, so it stays correct across
        /// pauses/backgrounding. DailyChallengeController's own day-unlock check
        /// (entryDate &lt;= today) rolls over at exactly this same instant, so this widget and any
        /// other Daily Challenge UI (the calendar popup) should both call this rather than each
        /// keeping their own countdown.
        /// </summary>
        public static TimeSpan GetTimeUntilNextLocalMidnight()
        {
            DateTime now = DailyChallengeClock.Now;
            DateTime nextMidnight = now.Date.AddDays(1);
            return nextMidnight - now;
        }

        private void SetPlayVisible(bool visible)
        {
            if (playButton != null)
            {
                playButton.gameObject.SetActive(visible);
                playButton.interactable = visible;
            }
        }

        private void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = interactable;
                canvasGroup.blocksRaycasts = interactable;
            }
        }

        private static void SetVisible(GameObject target, bool visible)
        {
            if (target != null)
            {
                target.SetActive(visible);
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
