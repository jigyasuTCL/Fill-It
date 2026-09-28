using System;
using System.Collections.Generic;
using System.Linq;
using Shared.GameConfig;
using UnityEngine;

namespace Shared.GameFlow
{
    public class UnlockManager : MonoBehaviour
    {
        public UnlockScreen unlockScreen;

        [Tooltip("Per-unlock content (id, header, description, which feature it unlocks). Kept " +
                 "as its own asset, separate from GlobalConfig - see UnlockFeaturesConfig.")]
        [SerializeField] private UnlockFeaturesConfig unlockFeaturesConfig;

        [Header("Feature Openers")]
        [SerializeField] private Game.SpinWheel.SpinWheelMenuButtonUIController spinWheelMenuButtonUIController;
        [Tooltip("OnUnlockStart is called when this feature's popup is about to show; " +
                 "UnlockTheFeatureFinal is called once the whole unlock flow (popup reveal + " +
                 "fly-to-target) completes, and itself opens the same calendar popup ShowPopup() does.")]
        [SerializeField] private DailyChallenges.DailyChallengeButtonUI dailyChallengeButtonUI;
        [SerializeField] private DailyRewardMainMenuButton dailyRewardMainMenuButton;

        [Header("Main Menu Targets")]
        [Tooltip("Where the unlock popup's main object flies to (matching this RectTransform's " +
                 "position/size) once PlayExitFadeOut finishes, before the popup itself closes - " +
                 "e.g. that feature's own bottom-nav icon. See UnlockScreen.OnClose/" +
                 "UnlockRevealSequencer.PlayMoveToTarget. Left unassigned skips the fly-in.")]
        [SerializeField] private RectTransform spinWheelMainMenuTarget;
        [SerializeField] private RectTransform dailyChallengeMainMenuTarget;
        [SerializeField] private RectTransform dailyRewardMainMenuTarget;

        [Header("Test")]
        [Tooltip("Which entry in unlockFeaturesConfig the test trigger below shows - bypasses " +
                 "level-gating/IsShown entirely so you can preview the popup/reveal animation " +
                 "on demand. Wire a UI Button's OnClick to TestShowUnlock() to trigger it from " +
                 "gameplay, or right-click this component's header in the Inspector during Play " +
                 "mode and use \"Test - Show Unlock Popup\".")]
        [SerializeField] private int testUnlockIndex;

        private List<UnlockData> unlockDatas = new List<UnlockData>();
        private Queue<UnlockData> pendingQueue = new Queue<UnlockData>();
        private UnlockData currentlyShownUnlock;
        private GlobalConfig config;

        private void Awake() { }

        public void CheckUnlocks(int currentLevel)
        {
            EnsureUnlockDataLoaded();

            var toShow = unlockDatas
                .Where(u =>ResolveUnlockLevel(u) > 0 && ResolveUnlockLevel(u) <= currentLevel && !u.IsShown)
                .OrderBy(u => u.priority)
                .ToList();

            if (toShow.Count == 0)
                return;

            pendingQueue.Clear();
            foreach (var u in toShow)
                pendingQueue.Enqueue(u);

            ShowNextInQueue();
        }

        private void EnsureUnlockDataLoaded()
        {
            if (unlockDatas != null && unlockDatas.Count > 0)
            {
                return;
            }

            config = Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config
                : null;
            unlockDatas = unlockFeaturesConfig != null ? unlockFeaturesConfig.Unlocks : new List<UnlockData>();
            unlockScreen.ApplyConfig(unlockDatas);
        }

        /// <summary>
        /// Test-only trigger, bypassing level-gating/IsShown/the pending-queue entirely - shows
        /// testUnlockIndex's popup directly so the reveal animation (see UnlockRevealSequencer)
        /// can be iterated on without actually reaching that unlock in real progression. Available
        /// as a right-click Inspector command in Play mode, and as a plain public method a real
        /// UI Button's OnClick can call too.
        /// </summary>
        [ContextMenu("Test - Show Unlock Popup")]
        public void TestShowUnlock()
        {
            if (unlockScreen == null)
            {
                Debug.LogWarning("UnlockManager: no UnlockScreen assigned, cannot test.");
                return;
            }

            EnsureUnlockDataLoaded();

            if (unlockDatas == null || unlockDatas.Count == 0)
            {
                Debug.LogWarning("UnlockManager: no unlocks configured in unlockFeaturesConfig to test.");
                return;
            }

            int index = Mathf.Clamp(testUnlockIndex, 0, unlockDatas.Count - 1);
            NotifyUnlockStart(unlockDatas[index].feature);
            unlockScreen.Show(index);
        }

        /// <summary>
        /// The unlock level for a given entry comes from that feature's own config
        /// (SpinTheWheel.UnlockLevel / DailyChallenge.UnlockLevel / DailyReward.unlockLevel)
        /// so the level only ever needs to be set in one place. UnlockData.levelNumber is
        /// only used as a manual fallback when no feature is assigned.
        /// </summary>
        private int ResolveUnlockLevel(UnlockData data)
        {
            if (config == null)
                return data.levelNumber;

            switch (data.feature)
            {
                case UnlockFeature.SpinWheel:
                    return config.SpinTheWheel.UnlockLevel;

                case UnlockFeature.DailyChallenge:
                    return config.DailyChallenge.UnlockLevel;

                case UnlockFeature.DailyReward:
                    return config.DailyReward.unlockLevel;

                default:
                    return data.levelNumber;
            }
        }

        /// <summary>
        /// Where feature's unlock popup should send its main object once PlayExitFadeOut
        /// finishes - see the Main Menu Targets fields above. Null (unassigned target, or
        /// UnlockFeature.None) means the caller should skip the fly-in entirely.
        /// </summary>
        public RectTransform GetMainMenuTarget(UnlockFeature feature)
        {
            switch (feature)
            {
                case UnlockFeature.SpinWheel:
                    return spinWheelMainMenuTarget;

                case UnlockFeature.DailyChallenge:
                    return dailyChallengeMainMenuTarget;

                case UnlockFeature.DailyReward:
                    return dailyRewardMainMenuTarget;

                default:
                    return null;
            }
        }

        private void ShowNextInQueue()
        {
            if (pendingQueue.Count == 0)
                return;

            currentlyShownUnlock = pendingQueue.Peek();
            int index = unlockDatas.IndexOf(currentlyShownUnlock);
            NotifyUnlockStart(currentlyShownUnlock.feature);
            unlockScreen.Show(index);
        }

        /// <summary>
        /// Fired right as this feature's unlock popup is about to show - lets its own Main Menu
        /// widget refresh itself before the popup appears. Counterpart to OpenFeature/
        /// UnlockTheFeatureFinal, which fires once the whole flow completes instead.
        /// </summary>
        private void NotifyUnlockStart(UnlockFeature feature)
        {
            switch (feature)
            {
                case UnlockFeature.SpinWheel:
                    spinWheelMenuButtonUIController?.OnUnlockStart();
                    break;

                case UnlockFeature.DailyChallenge:
                    dailyChallengeButtonUI?.OnUnlockStart();
                    break;

                case UnlockFeature.DailyReward:
                    dailyRewardMainMenuButton?.OnUnlockStart();
                    break;
            }
        }

        /// <summary>
        /// onFullyOpened, if given, fires once the unlocked feature's own panel has completely
        /// finished opening (not just once this popup is closed) - see OpenFeature/
        /// UnlockTheFeatureFinal on each feature's button controller. UnlockScreen uses this to
        /// know exactly when it's safe to re-enable every other Button in the scene again.
        /// </summary>
        public void OnUnlockClosed(Action onFullyOpened = null)
        {
            // Read from currentlyShownUnlock rather than the queue - CheckUnlocks can
            // clear/rebuild pendingQueue while a popup is on screen (e.g. the menu
            // re-enabling), which must not stop the feature for the popup actually being
            // closed right now from opening.
            UnlockData justClosed = currentlyShownUnlock;
            currentlyShownUnlock = null;

            if (justClosed == null)
            {
                onFullyOpened?.Invoke();
                return;
            }

            if (pendingQueue.Count > 0 && pendingQueue.Peek() == justClosed)
                pendingQueue.Dequeue();

            justClosed.MarkShown();

            // Open the unlocked feature instead of chaining straight to the next popup.
            // The next queued unlock (if any) is shown once the player is back on the
            // menu - see ShowNextPendingUnlock(), which the feature's close/return-to-menu
            // flow should call.
             OpenFeature(justClosed.feature, onFullyOpened);
        }

        private void OpenFeature(UnlockFeature feature, Action onFullyOpened = null)
        {
            // Debug.LogError($"Opening feature {feature} after unlock");
            switch (feature)
            {
                case UnlockFeature.SpinWheel:
                    spinWheelMenuButtonUIController.UnlockTheFeatureFinal(onFullyOpened);
                    break;

                case UnlockFeature.DailyChallenge:
                    dailyChallengeButtonUI.UnlockTheFeatureFinal(onFullyOpened);
                    break;

                case UnlockFeature.DailyReward:
                    dailyRewardMainMenuButton.UnlockTheFeatureFinal(onFullyOpened);
                    break;

                default:
                    onFullyOpened?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Call once the player is back on the menu after closing the feature that was
        /// just unlocked, to reveal the next queued unlock popup (if any).
        /// </summary>
        public void ShowNextPendingUnlock()
        {
            if (pendingQueue.Count > 0)
                ShowNextInQueue();
        }
    }
}
