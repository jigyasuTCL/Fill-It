using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Analytics;
using Shared.Currency;
using Shared.GameFlow;
using Shared.PlayerData;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

// Only job: open/close the panel and keep it in sync with DailyRewardManager.
// Doesn't know persistence or timing rules.
public class RewardPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    [Header("Main reward")]
    // Shown ONLY when the main reward is ready - this is the clickable "Collect" button.
    [SerializeField] private Button mainClaimButton;
    // Shown ONLY while the main reward is counting down. Not meant to be
    // interactable - it's just the timer display taking the button's place
    // in the layout (e.g. same slot, greyed-out skin, ticking label inside).
    [SerializeField] private GameObject mainTimerButton;
    [SerializeField] private TMP_Text mainAmountText;
    [SerializeField] private Image mainAmountIcon;
    [SerializeField] private TMP_Text countdownTextMain;
    [SerializeField] private TMP_Text countdownTextSub;
    [SerializeField] private Image SubRewardProgess;

    [Header("Icon lookup")]
    [Tooltip("Maps RewardItem/SubRewardItem.iconKey -> Sprite. Assign the shared IconDatabase asset here.")]
    [SerializeField] private IconDatabaseConfig iconDatabase;

    [Header("Sub rewards")]
    [Tooltip("Pre-placed in the scene/prefab (not instantiated) - wired here in ascending order " +
             "(index 0 is claimed first, matching manager.CurrentSubRewards' own order). Data is " +
             "pushed into each one by ApplySubRewardViews; any view beyond the current reward " +
             "count is just hidden rather than repurposed.")]
    [SerializeField] private List<RewardSlotView> subRewardSlotViews = new List<RewardSlotView>();

    [SerializeField] private DailyRewardManager manager;

    [Header("Popup Animation")]
    [Tooltip("Handles the open/close scale animation. Assign targetTransform, " +
             "canvasGroup and rootToToggle (usually = panelRoot) on this component.")]
    [SerializeField] private PopupAnimator popupAnimator;

    [Tooltip("The panel's Close button. Given a quick punch-scale tap feedback (separate from " +
             "the panel's own open/close animation above) and the click sound when Close() runs.")]
    [SerializeField] private Button closeButton;

    [Header("Collect Screen")]
    [Tooltip("Shown after a main/sub reward is claimed - animates the reward icon to the " +
             "coin container (coin rewards) or the booster destination (everything else).")]
    [SerializeField] private CollectScreenController collectScreenController;

    [SerializeField] private float collectScreenWaitTime = 1.5f;

    [Header("Coin Total")]
    [Tooltip("Shows the player's current coin total on this panel. Refreshed every time the panel opens.")]
    [SerializeField] private CoinContainerView coinContainer;

    [Tooltip("Checked for the next queued unlock popup once this panel finishes closing - " +
             "more than one feature can unlock at once (e.g. leveling past two unlock " +
             "thresholds in one go), so closing one feature must reveal the next.")]
    [SerializeField] private UnlockManager unlockManager;

    // Guards against spam-tapping the open button - PopupAnimator.Open() only
    // refuses to replay while it's mid-animation, so calling it again once
    // the panel is already fully open would restart the whole open animation.
    private bool isOpen;

    // True from a sub-reward claim until its collect screen finishes closing.
    // manager.FinishClaimSubReward fires OnSubRewardClaimed AND OnStateChanged
    // synchronously on claim, and OnStateChanged alone is enough to trigger
    // RefreshValues() (it's subscribed directly, below) - so without this guard,
    // ApplySubRewardViews still unlocks/animates the next slot immediately,
    // bypassing the onSequenceComplete gate entirely. While pending,
    // RefreshValues() updates everything except the sub-reward slots; the
    // deferred RefreshValues() call in HandleSubRewardClaimed's callback then
    // applies them once the collect screen is actually closed.
    private bool subRewardCollectPending;

    // True from the moment ANY reward (main or sub) is claimed until its collect screen
    // finishes closing. SetAllClaimButtonsInteractable(false) is called once when this flips
    // true; sub-reward slot buttons then stay disabled on their own (Setup() never touches a
    // slot's Button.interactable, only its CanvasGroup/active state), while UpdateMainButtons
    // re-checks this flag directly so TickCountdown's once-a-second refresh can't re-enable
    // the main button mid-animation either. Cleared back on the way out via
    // SetAllClaimButtonsInteractable(true) once the collect screen finishes.
    private bool collectScreenButtonsLocked;

    private void OnEnable()
    {
        manager.OnStateChanged += RefreshValues;

        manager.OnMainRewardClaimed += HandleMainClaimed;

        manager.OnSubRewardClaimed += HandleSubRewardClaimed;

        // Only the "ready" button is ever clickable - the timer button has
        // nothing to do, so it gets no listener.
        mainClaimButton.onClick.AddListener(
            HandleMainClaimClicked);
    }

    private void OnDisable()
    {
        manager.OnStateChanged -= RefreshValues;

        manager.OnMainRewardClaimed -= HandleMainClaimed;

        manager.OnSubRewardClaimed -= HandleSubRewardClaimed;

        mainClaimButton.onClick.RemoveListener(
            HandleMainClaimClicked);
    }

    // Wire this to your "open rewards" button.
    [ContextMenu("Open")]
    public void Open()
    {
        Open(null);
    }

    // onComplete fires once the panel has actually finished opening (its popupAnimator tween
    // complete, or immediately if there's no animator) - lets callers (e.g. the post-unlock
    // reveal flow) know precisely when it's safe to resume, rather than just when Open was called.
    public void Open(Action onComplete)
    {
        if (isOpen)
        {
            onComplete?.Invoke();
            return;
        }

        isOpen = true;

        PlayerAnalyticsData analytics = PlayerData.Instance.Analytics;
        analytics.DailyRewardOpenCount++;
        // No active gameplay session from the main menu - level_no reports the player's current
        // saved progression level instead (same source CatFlapGameplayManager.CurrentLevelNumber
        // itself resumes from).
        AnalyticsManager.Instance.LogDailyReward(PlayerData.Instance.CurrentLevelIndex + 1, analytics.DailyRewardOpenCount, analytics.FreeRewardCollectedCount, analytics.AdRewardCollectCount);

        RefreshValues();

        coinContainer?.Refresh();

        StopAllCoroutines();

        AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.PopupOpen);

        if (popupAnimator != null)
            popupAnimator.Open(onComplete);
        else
        {
            panelRoot.SetActive(true);
            onComplete?.Invoke();
        }

        StartCoroutine(TickCountdown());
    }

    public void Close()
    {
        if (!isOpen)
            return;

        isOpen = false;

        StopAllCoroutines();

        // AudioManager.PlayClickSound();
        PlayCloseButtonAnimation();

        if (popupAnimator != null)
        {
            popupAnimator.Close(() => unlockManager?.ShowNextPendingUnlock());
        }
        else
        {
            panelRoot.SetActive(false);
            unlockManager?.ShowNextPendingUnlock();
        }
    }

    // Quick punch-scale tap feedback on the close button itself, separate from the
    // panel's own open/close scale animation (PopupAnimator).
    private void PlayCloseButtonAnimation()
    {
        if (closeButton == null)
            return;

        closeButton.transform.DOKill();
        closeButton.transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 1, 0.5f);
    }

    private void HandleMainClaimClicked()
    {
        manager.TryClaimMain();
    }

    private void HandleMainClaimed(
        RewardItem reward)
    {
        collectScreenButtonsLocked = true;
        SetAllClaimButtonsInteractable(false);

        RefreshValues();

        RestartCountdown();

        // coinContainer only reflects TotalCoins as of the last Open() - a coin reward mutates
        // TotalCoins inside the collect screen's own sequence, well after this call returns, so
        // it needs its own refresh once that sequence (and the coin grant) has actually finished.
        ShowCollectScreen(
            reward,
            () =>
            {
                collectScreenButtonsLocked = false;
                // Setup() (called from RefreshValues -> ApplySubRewardViews below) never
                // touches a slot's Button.interactable, only its CanvasGroup/active state -
                // so the disable above would otherwise never get undone.
                SetAllClaimButtonsInteractable(true);
                RefreshValues();
                coinContainer?.Refresh();
            },
            mainAmountIcon != null ? mainAmountIcon.rectTransform : null);
    }

    private void HandleSubRewardClaimed(
        SubRewardItem reward)
    {
        // manager.OnStateChanged fires synchronously right after this (see manager.
        // FinishClaimSubReward) and is wired straight to RefreshValues() - set the guard
        // before ShowCollectScreen so that firing doesn't unlock/animate the next slot early.
        subRewardCollectPending = true;
        collectScreenButtonsLocked = true;
        SetAllClaimButtonsInteractable(false);

        // Same index used to populate subRewardSlotViews from manager.CurrentSubRewards (see
        // ApplySubRewardViews) - so this resolves to the exact slot the player just tapped.
        int slotIndex = manager.CurrentSubRewards?.FindIndex(r => r.id == reward.id) ?? -1;
        RectTransform jumpFromSource =
            slotIndex >= 0 && slotIndex < subRewardSlotViews.Count
                ? subRewardSlotViews[slotIndex]?.IconTransform
                : null;

        // Refresh (which flips this slot to Claimed and unlocks/animates the next one into
        // Claimable) only once the collect screen has finished flying/closing - otherwise the
        // next slot's unlock transition plays underneath the collect screen instead of after it.
        ShowCollectScreen(
            reward,
            () =>
            {
                subRewardCollectPending = false;
                collectScreenButtonsLocked = false;
                SetAllClaimButtonsInteractable(true);
                RefreshValues();
                RestartCountdown();
                coinContainer?.Refresh();
            },
            jumpFromSource);
    }

    private void ShowCollectScreen(BaseRewardItem reward, Action onComplete = null, RectTransform jumpFromSource = null)
    {
        if (collectScreenController == null || reward == null)
        {
            onComplete?.Invoke();
            return;
        }

        bool isCoin = string.Equals(reward.type, "coins", StringComparison.OrdinalIgnoreCase);

        collectScreenController.Show(
            reward.iconKey,
            isCoin,
            collectScreenWaitTime,
            reward.amount,
            onSequenceComplete: onComplete,
            jumpFromSource: jumpFromSource);
    }

    private void RestartCountdown()
    {
        if (!panelRoot.activeSelf)
            return;

        StopAllCoroutines();

        StartCoroutine(TickCountdown());
    }

    public void RefreshRewardValues()
    {
        RefreshValues();
    }

    private void RefreshValues()
    {
        if (!manager.IsConfigLoaded)
            return;

        if (!manager.IsSystemUnlocked())
        {
            mainAmountText.text =
                $"Unlocks at level {manager.Config.unlockLevel}";

            // Neither main button makes sense while the system is locked.
            SetMainButtonsActive(claimActive: false, timerActive: false);

            ApplySubRewardViews(null);

            // Reset progress when system is locked.
            if (SubRewardProgess != null)
                SubRewardProgess.fillAmount = 0f;

            return;
        }

        // ---------------------------------------------------------
        // MAIN REWARD
        // ---------------------------------------------------------

        bool mainReady =
            manager.GetState() == RewardCycleState.Ready;

        RewardItem mainReward =
            manager.CurrentMainReward;

        mainAmountText.text =
            mainReward != null
                ? "x" + mainReward.amount
                : "-";

        ApplyIcon(mainReward != null ? mainReward.iconKey : null);

        UpdateMainButtons(mainReady);

        // ---------------------------------------------------------
        // SUB REWARDS
        // ---------------------------------------------------------

        List<SubRewardItem> subRewards =
            manager.CurrentSubRewards;

        // Update progress bar.
        UpdateSubRewardProgress(subRewards);

        // Skip while a sub-reward collect screen is in flight - see subRewardCollectPending.
        if (!subRewardCollectPending)
            ApplySubRewardViews(subRewards);
    }

    private void SetAllClaimButtonsInteractable(bool interactable)
    {
        if (mainClaimButton != null)
            mainClaimButton.interactable = interactable;

        foreach (RewardSlotView view in subRewardSlotViews)
            view?.SetInteractable(interactable);
    }

    // Pushes subRewards into subRewardSlotViews by index, ascending (subRewardSlotViews[0] gets
    // subRewards[0], etc.) - the views are pre-placed in the scene/prefab, never instantiated, so
    // this only ever sets data/state on what's already there. Any view beyond subRewards.Count
    // (or every view, when subRewards is null - e.g. system locked) is just hidden.
    private void ApplySubRewardViews(List<SubRewardItem> subRewards)
    {
        for (int i = 0; i < subRewardSlotViews.Count; i++)
        {
            RewardSlotView view = subRewardSlotViews[i];
            if (view == null)
            {
                continue;
            }

            if (subRewards == null || i >= subRewards.Count)
            {
                view.gameObject.SetActive(false);
                continue;
            }

            view.gameObject.SetActive(true);

            SubRewardItem data = subRewards[i];
            string id = data.id;

            SubRewardSlotState slotState;

            if (manager.CanClaimSubReward(id))
            {
                slotState = SubRewardSlotState.Claimable;
            }
            else if (manager.IsSubRewardClaimed(id))
            {
                slotState = SubRewardSlotState.Claimed;
            }
            else
            {
                slotState = SubRewardSlotState.Locked;
            }

            view.Setup(
                data,
                slotState,
                () => manager.TryClaimSubReward(id));
        }
    }

    // Switches which of the two main-reward buttons is showing, and keeps
    // the countdown label in sync with whichever one is active.
    private void UpdateMainButtons(bool isReady)
    {
        SetMainButtonsActive(
            claimActive: isReady,
            timerActive: !isReady);

        if (isReady)
        {
            // Also called directly by TickCountdown (not just RefreshValues), so the lock
            // must be checked here rather than only where RefreshValues is invoked.
            mainClaimButton.interactable =
                !collectScreenButtonsLocked && manager.CanClaimMain();
        }
        else
        {
            UpdateCountdownTextMain();
        }
    }

    private void SetMainButtonsActive(bool claimActive, bool timerActive)
    {
        if (mainClaimButton != null)
            mainClaimButton.gameObject.SetActive(claimActive);

        if (mainTimerButton != null)
            mainTimerButton.gameObject.SetActive(timerActive);

        if (countdownTextMain != null)
            countdownTextMain.gameObject.SetActive(timerActive);
    }

    private void ApplyIcon(string iconKey)
    {
        if (mainAmountIcon == null)
            return;

        if (iconDatabase == null)
        {
            Debug.LogWarning(
                $"{nameof(RewardPanelController)} on {name} has no {nameof(IconDatabaseConfig)} " +
                "assigned - the main reward icon won't be shown.");
            return;
        }

        Sprite sprite = iconDatabase.GetIconOrFallback(iconKey);

        if (sprite != null)
        {
            mainAmountIcon.sprite = sprite;
        }

        // Hide the image entirely rather than showing Unity's broken-sprite
        // square if neither the key nor the fallback resolved.
        mainAmountIcon.enabled = sprite != null;
    }

    private void UpdateSubRewardProgress(
        List<SubRewardItem> subRewards)
    {
        if (SubRewardProgess == null)
            return;

        if (subRewards == null ||
            subRewards.Count == 0)
        {
            SubRewardProgess.fillAmount = 0f;
            return;
        }

        int completedCount = 0;

        foreach (SubRewardItem reward in subRewards)
        {
            if (manager.IsSubRewardClaimed(reward.id))
            {
                completedCount++;
            }
        }

        SubRewardProgess.fillAmount =
           math.clamp(completedCount / (float)(subRewards.Count-1f), 0f, 1f);
    }

    private void UpdateCountdownTextMain()
    {
        if (countdownTextMain == null)
            return;

        TimeSpan t =
            manager.TimeUntilReady();

        // Ready is now handled entirely by swapping to mainClaimButton -
        // this label only ever needs to show a ticking time.
        if (t <= TimeSpan.Zero)
            return;

        countdownTextMain.text = CountdownTimeFormatter.Format(t);
    }

    private void UpdateCountdownTextSub()
    {
        TimeSpan t =
            manager.TimeUntilSubReady();

        countdownTextSub.text = $"Reset in : {CountdownTimeFormatter.Format(t)}";
    }

    // Checks both main and sub-reward timers.
    //
    // Main timer:
    //     Swaps between the Collect button and the timer button, and
    //     updates the countdown until main reward becomes ready.
    //
    // Sub timer:
    //     Keeps monitoring the sub cooldown.
    private IEnumerator TickCountdown()
    {
        while (panelRoot.activeSelf)
        {
            bool mainCounting =
                manager.GetState() ==
                RewardCycleState.CountingDown;

            bool subCounting =
                manager.GetSubState() ==
                RewardCycleState.CountingDown;

            UpdateMainButtons(isReady: !mainCounting);

            if (subCounting)
            {
                UpdateCountdownTextSub();
            }

            if (!mainCounting && !subCounting)
            {
                RefreshValues();

                yield break;
            }

            yield return new WaitForSeconds(1f);
        }
    }
}