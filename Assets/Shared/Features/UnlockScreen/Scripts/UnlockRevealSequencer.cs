using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    /// <summary>
    /// Staged reveal for the Unlock popup's contents.
    ///
    /// Animation flow:
    ///
    ///   1. Main object starts at zero scale.
    ///   2. Its Animator starts immediately.
    ///   3. Main object rapidly scales up with a strong overshoot.
    ///   4. Main object holds briefly at the oversized scale.
    ///   5. Main object smoothly settles back to its resting scale.
    ///   6. Header fades in.
    ///   7. Unlock text fades in.
    ///   8. Description fades in.
    ///   9. Claim/Continue button fades in.
    ///  10. Button becomes interactable only after its fade is complete.
    ///
    /// Each visual element is optional. Missing references are simply skipped.
    /// </summary>
    public sealed class UnlockRevealSequencer : MonoBehaviour
    {
        [Header("Main Object Container")]
        [Tooltip("Parent the current unlock's mainObjectPrefab is instantiated under.")]
        [SerializeField] private RectTransform mainObjectContainer;

        [Header("Main Object Animation")]
        [Tooltip("Animator state played on the instantiated main object.")]
        [SerializeField] private string defaultMainAnimationStateName = "PlayAnimation";

        [Tooltip("Animator state the main object holds on in between Play Animation bursts.")]
        [SerializeField] private string defaultIdleStateName = "Idle";

        [Tooltip("How long the main object stays on the idle state before playing its Play " +
                 "Animation state again.")]
        [SerializeField] private float defaultIdleHoldDuration = 2f;

        [Tooltip("How quickly the main object grows from zero.")]
        [SerializeField] private float defaultScaleUpDuration = 0.28f;

        [Tooltip("Maximum scale reached during the pop. 1.25 = 125%.")]
        [SerializeField] private float defaultScaleUpOvershoot = 1.25f;

        [Tooltip("How long the oversized object stays before settling.")]
        [SerializeField] private float defaultOvershootHoldDuration = 0.08f;

        [Tooltip("How quickly the main object settles back to its normal size.")]
        [SerializeField] private float defaultScaleDownDuration = 0.28f;

        [Header("Header")]
        [SerializeField] private CanvasGroup headerGroup;

        [SerializeField] private float headerFadeDuration = 0.18f;

        [Header("Unlock Text")]
        [SerializeField] private CanvasGroup unlockTextGroup;

        [SerializeField] private float unlockTextFadeDuration = 0.18f;

        [Header("Description")]
        [SerializeField] private CanvasGroup descriptionGroup;

        [SerializeField] private float descriptionFadeDuration = 0.20f;

        [Header("Action Button (Claim/Continue)")]
        [SerializeField] private CanvasGroup actionButtonGroup;

        [Tooltip("Only becomes interactable after the button has completely faded in.")]
        [SerializeField] private Button actionButton;

        [SerializeField] private float actionButtonFadeDuration = 0.22f;

        [Header("Black Background")]
        [Tooltip("Full-screen black backing behind the popup content - needs a CanvasGroup on " +
                 "the same Image GameObject so PlayMoveToTarget can fade it.")]
        [SerializeField] private CanvasGroup blackBackgroundGroup;
        [SerializeField] private float blackBackgroundFadeOutDuration = 0.2f;

        [Header("Particle Effect")]
        [Tooltip("Plays alongside the reveal - needs a CanvasGroup on the same GameObject so " +
                 "PlayMoveToTarget can fade it.")]
        [SerializeField] private CanvasGroup particleEffectGroup;
        [SerializeField] private float particleEffectFadeOutDuration = 0.2f;

        [Header("Move To Target (after PlayExitFadeOut, before the popup closes)")]
        [Tooltip("Canvas units per second the main object travels along its curved path to the " +
                 "target - DOTween speed-based tweening, so it's a real speed (higher = faster) " +
                 "rather than a fixed duration that would move slower over short hops and faster " +
                 "over long ones. Used only when the current unlock's mainObjectPrefab has no " +
                 "MainObjectAnimationInfo, or leaves its own MoveToTargetSpeed unset - see " +
                 "MainObjectAnimationInfo.MoveToTargetSpeed for the per-prefab override.")]
        [SerializeField] private float defaultMoveToTargetSpeed = 1500f;

        [Tooltip("Hard cap on the flight duration regardless of distance/speed - a very long hop " +
                 "(e.g. a small popup far from its Main Menu target) would otherwise take too long.")]
        [SerializeField] private float maxMoveToTargetDuration = 0.9f;

        [Tooltip("How far the flight path bows away from a straight line to the target, as a " +
                 "fraction of the straight-line distance - 0 is a straight line, higher curves more.")]
        [SerializeField] private float moveToTargetArcFactor = 0.35f;

        [Tooltip("Keeps the curved flight path's arc control point from bowing past the edges of " +
                 "the screen - inset from each edge of the root Canvas, in canvas units, so the " +
                 "curve never quite touches the very edge either.")]
        [SerializeField] private float screenEdgePadding = 40f;

        private Canvas cachedRootCanvas;

        private MainObjectAnimationInfo currentMainObjectInstance;
        private RectTransform currentMainObjectRoot;
        private IReadOnlyList<Animator> currentMainObjectAnimators;
        private MainObjectAnimationInfo currentMainObjectInfo;

        private Vector3 currentMainObjectRestingScale = Vector3.one;

        private Coroutine playRoutine;
        private Coroutine idleCycleRoutine;

        private void Awake()
        {
            ResetVisualState();
        }

        /// <summary>
        /// Instantiates the prefab for the current unlock.
        /// The previous main object is destroyed first.
        /// </summary>
        public void SetMainObjectPrefab(MainObjectAnimationInfo prefab)
        {
            StopIdleCycle();

            if (currentMainObjectInstance != null)
            {
                Destroy(currentMainObjectInstance);
            }

            currentMainObjectInstance = null;
            currentMainObjectRoot = null;
            currentMainObjectAnimators = null;
            currentMainObjectInfo = null;

            if (prefab == null || mainObjectContainer == null)
            {
                return;
            }

            currentMainObjectInstance =
                Instantiate(prefab, mainObjectContainer);

            currentMainObjectRoot =
                currentMainObjectInstance.transform as RectTransform;

            currentMainObjectAnimators =
                currentMainObjectInstance.GetAnimators();

            currentMainObjectInfo =
                currentMainObjectInstance;

            if (currentMainObjectRoot != null)
            {
                currentMainObjectRestingScale =
                    currentMainObjectRoot.localScale;
            }
        }

        /// <summary>
        /// Resets everything to the hidden state.
        /// Call this before playing the popup reveal.
        /// </summary>
        public void ResetVisualState()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            StopIdleCycle();

            // Main object starts completely invisible.
            if (currentMainObjectRoot != null)
            {
                currentMainObjectRoot.localScale = Vector3.zero;
            }

            // All UI content starts hidden.
            SetAlpha(headerGroup, 0f);
            SetAlpha(unlockTextGroup, 0f);
            SetAlpha(descriptionGroup, 0f);
            SetAlpha(actionButtonGroup, 0f);

            // Button must not be clickable during the reveal.
            if (actionButton != null)
            {
                actionButton.interactable = false;
            }
        }

        /// <summary>
        /// Starts the complete reveal sequence.
        /// Call this after the popup panel has finished opening.
        /// </summary>
        public void Play()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
            }

            playRoutine = StartCoroutine(PlaySequence());
        }

        private IEnumerator PlaySequence()
        {
            // ---------------------------------------------------------
            // 1. MAIN OBJECT
            // ---------------------------------------------------------

            yield return PlayMainObject();

            // ---------------------------------------------------------
            // 2. HEADER
            // ---------------------------------------------------------

            yield return FadeIn(
                headerGroup,
                headerFadeDuration
            );

            // ---------------------------------------------------------
            // 3. UNLOCK TEXT
            // ---------------------------------------------------------

            yield return FadeIn(
                unlockTextGroup,
                unlockTextFadeDuration
            );

            // ---------------------------------------------------------
            // 4. DESCRIPTION
            // ---------------------------------------------------------

            yield return FadeIn(
                descriptionGroup,
                descriptionFadeDuration
            );

            // ---------------------------------------------------------
            // 5. ACTION BUTTON
            // ---------------------------------------------------------

            yield return FadeIn(
                actionButtonGroup,
                actionButtonFadeDuration
            );

            // Only enable the button after the entire reveal is finished.
            if (actionButton != null)
            {
                actionButton.interactable = true;
            }

            playRoutine = null;
        }

        /// <summary>
        /// Main reward/object animation.
        ///
        /// The Animator starts immediately while the object is scaling.
        ///
        /// Scale:
        ///
        /// 0%
        ///   ↓
        /// 125%
        ///   ↓
        /// short hold
        ///   ↓
        /// 100%
        /// </summary>
        private IEnumerator PlayMainObject()
        {
            if (currentMainObjectRoot == null)
            {
                yield break;
            }

            string stateName =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.StateName
                    : defaultMainAnimationStateName;

            float scaleUpDuration =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.ScaleUpDuration
                    : defaultScaleUpDuration;

            float scaleUpOvershoot =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.ScaleUpOvershoot
                    : defaultScaleUpOvershoot;

            float scaleDownDuration =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.ScaleDownDuration
                    : defaultScaleDownDuration;

            string idleStateName =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.IdleStateName
                    : defaultIdleStateName;

            float idleHoldDuration =
                currentMainObjectInfo != null
                    ? currentMainObjectInfo.IdleHoldDuration
                    : defaultIdleHoldDuration;

            // ---------------------------------------------------------
            // START ANIMATOR IMMEDIATELY
            // ---------------------------------------------------------

            PlayAnimatorState(stateName);

            // ---------------------------------------------------------
            // SCALE 0 → OVERSIZED
            // ---------------------------------------------------------

            Vector3 oversizedScale =
                currentMainObjectRestingScale *
                scaleUpOvershoot;

            yield return ScaleTo(
                currentMainObjectRoot,
                oversizedScale,
                scaleUpDuration,
                ScaleUpEase
            );

            // ---------------------------------------------------------
            // SHORT OVERSHOOT HOLD
            // ---------------------------------------------------------

            if (defaultOvershootHoldDuration > 0f)
            {
                yield return new WaitForSeconds(
                    defaultOvershootHoldDuration
                );
            }

            // ---------------------------------------------------------
            // SCALE OVERSIZED → NORMAL
            // ---------------------------------------------------------

            yield return ScaleTo(
                currentMainObjectRoot,
                currentMainObjectRestingScale,
                scaleDownDuration,
                ScaleSettleEase
            );

            // ---------------------------------------------------------
            // WAIT FOR ANIMATOR IF IT IS STILL PLAYING
            // ---------------------------------------------------------

            yield return WaitForAnimatorsToComplete();

            // ---------------------------------------------------------
            // SWITCH TO IDLE, THEN BREATHE BETWEEN IDLE AND PLAY ANIMATION
            // ---------------------------------------------------------

            if (currentMainObjectAnimators != null && currentMainObjectAnimators.Count > 0)
            {
                PlayAnimatorState(idleStateName);

                StopIdleCycle();
                idleCycleRoutine = StartCoroutine(
                    IdleCycleRoutine(stateName, idleStateName, idleHoldDuration)
                );
            }
        }

        /// <summary>
        /// Plays an Animator state on every animator tracked for the current main object.
        /// </summary>
        private void PlayAnimatorState(string state)
        {
            if (currentMainObjectAnimators == null)
            {
                return;
            }

            for (int i = 0; i < currentMainObjectAnimators.Count; i++)
            {
                Animator animator = currentMainObjectAnimators[i];
                if (animator != null)
                {
                    animator.Play(
                        state,
                        0,
                        0f
                    );
                }
            }
        }

        /// <summary>
        /// Waits one frame (so Animator has entered the state just played) then polls every
        /// frame until every tracked animator's current state has reached normalizedTime >= 1
        /// (played through at least once).
        ///
        /// Deliberately polls rather than pre-computing a single WaitForSeconds(remaining time)
        /// duration up front - a coroutine can only ever resume on a LATER frame than requested,
        /// so a fixed wait risks overshooting the exact frame the clip finishes. If that state's
        /// clip loops, the overshoot lets it wrap back to its own first frame for a frame before
        /// the caller's next PlayAnimatorState() cuts away from it - a visible "flash" back to
        /// the state's start pose, most noticeable switching between two visually distinct states
        /// (e.g. Daily Challenge's Transition -> Btn) rather than into a similar-looking idle.
        /// Polling catches normalizedTime the instant it crosses 1, so the caller's next Play()
        /// pre-empts that frame's animation evaluation before the wrap is ever rendered.
        /// </summary>
        private IEnumerator WaitForAnimatorsToComplete()
        {
            if (currentMainObjectAnimators == null)
            {
                yield break;
            }

            yield return null;

            while (true)
            {
                bool stillPlaying = false;

                for (int i = 0; i < currentMainObjectAnimators.Count; i++)
                {
                    Animator animator = currentMainObjectAnimators[i];
                    if (animator == null)
                    {
                        continue;
                    }

                    if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                    {
                        stillPlaying = true;
                        break;
                    }
                }

                if (!stillPlaying)
                {
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Runs independently of PlaySequence/playRoutine so it doesn't hold up the rest of the
        /// reveal (header/text/description/button fades). Alternates: hold on the idle state for
        /// idleHoldDuration, play the Play Animation state once, wait for it to finish, back to
        /// idle - repeating until stopped (main object destroyed, replaced, or sent to its
        /// Move To Target flight).
        /// </summary>
        private IEnumerator IdleCycleRoutine(
            string playStateName,
            string idleStateNameLocal,
            float idleHoldDurationLocal)
        {
            while (true)
            {
                yield return new WaitForSeconds(
                    Mathf.Max(0f, idleHoldDurationLocal)
                );

                PlayAnimatorState(playStateName);

                yield return WaitForAnimatorsToComplete();

                PlayAnimatorState(idleStateNameLocal);
            }
        }

        /// <summary>
        /// Stops the idle/play breathing loop, if running.
        /// </summary>
        private void StopIdleCycle()
        {
            if (idleCycleRoutine != null)
            {
                StopCoroutine(idleCycleRoutine);
                idleCycleRoutine = null;
            }
        }

        /// <summary>
        /// Generic scale animation.
        /// </summary>
        private static IEnumerator ScaleTo(
            RectTransform target,
            Vector3 targetScale,
            float duration,
            System.Func<float, float> easing)
        {
            if (target == null)
            {
                yield break;
            }

            Vector3 startScale = target.localScale;

            float seconds = Mathf.Max(
                0.01f,
                duration
            );

            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;

                float normalizedTime =
                    Mathf.Clamp01(elapsed / seconds);

                float easedTime =
                    easing != null
                        ? easing(normalizedTime)
                        : normalizedTime;

                target.localScale =
                    Vector3.LerpUnclamped(
                        startScale,
                        targetScale,
                        easedTime
                    );

                yield return null;
            }

            target.localScale = targetScale;
        }

        /// <summary>
        /// Strong pop used when the main object first appears.
        ///
        /// This gives the object the fast:
        ///
        ///     small → large → slight overshoot → settle
        ///
        /// feeling seen in the video.
        /// </summary>
        private static float ScaleUpEase(float t)
        {
            // Stronger back-out than the normal EaseOutBack.
            const float c1 = 2.2f;
            const float c3 = c1 + 1f;

            float f = t - 1f;

            return 1f +
                   c3 * f * f * f +
                   c1 * f * f;
        }

        /// <summary>
        /// Softer easing for the final settle.
        /// </summary>
        private static float ScaleSettleEase(float t)
        {
            // Cubic ease-out.
            float inverse = 1f - t;

            return 1f -
                   inverse * inverse * inverse;
        }

        /// <summary>
        /// Fades a CanvasGroup from its current alpha to 1.
        /// </summary>
        private static IEnumerator FadeIn(
            CanvasGroup group,
            float duration)
        {
            if (group == null)
            {
                yield break;
            }

            float startAlpha = group.alpha;

            float seconds = Mathf.Max(
                0.01f,
                duration
            );

            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / seconds
                    );

                // Smooth fade rather than a completely linear fade.
                float easedT =
                    t * t * (3f - 2f * t);

                group.alpha =
                    Mathf.Lerp(
                        startAlpha,
                        1f,
                        easedT
                    );

                yield return null;
            }

            group.alpha = 1f;
        }

        private static void SetAlpha(
            CanvasGroup group,
            float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        /// <summary>
        /// Fades out every revealed element EXCEPT the main object - call once the player has
        /// acted on the reveal (e.g. tapped Claim/Continue), before actually closing the popup /
        /// calling whatever "open the unlocked feature" function follows, so the main object
        /// stays visible through that hand-off instead of vanishing with everything else.
        /// </summary>
        public void PlayExitFadeOut(System.Action onComplete = null)
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
            }

            playRoutine = StartCoroutine(ExitFadeOutSequence(onComplete));
        }

        private IEnumerator ExitFadeOutSequence(System.Action onComplete)
        {
            if (actionButton != null)
            {
                actionButton.interactable = false;
            }

            yield return RunParallel(
                FadeOut(headerGroup, headerFadeDuration),
                FadeOut(unlockTextGroup, unlockTextFadeDuration),
                FadeOut(descriptionGroup, descriptionFadeDuration),
                FadeOut(actionButtonGroup, actionButtonFadeDuration)
            );

            playRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator RunParallel(params IEnumerator[] routines)
        {
            var running = new Coroutine[routines.Length];
            for (int i = 0; i < routines.Length; i++)
            {
                running[i] = StartCoroutine(routines[i]);
            }

            for (int i = 0; i < running.Length; i++)
            {
                if (running[i] != null)
                {
                    yield return running[i];
                }
            }
        }

        /// <summary>
        /// Fades a CanvasGroup from its current alpha to 0 - the exit-side counterpart to FadeIn.
        /// </summary>
        private static IEnumerator FadeOut(CanvasGroup group, float duration)
        {
            if (group == null)
            {
                yield break;
            }

            float startAlpha = group.alpha;
            float seconds = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                float easedT = t * t * (3f - 2f * t);
                group.alpha = Mathf.Lerp(startAlpha, 0f, easedT);
                yield return null;
            }

            group.alpha = 0f;
        }

        /// <summary>
        /// Flies the current main object to target - matching its world position AND world size
        /// (so it visually docks into whatever slot target represents, e.g. a Main Menu bottom-
        /// nav icon) - then destroys it. Call after PlayExitFadeOut, before the popup itself
        /// closes, so the main object is still on screen (everything else already faded) when it
        /// starts the flight. A null target (feature has no configured Main Menu target) or no
        /// main object currently alive just invokes onComplete immediately, skipping the flight.
        /// </summary>
        public void PlayMoveToTarget(RectTransform target, System.Action onComplete = null)
        {
            if (target == null || currentMainObjectRoot == null)
            {
                onComplete?.Invoke();
                return;
            }
// Debug .LogError("3");
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
            }

            playRoutine = StartCoroutine(MoveToTargetSequence(target, onComplete));
        }

       private IEnumerator MoveToTargetSequence(
    RectTransform target,
    System.Action onComplete)
{
    // Fade out background and particles before movement.
    yield return RunParallel(
        FadeOut(blackBackgroundGroup, blackBackgroundFadeOutDuration),
        FadeOut(particleEffectGroup, particleEffectFadeOutDuration)
    );

    // Stop the idle/play breathing loop before whatever plays next for the flight.
    StopIdleCycle();

    yield return PlayTransitionAndStartFlightState();

    RectTransform root = currentMainObjectRoot;

    GameObject instance =
        currentMainObjectInstance != null
            ? currentMainObjectInstance.gameObject
            : null;

    // Deliberately NOT reparented under target.parent - target is often the real, permanent
    // feature icon (e.g. DailyChallengeButtonUI.unlockRevealObject), which OnUnlockStart hides
    // (SetActive(false)) for the whole unlock flow. Reparenting under it would inherit that
    // inactive state and make the flying clone disappear mid-flight instead of animating.
    // World position/scale animate correctly without reparenting.

    Vector3 startPosition = root.position;
    Vector3 startScale = root.localScale;
    Vector3 targetPosition = target.position;

    // Determine final scale.
    Vector3 targetScale;

    if (currentMainObjectInfo != null)
    {
        targetScale = currentMainObjectInfo.FinalScale;
    }
    else
    {
        Vector3 startWorldSize = GetWorldSize(root);
        Vector3 targetWorldSize = GetWorldSize(target);

        targetScale = new Vector3(
            startWorldSize.x > 0f
                ? startScale.x * (targetWorldSize.x / startWorldSize.x)
                : startScale.x,

            startWorldSize.y > 0f
                ? startScale.y * (targetWorldSize.y / startWorldSize.y)
                : startScale.y,

            startScale.z
        );
    }

    root.DOKill();

    // ---------------------------------------------------------
    // MOVEMENT (curved side-arc, or a parabolic toss - see UseArcMovement below)
    // ---------------------------------------------------------

    float distance = Vector3.Distance(
        startPosition,
        targetPosition
    );

    float speed = Mathf.Max(
        1f,
        currentMainObjectInfo != null ? currentMainObjectInfo.MoveToTargetSpeed : defaultMoveToTargetSpeed
    );

    float duration = Mathf.Min(distance / speed, Mathf.Max(0.01f, maxMoveToTargetDuration));

    Sequence flight = DOTween.Sequence();

    // Two follow paths for the flight's movement, switched explicitly by the current unlock's
    // own mainObjectPrefab via MainObjectAnimationInfo.UseArcMovement - same on/off-by-default
    // pattern as UseTransitionAnimation above. False (or no MainObjectAnimationInfo at all): the
    // existing bowed side-arc path. True: a classic parabolic toss/jump feel instead.
    bool useArcMovement = currentMainObjectInfo != null && currentMainObjectInfo.UseArcMovement;

    if (useArcMovement)
    {
        float jumpPower = currentMainObjectInfo.ArcJumpPower;
        int numJumps = Mathf.Max(1, currentMainObjectInfo.ArcNumJumps);

        // Straight-up-and-down toss to target, rather than bowing sideways.
        flight.Join(
            root.DOJump(
                targetPosition,
                jumpPower,
                numJumps,
                duration
            ).SetEase(Ease.Linear)
        );
    }
    else
    {
        // Bows the flight path away from a straight line to target, by moveToTargetArcFactor times
        // the straight-line distance - 0 collapses back to a straight line. The bow direction is the
        // straight line's direction rotated 90 degrees (in the canvas's XY plane), so the object
        // always arcs to one consistent side rather than cutting straight across.
        Vector3 midpoint = (startPosition + targetPosition) * 0.5f;
        Vector3 direction = targetPosition - startPosition;
        Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f).normalized;
        Vector3 arcControlPoint = midpoint + perpendicular * (distance * moveToTargetArcFactor);
        arcControlPoint = ClampToScreenBounds(arcControlPoint);

        Vector3[] path = { arcControlPoint, targetPosition };

        // Curved movement through arcControlPoint on the way to target.
        flight.Join(
            root.DOPath(
                path,
                duration,
                PathType.CatmullRom
            ).SetEase(Ease.Linear)
        );
    }

    // Scale at the same time as movement.
    flight.Join(
        root.DOScale(
            targetScale,
            duration
        ).SetEase(Ease.Linear)
    );

    bool flightComplete = false;

    flight.OnComplete(() =>
    {
        flightComplete = true;
    });

    yield return new WaitUntil(() => flightComplete);

    // Ensure exact final values.
    root.position = targetPosition;
    root.localScale = targetScale;

    if (instance != null)
    {
        Destroy(instance);
    }

    currentMainObjectInstance = null;
    currentMainObjectRoot = null;
    currentMainObjectAnimators = null;
    currentMainObjectInfo = null;

    playRoutine = null;

    onComplete?.Invoke();
}

        /// <summary>
        /// Clamps a world-space point so it stays within the root Canvas's own bounds (inset by
        /// screenEdgePadding) - used to keep MoveToTargetSequence's curved arc control point from
        /// bowing off-screen when start and target sit close to an edge or corner. Falls back to
        /// returning the point unchanged if no Canvas can be found (e.g. mainObjectContainer
        /// unassigned) rather than guessing at bounds.
        /// </summary>
        private Vector3 ClampToScreenBounds(Vector3 point)
        {
            if (cachedRootCanvas == null && mainObjectContainer != null)
            {
                Canvas canvas = mainObjectContainer.GetComponentInParent<Canvas>();
                cachedRootCanvas = canvas != null ? canvas.rootCanvas : null;
            }

            RectTransform canvasRect = cachedRootCanvas != null ? cachedRootCanvas.transform as RectTransform : null;
            if (canvasRect == null)
            {
                return point;
            }

            Vector3[] corners = new Vector3[4];
            canvasRect.GetWorldCorners(corners);

            // GetWorldCorners order: 0 = bottom-left, 2 = top-right.
            float minX = corners[0].x + screenEdgePadding;
            float maxX = corners[2].x - screenEdgePadding;
            float minY = corners[0].y + screenEdgePadding;
            float maxY = corners[2].y - screenEdgePadding;

            // A canvas smaller than double the padding would invert min/max - skip clamping
            // rather than collapsing the point to a single spot.
            if (minX > maxX || minY > maxY)
            {
                return point;
            }

            return new Vector3(
                Mathf.Clamp(point.x, minX, maxX),
                Mathf.Clamp(point.y, minY, maxY),
                point.z
            );
        }

        /// <summary>
        /// Two follow paths for what plays during the fly-to-target flight, switched explicitly
        /// by the current unlock's own mainObjectPrefab via MainObjectAnimationInfo.
        /// UseTransitionAnimation (e.g. true on Daily Challenge's prefab, false/unset on every
        /// other unlock, e.g. Spin Wheel):
        ///
        ///   - False (or no MainObjectAnimationInfo at all): plain fallback - just replays this
        ///     object's own StateName/IdleStateName pair (the same one used for the reveal) once
        ///     for the flight, same as before this feature existed.
        ///   - True: two-pair Transition flow - plays TransitionStateName once and waits for it
        ///     to finish (e.g. a "turning into a button" beat) BEFORE the flight's move/scale
        ///     tween starts, then plays the second pair for the flight itself: ButtonStateName,
        ///     followed by ButtonIdleStateName (if also set) looping for whatever's left of the
        ///     flight once ButtonStateName finishes.
        /// </summary>
        private IEnumerator PlayTransitionAndStartFlightState()
        {
            bool useTransitionAnimation =
                currentMainObjectInfo != null && currentMainObjectInfo.UseTransitionAnimation;

            if (!useTransitionAnimation)
            {
                string flightStateName =
                    currentMainObjectInfo != null ? currentMainObjectInfo.StateName : defaultMainAnimationStateName;

                PlayAnimatorState(flightStateName);
                yield break;
            }

            string transitionStateName = currentMainObjectInfo.TransitionStateName;
            if (!string.IsNullOrEmpty(transitionStateName))
            {
                PlayAnimatorState(transitionStateName);
                yield return WaitForAnimatorsToComplete();
            }

            string buttonStateName = currentMainObjectInfo.ButtonStateName;
            PlayAnimatorState(!string.IsNullOrEmpty(buttonStateName) ? buttonStateName : currentMainObjectInfo.StateName);

            string buttonIdleStateName = currentMainObjectInfo.ButtonIdleStateName;
            if (!string.IsNullOrEmpty(buttonIdleStateName))
            {
                yield return WaitForAnimatorsToComplete();
                PlayAnimatorState(buttonIdleStateName);
            }
        }

        // World-space width/height of rect - anchor/parent/canvas-scale independent, so it's safe
        // to compare a popup-local main object against a Main Menu target living under a
        // completely different hierarchy.
        private static Vector3 GetWorldSize(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float width = Vector3.Distance(corners[0], corners[3]);
            float height = Vector3.Distance(corners[0], corners[1]);
            return new Vector3(width, height, 1f);
        }

    }
}