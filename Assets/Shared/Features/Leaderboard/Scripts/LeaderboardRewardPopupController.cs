using DG.Tweening;
using Shared.GameConfig;
using Shared.GameFlow;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Leaderboard result popup.
    ///
    /// The whole entrance is a CHAIN OF ONE-STEP FUNCTIONS, not a single authored sequence. Each
    /// step does one thing, takes an onComplete, and is preceded by its own delay field, so any
    /// beat can be retimed - or an extra one slotted in - without unpicking the rest:
    ///
    ///   1  StepShowPodium        podium platforms + highlight, no avatar frame and no name yet
    ///   2  StepFirstPlaceFrame   the winner's frame drops in (the other two fade up behind it)
    ///   3  StepHeadline          headline scales in
    ///   4  StepPodiumMove        the whole podium group slides up by podiumMoveUpY
    ///   5  StepMainGift          the main gift scales up and fades in
    ///   6  StepCollectButton     the collect button scales up and fades in
    ///   -  ...then it waits. Nothing else runs until the player presses Collect.
    ///   7  StepDismissPodium     podium, headline and collect button all alpha out together
    ///   8  StepOpenGift          the gift plays its "OpenAnimation" Animator state
    ///   9  StepSpawnRewards      a slot per reward is spawned into the horizontal container, then
    ///                            each reward flies out of the gift into its own slot, in order
    ///   10 StepFinalButtons      Collect / 2X-with-ads / Close scale in
    ///
    /// The podium objects are NEVER instantiated - assign one authored LeaderboardRowView per rank
    /// and only their bound data changes between cycles. The reward cards ARE pooled instances,
    /// because their count is not known until the event settles.
    /// </summary>
    public sealed class LeaderboardRewardPopupController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private PopupScaleAnimator popupAnimator;

        [Header("Top 3 Podium - Assign Existing Objects")]
        [SerializeField] private LeaderboardRowView firstPlacePodium;
        [SerializeField] private LeaderboardRowView secondPlacePodium;
        [SerializeField] private LeaderboardRowView thirdPlacePodium;

        [Header("Step 1 - Podium Reveal")]
        // On from the very first frame, before anybody has a face: this is what the podium looks
        // like while the winner is still on the way down.
        [SerializeField] private GameObject podiumHighlightRoot;
        // The winner's avatar frame + name. Hidden at step 1, revealed by step 2's drop.
        [SerializeField] private CanvasGroup firstPlaceDetailsGroup;
        // 2nd and 3rd place's frame + name. They fade up behind the winner rather than dropping,
        // so the eye stays on first place.
        [SerializeField] private CanvasGroup[] otherPlaceDetailsGroups = new CanvasGroup[2];
        [SerializeField] private float podiumDetailsFadeDuration = 0.25f;
        [SerializeField] private float otherPlacesRevealDelay = 0.15f;

        [Header("Step 2 - First Place Frame")]
        [SerializeField] private float delayBeforeFirstAvatar;
        [SerializeField] private GameObject firstPlaceAvatarRoot;
        [SerializeField] private float firstAvatarStartOffsetY = 250f;
        [SerializeField] private float firstAvatarDropDuration = 0.55f;
        [SerializeField] private float WaitBeforeAllReveal = 0.55f;
        [SerializeField] private Ease firstAvatarDropEase = Ease.OutBack;

        [Header("Step 3 - Headline")]
        [SerializeField] private GameObject headlineRoot;
        [SerializeField] private TMP_Text headlineText;
        [SerializeField] private float headlineScaleFrom = 0.7f;
        [SerializeField] private float headlineScaleDuration = 0.28f;
        [SerializeField] private float headlineDelay = 0.12f;

        [Header("Step 4 - Podium Move")]
        [SerializeField] private float delayBeforePodiumMove;
        [SerializeField] private RectTransform podiumContentRoot;
        [SerializeField] private float podiumMoveUpY = 100f;
        [SerializeField] private float podiumMoveDuration = 0.35f;
        [SerializeField] private Ease podiumMoveEase = Ease.OutCubic;

        [Header("Step 5 - Main Gift")]
        [SerializeField] private float delayBeforeMainGift;
        // The same object all the way through: it scales in here at step 5, and plays its
        // "OpenAnimation" at step 8. Formerly named rewardTransitionRoot.
        [FormerlySerializedAs("rewardTransitionRoot")]
        [SerializeField] private GameObject mainGiftRoot;
        [FormerlySerializedAs("rewardTransitionAnimator")]
        [SerializeField] private Animator mainGiftAnimator;
        // Played the moment the gift appears, and left looping until Collect is pressed.
        [SerializeField] private string mainGiftIdleAnimation = "IdleAnimation";
        [FormerlySerializedAs("rewardTransitionAnimation")]
        [SerializeField] private string mainGiftOpenAnimation = "OpenAnimation";
        [SerializeField] private float mainGiftScaleFrom = 0.6f;
        [SerializeField] private float mainGiftScaleDuration = 0.35f;
        [SerializeField] private Ease mainGiftScaleEase = Ease.OutBack;

        [Header("Step 6 - Collect Button")]
        [SerializeField] private GameObject collectActionRoot;
        [SerializeField] private Button collectButton;
        [SerializeField] private float collectActionScaleFrom = 0.75f;
        [SerializeField] private float collectActionScaleDuration = 0.3f;
        [SerializeField] private float collectActionDelay = 0.08f;

        [Header("Step 7 - Dismiss")]
        [SerializeField] private float delayBeforeDismiss;
        [SerializeField] private float dismissFadeDuration = 0.25f;

        [Header("Step 8 - Gift Opens")]
        [SerializeField] private float delayBeforeGiftOpen;
        // How long to let the gift's OpenAnimation run before the rewards burst out of it. This is
        // the "X amount of sec" between the lid opening and the prizes appearing.
        [FormerlySerializedAs("rewardTransitionAnimationDuration")]
        [SerializeField] private float delayBeforeRewardSpawn = 0.8f;

        [Header("Step 9 - Rewards")]
        [SerializeField] private GameObject rewardStageRoot;
        [SerializeField] private LeaderboardRewardRowView rewardRowPrefab;
        // The LAYOUT CONTAINER the rewards end up in - a vertical or horizontal layout group. One
        // empty slot is spawned in here per reward, the layout positions the slots, and each card
        // is handed over to its slot once it lands.
        [SerializeField] private RectTransform rewardRowParent;
        // Where the cards live WHILE THEY FLY. Must be outside rewardRowParent, or the layout group
        // would fight the tween for control of their position every frame. Leave empty to use the
        // reward stage root.
        [SerializeField] private RectTransform rewardFlightParent;
        // Optional. Leave empty and a plain sized RectTransform is created per reward, matching the
        // card's own anchors, pivot and size so it lands square on the slot.
        [SerializeField] private RectTransform rewardSlotPrefab;
        // Explicit landing spots. When these are assigned they WIN over the spawned slots, for a
        // layout that is authored by hand rather than by a layout group.
        [SerializeField] private List<RectTransform> rewardPositions = new List<RectTransform>();
        // Where the cards start their flight. Leave empty to launch them from the gift itself.
        [SerializeField] private RectTransform rewardSpawnPoint;
        [SerializeField] private float rewardScaleFrom = 0.55f;
        [SerializeField] private float rewardMoveDuration = 0.45f;
        [SerializeField] private float rewardRevealStagger = 0.1f;
        [SerializeField] private Ease rewardMoveEase = Ease.OutBack;

        [Header("Step 10 - Final Buttons")]
        [SerializeField] private float delayBeforeFinalButtons = 0.1f;
        [SerializeField] private GameObject finalButtonsRoot;
        [SerializeField] private Button finalCollectButton;
        [SerializeField] private Button doubleCollectButton;
        [SerializeField] private Button finalCloseButton;
        [SerializeField] private UnityEvent onRequestDoubleCollectAd;
        [SerializeField] private float finalButtonsScaleFrom = 0.7f;
        [SerializeField] private float finalButtonsScaleDuration = 0.3f;

        [Header("References")]
        [SerializeField] private LeaderboardManager leaderboard;
        [SerializeField] private IconDatabaseConfig iconDatabase;
        // Closed as this popup opens. The board has done its job by then - it showed the standing
        // that earned the reward - and leaving it up behind the collect screen means the player
        // dismisses two things to get back to the game.
        [SerializeField] private LeaderboardPanelController leaderboardPanel;

        [Header("Completion")]
        // Raised once the popup has finished closing, however it was dismissed: Collect, 2X, or the
        // close button. THIS is where the level-win flow continues - see RaiseCompleted.
        //
        // Named apart from the Action onComplete the step functions take: same word, completely
        // different thing, and inside a step the parameter would quietly shadow this field.
        [SerializeField] private UnityEvent onRewardFlowComplete;

        [Header("Result Text")]
        [SerializeField] private TMP_Text rankLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private Image rankBadgeImage;
        [SerializeField] private Sprite[] rankBadges = new Sprite[3];

        [Header("Copy")]
        [SerializeField] private string rankFormat = "You finished {0}!";
        [SerializeField] private string scoreFormat = "{0} fish";
        [SerializeField] private string headlineFormat = "You've won {0} place {1} times!";
        [SerializeField] private string headlineFirstWinFormat = "You've won {0} place!";
        [SerializeField] private Color headlineHighlightColor = new Color(0.34f, 0.85f, 0.36f, 1f);

        [Header("Behaviour")]
        [SerializeField] private bool autoOpenWhenRewardsPending = true;

        private readonly List<LeaderboardRewardRowView> spawnedRewardRows = new List<LeaderboardRewardRowView>();
        private readonly List<RectTransform> spawnedRewardSlots = new List<RectTransform>();

        private CanvasGroup podiumCanvasGroup;
        private CanvasGroup collectActionCanvasGroup;
        private CanvasGroup headlineCanvasGroup;
        private CanvasGroup mainGiftCanvasGroup;
        private RectTransform firstAvatarRect;
        private RectTransform headlineRect;
        private RectTransform collectActionRect;
        private RectTransform mainGiftRect;
        private RectTransform finalButtonsRect;

        private Vector2 originalPodiumPosition;
        private Vector2 originalFirstAvatarPosition;
        private Vector3 originalHeadlineScale;
        private Vector3 originalCollectActionScale;
        private Vector3 originalMainGiftScale;
        private Vector3 originalFinalButtonsScale;

        private bool opening;
        private bool rewardStageStarted;
        private bool finalButtonsShown;
        private Sequence currentSequence;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>
        /// Raised once the popup is fully closed and whatever was owed has been handed over.
        ///
        /// The code-side twin of onComplete, for a caller that would rather subscribe than wire an
        /// Inspector event - the win flow, which needs to carry on only after the player is done
        /// here.
        /// </summary>
        public event Action Completed;

        // =========================================================
        // LIFECYCLE
        // =========================================================

        private void Awake()
        {
            AddButtonListener(collectButton, OnFirstCollectClicked);
            AddButtonListener(finalCollectButton, Collect);
            AddButtonListener(doubleCollectButton, OnDoubleCollectClicked);
            AddButtonListener(finalCloseButton, Close);

            EnsureCanvasGroup(ref podiumCanvasGroup, podiumContentRoot != null ? podiumContentRoot.gameObject : null);
            EnsureCanvasGroup(ref collectActionCanvasGroup, collectActionRoot);
            EnsureCanvasGroup(ref headlineCanvasGroup, headlineRoot);
            EnsureCanvasGroup(ref mainGiftCanvasGroup, mainGiftRoot);

            CacheTransforms();
            HideRuntimeStages();

            if (panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }

            WarnIfMisconfigured();
        }

        private void OnEnable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= HandlePendingRewardsChanged;
                leaderboard.PendingRewardsChanged += HandlePendingRewardsChanged;
            }

            HandlePendingRewardsChanged();
        }

        private void OnDisable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= HandlePendingRewardsChanged;
            }

            KillTweens();
        }

        private void HandlePendingRewardsChanged()
        {
            if (!autoOpenWhenRewardsPending || leaderboard == null || IsOpen)
            {
                return;
            }

            // ShouldAutoOpenRewards, not HasRewards: during the cooldown the reward exists but
            // the board is still showing the standings that earned it, and the leaderboard's own
            // Collect button is the way in. This only becomes true as the next event opens.
            if (leaderboard.ShouldAutoOpenRewards)
            {
                Open();
            }
        }

        [ContextMenu("Open")]
        public void Open()
        {
            if (panelRoot == null || leaderboard == null)
            {
                WarnIfMisconfigured();
                return;
            }

            LeaderboardPendingRewardsData pending = leaderboard.PendingRewards;
            if (!pending.HasRewards)
            {
                return;
            }

            KillTweens();

            opening = true;
            rewardStageStarted = false;
            finalButtonsShown = false;

            // Before this one is up, so the two are never both on screen for a frame.
            CloseLeaderboardPanel();

            panelRoot.SetActive(true);
            Bind(pending);
            ResetAnimationState();

            popupAnimator?.PlayOpen();

            PlayEntranceChain();
        }

        // =========================================================
        // THE CHAIN
        // =========================================================

        /// <summary>
        /// Runs steps 1 to 6 and then stops, leaving the popup waiting on the Collect button.
        ///
        /// Written as nested callbacks rather than one Sequence so each step stays independently
        /// retimable: the delay in front of a step belongs to that step, and inserting a new beat
        /// means adding one function, not re-threading an Append chain.
        /// </summary>
        private void PlayEntranceChain()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StepShowPodium(() =>
                StepFirstPlaceFrame(() =>
                    StepHeadline(() =>
                        StepPodiumMove(() =>
                            StepMainGift(() =>
                                StepCollectButton(() =>
                                {
                                    // Step 6 done - hand control to the player. Nothing runs again
                                    // until OnFirstCollectClicked.
                                    opening = false;
                                }))))));
        }

        /// <summary>
        /// STEP 1. The podium exists: platforms and highlight on screen, but nobody has a face or a
        /// name yet. Instant by design - this is the state the entrance animates out of.
        /// </summary>
        private void StepShowPodium(Action onComplete)
        {
            SetPodiumActive(true);
            SetActive(podiumHighlightRoot, true);
            SetActive(headlineRoot, true);
            SetActive(collectActionRoot, true);

            // Hidden, not deactivated: a deactivated object cannot be faded, and the layout should
            // already be reserving their space so nothing shifts when they appear.
            SetGroupAlpha(firstPlaceDetailsGroup, 0f);
            for (int index = 0; index < otherPlaceDetailsGroups.Length; index++)
            {
                SetGroupAlpha(otherPlaceDetailsGroups[index], 0f);
            }

            SetGroupAlpha(headlineCanvasGroup, 0f);
            SetGroupAlpha(collectActionCanvasGroup, 0f);
            SetGroupAlpha(mainGiftCanvasGroup, 0f);
            SetActive(mainGiftRoot, false);

            onComplete?.Invoke();
        }

        /// <summary>
        /// STEP 2. The winner's frame drops in from above and its details fade up with it; 2nd and
        /// 3rd fade up behind, offset by otherPlacesRevealDelay.
        /// </summary>
        private void StepFirstPlaceFrame(Action onComplete)
        {
            Sequence sequence = NewSequence(delayBeforeFirstAvatar);

            if (firstAvatarRect != null)
            {
                firstAvatarRect.anchoredPosition = originalFirstAvatarPosition + Vector2.up * firstAvatarStartOffsetY;
                
                sequence.Append(
                    firstAvatarRect
                        .DOAnchorPos(originalFirstAvatarPosition, firstAvatarDropDuration)
                        .SetEase(firstAvatarDropEase));
            }

            if (firstPlaceDetailsGroup != null)
            {
                sequence.Join(firstPlaceDetailsGroup.DOFade(1f, podiumDetailsFadeDuration));
            }
            RunSequence(sequence, onComplete);
        }

        /// <summary>STEP 3. The headline scales in.</summary>
        private void StepHeadline(Action onComplete)
        {
            Sequence sequence = NewSequence(headlineDelay);

            if (headlineRect != null)
            {
                headlineRect.localScale = originalHeadlineScale * Mathf.Max(0.01f, headlineScaleFrom);
                sequence.Append(
                    headlineRect
                        .DOScale(originalHeadlineScale, headlineScaleDuration)
                        .SetEase(Ease.OutBack));
            }

            if (headlineCanvasGroup != null)
            {
                sequence.Join(headlineCanvasGroup.DOFade(1f, headlineScaleDuration));
            }

            RunSequence(sequence, onComplete);
        }

        /// <summary>STEP 4. The whole podium group slides up, making room for the gift below it.</summary>
        private void StepPodiumMove(Action onComplete)
        {
            Sequence sequence = NewSequence(delayBeforePodiumMove);

            if (podiumContentRoot != null)
            {
                sequence.Append(
                    podiumContentRoot
                        .DOAnchorPos(originalPodiumPosition + Vector2.up * podiumMoveUpY, podiumMoveDuration)
                        .SetEase(podiumMoveEase));
            }
            for (int index = 0; index < otherPlaceDetailsGroups.Length; index++)
            {
                CanvasGroup group = otherPlaceDetailsGroups[index];
                if (group == null)
                {
                    continue;
                }

                sequence.Insert(
                    Mathf.Max(0.0001f, otherPlacesRevealDelay),
                    group.DOFade(1f, podiumDetailsFadeDuration));
            }

            RunSequence(sequence, onComplete);
        }

        /// <summary>STEP 5. The main gift scales up and fades in.</summary>
        private void StepMainGift(Action onComplete)
        {
            SetActive(mainGiftRoot, true);
            // Started here rather than left to the Animator's default state: the gift is reused
            // across cycles, so without an explicit rewind it would still be sitting on the last
            // frame of OpenAnimation from the previous run.
            PlayGiftAnimation(mainGiftIdleAnimation);

            Sequence sequence = NewSequence(delayBeforeMainGift);

            if (mainGiftRect != null)
            {
                mainGiftRect.localScale = originalMainGiftScale * Mathf.Max(0.01f, mainGiftScaleFrom);
                sequence.Append(
                    mainGiftRect
                        .DOScale(originalMainGiftScale, mainGiftScaleDuration)
                        .SetEase(mainGiftScaleEase));
            }

            if (mainGiftCanvasGroup != null)
            {
                sequence.Join(mainGiftCanvasGroup.DOFade(1f, mainGiftScaleDuration));
            }

            RunSequence(sequence, onComplete);
        }

        /// <summary>STEP 6. The collect button scales up and fades in. Then everything waits.</summary>
        private void StepCollectButton(Action onComplete)
        {
            Sequence sequence = NewSequence(collectActionDelay);

            if (collectActionRect != null)
            {
                collectActionRect.localScale = originalCollectActionScale * Mathf.Max(0.01f, collectActionScaleFrom);
                sequence.Append(
                    collectActionRect
                        .DOScale(originalCollectActionScale, collectActionScaleDuration)
                        .SetEase(Ease.OutBack));
            }

            if (collectActionCanvasGroup != null)
            {
                sequence.Join(collectActionCanvasGroup.DOFade(1f, collectActionScaleDuration));
            }

            RunSequence(sequence, onComplete);
        }

        /// <summary>
        /// The gate between the two halves. Guarded against a double-tap and against being pressed
        /// while the entrance is still running.
        /// </summary>
        private void OnFirstCollectClicked()
        {
            if (opening || rewardStageStarted || leaderboard == null)
            {
                return;
            }

            if (!leaderboard.PendingRewards.HasRewards)
            {
                return;
            }

            rewardStageStarted = true;

            if (collectButton != null)
            {
                collectButton.interactable = false;
            }

            StepDismissPodium(() =>
                StepOpenGift(() =>
                    StepSpawnRewards(() =>
                        StepFinalButtons(null))));
        }

        /// <summary>STEP 7. Podium, headline and collect button all alpha out together.</summary>
        private void StepDismissPodium(Action onComplete)
        {
            Sequence sequence = NewSequence(delayBeforeDismiss);
            float duration = Mathf.Max(0.01f, dismissFadeDuration);

            if (podiumCanvasGroup != null)
            {
                sequence.Join(podiumCanvasGroup.DOFade(0f, duration));
            }

            if (headlineCanvasGroup != null)
            {
                sequence.Join(headlineCanvasGroup.DOFade(0f, duration));
            }

            if (collectActionCanvasGroup != null)
            {
                sequence.Join(collectActionCanvasGroup.DOFade(0f, duration));
            }

            RunSequence(sequence, () =>
            {
                // Deactivated only once they are invisible, so nothing pops.
                SetPodiumActive(false);
                SetActive(podiumHighlightRoot, false);
                SetActive(headlineRoot, false);
                SetActive(collectActionRoot, false);
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// STEP 8. The gift plays its open animation. The step completes after
        /// delayBeforeRewardSpawn, which is the beat between the lid opening and the prizes coming
        /// out - the Animator is left running underneath.
        /// </summary>
        private void StepOpenGift(Action onComplete)
        {
            SetActive(mainGiftRoot, true);

            Sequence sequence = NewSequence(delayBeforeGiftOpen);

            sequence.AppendCallback(() => PlayGiftAnimation(mainGiftOpenAnimation));

            sequence.AppendInterval(Mathf.Max(0f, delayBeforeRewardSpawn));

            RunSequence(sequence, onComplete);
        }

        /// <summary>
        /// STEP 9. One slot per reward is spawned into the horizontal container, then each reward
        /// card launches from the gift and flies into its own slot, in order and staggered.
        ///
        /// The cards are NOT children of the container: they are parented to rewardRowParent and
        /// moved in world space to the slot's position, because a layout group would otherwise
        /// overwrite their position every frame of the flight.
        /// </summary>
        private void StepSpawnRewards(Action onComplete)
        {
            SetActive(rewardStageRoot, true);

            LeaderboardPendingRewardsData pending = leaderboard.PendingRewards;
            BuildRewardRows(pending);
            BuildRewardSlots(pending);

            // Positions only settle once the layout group has run; without this the first flight
            // targets whatever the slots were before they were laid out.
            if (rewardRowParent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rewardRowParent);
            }

            float delay = 0f;
            int animatedCount = 0;

            for (int index = 0; index < spawnedRewardRows.Count; index++)
            {
                LeaderboardRewardRowView row = spawnedRewardRows[index];
                if (row == null || !row.gameObject.activeSelf)
                {
                    continue;
                }

                RectTransform rowRect = row.transform as RectTransform;
                RectTransform target = ResolveRewardTarget(index);

                if (rowRect == null || target == null)
                {
                    // Nowhere to fly to - show it where it is rather than leaving it invisible.
                    row.gameObject.SetActive(true);
                    SetGroupAlpha(EnsureRowGroup(row), 1f);
                    continue;
                }

                rowRect.position = rewardSpawnPoint != null
                    ? rewardSpawnPoint.position
                    : (mainGiftRect != null ? mainGiftRect.position : rowRect.position);
                rowRect.localScale = Vector3.one * Mathf.Max(0.01f, rewardScaleFrom);

                CanvasGroup group = EnsureRowGroup(row);
                SetGroupAlpha(group, 0f);

                Sequence flight = DOTween.Sequence().SetUpdate(true).SetDelay(delay);
                flight.Append(rowRect.DOScale(Vector3.one, rewardMoveDuration).SetEase(rewardMoveEase));
                flight.Join(rowRect.DOMove(target.position, rewardMoveDuration).SetEase(rewardMoveEase));

                if (group != null)
                {
                    flight.Join(group.DOFade(1f, rewardMoveDuration * 0.65f));
                }

                // Handed over to the layout the moment it lands. Until now the card was flying in
                // world space with nothing managing it; from here the slot owns its position, so
                // the list reflows correctly if anything resizes afterwards.
                RectTransform landing = target;
                flight.OnComplete(() => Dock(rowRect, landing));

                delay += Mathf.Max(0f, rewardRevealStagger);
                animatedCount++;
            }

            float total = animatedCount <= 0 ? 0f : delay + rewardMoveDuration;
            DOVirtual.DelayedCall(total, () => onComplete?.Invoke(), true).SetUpdate(true);
        }

        /// <summary>
        /// Plays a state on the gift from its first frame. Rewound explicitly (the 0f) so replaying
        /// the same state name restarts it rather than being ignored as "already playing".
        /// </summary>
        private void PlayGiftAnimation(string stateName)
        {
            if (mainGiftAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return;
            }

            mainGiftAnimator.Play(stateName, 0, 0f);
        }

        /// <summary>STEP 10. Collect / 2X / Close scale in.</summary>
        private void StepFinalButtons(Action onComplete)
        {
            if (!IsOpen || finalButtonsShown)
            {
                return;
            }

            finalButtonsShown = true;
            SetActive(finalButtonsRoot, true);

            Sequence sequence = NewSequence(delayBeforeFinalButtons);

            if (finalButtonsRect != null)
            {
                finalButtonsRect.localScale = originalFinalButtonsScale * Mathf.Max(0.01f, finalButtonsScaleFrom);
                sequence.Append(
                    finalButtonsRect
                        .DOScale(originalFinalButtonsScale, finalButtonsScaleDuration)
                        .SetEase(Ease.OutBack));
            }

            RunSequence(sequence, onComplete);
        }

        // =========================================================
        // CLAIMING
        // =========================================================

        public void Collect()
        {
            if (leaderboard == null)
            {
                return;
            }

            leaderboard.ClaimPendingRewards();
            Close();
        }

        private void OnDoubleCollectClicked()
        {
            // Grants nothing by itself. Hook the rewarded ad to this UnityEvent and call
            // CompleteDoubleCollectAfterAd ONLY once the ad reports a successful reward - a 2X that
            // pays out on a cancelled ad is a live economy exploit.
            onRequestDoubleCollectAd?.Invoke();
        }

        /// <summary>Call from the rewarded-ad success callback. Never on cancel or failure.</summary>
        public void CompleteDoubleCollectAfterAd()
        {
            if (!finalButtonsShown || leaderboard == null)
            {
                return;
            }

            leaderboard.ClaimPendingRewards(true);
            Close();
        }

        public void Close()
        {
            KillTweens();

            if (panelRoot == null)
            {
                return;
            }

            // Nothing to complete if it was never up - Close is also called defensively.
            bool wasOpen = panelRoot.activeSelf;

            if (popupAnimator != null && isActiveAndEnabled)
            {
                popupAnimator.PlayClose(() =>
                {
                    panelRoot.SetActive(false);
                    ResetAnimationState();
                    if (wasOpen)
                    {
                        RaiseCompleted();
                    }
                });
                return;
            }

            panelRoot.SetActive(false);
            ResetAnimationState();

            if (wasOpen)
            {
                RaiseCompleted();
            }
        }

        /// <summary>
        /// Takes the board down as this popup comes up.
        ///
        /// Falls back to a scene search when the reference is empty, because it usually IS empty:
        /// this popup is a prefab and the board is a scene object, so the link cannot be authored
        /// on the prefab at all - only on each instance, which is exactly the kind of wiring that
        /// goes missing. Resolved once and cached; the cost is one search per session.
        /// </summary>
        private void CloseLeaderboardPanel()
        {
            if (leaderboardPanel == null)
            {
                leaderboardPanel = FindFirstObjectByType<LeaderboardPanelController>(FindObjectsInactive.Include);
            }

            if (leaderboardPanel != null && leaderboardPanel.IsOpen)
            {
                leaderboardPanel.Close();
            }
        }

        /// <summary>
        /// The hand-off back to whatever was interrupted to show this.
        ///
        /// Raised AFTER the close animation, not when the button is pressed: the level-win flow
        /// that resumes here will put its own popup on screen, and starting it early puts two
        /// popups on top of each other.
        /// </summary>
        private void RaiseCompleted()
        {
            Debug.Log(
                "[Leaderboard] Reward popup complete - the level-win flow continues from here. "
                + "Hook this up in CatFlapGameplayManager.HandleLevelWon (around the "
                + "leaderboardManager.AddWinPoints call), by subscribing to "
                + "LeaderboardRewardPopupController.Completed or assigning its onRewardFlowComplete event.",
                this);

            onRewardFlowComplete?.Invoke();
            Completed?.Invoke();
        }

        // =========================================================
        // BINDING
        // =========================================================

        private void Bind(LeaderboardPendingRewardsData pending)
        {
            if (rankLabel != null)
            {
                rankLabel.text = string.Format(rankFormat, ResolveOrdinal(pending.Rank));
            }

            if (scoreLabel != null)
            {
                scoreLabel.text = string.Format(scoreFormat, pending.Score);
            }

            if (rankBadgeImage != null)
            {
                int badgeIndex = pending.Rank - 1;
                Sprite badge = rankBadges != null && badgeIndex >= 0 && badgeIndex < rankBadges.Length
                    ? rankBadges[badgeIndex]
                    : null;

                rankBadgeImage.sprite = badge;
                rankBadgeImage.enabled = badge != null;
            }

            if (headlineText != null)
            {
                headlineText.text = BuildHeadline(pending);
            }

            BindPodiumRow(firstPlacePodium, pending, 0);
            BindPodiumRow(secondPlacePodium, pending, 1);
            BindPodiumRow(thirdPlacePodium, pending, 2);
        }

        private void BindPodiumRow(LeaderboardRowView podium, LeaderboardPendingRewardsData pending, int index)
        {
            if (podium == null)
            {
                return;
            }

            if (pending == null || pending.Podium == null || index >= pending.Podium.Count)
            {
                podium.gameObject.SetActive(false);
                return;
            }

            LeaderboardPodiumPlayerData data = pending.Podium[index];
            LeaderboardEntry entry = leaderboard.ResolvePendingPodiumEntry(data, index + 1);

            podium.gameObject.SetActive(true);
            podium.Bind(entry, null);
            podium.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Builds the cards OUTSIDE the layout container, ready to fly in.
        ///
        /// Pooled rather than destroyed - the popup runs again next event and the counts are tiny.
        /// Pooled cards are pulled back out of last run's slots first, or they would start their
        /// flight already docked where they are supposed to finish it.
        /// </summary>
        private void BuildRewardRows(LeaderboardPendingRewardsData pending)
        {
            RectTransform flightParent = ResolveFlightParent();
            if (rewardRowPrefab == null || flightParent == null || pending == null)
            {
                return;
            }

            for (int index = 0; index < spawnedRewardRows.Count; index++)
            {
                if (spawnedRewardRows[index] != null)
                {
                    spawnedRewardRows[index].gameObject.SetActive(false);
                }
            }

            for (int index = 0; index < pending.Rewards.Count; index++)
            {
                LeaderboardRewardData reward = pending.Rewards[index];

                if (index >= spawnedRewardRows.Count)
                {
                    spawnedRewardRows.Add(Instantiate(rewardRowPrefab, flightParent));
                }

                LeaderboardRewardRowView row = spawnedRewardRows[index];
                row.transform.SetParent(flightParent, false);
                row.gameObject.SetActive(true);
                row.Bind(ResolveIcon(reward), ResolveLabel(reward), reward.Amount);
            }
        }

        /// <summary>
        /// Parents a landed card into its slot and lets the layout take it from here.
        ///
        /// anchoredPosition is zeroed rather than preserved: the slot was built with the card's own
        /// anchors, pivot and size, so zero IS the spot the card just flew to - no visible jump.
        /// </summary>
        private static void Dock(RectTransform card, RectTransform slot)
        {
            if (card == null || slot == null)
            {
                return;
            }

            card.SetParent(slot, false);
            card.anchoredPosition = Vector2.zero;
            card.localScale = Vector3.one;
        }

        /// <summary>Anywhere outside the layout container. Falls back so this can be left unassigned.</summary>
        private RectTransform ResolveFlightParent()
        {
            if (rewardFlightParent != null)
            {
                return rewardFlightParent;
            }

            if (rewardStageRoot != null && rewardStageRoot.transform is RectTransform stageRect)
            {
                return stageRect;
            }

            return transform as RectTransform;
        }

        /// <summary>
        /// One empty landing slot per reward, inside the layout container.
        ///
        /// These exist so the layout group has something to measure and arrange BEFORE the cards
        /// arrive - the flight needs a final position to aim at, and the only thing that knows it
        /// is the layout itself. Pooled the same way the cards are.
        /// </summary>
        private void BuildRewardSlots(LeaderboardPendingRewardsData pending)
        {
            if (rewardRowParent == null || pending == null)
            {
                return;
            }

            for (int index = 0; index < spawnedRewardSlots.Count; index++)
            {
                if (spawnedRewardSlots[index] != null)
                {
                    spawnedRewardSlots[index].gameObject.SetActive(false);
                }
            }

            for (int index = 0; index < pending.Rewards.Count; index++)
            {
                if (index >= spawnedRewardSlots.Count)
                {
                    spawnedRewardSlots.Add(rewardSlotPrefab != null
                        ? Instantiate(rewardSlotPrefab, rewardRowParent)
                        : CreateRewardSlot(index));
                }

                RectTransform slot = spawnedRewardSlots[index];
                slot.gameObject.SetActive(true);
                // Re-asserted every run: the pool is shared across cycles and the order has to
                // match the reward order, not the order the slots happened to be created in.
                slot.SetSiblingIndex(index);
            }
        }

        /// <summary>
        /// Builds a slot from the reward card itself - same anchors, same pivot, same size.
        ///
        /// Matching all three is what lets a landed card dock with anchoredPosition zero and not
        /// move a pixel: the slot occupies exactly the rect the card was flown to. A LayoutElement
        /// carries the size so a layout group that controls its children still reserves the right
        /// space for it.
        /// </summary>
        private RectTransform CreateRewardSlot(int index)
        {
            var slotGO = new GameObject("RewardSlot" + index, typeof(RectTransform));
            var slot = (RectTransform)slotGO.transform;
            slot.SetParent(rewardRowParent, false);

            var template = rewardRowPrefab != null ? rewardRowPrefab.transform as RectTransform : null;
            Vector2 size = template != null ? template.rect.size : new Vector2(200f, 200f);

            if (template != null)
            {
                slot.anchorMin = template.anchorMin;
                slot.anchorMax = template.anchorMax;
                slot.pivot = template.pivot;
            }

            slot.sizeDelta = size;

            LayoutElement element = slotGO.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            return slot;
        }

        /// <summary>Authored positions win; otherwise the spawned slot for that index.</summary>
        private RectTransform ResolveRewardTarget(int index)
        {
            if (index < rewardPositions.Count && rewardPositions[index] != null)
            {
                return rewardPositions[index];
            }

            return index < spawnedRewardSlots.Count ? spawnedRewardSlots[index] : null;
        }

        private Sprite ResolveIcon(LeaderboardRewardData reward)
        {
            if (iconDatabase == null || string.IsNullOrWhiteSpace(reward.IconKey))
            {
                return null;
            }

            return iconDatabase.TryGetIcon(reward.IconKey, out Sprite icon) ? icon : null;
        }

        private static string ResolveLabel(LeaderboardRewardData reward)
        {
            switch (reward.Kind)
            {
                case LeaderboardRewardKind.Coins:
                    return "Coins";
                case LeaderboardRewardKind.Booster:
                    return string.IsNullOrWhiteSpace(reward.ItemId) ? "Booster" : reward.ItemId + " Booster";
                case LeaderboardRewardKind.ProfileAvatar:
                    return "New Avatar";
                case LeaderboardRewardKind.ProfileFrame:
                    return "New Frame";
                default:
                    return string.Empty;
            }
        }

        private string BuildHeadline(LeaderboardPendingRewardsData pending)
        {
            string ordinal = ResolveOrdinal(pending.Rank);

            if (pending.RankWinCount <= 1)
            {
                return string.Format(headlineFirstWinFormat, ordinal);
            }

            string count = "<color=#" + ColorUtility.ToHtmlStringRGB(headlineHighlightColor) + ">"
                + pending.RankWinCount + "</color>";

            return string.Format(headlineFormat, ordinal, count);
        }

        // =========================================================
        // TWEEN PLUMBING
        // =========================================================

        /// <summary>
        /// A fresh unscaled sequence with this step's lead-in delay already on it.
        ///
        /// Unscaled throughout: the results screen runs over a paused menu, where a scaled tween
        /// would never advance.
        /// </summary>
        private Sequence NewSequence(float delayBefore)
        {
            Sequence sequence = DOTween.Sequence().SetUpdate(true);

            // Never empty. A Sequence with nothing in it is not reliably driven to completion, and
            // a step that never completes would stall the chain silently - every step after it
            // simply never runs. The floor costs one frame and removes the whole failure mode.
            sequence.AppendInterval(Mathf.Max(0.0001f, delayBefore));

            return sequence;
        }

        /// <summary>
        /// Tracks the sequence so KillTweens can stop it, and fires the next step on completion.
        ///
        /// An empty sequence still completes on the next tick, so a step whose objects are all
        /// unassigned passes the chain along instead of stalling it - which is what makes this safe
        /// to wire up one reference at a time.
        /// </summary>
        private void RunSequence(Sequence sequence, Action onComplete)
        {
            currentSequence = sequence;
            sequence.OnComplete(() =>
            {
                currentSequence = null;
                onComplete?.Invoke();
            });
        }

        private void CacheTransforms()
        {
            firstAvatarRect = firstPlaceAvatarRoot != null ? firstPlaceAvatarRoot.transform as RectTransform : null;
            headlineRect = headlineRoot != null ? headlineRoot.transform as RectTransform : null;
            collectActionRect = collectActionRoot != null ? collectActionRoot.transform as RectTransform : null;
            mainGiftRect = mainGiftRoot != null ? mainGiftRoot.transform as RectTransform : null;
            finalButtonsRect = finalButtonsRoot != null ? finalButtonsRoot.transform as RectTransform : null;

            if (podiumContentRoot != null)
            {
                originalPodiumPosition = podiumContentRoot.anchoredPosition;
            }

            if (firstAvatarRect != null)
            {
                originalFirstAvatarPosition = firstAvatarRect.anchoredPosition;
            }

            originalHeadlineScale = headlineRect != null ? headlineRect.localScale : Vector3.one;
            originalCollectActionScale = collectActionRect != null ? collectActionRect.localScale : Vector3.one;
            originalMainGiftScale = mainGiftRect != null ? mainGiftRect.localScale : Vector3.one;
            originalFinalButtonsScale = finalButtonsRect != null ? finalButtonsRect.localScale : Vector3.one;
        }

        /// <summary>Everything back to how it was authored, so the next open starts clean.</summary>
        private void ResetAnimationState()
        {
            opening = false;
            rewardStageStarted = false;
            finalButtonsShown = false;

            ClearRewardContainer();

            if (podiumContentRoot != null)
            {
                podiumContentRoot.anchoredPosition = originalPodiumPosition;
            }

            if (firstAvatarRect != null)
            {
                firstAvatarRect.anchoredPosition = originalFirstAvatarPosition;
            }

            if (headlineRect != null)
            {
                headlineRect.localScale = originalHeadlineScale;
            }

            if (collectActionRect != null)
            {
                collectActionRect.localScale = originalCollectActionScale;
            }

            if (mainGiftRect != null)
            {
                mainGiftRect.localScale = originalMainGiftScale;
            }

            if (finalButtonsRect != null)
            {
                finalButtonsRect.localScale = originalFinalButtonsScale;
            }

            SetGroupAlpha(podiumCanvasGroup, 1f);
            SetGroupAlpha(collectActionCanvasGroup, 1f);
            SetGroupAlpha(headlineCanvasGroup, 1f);
            SetGroupAlpha(mainGiftCanvasGroup, 1f);

            if (collectButton != null)
            {
                collectButton.interactable = true;
            }

            HideRuntimeStages();
            SetPodiumActive(false);
        }

        /// <summary>
        /// Empties the layout container of last cycle's prizes.
        ///
        /// Needed because the cards END their flight parented INTO the container - so unlike every
        /// other part of this popup, the reward stage does not return to its authored state on its
        /// own. Without this, opening a second time shows the previous cycle's rewards already
        /// sitting in place before the gift has even opened.
        ///
        /// Pooled, not destroyed: the cards are reused, just pulled back out to the flight parent
        /// and hidden. Inactive children are ignored by layout groups, so the container measures as
        /// genuinely empty.
        /// </summary>
        private void ClearRewardContainer()
        {
            RectTransform flightParent = ResolveFlightParent();

            for (int index = 0; index < spawnedRewardRows.Count; index++)
            {
                LeaderboardRewardRowView row = spawnedRewardRows[index];
                if (row == null)
                {
                    continue;
                }

                if (flightParent != null)
                {
                    // Undocks it. Skipping this would deactivate the card along with its slot and
                    // then re-parent a hidden object next run.
                    row.transform.SetParent(flightParent, false);
                }

                row.gameObject.SetActive(false);
            }

            for (int index = 0; index < spawnedRewardSlots.Count; index++)
            {
                if (spawnedRewardSlots[index] != null)
                {
                    spawnedRewardSlots[index].gameObject.SetActive(false);
                }
            }
        }

        private void HideRuntimeStages()
        {
            SetActive(finalButtonsRoot, false);
            SetActive(mainGiftRoot, false);
            SetActive(rewardStageRoot, false);
            SetActive(collectActionRoot, false);
            SetActive(headlineRoot, false);
        }

        private void SetPodiumActive(bool active)
        {
            SetActive(firstPlacePodium, active);
            SetActive(secondPlacePodium, active);
            SetActive(thirdPlacePodium, active);
        }

        private void KillTweens()
        {
            currentSequence?.Kill();
            currentSequence = null;

            KillOn(podiumContentRoot);
            KillOn(firstAvatarRect);
            KillOn(headlineRect);
            KillOn(collectActionRect);
            KillOn(mainGiftRect);
            KillOn(finalButtonsRect);

            podiumCanvasGroup?.DOKill();
            collectActionCanvasGroup?.DOKill();
            headlineCanvasGroup?.DOKill();
            mainGiftCanvasGroup?.DOKill();
            firstPlaceDetailsGroup?.DOKill();

            for (int index = 0; index < otherPlaceDetailsGroups.Length; index++)
            {
                otherPlaceDetailsGroups[index]?.DOKill();
            }

            for (int index = 0; index < spawnedRewardRows.Count; index++)
            {
                if (spawnedRewardRows[index] == null)
                {
                    continue;
                }

                spawnedRewardRows[index].transform.DOKill();
                spawnedRewardRows[index].GetComponent<CanvasGroup>()?.DOKill();
            }
        }

        private static void KillOn(Transform target)
        {
            target?.DOKill();
        }

        private CanvasGroup EnsureRowGroup(LeaderboardRewardRowView row)
        {
            CanvasGroup group = row.GetComponent<CanvasGroup>();
            return group != null ? group : row.gameObject.AddComponent<CanvasGroup>();
        }

        private static void SetGroupAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        private static void AddButtonListener(Button button, UnityAction action)
        {
            if (button == null || action == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void EnsureCanvasGroup(ref CanvasGroup group, GameObject target)
        {
            if (group != null || target == null)
            {
                return;
            }

            group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static void SetActive(Component target, bool active)
        {
            if (target != null)
            {
                SetActive(target.gameObject, active);
            }
        }

        private static string ResolveOrdinal(int rank)
        {
            // 11th/12th/13th are the exception every naive suffix rule gets wrong.
            int lastTwo = rank % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return rank + "th";
            }

            switch (rank % 10)
            {
                case 1: return rank + "st";
                case 2: return rank + "nd";
                case 3: return rank + "rd";
                default: return rank + "th";
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnIfMisconfigured()
        {
            if (leaderboard == null)
            {
                Debug.LogWarning("[Leaderboard] Assign LeaderboardManager to LeaderboardRewardPopupController.", this);
            }

            if (panelRoot == null)
            {
                Debug.LogWarning("[Leaderboard] Assign Panel Root to LeaderboardRewardPopupController.", this);
            }

            if (firstPlacePodium == null || secondPlacePodium == null || thirdPlacePodium == null)
            {
                Debug.LogWarning("[Leaderboard] Assign all three existing podium LeaderboardRowView objects.", this);
            }

            if (rewardRowPrefab == null)
            {
                Debug.LogWarning("[Leaderboard] Assign Reward Row Prefab.", this);
            }

            if (rewardRowParent != null && rewardFlightParent == rewardRowParent)
            {
                Debug.LogWarning("[Leaderboard] Reward Flight Parent must NOT be the same object as Reward Row Parent - the layout group would fight the flight tween.", this);
            }
        }
    }
}
