using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Currency;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.SpinWheel
{
    [Serializable]
    public class SegmentAngleEntry
    {
        public float Angle;
    }

    [Serializable]
    public class RewardTextAndIamge
    {
        public TMP_Text RewardAmount;
        public Image RewardImage;

        [Tooltip("Optional. Played on this segment when the wheel lands on it. Leave unassigned for segments that shouldn't get a win particle burst.")]
        public ParticleSystem RewardParticle;
    }

    public class SpinWheelUIController : MonoBehaviour
    {
        [SerializeField] private SpinWheelManager _manager;
        [SerializeField] private Button _testSpinButton;
        [SerializeField] private Button _cheatRotationButton;
        [SerializeField] private GameObject _DisableSpinButton;
        [SerializeField] private GameObject _closeButton;

        [Tooltip("Optional. If the Spin button has a ButtonPressScaleFeedback, its tap animation is " +
                 "allowed to finish before the button is hidden/disabled. Auto-resolved from _testSpinButton if left unassigned.")]
        [SerializeField] private ButtonPressScaleFeedback _spinButtonPressFeedback;

        [Header("Locked Spin Button Feedback (tapping _DisableSpinButton while the wheel isn't available)")]
        [Tooltip("Plays SoundName.ActionBlocked.")]
        [SerializeField] private bool _playLockedSpinSfx = true;
        [Tooltip("Plays ImpactFeedbackStyle.Heavy, same style used elsewhere in this project for a wrong/blocked tap.")]
        [SerializeField] private bool _playLockedSpinHaptic = true;
        [Tooltip("Shakes _DisableSpinButton left/right and decays back to rest.")]
        [SerializeField] private bool _playLockedSpinNudge = true;
        [SerializeField] private float _lockedSpinNudgeDistance = 10f;
        [SerializeField] private float _lockedSpinNudgeDuration = 0.3f;
        [SerializeField] private int _lockedSpinNudgeVibrato = 10;

        private Button _disableSpinButtonComponent;
        private Tween _lockedSpinNudgeTween;
        private Vector2 _lockedSpinNudgeRestingPosition;
        private bool _lockedSpinNudgeRestingCaptured;

        [Header("Wheel")]
        [SerializeField] private Image _wheelImage;

        [Header("Spin Progress")]
        [SerializeField] private Image _ProgessBarImage;
        [SerializeField] private TMP_Text _ProgessText;
        [SerializeField] private TMP_Text _TitleText;

        [Header("Rotation Animation")]
        [SerializeField] private float _rotationDuration = 3f;
        [SerializeField]
        private AnimationCurve _rotationEaseCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Idle Rotation")]
        [Tooltip("Degrees per second the wheel slowly spins clockwise on its own while idle (not " +
                 "mid-spin and not showing the collect screen). Set negative to go counter-clockwise instead.")]
        [SerializeField] private float _idleRotationSpeed = 10f;

        private Coroutine _idleRotationCoroutine;

        [Header("Win Segment Feedback")]
        [Tooltip("Punch-scale strength applied to the winning segment's reward image when the wheel lands on it.")]
        [SerializeField] private float _winPunchScaleStrength = 0.3f;
        [Tooltip("Duration of the win punch-scale effect.")]
        [SerializeField] private float _winPunchScaleDuration = 0.4f;
        [Tooltip("Number of vibrations for the win punch-scale effect.")]
        [SerializeField] private int _winPunchScaleVibrato = 6;
        [Tooltip("Elasticity of the win punch-scale effect (0 = no overshoot, 1 = max overshoot/bounce).")]
        [SerializeField, Range(0f, 1f)] private float _winPunchScaleElasticity = 0.6f;

        [Header("Pointer Tick Feedback")]
        [Tooltip("The static pointer/indicator at the top of the wheel. Flicked with a quick punch " +
                 "each time a segment boundary passes beneath it while the wheel is spinning.")]
        [SerializeField] private RectTransform _pointerTransform;
        [Tooltip("The pointer's resting Z rotation - every tick always starts and ends here, so rapid/overlapping ticks never drift away from it.")]
        [SerializeField] private float _pointerRestAngle = -90f;
        [Tooltip("Max Z deflection (degrees) away from the rest angle when a tick fires.")]
        [SerializeField] private float _pointerTickMaxAngle = 14f;
        [Tooltip("Total duration of one tick: 40% spent swinging out to the max angle, 60% easing back to rest.")]
        [SerializeField] private float _pointerTickDuration = 0.12f;

        [Tooltip("GameObject holding the wheel/segment colliders. A Kinematic Rigidbody2D is added " +
                 "to it while the wheel is actually rotating (so Physics2D tracks its movement and " +
                 "fires the segment triggers) and removed again once it stops.")]
        [SerializeField] private GameObject _wheelColliderHost;

        private Rigidbody2D _wheelRigidbody2D;

        [Tooltip("Every SpinWheelPointerTickTrigger under this wheel (one per segment collider) - " +
                 "drag them all in here so each one is wired to this controller from a single list " +
                 "instead of assigning the controller reference separately on every trigger object.")]
        [SerializeField] private List<SpinWheelPointerTickTrigger> _pointerTickTriggers = new List<SpinWheelPointerTickTrigger>();

        private Coroutine _activeRotationCoroutine;

        private List<RewardConfig> _wheelSegmentsInVisualOrder =
            new List<RewardConfig>();

        [SerializeField]
        private List<SegmentAngleEntry> _segmentAngles =
            new List<SegmentAngleEntry>();

        [SerializeField]
        private List<RewardTextAndIamge> _rewardTextIamge =
            new List<RewardTextAndIamge>();

        [Header("Icon Lookup")]
        [Tooltip("Maps a reward's RewardId -> Sprite. Assign the shared IconDatabase asset here.")]
        [SerializeField]
        private IconDatabaseConfig _iconDatabase;

        [SerializeField]
        private int _extraFullSpins = 4;

        [Header("Collect Screen")]
        [Tooltip("Shown after the wheel lands — animates the reward icon to the coin " +
                 "container (coin rewards) or reveals the play button (booster rewards).")]
        [SerializeField] private CollectScreenController _collectScreenController;

        [Header("Coin Total")]
        [Tooltip("Shows the player's current coin total on this panel. Refreshed every time the panel opens.")]
        [SerializeField] private CoinContainerView _coinContainer;

        public event Action<float, Action> OnPlaySpinAnimation;

        public RectTransform WheelTransform =>
            _wheelImage != null
                ? _wheelImage.rectTransform
                : null;

        // ---------------------------------------------------------------
        // UNITY
        // ---------------------------------------------------------------

        private void Awake()
        {
            if (_spinButtonPressFeedback == null && _testSpinButton != null)
                _spinButtonPressFeedback = _testSpinButton.GetComponent<ButtonPressScaleFeedback>();

            for (int index = 0; index < _pointerTickTriggers.Count; index++)
            {
                if (_pointerTickTriggers[index] != null)
                    _pointerTickTriggers[index].SetController(this);
            }

            if (_testSpinButton != null)
                _testSpinButton.onClick.AddListener(
                    OnTestSpinButtonClicked);

            if (_cheatRotationButton != null)
                _cheatRotationButton.onClick.AddListener(
                    Cheat_TestRotation);

            if (_DisableSpinButton != null)
            {
                _disableSpinButtonComponent = _DisableSpinButton.GetComponent<Button>();
                if (_disableSpinButtonComponent != null)
                    _disableSpinButtonComponent.onClick.AddListener(HandleDisableSpinButtonClicked);
            }

            OnPlaySpinAnimation +=
                PlayRotationAnimation;

            if (_manager != null)
            {
                _manager.OnConfigApplied +=
                    SyncSegmentsFromConfig;

                _manager.OnConfigApplied +=
                    RefreshSpinProgress;

                _manager.OnProgressChanged +=
                    RefreshSpinProgress;

                _manager.OnSpinBecameAvailable +=
                    RefreshSpinProgress;

                SyncSegmentsFromConfig();
                RefreshSpinProgress();
            }
        }

        private void OnEnable()
        {
            // Re-sync the spin button every time this panel is shown - progress made
            // while it was closed (e.g. winning the required levels) still needs to
            // flip the button on even though no event fired while we were inactive.
            RefreshSpinProgress();

            _coinContainer?.Refresh();

            ResetPointerToRestAngle();

            StartIdleRotation();
        }

        private void OnDisable()
        {
            StopIdleRotation();

            // The panel this button lives on can close mid-shake - kill the tween outright
            // instead of leaving the button reopen visually offset from its real resting position.
            _lockedSpinNudgeTween?.Kill();
            _lockedSpinNudgeTween = null;

            if (_lockedSpinNudgeRestingCaptured)
            {
                RectTransform target = _DisableSpinButton != null ? _DisableSpinButton.transform as RectTransform : null;
                if (target != null)
                    target.anchoredPosition = _lockedSpinNudgeRestingPosition;
            }
        }

        // Snaps the pointer straight to its rest angle with no animation - without this, the
        // first time the panel is shown it just keeps whatever rotation it was authored with in
        // the Editor (which may not match _pointerRestAngle), so it visibly pops into place on
        // the very first tick instead of already sitting at rest.
        private void ResetPointerToRestAngle()
        {
            if (_pointerTransform == null)
                return;

            _pointerTransform.DOKill();
            _pointerTransform.localEulerAngles = new Vector3(0f, 0f, _pointerRestAngle);
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnConfigApplied -=
                    SyncSegmentsFromConfig;

                _manager.OnConfigApplied -=
                    RefreshSpinProgress;

                _manager.OnProgressChanged -=
                    RefreshSpinProgress;

                _manager.OnSpinBecameAvailable -=
                    RefreshSpinProgress;
            }

            if (_testSpinButton != null)
                _testSpinButton.onClick.RemoveListener(
                    OnTestSpinButtonClicked);

            if (_cheatRotationButton != null)
                _cheatRotationButton.onClick.RemoveListener(
                    Cheat_TestRotation);

            if (_disableSpinButtonComponent != null)
                _disableSpinButtonComponent.onClick.RemoveListener(HandleDisableSpinButtonClicked);

            OnPlaySpinAnimation -=
                PlayRotationAnimation;
        }

        // ---------------------------------------------------------------
        // SPIN PROGRESS UI
        // ---------------------------------------------------------------

        /// <summary>
        /// Refreshes the progress image and text.
        ///
        /// 0/5 -> 0%
        /// 1/5 -> 20%
        /// 2/5 -> 40%
        /// 3/5 -> 60%
        /// 4/5 -> 80%
        /// 5/5 -> 100%
        /// </summary>
        private void RefreshSpinProgress()
        {
            if (_manager == null)
                return;

            bool spinAvailable = _manager.IsSpinAvailable();

            // -----------------------------------------------------------
            // PROGRESS BAR
            // -----------------------------------------------------------

            if (_ProgessBarImage != null)
            {
                if (_manager.HasFreeSpin)
                {
                    _ProgessBarImage.fillAmount = 1f;
                }
                else
                {
                    int required =
                        _manager.WinsRequiredForNextSpin;

                    int current =
                        _manager.CurrentWins;

                    if (required > 0)
                    {
                        _ProgessBarImage.fillAmount =
                            Mathf.Clamp01(
                                (float)current / required);
                    }
                    else
                    {
                        _ProgessBarImage.fillAmount = 0f;
                    }
                }
            }

            // -----------------------------------------------------------
            // PROGRESS TEXT
            // -----------------------------------------------------------

            if (_ProgessText != null)
            {
                if (_manager.HasFreeSpin)
                {
                    _ProgessText.text = "Free";
                }
                else
                {
                    int current =
                        _manager.CurrentWins;

                    int required =
                        _manager.WinsRequiredForNextSpin;

                    _ProgessText.text =
                        required > 0
                            ? $"{current}/{required}"
                            : "0/0";
                }
            }

            // -----------------------------------------------------------
            // TITLE TEXT
            // -----------------------------------------------------------

            // Only makes sense while the player still has levels left to win toward the next
            // spin - hidden once a spin is ready (free spin, or nothing left required/value is
            // 0), so it never shows a stale or meaningless "win 0 levels" message. Also left
            // alone while the wheel is actively spinning: ClaimSpin() fires OnProgressChanged
            // synchronously the instant Spin is tapped, well before the rotation animation
            // finishes, so without this guard the title would pop back on immediately instead
            // of waiting for the spin to actually finish.
            if (_TitleText != null && _activeRotationCoroutine == null)
            {
                _TitleText.gameObject.SetActive(true);

                if (!spinAvailable)
                {
                    int current =
                        _manager.CurrentWins;

                    int required =
                        _manager.WinsRequiredForNextSpin;

                    _TitleText.text =
                        $"Win {Mathf.Max(0, required - current)} levels to spin the wheel";
                }
                else
                {
                    _TitleText.text = "Ready to Spin!";
                }
            }

            // -----------------------------------------------------------
            // SPIN BUTTON AVAILABILITY
            // -----------------------------------------------------------

            if (_testSpinButton != null)
                _testSpinButton.gameObject.SetActive(spinAvailable);

            if (_DisableSpinButton != null)
                _DisableSpinButton.SetActive(!spinAvailable);
        }

        // ---------------------------------------------------------------
        // CONFIG / SEGMENTS
        // ---------------------------------------------------------------

        private void SyncSegmentsFromConfig()
        {
            if (_manager.Rewards == null)
                return;

            _wheelSegmentsInVisualOrder =
                new List<RewardConfig>(
                    _manager.Rewards);

            int segmentCount =
                _wheelSegmentsInVisualOrder.Count;

            if (segmentCount == 0)
                return;

            float segmentAngle =
                360f / segmentCount;

            var rebuilt =
                new List<SegmentAngleEntry>(
                    segmentCount);

            for (int i = 0;
                 i < segmentCount;
                 i++)
            {
                if (i < _segmentAngles.Count)
                {
                    rebuilt.Add(
                        _segmentAngles[i]);
                }
                else
                {
                    float defaultAngle =
                        segmentAngle * i +
                        segmentAngle / 2f;

                    rebuilt.Add(
                        new SegmentAngleEntry
                        {
                            Angle = defaultAngle
                        });
                }
            }

            _segmentAngles = rebuilt;

            RefreshAllSegmentLooks();
        }

        // ---------------------------------------------------------------
        // REWARD VISUALS
        // ---------------------------------------------------------------

        public void SetLook(
            string rewardId,
            string text)
        {
            int index =
                _wheelSegmentsInVisualOrder.FindIndex(
                    r => r.RewardId == rewardId);

            if (index < 0)
            {
                Debug.LogError(
                    $"[SpinWheel] SetLook: '{rewardId}' " +
                    $"isn't in the current reward list.");

                return;
            }

            if (index >= _rewardTextIamge.Count)
            {
                Debug.LogError(
                    $"[SpinWheel] SetLook: no " +
                    $"_rewardTextIamge entry at position {index}.");

                return;
            }

            var slot =
                _rewardTextIamge[index];

            if (slot.RewardAmount != null)
                slot.RewardAmount.text = text;

            if (_iconDatabase == null)
            {
                Debug.LogWarning(
                    $"[SpinWheel] SetLook: no {nameof(IconDatabaseConfig)} " +
                    "assigned - icons won't be shown.");

                return;
            }

            RewardConfig reward =
                _wheelSegmentsInVisualOrder[index];

            string iconKey =
                string.IsNullOrEmpty(reward.RewardIconKey)
                    ? rewardId
                    : reward.RewardIconKey;

            Sprite icon =
                _iconDatabase.GetIconOrFallback(iconKey);

            if (slot.RewardImage != null)
                slot.RewardImage.sprite = icon;

            if (icon == null)
            {
                Debug.LogWarning(
                    $"[SpinWheel] SetLook: no icon " +
                    $"registered for '{iconKey}'.");
            }
        }

        public void SetLook(
            RewardConfig reward)
        {
            string text =
                $"x{reward.Amount}";

            SetLook(
                reward.RewardId,
                text);
        }

        public void RefreshAllSegmentLooks()
        {
            foreach (var reward
                     in _wheelSegmentsInVisualOrder)
            {
                SetLook(reward);
            }
        }

        // ---------------------------------------------------------------
        // IDLE ROTATION
        // ---------------------------------------------------------------

        // Slow constant spin played while the wheel is just sitting on screen - stopped the
        // moment an actual spin starts (see PlayRotationAnimation) and only resumed once the
        // collect screen for that spin's reward has fully finished (see ShowCollectScreen).
        private void StartIdleRotation()
        {
            if (_wheelImage == null)
                return;

            if (_idleRotationCoroutine != null)
                return;

            _idleRotationCoroutine =
                StartCoroutine(IdleRotationCoroutine());
        }

        private void StopIdleRotation()
        {
            if (_idleRotationCoroutine == null)
                return;

            StopCoroutine(_idleRotationCoroutine);
            _idleRotationCoroutine = null;
        }

        private IEnumerator IdleRotationCoroutine()
        {
            RectTransform wheelRect =
                _wheelImage.rectTransform;

            while (true)
            {
                float currentZ =
                    wheelRect.localEulerAngles.z;

                // Subtracting (not adding) keeps idle drift going clockwise, matching the actual
                // spin animation's direction - see RotateWheelCoroutine's clockwiseDelta.
                wheelRect.localEulerAngles =
                    new Vector3(
                        0f,
                        0f,
                        currentZ - _idleRotationSpeed * Time.deltaTime);

                yield return null;
            }
        }

        // ---------------------------------------------------------------
        // ROTATION
        // ---------------------------------------------------------------

        public void PlayRotationAnimation(
            float absoluteTargetAngle,
            Action onComplete)
        {
            if (_wheelImage == null)
            {
                Debug.LogError(
                    "[SpinWheel] No _wheelImage assigned.");

                onComplete?.Invoke();

                return;
            }

            if (_activeRotationCoroutine != null)
                StopCoroutine(
                    _activeRotationCoroutine);

            StopIdleRotation();

            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.SpinTheWheel);

            if (_wheelColliderHost != null && _wheelRigidbody2D == null)
            {
                _wheelRigidbody2D = _wheelColliderHost.AddComponent<Rigidbody2D>();
                _wheelRigidbody2D.bodyType = RigidbodyType2D.Kinematic;
            }

            StartCoroutine(
                DisableSpinButtonsAfterPressFeedback());

            _activeRotationCoroutine =
                StartCoroutine(
                    RotateWheelCoroutine(
                        absoluteTargetAngle,
                        onComplete));
        }

        // Waits for the Spin button's own tap animation (scale down/back up) to finish
        // before hiding/disabling it - disabling it immediately on click would cut that
        // animation off mid-play.
        private IEnumerator DisableSpinButtonsAfterPressFeedback()
        {
            if (_spinButtonPressFeedback != null)
                yield return new WaitUntil(
                    () => !_spinButtonPressFeedback.IsAnimating);

            SetSpinButtonsInteractable(false);
        }

        private IEnumerator RotateWheelCoroutine(
            float absoluteTargetAngle,
            Action onComplete)
        {
            RectTransform wheelRect =
                _wheelImage.rectTransform;

            float startZ =
                NormalizeAngle(
                    wheelRect.localEulerAngles.z);

            float targetZ =
                NormalizeAngle(
                    absoluteTargetAngle);

            // Increasing Z rotates counter-clockwise, so the wheel spins clockwise by walking Z
            // downward - clockwiseDelta is the angular distance from startZ to targetZ going in
            // that direction (0-360), same idea as forwardDelta before but measured the other way.
            float clockwiseDelta =
                NormalizeAngle(
                    startZ - targetZ);

            float totalRotation =
                clockwiseDelta +
                (360f * _extraFullSpins);

            float endZ =
                startZ - totalRotation;

            float elapsed = 0f;

            while (elapsed < _rotationDuration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed /
                        _rotationDuration);

                float eased =
                    _rotationEaseCurve.Evaluate(t);

                float currentZ =
                    Mathf.LerpUnclamped(
                        startZ,
                        endZ,
                        eased);

                wheelRect.localEulerAngles =
                    new Vector3(
                        0f,
                        0f,
                        currentZ);

                yield return null;
            }

            wheelRect.localEulerAngles =
                new Vector3(
                    0f,
                    0f,
                    targetZ);

            _activeRotationCoroutine = null;

            if (_wheelRigidbody2D != null)
            {
                Destroy(_wheelRigidbody2D);
                _wheelRigidbody2D = null;
            }

            RefreshSpinProgress();

            if (_cheatRotationButton != null)
                _cheatRotationButton.interactable = true;

            // _closeButton stays hidden through the win-segment punch and the collect screen -
            // see HandleCollectSequenceComplete/ShowCollectScreen, which are what re-enable it,
            // so the player can't close the wheel while the reward reveal is still playing.
            onComplete?.Invoke();
        }

        // Called by SpinWheelPointerTickTrigger's OnTriggerEnter2D - each segment on the wheel
        // carries a trigger collider that physically sweeps past a collider on the pointer, so
        // this fires exactly once per segment boundary actually passing under the pointer
        // (Unity only raises OnTriggerEnter2D once per overlap, not repeatedly while inside it).
        public void NotifyPointerTick()
        {
            PlayPointerTick();
        }

        // Quick flick out to _pointerTickMaxAngle, then a soft settle back onto _pointerRestAngle,
        // as if a wheel segment just knocked past it. Uses two explicit absolute DOLocalRotate
        // tweens (not DOPunchRotation's relative offset-from-start-value approach) so every tick
        // targets known angles regardless of where the pointer currently is - a new tick
        // interrupting a previous one mid-flight just redirects smoothly from its current angle
        // instead of compounding/drifting, and OnComplete still pins it to rest as a safety net.
        private void PlayPointerTick()
        {
            if (_pointerTransform == null)
                return;

            _pointerTransform.DOKill();

            float deflectedAngle = _pointerRestAngle + _pointerTickMaxAngle;
            float outDuration = _pointerTickDuration * 0.4f;
            float backDuration = _pointerTickDuration * 0.6f;

            Sequence seq = DOTween.Sequence();
            seq.SetTarget(_pointerTransform);

            // Snap out to the max deflection...
            seq.Append(_pointerTransform
                .DOLocalRotate(new Vector3(0f, 0f, deflectedAngle), outDuration)
                .SetEase(Ease.OutQuad));

            // ...then ease back to rest with a touch of overshoot for a softer settle.
            seq.Append(_pointerTransform
                .DOLocalRotate(new Vector3(0f, 0f, _pointerRestAngle), backDuration)
                .SetEase(Ease.OutBack));

            seq.OnComplete(() =>
                _pointerTransform.localEulerAngles = new Vector3(0f, 0f, _pointerRestAngle));
        }

        private static float NormalizeAngle(
            float degrees)
        {
            float normalized =
                degrees % 360f;

            if (normalized < 0f)
                normalized += 360f;

            return normalized;
        }

        // ---------------------------------------------------------------
        // BUTTONS
        // ---------------------------------------------------------------

        private void SetSpinButtonsInteractable(
            bool interactable)
        {
            if (_testSpinButton != null)
                _testSpinButton.gameObject.SetActive(
                    interactable);

            if (_DisableSpinButton != null)
                _DisableSpinButton.SetActive(!interactable);

            if (_cheatRotationButton != null)
                _cheatRotationButton.interactable =
                    interactable;

            if (_closeButton != null)
                _closeButton.SetActive(
                    interactable);
        }

        // Wrong/negative feedback for tapping the wheel's locked state (_DisableSpinButton) -
        // each of SFX/haptic/nudge can be toggled independently from the Inspector, scoped only
        // to this button. Same ImpactFeedbackStyle used elsewhere in this project for a
        // wrong/blocked tap (e.g. BoosterButtonUI.HandleBlockedClick).
        private void HandleDisableSpinButtonClicked()
        {
            if (_playLockedSpinSfx)
                AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.ActionBlocked);

            if (_playLockedSpinHaptic)
                Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Heavy);

            if (_playLockedSpinNudge)
                PlayLockedSpinNudge();
        }

        // Small left/right shake that decays back to rest - same DOPunchAnchorPos shape as
        // BoosterButtonUI.PlayLockedNudge.
        private void PlayLockedSpinNudge()
        {
            RectTransform target = _DisableSpinButton != null ? _DisableSpinButton.transform as RectTransform : null;
            if (target == null)
                return;

            if (!_lockedSpinNudgeRestingCaptured)
            {
                _lockedSpinNudgeRestingPosition = target.anchoredPosition;
                _lockedSpinNudgeRestingCaptured = true;
            }

            _lockedSpinNudgeTween?.Kill();
            target.anchoredPosition = _lockedSpinNudgeRestingPosition;
            _lockedSpinNudgeTween = target
                .DOPunchAnchorPos(new Vector2(_lockedSpinNudgeDistance, 0f), _lockedSpinNudgeDuration, _lockedSpinNudgeVibrato)
                .SetUpdate(true) // unscaled time - plays even if this popup pauses gameplay time
                .OnComplete(() => _lockedSpinNudgeTween = null);
        }

        private void OnTestSpinButtonClicked()
        {
            if (_manager == null)
            {
                Debug.LogError(
                    "[SpinWheel] SpinWheelManager reference " +
                    "not set.");

                return;
            }

            if (!_manager.TrySpin(
                    out RewardConfig reward))
            {
                return;
            }

            SpinToReward(reward);
        }

        // ---------------------------------------------------------------
        // REWARD TARGET
        // ---------------------------------------------------------------

        public float GetTargetAngleForReward(
            RewardConfig reward)
        {
            if (_segmentAngles.Count == 0)
            {
                throw new InvalidOperationException(
                    "[SpinWheel] Wheel has no segment angles configured.");
            }

            int index =
                _wheelSegmentsInVisualOrder.FindIndex(
                    r => r.RewardId == reward.RewardId);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"[SpinWheel] Reward '{reward.RewardId}' " +
                    $"isn't in the current reward list.");
            }

            if (index >= _segmentAngles.Count)
            {
                throw new InvalidOperationException(
                    $"[SpinWheel] No _segmentAngles entry " +
                    $"at position {index}.");
            }

            return _segmentAngles[index].Angle;
        }

        // ---------------------------------------------------------------
        // SPIN
        // ---------------------------------------------------------------

        public void SpinToReward(
            RewardConfig reward)
        {
            float targetAngle =
                GetTargetAngleForReward(
                    reward);

            OnPlaySpinAnimation?.Invoke(
                targetAngle,
                () =>
                {
                    _manager.NotifySpinAnimationComplete();

                    PlayWinSegmentEffects(
                        reward,
                        () => ShowCollectScreen(reward));
                });
        }

        // Same visual-order index lookup used by SetLook/GetTargetAngleForReward, shared here so
        // PlayWinSegmentEffects and ShowCollectScreen always agree on which segment the wheel
        // actually landed on.
        private RewardTextAndIamge GetRewardSlot(
            RewardConfig reward)
        {
            int index =
                _wheelSegmentsInVisualOrder.FindIndex(
                    r => r.RewardId == reward.RewardId);

            if (index < 0 || index >= _rewardTextIamge.Count)
                return null;

            return _rewardTextIamge[index];
        }

        // Currently playing win particle (if any) - enabled in PlayWinSegmentEffects, disabled
        // again in HandleCollectSequenceComplete once the collect panel has fully closed, so it
        // never keeps running/sitting active in the background between spins.
        private ParticleSystem _activeWinParticle;

        // Punch-scales the winning segment's reward image and plays its assigned particle
        // effect (if any). onComplete fires once the punch-scale tween itself finishes (not
        // just started), so there's a visible gap before the collect screen opens instead of
        // both playing on top of each other. Falls back to firing onComplete immediately if
        // there's no image to punch (nothing to wait on).
        private void PlayWinSegmentEffects(
            RewardConfig reward,
            Action onComplete)
        {
            DisableWinParticle();

            RewardTextAndIamge slot =
                GetRewardSlot(reward);

            if (slot == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (slot.RewardParticle != null)
            {
                slot.RewardParticle.gameObject.SetActive(true);
                slot.RewardParticle.Play();

                _activeWinParticle = slot.RewardParticle;
            }

            if (slot.RewardImage == null)
            {
                onComplete?.Invoke();
                return;
            }

            slot.RewardImage.transform.DOKill();

            slot.RewardImage.transform.DOPunchScale(
                    Vector3.one * _winPunchScaleStrength,
                    _winPunchScaleDuration,
                    _winPunchScaleVibrato,
                    _winPunchScaleElasticity)
                .OnComplete(() => onComplete?.Invoke());
        }

        // ---------------------------------------------------------------
        // COLLECT SCREEN
        // ---------------------------------------------------------------
[SerializeField]
        float waitForSeconds=1f;
        private void ShowCollectScreen(
            RewardConfig reward)
        {
            if (_collectScreenController == null)
            {
                // No collect screen to wait on - re-enable now rather than leaving _closeButton
                // stuck hidden forever with nothing left to close it.
                if (_closeButton != null)
                    _closeButton.SetActive(true);

                return;
            }

            bool isCoin =
                reward.Type == RewardType.Coins;

            string iconKey =
                string.IsNullOrEmpty(reward.RewardIconKey)
                    ? reward.RewardId
                    : reward.RewardIconKey;

            RewardTextAndIamge slot =
                GetRewardSlot(reward);

            RectTransform jumpFromSource =
                slot != null && slot.RewardImage != null
                    ? slot.RewardImage.rectTransform
                    : null;

            _collectScreenController.Show(
                iconKey,
                isCoin,
                waitForSeconds,
                reward.Amount,
                onSequenceComplete: HandleCollectSequenceComplete,
                jumpFromSource: jumpFromSource);
        }

        // _coinContainer only reflects TotalCoins as of the last OnEnable - a coin reward mutates
        // TotalCoins inside the collect screen's own sequence, well after that point, so it needs
        // its own refresh once that sequence (and the coin grant) has actually finished.
        private void HandleCollectSequenceComplete()
        {
            _coinContainer?.Refresh();

            DisableWinParticle();

            StartIdleRotation();

            if (_closeButton != null)
                _closeButton.SetActive(true);
        }

        private void DisableWinParticle()
        {
            if (_activeWinParticle == null)
                return;

            _activeWinParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _activeWinParticle.gameObject.SetActive(false);
            _activeWinParticle = null;
        }

        // ---------------------------------------------------------------
        // DEBUG ROTATION
        // ---------------------------------------------------------------

        [ContextMenu("Cheat - Test Rotation")]
        public void Cheat_TestRotation()
        {
            float randomLandingAngle =
                UnityEngine.Random.Range(
                    0f,
                    360f);

            PlayRotationAnimation(
                randomLandingAngle,
                () =>
                {
                });
        }
    }
}