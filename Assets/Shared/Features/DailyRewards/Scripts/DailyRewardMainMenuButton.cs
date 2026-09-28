using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DailyRewardMainMenuButton : MonoBehaviour
{
    [Header("References")]
    public RewardPanelController rewardPanelController;

    [SerializeField] private DailyRewardManager rewardManager;
    [SerializeField] private Button button;

    [Header("Main Menu UI")]
    [SerializeField] private GameObject NotificationImage;
    [SerializeField] private GameObject timerRoot;
    [SerializeField] private TMP_Text mainMenuTimer;
    [SerializeField] private TMP_Text UnlockLevelText;

    [Header("Reward Ready State")]
    [Tooltip("Shown only while the reward is ready to be collected (e.g. a glow/pulse/CTA graphic).")]
    [SerializeField] private GameObject rewardReadyIndicator;

    [Header("Locked State")]
    [SerializeField] private GameObject lockedOverlay;

    [Tooltip("Only plays its idle/play loop while this button is locked.")]
    [SerializeField] private AnimatorIdlePlayLoop idlePlayLoop;

    [Header("Unlock Reveal - hidden by OnUnlockStart, grows/moves into place in UnlockTheFeatureFinal")]
    [SerializeField] private GameObject unlockRevealObject;
    [SerializeField] private List<GameObject> objectToDisableDuringUnlock = new List<GameObject>();
    [SerializeField] private Vector3 unlockStartPosition;
    [SerializeField] private Vector3 unlockStartScale;
    [SerializeField] private Vector3 unlockEndPosition;
    [SerializeField] private Vector3 unlockEndScale;
    [SerializeField] private float unlockRevealDuration = 0.4f;

    private Coroutine timerCoroutine;

    private Func<int> _currentLevelNumberProvider;

    public void LoadData(Func<int> currentLevelNumberProvider)
    {
        _currentLevelNumberProvider = currentLevelNumberProvider;

        if (_currentLevelNumberProvider == null)
            return;

        // CurrentLevelNumber is already initialized before LoadData()
        // is called, so no startup delay/check is required.
        int currentLevel = _currentLevelNumberProvider.Invoke();

        ApplyLevel(currentLevel);
    }

    private void ApplyLevel(int level)
    {
        if (rewardManager == null)
            return;

        // Load the reward system using the current level.
        rewardManager.loadAwake(level);

        // Refresh the reward panel after the manager has loaded.
        if (rewardPanelController != null)
        {
            rewardPanelController.RefreshRewardValues();
        }

        // Immediately refresh the main-menu button UI.
        SetData();
    }

    private void OnEnable()
    {
        Subscribe();

        StartTimer();

        // Refresh immediately when the object becomes enabled.
        SetData();
    }

    private void OnDisable()
    {
        Unsubscribe();

        StopTimer();
    }

    private void Subscribe()
    {
        if (rewardManager == null)
            return;

        rewardManager.OnStateChanged -= SetData;
        rewardManager.OnStateChanged += SetData;
    }

    private void Unsubscribe()
    {
        if (rewardManager == null)
            return;

        rewardManager.OnStateChanged -= SetData;
    }

    public void OnButtonClick()
    {
        OnButtonClick(null);
    }

    // onComplete lets UnlockTheFeatureFinal know once the reward panel has actually finished
    // opening (not just that Open was called) - see RewardPanelController.Open.
    private void OnButtonClick(Action onComplete)
    {
        if (IsLocked())
        {
            onComplete?.Invoke();
            return;
        }

        // AudioManager.PlayClickSound();

        if (rewardPanelController != null)
        {
            rewardPanelController.Open(onComplete);
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>Called when this feature's unlock flow begins (see
    /// UnlockManager.ShowNextInQueue) - hides unlockRevealObject (if assigned) so it's not on
    /// screen until UnlockTheFeatureFinal reveals it, and refreshes this button's own state so
    /// it reads correctly by the time the unlock popup shows.</summary>
    public void OnUnlockStart()
    {
        SetData();
        if (unlockRevealObject != null)
        {
            unlockRevealObject.SetActive(false);
        }
        objectToDisableDuringUnlock.ForEach(o => o.SetActive(false));
    }

    /// <summary>Final call once the unlock flow (popup reveal + fly-to-target) completes - plays
    /// unlockRevealObject's own reveal (grows/moves from unlockStartPosition/unlockStartScale to
    /// unlockEndPosition/unlockEndScale) and only opens the panel (see OnButtonClick) once that
    /// finishes. Falls back to opening immediately if unlockRevealObject isn't assigned.</summary>
    public void UnlockTheFeatureFinal(Action onComplete = null)
    {
        if (unlockRevealObject == null)
        {
            OnButtonClick(onComplete);
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
        objectToDisableDuringUnlock.ForEach(o => o.SetActive(true));
            DOVirtual.DelayedCall(1f,() =>
            {
            OnButtonClick(onComplete);
            });

        });
    }

    [ContextMenu("SetData")]
    public void SetData()
    {
        if (rewardManager == null ||
            !rewardManager.IsConfigLoaded)
        {
            return;
        }

        // Handle locked/unlocked state first.
        if (LockDailyReward())
        {
            return;
        }

        bool isReady =
            rewardManager.GetState() ==
            RewardCycleState.Ready;

        // Unlocked state is always clickable.
        if (button != null)
        {
            button.interactable = true;
        }

        if (NotificationImage != null)
        {
            NotificationImage.SetActive(isReady);
        }

        SetRewardReadyIndicator(isReady);

        if (mainMenuTimer == null)
            return;

        if (isReady)
        {
            mainMenuTimer.text = "FREE";
        }
        else
        {
            UpdateTimer();
        }
    }

    public bool LockDailyReward()
    {
        bool isLocked = IsLocked();

        // Always keep the locked overlay synchronized.
        if (lockedOverlay != null)
        {
            lockedOverlay.SetActive(isLocked);
        }

        idlePlayLoop?.SetLocked(isLocked);

        // Always keep the button synchronized.
        if (button != null)
        {
            button.interactable = !isLocked;
        }

        // Always keep the timer container synchronized.
        if (timerRoot != null)
        {
            timerRoot.SetActive(!isLocked);
        }

        // -------------------------
        // LOCKED
        // -------------------------
        if (isLocked)
        {
            if (NotificationImage != null)
            {
                NotificationImage.SetActive(false);
            }

            SetRewardReadyIndicator(false);

            if (UnlockLevelText != null)
            {
                UnlockLevelText.text = rewardManager.IsComingSoon()
                    ? "Coming Soon"
                    : $"Unlocks at level {rewardManager.Config.unlockLevel}";
            }

            if (mainMenuTimer != null)
            {
                mainMenuTimer.text = "";
            }

            return true;
        }

        // -------------------------
        // UNLOCKED
        // -------------------------
        if (UnlockLevelText != null)
        {
            UnlockLevelText.text = "";
        }

        return false;
    }

    private bool IsLocked()
    {
        if (rewardManager == null)
            return false;

        if (!rewardManager.IsConfigLoaded)
            return false;

        return !rewardManager.IsSystemUnlocked();
    }

    private void UpdateTimer()
    {
        if (rewardManager == null ||
            !rewardManager.IsConfigLoaded)
        {
            return;
        }

        TimeSpan time =
            rewardManager.TimeUntilReady();

        // Reward is ready.
        if (time <= TimeSpan.Zero)
        {
            if (mainMenuTimer != null)
            {
                mainMenuTimer.text = "FREE";
            }

            if (NotificationImage != null)
            {
                NotificationImage.SetActive(true);
            }

            SetRewardReadyIndicator(true);

            return;
        }

        // Reward is still on cooldown.
        if (mainMenuTimer != null)
        {
            mainMenuTimer.text = CountdownTimeFormatter.Format(time);
        }

        if (NotificationImage != null)
        {
            NotificationImage.SetActive(false);
        }

        SetRewardReadyIndicator(false);
    }

    private void SetRewardReadyIndicator(bool isReady)
    {
        if (rewardReadyIndicator != null)
        {
            rewardReadyIndicator.SetActive(isReady);
        }
    }

    private void StartTimer()
    {
        StopTimer();

        timerCoroutine =
            StartCoroutine(TimerRoutine());
    }

    private void StopTimer()
    {
        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
    }

    private IEnumerator TimerRoutine()
    {
        while (true)
        {
            if (rewardManager != null &&
                rewardManager.IsConfigLoaded)
            {
                // Refresh the whole state every second.
                // This also catches the moment the reward becomes FREE.
                SetData();
            }

            yield return new WaitForSeconds(1f);
        }
    }
}