using FillIt.Runtime;
using System;
using DG.Tweening;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Game.SpinWheel
{
    public class SpinWheelMenuButtonUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SpinWheelManager _manager;
        [SerializeField] private Button _button;

        [Header("States")]
        [SerializeField] private GameObject _lockedState;
        [SerializeField] private GameObject _notAvailableState;
        [SerializeField] private GameObject _availableState;

        [Header("Spin The Wheel UI")]
        [SerializeField] private GameObject _spinTheWheel;
        [SerializeField] private Vector3 startPosition;
        [SerializeField] private Vector3 startScale;
        [SerializeField] private Vector3 EndPosition;
        [SerializeField] private Vector3 endScale;
        [SerializeField] List<GameObject> diableObject = new List<GameObject>();    
        [SerializeField] private float spinTheWheelRevealDuration = 1f;

        [Header("Locked UI")]
        [SerializeField] private TMP_Text _lockedText;

        [Header("Not Available UI")]
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private Image _lockedProgressBar;

        [Header("Available UI")]
        [SerializeField] private GameObject _notification;

        [Tooltip("Only plays its idle/play loop while this button is in the Locked state.")]
        [SerializeField] private AnimatorIdlePlayLoop _idlePlayLoop;

        [Header("Cheat")]
        public int levelWinNumber = 3;
        public bool EnableCheat = false;

        private Func<int> _currentLevelNumberProvider;

        public void SetLevel(Func<int> currentLevelNumberProvider)
        {
            _currentLevelNumberProvider = currentLevelNumberProvider;

            if (_currentLevelNumberProvider == null)
                return;

            // CurrentLevelNumber is already initialized before SetLevel is called.
            ApplyLevel(_currentLevelNumberProvider.Invoke());

            // Prevent duplicate listeners if SetLevel is called more than once.
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnButtonClicked);
                _button.onClick.AddListener(OnButtonClicked);
            }
        }

        private void ApplyLevel(int level)
        {
            Shared.GameConfig.GlobalConfig config =
                Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                    ? Shared.Boot.GlobalConfigRuntimeContext.Config
                    : null;

            if (config == null)
                return;

            // Feature disabled completely.
            if (!config.SpinTheWheel.Enabled)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            if (_manager == null)
                return;

            _manager.SetAwake(
                level,
                config.SpinTheWheel.UnlockLevel
            );

            Refresh();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_manager == null)
                return;

            _manager.OnWheelUnlocked += Refresh;
            _manager.OnSpinBecameAvailable += Refresh;
            _manager.OnRewardClaimed += OnRewardClaimed;
            _manager.OnConfigApplied += Refresh;
        }

        private void Unsubscribe()
        {
            if (_manager == null)
                return;

            _manager.OnWheelUnlocked -= Refresh;
            _manager.OnSpinBecameAvailable -= Refresh;
            _manager.OnRewardClaimed -= OnRewardClaimed;
            _manager.OnConfigApplied -= Refresh;
        }

        public void Refresh()
        {
            if (_manager == null)
                return;

            if (!_manager.IsFeatureUnlocked)
            {
                ShowLocked();
            }
            else if (_manager.IsSpinAvailable())
            {
                ShowAvailable();
            }
            else
            {
                ShowNotAvailable();
            }
        }

        private void ShowLocked()
        {
            SetState(
                locked: true,
                notAvailable: false,
                available: false
            );

            if (_lockedText != null)
            {
                _lockedText.text = _manager.IsComingSoon
                    ? "Coming Soon"
                    : $"Unlocks at level {_manager.UnlockLevel}";
            }

            if (_button != null)
                _button.interactable = false;

            if (_notification != null)
                _notification.SetActive(false);
        }

        private void ShowNotAvailable()
        {
            SetState(
                locked: false,
                notAvailable: true,
                available: false
            );

            if (_progressText != null)
            {
                _progressText.text =
                    $"{_manager.CurrentWins}/{_manager.WinsRequiredForNextSpin}";
            }
            if (_lockedProgressBar != null)
            {
                int required = _manager.WinsRequiredForNextSpin;
                _lockedProgressBar.fillAmount =
                    required > 0
                        ? Mathf.Clamp01((float)_manager.CurrentWins / required)
                        : 0f;
            }
            if (_notification != null)
                _notification.SetActive(false);

            if (_button != null)
                _button.interactable = true;
        }

        private void ShowAvailable()
        {
            SetState(
                locked: false,
                notAvailable: false,
                available: true
            );

            if (_notification != null)
                _notification.SetActive(true);

            if (_button != null)
                _button.interactable = true;
        }

        private void SetState(
            bool locked,
            bool notAvailable,
            bool available)
        {
            if (_lockedState != null)
                _lockedState.SetActive(locked);

            if (_notAvailableState != null)
                _notAvailableState.SetActive(notAvailable);

            if (_availableState != null)
                _availableState.SetActive(available);

            _idlePlayLoop?.SetLocked(locked);
        }

        [ContextMenu("Level Win")]
        public void LevelWin()
        {
            if (_manager == null)
                return;

            if (EnableCheat)
            {
                for (int i = 0; i < levelWinNumber; i++)
                {
                    _manager.OnRegularLevelWon();
                }
            }
            else
            {
                _manager.OnRegularLevelWon();
            }
        }

        public void OnButtonClicked()
        {
            OnButtonClicked(null);
        }

        // onComplete lets UnlockTheFeatureFinal know once the panel itself has actually finished
        // opening (not just that OpenPanel was called) - see SpinWheelManager.OpenPanel.
        private void OnButtonClicked(Action onComplete)
        {
            if (_manager == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!_manager.IsFeatureUnlocked)
            {
                onComplete?.Invoke();
                return;
            }

            // AudioManager.PlayClickSound();

            _manager.OpenPanel(onComplete);
            Refresh();
        }

        /// <summary>Called when this feature's unlock flow begins (see
        /// UnlockManager.ShowNextInQueue) - refreshes this button's own state so it reads
        /// correctly by the time the unlock popup shows.</summary>
        public void OnUnlockStart()
        {
            Refresh();
            _spinTheWheel.SetActive(false);
            diableObject.ForEach(o => o.SetActive(false));
        }

        /// <summary>Final call once the unlock flow (popup reveal + fly-to-target) completes -
        /// plays _spinTheWheel's own reveal (grows/moves from startPosition/startScale to
        /// EndPosition/endScale) and only opens the panel (see OnButtonClicked) once that
        /// finishes. Falls back to opening immediately if _spinTheWheel isn't assigned.</summary>
        public void UnlockTheFeatureFinal(Action onComplete = null)
        {
            if (_spinTheWheel == null)
            {
                OnButtonClicked(onComplete);
                return;
            }

            _spinTheWheel.SetActive(true);
            _spinTheWheel.transform.localPosition = startPosition;
            _spinTheWheel.transform.localScale = startScale;

            _spinTheWheel.transform.DOKill();

            Sequence sequence = DOTween.Sequence();

            sequence.Join(_spinTheWheel.transform.DOLocalMove(EndPosition, spinTheWheelRevealDuration).SetEase(Ease.OutBack));
            sequence.Join(_spinTheWheel.transform.DOScale(endScale, spinTheWheelRevealDuration).SetEase(Ease.OutBack));
            sequence.OnComplete(() =>
            {
            diableObject.ForEach(o => o.SetActive(true));

                DOVirtual.DelayedCall(0f,() =>
                {
                  OnButtonClicked(onComplete);
                });


            });
        }

        private void OnRewardClaimed(RewardConfig reward)
        {
            Refresh();
        }
    }
}