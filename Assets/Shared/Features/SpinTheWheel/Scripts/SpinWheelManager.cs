using System;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Analytics;
using Shared.GameFlow;
using Shared.Notifications;
using Shared.PlayerData;
using UnityEngine;
using UnityEngine.UI;

namespace Game.SpinWheel
{
    /// <summary>
    /// Local, persisted progression state for the wheel.
    /// </summary>
    [Serializable]
    public class SpinWheelPlayerState
    {
        public int NextSpinIndex = 1;
        public int WinsSinceLastSpin;
        public bool HasFreeSpinAvailable;
    }

    /// <summary>
    /// Central controller for the Spin the Wheel feature.
    /// </summary>
    public class SpinWheelManager : MonoBehaviour
    {
        [SerializeField]
        private SpinWheelPlayerState _state =
            new SpinWheelPlayerState();

        private SpinWheelRemoteConfig _config;

        private const string SaveKey = "spin_wheel_player_state";
        private const string SpinReadyNotificationId = "spin_wheel_ready";
        private const float SpinReadyNotificationHours = 4f;

        public event Action OnWheelUnlocked;
        public event Action OnSpinBecameAvailable;
        public event Action OnProgressChanged;
        public event Action<RewardConfig> OnRewardClaimed;
        public event Action OnConfigApplied;

        [Header("Panel")]
        [Tooltip("Handles the open/close scale animation. Assign targetTransform, " +
                 "canvasGroup and rootToToggle (= the panel root) on this component.")]
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private Button _closeButton;

        [Tooltip("Checked for the next queued unlock popup once this panel finishes closing - " +
                 "more than one feature can unlock at once (e.g. leveling past two unlock " +
                 "thresholds in one go), so closing one feature must reveal the next.")]
        [SerializeField] private UnlockManager unlockManager;

        public bool IsPanelOpen =>
            popupAnimator != null && popupAnimator.IsOpen;

        public bool IsSpinInProgress { get; private set; }

        private int CurrentLevel = 0;

        public int UnlockLevel { get; private set; }

        // ---------------------------------------------------------------
        // REWARDS
        // ---------------------------------------------------------------

        public IReadOnlyList<RewardConfig> Rewards
        {
            get
            {
                if (_config?.RewardTables == null ||
                    _config.RewardTables.Count == 0)
                {
                    return null;
                }

                int completedSpinsSoFar =
                    _state.NextSpinIndex - 1;

                var (table, _, _) =
                    _config.ResolveActiveReward(
                        completedSpinsSoFar);

                return table.Rewards;
            }
        }

        // ---------------------------------------------------------------
        // INITIALIZATION
        // ---------------------------------------------------------------

        public void SetAwake(int level, int unlockLevel)
        {
            UnlockLevel = unlockLevel;
            CurrentLevel = level;

            LoadState();
            ApplyConfig();
        }

        // ---------------------------------------------------------------
        // PANEL
        // ---------------------------------------------------------------

        public void OpenPanel()
        {
            OpenPanel(null);
        }

        // onComplete fires once the panel has actually finished its open animation - lets callers
        // (e.g. the post-unlock reveal flow) know precisely when it's safe to resume, rather than
        // just when OpenPanel was called.
        public void OpenPanel(Action onComplete)
        {
            if (popupAnimator == null ||
                popupAnimator.IsAnimating ||
                IsPanelOpen)
            {
                onComplete?.Invoke();
                return;
            }

            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.PopupOpen);
            popupAnimator.Open(onComplete);

            PlayerAnalyticsData analytics = PlayerData.Instance.Analytics;
            analytics.SpinWheelOpenCount++;
            AnalyticsManager.Instance.LogSpinWheel(PlayerData.Instance.CurrentLevelIndex + 1, analytics.SpinWheelOpenCount, CurrentSpinsToday(analytics));
        }

        // SpinsToday resets the first time it's checked on a new UTC day - there's no separate
        // periodic reset job, so every read (open or actual spin) self-heals it instead.
        private static int CurrentSpinsToday(PlayerAnalyticsData analytics)
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (analytics.LastSpinDateUtc != today)
            {
                analytics.SpinsToday = 0;
                analytics.LastSpinDateUtc = today;
            }

            return analytics.SpinsToday;
        }

        public void ClosePanel()
        {
            if (IsSpinInProgress)
            {
                Debug.LogWarning(
                    "[SpinWheel] ClosePanel refused — spin still in progress.");

                return;
            }

            if (popupAnimator == null ||
                popupAnimator.IsAnimating ||
                !IsPanelOpen)
            {
                return;
            }

            // AudioManager.PlayClickSound();
            PlayCloseButtonAnimation();

            popupAnimator.Close(() => unlockManager?.ShowNextPendingUnlock());
        }

        // Quick punch-scale tap feedback on the close button itself, separate from the
        // panel's own open/close scale animation (PopupAnimator).
        private void PlayCloseButtonAnimation()
        {
            if (_closeButton == null)
                return;

            _closeButton.transform.DOKill();
            _closeButton.transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 1, 0.5f);
        }

        // ---------------------------------------------------------------
        // FEATURE STATE
        // ---------------------------------------------------------------

        public bool IsComingSoon =>
            UnlockLevel == -1;

        public bool IsFeatureUnlocked =>
            !IsComingSoon && CurrentLevel >= UnlockLevel;

        public int CurrentWins =>
            _state.WinsSinceLastSpin;

        public int CurrentSpinIndex =>
            _state.NextSpinIndex;

        public bool HasFreeSpin =>
            _state.HasFreeSpinAvailable;

        /// <summary>
        /// Number of wins required for the upcoming spin.
        /// </summary>
        public int WinsRequiredForNextSpin
        {
            get
            {
                if (_config == null ||
                    CurrentLevel < UnlockLevel)
                {
                    return 0;
                }

                if (_state.HasFreeSpinAvailable)
                    return 0;

                var stage =
                    _config.GetStageForSpinIndex(
                        _state.NextSpinIndex);

                return stage.WinsRequired;
            }
        }

        /// <summary>
        /// Number of wins remaining.
        ///
        /// Example:
        /// 0/5 -> 5
        /// 1/5 -> 4
        /// 4/5 -> 1
        /// 5/5 -> 0
        /// </summary>
        public int WinsRemainingForNextSpin
        {
            get
            {
                if (_state.HasFreeSpinAvailable)
                    return 0;

                int required =
                    WinsRequiredForNextSpin;

                if (required <= 0)
                    return 0;

                return Mathf.Max(
                    0,
                    required - CurrentWins);
            }
        }

        /// <summary>
        /// Progress from 0 to 1.
        /// </summary>
        public float SpinProgress
        {
            get
            {
                if (_state.HasFreeSpinAvailable)
                    return 1f;

                int required =
                    WinsRequiredForNextSpin;

                if (required <= 0)
                    return 0f;

                return Mathf.Clamp01(
                    (float)CurrentWins / required);
            }
        }

        /// <summary>
        /// Progress text.
        /// Example: 0/5, 1/5, 2/5.
        /// </summary>
        public string SpinProgressText
        {
            get
            {
                if (_state.HasFreeSpinAvailable)
                    return "FREE";

                int required =
                    WinsRequiredForNextSpin;

                if (required <= 0)
                    return "0/0";

                return $"{CurrentWins}/{required}";
            }
        }

        // ---------------------------------------------------------------
        // REMOTE CONFIG
        // ---------------------------------------------------------------

        public void ApplyConfig()
        {
            Shared.GameConfig.GlobalConfig config =
                Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                    ? Shared.Boot.GlobalConfigRuntimeContext.Config
                    : null;

            if (config == null)
            {
                Debug.LogError(
                    "[SpinWheel] ApplyConfig called before GlobalConfig was available.");

                return;
            }

            SpinWheelRemoteConfig mapped =
                SpinWheelConfigMapper.ToRuntimeConfig(
                    config.SpinTheWheel);

            ApplyRemoteConfig(mapped);
        }

        public void ApplyRemoteConfig(
            SpinWheelRemoteConfig newConfig)
        {
            _config = newConfig;

            CheckUnlockByLevel(
                CurrentPlayerLevel());

            OnConfigApplied?.Invoke();
            OnProgressChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // FEATURE UNLOCK
        // ---------------------------------------------------------------

        public void CheckUnlockByLevel(
            int currentRegularLevel)
        {
            CurrentLevel = currentRegularLevel;

            if (_config == null)
                return;

            if (CurrentLevel < UnlockLevel)
                return;

            if (!_state.HasFreeSpinAvailable &&
                _state.NextSpinIndex == 1 &&
                _state.WinsSinceLastSpin == 0)
            {
                _state.HasFreeSpinAvailable = true;

                SaveState();

                OnWheelUnlocked?.Invoke();
                OnSpinBecameAvailable?.Invoke();
                OnProgressChanged?.Invoke();

                ScheduleSpinReadyNotification();
                return;
            }

            // Re-checked every time this feature reloads (this method runs once per Main Menu
            // session via ApplyRemoteConfig/SetAwake) - a notification already scheduled above (or
            // by ClaimSpin below) can get silently wiped by LocalNotificationAppStateWatcher.
            // SuppressNotificationsOnResume, which cancels EVERY scheduled notification on every
            // app foreground, not just ones due now. If a free spin is still sitting available
            // from an earlier session, re-arm its reminder here rather than leaving the player
            // with no notification until they happen to claim it (which reschedules the NEXT
            // one itself, taking back over from there).
            if (_state.HasFreeSpinAvailable)
            {
                ScheduleSpinReadyNotification();
            }
        }

        // ---------------------------------------------------------------
        // NOTIFICATION
        // ---------------------------------------------------------------

        // Reminds the player to come back and spin - (re)scheduled on first-time unlock, every
        // time a spin is claimed, AND every later session (see CheckUnlockByLevel) while a free
        // spin is still sitting available, always SpinReadyNotificationHours out from that moment.
        private void ScheduleSpinReadyNotification()
        {
            LocalNotificationController.Instance.ScheduleNotification(
                SpinReadyNotificationId,
                DateTime.Now.AddHours(SpinReadyNotificationHours),
                "You've got a free spin waiting! \U0001F3A1",
                "What will you win today?");
        }

        // ---------------------------------------------------------------
        // WIN TRACKING
        // ---------------------------------------------------------------

        /// <summary>
        /// Call this every time the player wins a regular level.
        /// </summary>
        public void OnRegularLevelWon()
        {
            if (_config == null ||
                CurrentLevel < UnlockLevel)
            {
                return;
            }

            // Don't add wins if a spin is already available.
            if (IsSpinAvailable())
                return;

            _state.WinsSinceLastSpin++;

            var stage =
                _config.GetStageForSpinIndex(
                    _state.NextSpinIndex);

            if (_state.WinsSinceLastSpin >=
                stage.WinsRequired)
            {
                _state.WinsSinceLastSpin =
                    stage.WinsRequired;

                SaveState();

                // Update progress UI.
                OnProgressChanged?.Invoke();

                // Notify that the spin can now be claimed.
                OnSpinBecameAvailable?.Invoke();

                return;
            }

            SaveState();

            // Update progress UI.
            OnProgressChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // SPIN AVAILABILITY
        // ---------------------------------------------------------------

        public bool TrySpin(
            out RewardConfig reward)
        {
            reward = null;

            if (!IsSpinAvailable())
                return false;

            reward = ClaimSpin();

            PlayerAnalyticsData analytics = PlayerData.Instance.Analytics;
            int spinsToday = CurrentSpinsToday(analytics) + 1;
            analytics.SpinsToday = spinsToday;
            AnalyticsManager.Instance.LogSpinWheel(PlayerData.Instance.CurrentLevelIndex + 1, analytics.SpinWheelOpenCount, spinsToday);

            return true;
        }

        public bool IsSpinAvailable()
        {
            if (_config == null ||
                CurrentLevel < UnlockLevel)
            {
                return false;
            }

            if (_state.HasFreeSpinAvailable)
                return true;

            var stage =
                _config.GetStageForSpinIndex(
                    _state.NextSpinIndex);

            return _state.WinsSinceLastSpin >=
                   stage.WinsRequired;
        }

        // ---------------------------------------------------------------
        // CLAIM SPIN
        // ---------------------------------------------------------------

        public RewardConfig ClaimSpin()
        {
            if (!IsSpinAvailable())
            {
                throw new InvalidOperationException(
                    "[SpinWheel] ClaimSpin called while no spin is available.");
            }

            int completedSpinsSoFar =
                _state.NextSpinIndex - 1;

            RewardConfig reward =
                RewardSelector.SelectReward(
                    _config,
                    completedSpinsSoFar);

            _state.HasFreeSpinAvailable = false;

            // Reset progress for next spin.
            _state.WinsSinceLastSpin = 0;

            // Move to next spin.
            _state.NextSpinIndex++;

            SaveState();

            ScheduleSpinReadyNotification();

            // Refresh progress immediately.
            OnProgressChanged?.Invoke();

            IsSpinInProgress = true;

            if (_closeButton != null)
                _closeButton.interactable = false;

            OnRewardClaimed?.Invoke(reward);

            return reward;
        }

        // ---------------------------------------------------------------
        // SPIN ANIMATION COMPLETE
        // ---------------------------------------------------------------

        public void NotifySpinAnimationComplete()
        {
            IsSpinInProgress = false;

            if (_closeButton != null)
                _closeButton.interactable = true;

            OnConfigApplied?.Invoke();
            OnProgressChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // SAVE
        // ---------------------------------------------------------------

        private readonly
            Shared.PlayerData.PlayerDataSaveSystem _saveSystem =
            new Shared.PlayerData.PlayerDataSaveSystem();

        private void SaveState()
        {
            Shared.PlayerData.PlayerData data =
                Shared.PlayerData.PlayerData.Instance;

            data.SetString(
                SaveKey,
                JsonUtility.ToJson(_state));

            string fileName =
                string.IsNullOrWhiteSpace(
                    Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                    ? Shared.PlayerData.PlayerDataDefaults.FileName
                    : Shared.PlayerData.PlayerDataRuntimeContext.FileName;

            _saveSystem.Save(
                fileName,
                data);
        }

        private void LoadState()
        {
            Shared.PlayerData.PlayerData data =
                Shared.PlayerData.PlayerData.Instance;

            if (data.TryGetString(
                    SaveKey,
                    out string json) &&
                !string.IsNullOrEmpty(json))
            {
                _state = JsonUtility.FromJson<SpinWheelPlayerState>(json);

                if (_state == null)
                    _state = new SpinWheelPlayerState();
            }
        }

        // ---------------------------------------------------------------
        // LEVEL
        // ---------------------------------------------------------------

        private int CurrentPlayerLevel()
        {
            return CurrentLevel;
        }

        // ---------------------------------------------------------------
        // DEBUG
        // ---------------------------------------------------------------

        public void Debug_ForceSpinAvailable()
        {
            if (_config == null)
            {
                Debug.LogWarning(
                    "[SpinWheel] Debug_ForceSpinAvailable called before config was applied.");

                return;
            }

            if (_state.NextSpinIndex == 1)
            {
                _state.HasFreeSpinAvailable = true;
            }
            else
            {
                var stage =
                    _config.GetStageForSpinIndex(
                        _state.NextSpinIndex);

                _state.WinsSinceLastSpin =
                    stage.WinsRequired;
            }

            SaveState();

            OnProgressChanged?.Invoke();
            OnSpinBecameAvailable?.Invoke();
        }

        [ContextMenu("Cheat - Force Spin")]
        public void Debug_CheatSpin()
        {
            if (_config == null)
            {
                Debug.LogWarning(
                    "[SpinWheel] Debug_CheatSpin called before config was applied.");

                return;
            }

            Debug_ForceSpinAvailable();

            ClaimSpin();
        }
    }
}