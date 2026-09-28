using DG.Tweening;
using TMPro;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shared.Boosters
{
    public sealed class BoosterButtonUI : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private BoosterType boosterType;
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text lockLabel;
        [SerializeField] private TMP_Text countLabel;
        [SerializeField] private TMP_Text costLabel;
        [SerializeField] private GameObject lockRoot;
        [SerializeField] private GameObject countRoot;
        [SerializeField] private GameObject costRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image buttonGraphic;
        [SerializeField] private Image iconGraphic;
        [SerializeField] private Sprite enabledSprite;
        [SerializeField] private Sprite disabledSprite;

        [Header("Blocked-Tap Nudge (plays alongside the negative haptic - see HandleBlockedClick)")]
        [Tooltip("Shaken left/right when this button is tapped while non-interactable - locked by level (Hint/Fix), or unavailable (Undo with nothing to undo). Auto-resolved to this GameObject's own RectTransform if left unassigned.")]
        [SerializeField] private RectTransform nudgeTarget;
        [SerializeField] private float nudgeDistance = 10f;
        [SerializeField] private float nudgeDuration = 0.3f;
        [SerializeField] private int nudgeVibrato = 10;

        [Header("Hint Count Added Animation (Hint only - plays when the hint count goes UP)")]
        [Tooltip("Scaled up, held while the number counts, then scaled back down. Auto-resolved to countRoot's own RectTransform if left unassigned.")]
        [SerializeField] private RectTransform countBaseTransform;
        [SerializeField] private float hintCountScaleUpDuration = 0.18f;
        [SerializeField] private float hintCountScaleAmount = 1.25f;
        [SerializeField] private float hintCountNumberDuration = 0.35f;

        [Header("Unlock Animation (plays once, the moment this booster transitions locked -> unlocked)")]
        [Tooltip("When enabled: unlockAnimator plays first, THEN (once it finishes) the particle burst fires alongside unlockContainer scaling up and unlockFadeImage fading out, before revealing the Count/Cost state underneath. When disabled, that reveal is instant, same as before this feature existed.")]
        [SerializeField] private bool playUnlockAnimation = true;
        [Tooltip("Optional Animator played first, via unlockAnimatorTrigger. Leave unassigned to skip straight to the particle/scale-up/fade-out beat.")]
        [SerializeField] private Animator unlockAnimator;
        [SerializeField] private string unlockAnimatorTrigger = "Unlock";
        [Tooltip("How long to wait for unlockAnimator's triggered animation to finish before moving on to the particle/scale-up/fade-out beat.")]
        [SerializeField] private float unlockAnimatorDuration = 0.5f;
        [SerializeField] private float waitBeforeUnlockAnimation = 1f;
        [Tooltip("Dedicated container for the whole unlock celebration (holds unlockAnimator/particle anchor/unlockFadeImage) - activated when the celebration starts, deactivated once it's done. This is what scales up.")]
        [SerializeField] private RectTransform unlockContainer;
        [Tooltip("The single image that fades out (alongside unlockContainer scaling up) - NOT the whole container, just this one image.")]
        [SerializeField] private Image unlockFadeImage;
        [Tooltip("How much unlockContainer scales up while unlockFadeImage fades out, on top of its normal scale.")]
        [SerializeField] private float unlockRevealScaleAmount = 1.3f;
        [SerializeField] private float unlockFadeOutDuration = 0.35f;
        [Tooltip("Optional. Played (Clear + Play) the moment unlockContainer starts scaling up/unlockFadeImage starts fading out.")]
        [SerializeField] private ParticleSystem unlockParticles;

        // -1 means "never rendered yet" - guards the very first Render() (e.g. right after the
        // panel opens) from playing the added-animation just because count went from "unknown" to
        // whatever the wallet already holds.
        private int lastRenderedCount = -1;
        private Sequence hintCountAnimSequence;

        private Tween unlockAnimTween;

        private bool configActive = true;
        // Mirrors whatever SetInteractable was last called with - covers BOTH ways a booster ends
        // up non-clickable: level-locked (Hint/Fix's "unlock at level X") and simply unavailable
        // (Undo with nothing left to undo). OnPointerClick below reads this directly rather than
        // a separate "locked" concept, so both cases get the same negative-feedback treatment for
        // free with no per-booster-type branching needed.
        private bool isInteractable = true;
        private UnityAction buttonClickHandler;
        private Func<bool> undoAvailabilityProvider;

        private Tween nudgeTween;
        private Vector2 nudgeRestingPosition;
        private bool nudgeRestingCaptured;

        public BoosterType BoosterType => boosterType;
        public Button Button => button;

        private void Awake()
        {
            ResolveReferences();
        }

        // Right-click this component's header (or the "..." menu) in Play Mode and pick "Test
        // Unlock Animation" to force PlayUnlockAnimation regardless of the locked->unlocked
        // transition guard - same testing convention used elsewhere in this project
        // (CollectScreenController/SpinWheelUIController/HardLevelPopupController).
        [ContextMenu("Test Unlock Animation")]
        private void TestUnlockAnimation()
        {
            PlayUnlockAnimation(lastRenderedCount, 0);
        }

        private void OnDestroy()
        {
            ClearClickBinding();
        }

        public void Configure(BoosterType type)
        {
            boosterType = type;
            ResolveReferences();
        }

        public void BindClick(Action<BoosterType> clickAction)
        {
            ResolveReferences();
            ClearClickBinding();
            if (button == null || clickAction == null)
            {
                return;
            }

            buttonClickHandler = () => clickAction.Invoke(boosterType);
            button.onClick.AddListener(buttonClickHandler);
        }

        public void SetUndoAvailabilityProvider(Func<bool> provider)
        {
            undoAvailabilityProvider = provider;
        }

        // IPointerClickHandler fires on this GameObject regardless of button.interactable -
        // Button itself swallows onClick while non-interactable (see SetInteractable above), so
        // this is the only way to actually detect a tap landing on a disabled booster.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isInteractable)
            {
                HandleBlockedClick();
            }
        }

        // Negative haptic + a visual nudge for tapping a booster that's currently non-interactable
        // - locked by level (Hint/Fix) or unavailable (Undo with nothing to undo) alike. Same
        // ImpactFeedbackStyle used elsewhere in this project for a wrong/negative result (e.g.
        // CatFlapGameplayManager.Validation wrong-rule feedback).
        private void HandleBlockedClick()
        {
            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Heavy);
            PlayLockedNudge();
        }

        private void OnDisable()
        {
            // The popup/panel this button lives on can close mid-shake - kill the tween outright
            // instead of leaving the button reopen visually offset from its real resting position.
            nudgeTween?.Kill();
            nudgeTween = null;

            if (nudgeRestingCaptured && nudgeTarget != null)
            {
                nudgeTarget.anchoredPosition = nudgeRestingPosition;
            }

            StopHintCountAnimation();
            KillUnlockAnimation();
        }

        // Small left/right shake that decays back to rest - reads as a "no, not yet" nudge
        // alongside the negative haptic above. DOPunchAnchorPos already oscillates-and-decays
        // back to the position it started from, same shape as the old manual sine-decay coroutine.
        private void PlayLockedNudge()
        {
            if (nudgeTarget == null)
            {
                return;
            }

            if (!nudgeRestingCaptured)
            {
                nudgeRestingPosition = nudgeTarget.anchoredPosition;
                nudgeRestingCaptured = true;
            }

            nudgeTween?.Kill();
            nudgeTarget.anchoredPosition = nudgeRestingPosition;
            nudgeTween = nudgeTarget
                .DOPunchAnchorPos(new Vector2(nudgeDistance, 0f), nudgeDuration, nudgeVibrato)
                .SetUpdate(true) // unscaled time - plays even if this popup pauses gameplay time
                .OnComplete(() => nudgeTween = null);
        }

        // Hint only: scales countBaseTransform up, counts countLabel from fromCount to toCount
        // WHILE staying scaled up, then scales back down to normal - one continuous Sequence so
        // the count only ever climbs while the base is visibly popped, never during either scale
        // step. Called only when the hint count actually increased (see Render).
        private void PlayHintCountAddedAnimation(int fromCount, int toCount)
        {
            RectTransform target = ResolveHintCountBaseTransform();
            if (target == null || countLabel == null)
            {
                SetText(countLabel, toCount.ToString());
                return;
            }

            hintCountAnimSequence?.Kill();
            target.localScale = Vector3.one;
            SetText(countLabel, fromCount.ToString());

            int displayValue = fromCount;
            Tween countUpTween = DOTween.To(() => displayValue, value =>
            {
                displayValue = value;
                SetText(countLabel, displayValue.ToString());
            }, toCount, hintCountNumberDuration).SetEase(Ease.OutCubic);

            hintCountAnimSequence = DOTween.Sequence()
                .Append(target.DOScale(Vector3.one * hintCountScaleAmount, hintCountScaleUpDuration).SetEase(Ease.OutBack))
                .Append(countUpTween)
                .Append(target.DOScale(Vector3.one, hintCountScaleUpDuration).SetEase(Ease.InOutSine))
                .OnComplete(() =>
                {
                    SetText(countLabel, toCount.ToString());
                    hintCountAnimSequence = null;
                });
        }

        // Kills whatever's running/pending and snaps the base back to normal scale - guards
        // against this button being disabled, re-locked, or losing its count display entirely
        // while mid-animation.
        private void StopHintCountAnimation()
        {
            hintCountAnimSequence?.Kill();
            hintCountAnimSequence = null;

            RectTransform target = ResolveHintCountBaseTransform();
            if (target != null)
            {
                target.localScale = Vector3.one;
            }
        }

        private RectTransform ResolveHintCountBaseTransform()
        {
            if (countBaseTransform != null)
            {
                return countBaseTransform;
            }

            return countRoot != null ? countRoot.transform as RectTransform : null;
        }

        // Set the one time the unlock celebration plays, so arriving on the unlock level again
        // (a restart, a replay) doesn't celebrate twice.
        private bool hasPlayedUnlockAnimation;

        public void CheckForUnLockAnimationState(BoosterWallet wallet, int currentLevelNumber)
        {
            int unlockLevel = wallet.Config.GetUnlockLevel(boosterType);
            bool active = wallet.Config.IsActive(boosterType);
            configActive = active;
            bool unlocked = active && (unlockLevel == 0 || currentLevelNumber >= unlockLevel);
            int count = wallet.GetCount(boosterType);
            int cost = wallet.Config.GetCost(boosterType);

            if (!unlocked)
            {
                ShowLockedState(unlockLevel);
                return;
            }

            if (currentLevelNumber == unlockLevel && !hasPlayedUnlockAnimation)
            {
                hasPlayedUnlockAnimation = true;
                PlayUnlockAnimation(count, cost);
                return;
            }

            SetText(lockLabel, string.Empty);
            ApplyUnlockedVisualState(count, cost);
        }

        public void Render(BoosterWallet wallet, int currentLevelNumber, bool allowUnlockAnimation = false)
        {
            ResolveReferences();

            if (wallet == null)
            {
                configActive = false;
                SetInteractable(false);
                SetActiveState(null);
                StopHintCountAnimation();
                SetText(lockLabel, string.Empty);
                SetText(countLabel, string.Empty);
                SetText(costLabel, string.Empty);
                return;
            }

            int unlockLevel = wallet.Config.GetUnlockLevel(boosterType);
            bool active = wallet.Config.IsActive(boosterType);
            configActive = active;
            bool unlocked = active && (unlockLevel == 0 || currentLevelNumber >= unlockLevel);
            int count = wallet.GetCount(boosterType);
            int cost = wallet.Config.GetCost(boosterType);

            if (!active)
            {
                SetInteractable(false);
                SetActiveState(null);
                StopHintCountAnimation();
                SetText(lockLabel, string.Empty);
                SetText(countLabel, string.Empty);
                SetText(costLabel, string.Empty);
                gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            SetText(titleLabel, boosterType.ToString());

            // Undo is free and unlimited: it has no count/cost to display, it is simply
            // greyed out whenever there is nothing left to undo.
            if (boosterType == BoosterType.Undo)
            {
                SetActiveState(null);
                SetText(lockLabel, string.Empty);
                SetText(countLabel, string.Empty);
                SetText(costLabel, string.Empty);
                SetInteractable(unlocked && (undoAvailabilityProvider == null || undoAvailabilityProvider()));
                return;
            }

            SetInteractable(unlocked);

            if (!allowUnlockAnimation)
            {
                // Repaint only - never plays (or consumes) the celebration, that is the
                // level-start render's job below.
                if (!unlocked)
                {
                    ShowLockedState(unlockLevel);
                    return;
                }

                // Same rule as below: if the celebration is still due this level, keep showing
                // locked rather than revealing Count/Cost here - an earlier repaint (e.g.
                // Configure()'s own, right as the level loads) would otherwise flash "ready to
                // use" a moment before PlayUnlockAnimation flips it back to locked.
                if (currentLevelNumber == unlockLevel && !hasPlayedUnlockAnimation && playUnlockAnimation)
                {
                    ShowLockedState(unlockLevel);
                    return;
                }

                SetText(lockLabel, string.Empty);
                ApplyUnlockedVisualState(count, cost);
                return;
            }

            if (!unlocked)
            {
                ShowLockedState(unlockLevel);
                return;
            }

            SetText(lockLabel, string.Empty);

            // The whole rule: the celebration plays at level start, on the exact level this booster
            // unlocks, once. Every level after that is simply >= unlockLevel, so it falls straight
            // through to the normal Count/Cost state instead of celebrating again.
            bool shouldCelebrateUnlock = currentLevelNumber == unlockLevel && !hasPlayedUnlockAnimation;

            if (shouldCelebrateUnlock && playUnlockAnimation)
            {
                hasPlayedUnlockAnimation = true;
                PlayUnlockAnimation(count, cost);
                return;
            }

            ApplyUnlockedVisualState(count, cost);
        }

        private void ShowLockedState(int unlockLevel)
        {
            KillUnlockAnimation();
            SetActiveState(lockRoot);
            StopHintCountAnimation();
            SetText(lockLabel, $"level {unlockLevel}");
            SetText(countLabel, string.Empty);
            SetText(costLabel, string.Empty);
        }

        // The actual Count/Cost reveal - called immediately on a normal Render(), or deferred
        // until PlayUnlockAnimation's sequence finishes on the Render() that just unlocked.
        private void ApplyUnlockedVisualState(int count, int cost)
        {
            if (count > 0)
            {
                SetActiveState(countRoot);
                SetText(costLabel, string.Empty);

                // -1 (never rendered) never counts as an "increase" - otherwise the very first
                // Render() after this panel opens would play the added-animation just because
                // count went from "unknown" to whatever the wallet already holds.
                bool isFirstRender = lastRenderedCount < 0;
                if (boosterType == BoosterType.Hint && !isFirstRender && count > lastRenderedCount)
                {
                    PlayHintCountAddedAnimation(lastRenderedCount, count);
                }
                else
                {
                    StopHintCountAnimation();
                    SetText(countLabel, count.ToString());
                }

                lastRenderedCount = count;
                return;
            }

            StopHintCountAnimation();
            lastRenderedCount = count;
            SetActiveState(costRoot);
            SetText(countLabel, string.Empty);
            SetText(costLabel, cost.ToString());
        }

        // Plays once, the instant this booster transitions locked -> unlocked: unlockAnimator
        // plays first, THEN (once it finishes) the particle burst fires alongside unlockContainer
        // scaling up and unlockFadeImage fading out, THEN reveals the Count/Cost state underneath
        // - see ApplyUnlockedVisualState.

bool isCalledUnlockAnimation = false;

        private void PlayUnlockAnimation(int count, int cost)
        {
            if (isCalledUnlockAnimation)
            {
                return;
            }

            isCalledUnlockAnimation = true;

            // Hide lockRoot/countRoot/costRoot entirely - unlockContainer (with its OWN lock-look
            // graphic, unlockFadeImage) takes over showing "still locked" for the celebration.
            SetActiveState(null);

            if (unlockAnimator != null)
            {
                unlockAnimator.Play("Locked");
            }

            // Hide the normal unlocked states until the unlock animation is finished.
            if (countRoot != null)
                countRoot.SetActive(false);

            if (costRoot != null)
                costRoot.SetActive(false);

            unlockAnimTween?.Kill();
            unlockAnimTween = null;

            // Make sure the unlock animation container is visible.
            if (unlockContainer != null)
            {
                unlockContainer.gameObject.SetActive(true);
                unlockContainer.localScale = Vector3.one;
            }

            if (unlockFadeImage != null)
            {
                SetAlpha(unlockFadeImage, 1f);
            }

            // ---------------------------------------------------------
            // 1. WAIT BEFORE STARTING THE UNLOCK ANIMATION
            // ---------------------------------------------------------
            unlockAnimTween = DOVirtual.DelayedCall(
                waitBeforeUnlockAnimation,
                () =>
                {
                    if (unlockAnimator != null)
                    {
                        unlockAnimator.gameObject.SetActive(true);
                        unlockAnimator.Play(unlockAnimatorTrigger);
                    }

                    // -------------------------------------------------
                    // 2. WAIT FOR THE UNLOCK ANIMATOR
                    // -------------------------------------------------
                    unlockAnimTween = DOVirtual.DelayedCall(
                        Mathf.Max(0f, unlockAnimatorDuration),
                        () =>
                        {
                            if (unlockParticles != null)
                            {
                                unlockParticles.Clear();
                                unlockParticles.Play();
                            }

                            // -------------------------------------------------
                            // 3. FADE OUT THE UNLOCK IMAGE
                            // -------------------------------------------------
                            if (unlockFadeImage != null)
                            {
                                unlockAnimTween = unlockFadeImage
                                    .DOFade(0f, unlockFadeOutDuration)
                                    .SetEase(Ease.OutCubic)
                                    .OnComplete(() =>
                                    {
                                        FinishUnlockAnimation(count, cost);
                                    });
                            }
                            else
                            {
                                FinishUnlockAnimation(count, cost);
                            }
                        }
                    );
                }
            );
        }

        private void FinishUnlockAnimation(int count, int cost)
        {
            isCalledUnlockAnimation = false;
            unlockAnimTween = null;

            // Hide the unlock animation after the fade has completed.
            if (unlockContainer != null)
            {
                unlockContainer.localScale = Vector3.one;
                unlockContainer.gameObject.SetActive(false);
                    if (unlockAnimator != null)
                    {
                        unlockAnimator.Play("Locked");
                    }
            }

            // Reset the fade image so the animation can play again.
            if (unlockFadeImage != null)
            {
                SetAlpha(unlockFadeImage, 1f);
            }

            // ---------------------------------------------------------
            // 4. FINALLY SHOW THE NORMAL UNLOCKED STATE
            // ---------------------------------------------------------
            ApplyUnlockedVisualState(count, cost);
        }

        // Guards against this button being disabled/re-locked/destroyed mid-animation - snaps
        // unlockContainer back to its normal scale/alpha and hides it, rather than leaving it
        // stuck mid-reveal.
        private void KillUnlockAnimation()
        {
            unlockAnimTween?.Kill();
            unlockAnimTween = null;

            isCalledUnlockAnimation = false;

            if (unlockContainer != null)
            {
                unlockContainer.localScale = Vector3.one;
                unlockContainer.gameObject.SetActive(false);
            }

            if (unlockFadeImage != null)
            {
                SetAlpha(unlockFadeImage, 1f);
            }
        }

        public void SetInputLocked(bool isLocked)
        {
            ResolveReferences();

            if (canvasGroup != null)
            {
                canvasGroup.interactable = configActive && !isLocked;
                canvasGroup.blocksRaycasts = configActive && !isLocked;
            }

            if (button != null)
            {
                button.interactable = configActive && !isLocked;
            }

            ApplyInteractableSprite(configActive && !isLocked);
        }

        private void SetInteractable(bool interactable)
        {
            isInteractable = interactable;

            if (button != null)
            {
                button.interactable = interactable;
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = interactable;
                // Deliberately NOT tied to interactable - blocksRaycasts=false would make the
                // GraphicRaycaster skip this button's whole hierarchy entirely, so a tap on a
                // still-locked booster would never reach OnPointerClick above and the negative
                // haptic could never fire. button.interactable=false already keeps Button's own
                // onClick/visual transition from doing anything.
                canvasGroup.blocksRaycasts = true;
            }

            ApplyInteractableSprite(interactable);
        }

        // Explicit sprite swap driven from code, independent of the Button's own Transition
        // setup - only swaps if a disabledSprite is actually assigned, so booster buttons that
        // don't set one keep relying on their own Transition/Color Tint as before.
        private void ApplyInteractableSprite(bool interactable)
        {
            if (buttonGraphic == null)
            {
                return;
            }

            // Undo has no dedicated disabled sprite - it just dims via opacity instead of
            // swapping sprites, including its icon child if one exists.
            if (boosterType == BoosterType.Undo)
            {
                float alpha = interactable ? 1f : 0.5f;
                SetAlpha(buttonGraphic, alpha);
                SetAlpha(iconGraphic, alpha);
                return;
            }

            if (disabledSprite == null)
            {
                return;
            }

            buttonGraphic.sprite = interactable
                ? (enabledSprite != null ? enabledSprite : buttonGraphic.sprite)
                : disabledSprite;
        }

        private static void SetAlpha(Image graphic, float alpha)
        {
            if (graphic == null)
            {
                return;
            }

            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
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

        private void SetActiveState(GameObject activeRoot)
        {
            SetVisible(lockRoot, activeRoot == lockRoot);
            SetVisible(countRoot, activeRoot == countRoot);
            SetVisible(costRoot, activeRoot == costRoot);
        }

        private void ClearClickBinding()
        {
            if (button == null || buttonClickHandler == null)
            {
                buttonClickHandler = null;
                return;
            }

            button.onClick.RemoveListener(buttonClickHandler);
            buttonClickHandler = null;
        }

        private void ResolveReferences()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (nudgeTarget == null)
            {
                nudgeTarget = transform as RectTransform;
            }

            if (buttonGraphic == null && button != null)
            {
                buttonGraphic = button.targetGraphic as Image;
                if (buttonGraphic == null)
                {
                    buttonGraphic = button.GetComponent<Image>();
                }
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (iconGraphic == null)
            {
                GameObject namedIcon = FindChild("Icon");
                iconGraphic = namedIcon != null ? namedIcon.GetComponent<Image>() : null;
            }

            GameObject namedLockRoot = FindChild("LockRoot");
            GameObject namedCountRoot = FindChild("CountRoot");
            GameObject namedCostRoot = FindChild("CostRoot");
            lockRoot = namedLockRoot != null ? namedLockRoot : lockRoot;
            countRoot = namedCountRoot != null ? namedCountRoot : countRoot;
            costRoot = namedCostRoot != null ? namedCostRoot : costRoot;

            TMP_Text namedLockLabel = FindLabel(lockRoot, "LockLabel");
            TMP_Text namedCountLabel = FindLabel(countRoot, "CountLabel");
            TMP_Text namedCostLabel = FindLabel(costRoot, "CostLabel");
            TMP_Text namedTitleLabel = FindLabel(gameObject, "Label");
            lockLabel = namedLockLabel != null ? namedLockLabel : lockLabel;
            countLabel = namedCountLabel != null ? namedCountLabel : countLabel;
            costLabel = namedCostLabel != null ? namedCostLabel : costLabel;
            titleLabel = namedTitleLabel != null ? namedTitleLabel : titleLabel;
        }

        private GameObject FindChild(string childName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i].gameObject;
                }
            }

            return null;
        }

        private static TMP_Text FindLabel(GameObject root, string labelName)
        {
            if (root == null)
            {
                return null;
            }

            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].name == labelName)
                {
                    return labels[i];
                }
            }

            return labels.Length > 0 ? labels[0] : null;
        }
    }
}
