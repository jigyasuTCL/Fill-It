using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// One rendered row - rank, name, score - used for BOTH a top-3 podium place and an ordinary
    /// row inside the scroll list. The same prefab serves both so a row keeps its identity (and so
    /// its own move/punch animation) when it crosses between the two; see
    /// LeaderboardPanelController.RenderBoard.
    ///
    /// Owns only its own presentation and animation. It has no idea what rank it "should" be, when
    /// the board refreshes, or where it sits relative to other rows - the controller drives all of
    /// that and hands this a finished anchored position.
    /// </summary>
    public sealed class LeaderboardRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI rankLabel;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI scoreLabel;
        // The profile avatar: the avatar sits inside the frame, so author frameImage ABOVE avatarImage
        // in the hierarchy. Both optional and independent - a row with neither assigned renders
        // name + score exactly as it did before profiles existed, and either is hidden on its own
        // when that entry has no art for it.
        [SerializeField] private Image avatarImage;
        [SerializeField] private Image frameImage;

        [Header("Rank")]
        // The top three wear a medal instead of a plain number. Exactly one of these roots is ever
        // visible: index 0 = 1st, 1 = 2nd, 2 = 3rd, and anything past the end of the array falls
        // through to the plain number - so a project that only art-directs gold can leave the
        // other two empty.
        [SerializeField] private GameObject rankBadgeRoot;
        [SerializeField] private Image rankBadgeImage;
        [SerializeField] private Sprite[] topRankBadges = new Sprite[3];
        // Holds the plain rank number, shown for every place the badges do not cover. rankLabel
        // lives inside this in the usual setup, but the two are separate so a design can keep the
        // number visible ON the medal too.
        [SerializeField] private GameObject plainRankRoot;

        [Header("Gift")]
        // The prize icon on a rewarded placing. The panel resolves which sprite (it owns the
        // reward table and the icon database); this just shows or hides it.
        [SerializeField] private GameObject giftRoot;
        [SerializeField] private Image giftImage;
        // Optional - a frame/tint object shown only on the local player's own row, so they can
        // pick themselves out of the list at a glance. Left unassigned, the player's row simply
        // renders like any other.
        [SerializeField] private GameObject playerHighlightRoot;
        // Auto-resolved (and added if missing) in Awake, same self-wiring approach as
        // FtueBannerController's own canvas group - no prefab wiring needed for the fade to work.
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Animation")]
        // How far past full size a row overshoots when its rank changes - the same "punch" idiom
        // the HUD already uses (see CatFlapStatusViewAdapter.ScalePunchRoutine). Kept subtle: this
        // fires on several rows at once, and a big pop on each reads as noise.
        [SerializeField] private float rankChangePunchScale = 1.06f;
        [SerializeField] private float rankChangePunchDurationSeconds = 0.22f;
        // A row appearing for the first time fades in rather than popping, so opening the board
        // doesn't fire twenty punch animations at once.
        [SerializeField] private float appearFadeDurationSeconds = 0.18f;
        // How small a row starts before it fades in. 1 is a pure fade with no scale at all.
        [SerializeField] private float appearScaleFrom = 0.92f;

        // Shape of a row's slide from its old slot to its new one, sampled 0..1 across
        // LeaderboardPanelController.rowMoveDurationSeconds. Defaults to an ease-out: rows leave
        // quickly and settle gently, which is what makes a reshuffle readable rather than floaty.
        // Overshoot past 1 near the end for a springier feel.
        [SerializeField]
        private AnimationCurve rowMoveCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(1f, 1f, 0f, 0f));

        // Shape of the rank-change punch, sampled 0..1 and multiplied into the overshoot. Defaults
        // to a symmetric out-and-back.
        [SerializeField]
        private AnimationCurve rankChangePunchCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));

        [Header("Podium Data Swap")]
        // A podium place never changes rank - slot 1 is always 1st - so when somebody new takes it,
        // there is no movement to read the change by. Instead the card drops out of sight, picks up
        // the new name and score down there, and rises back carrying them.
        //
        // Author the podium so this distance takes the card behind its pedestal or off the bottom
        // of a mask: the whole illusion is that the swap is never seen happening.
        [SerializeField] private float podiumSwapDipDistance = 160f;
        [SerializeField] private float podiumSwapDownSeconds = 0.18f;
        // Beat at the bottom, after the new data is in and before the rise. 0 swaps and returns in
        // one motion.
        [SerializeField] private float podiumSwapHoldSeconds = 0.05f;
        [SerializeField] private float podiumSwapUpSeconds = 0.24f;
        [SerializeField]
        private AnimationCurve podiumSwapCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(1f, 1f, 0f, 0f));

        [Header("Climb")]
        // A row moving UP does not just slide: it swells, travels at that size, then settles back.
        // Holding the scale for the whole journey is what reads as "this one is being lifted past
        // the others" rather than a row that happens to wobble on its way.
        [SerializeField] private float climbScale = 1.15f;
        [SerializeField] private float climbScaleUpSeconds = 0.12f;
        [SerializeField] private float climbSettleSeconds = 0.15f;
        [SerializeField]
        private AnimationCurve climbScaleCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(1f, 1f, 0f, 0f));

        private RectTransform rect;
        // Where this row belongs in the sibling order when it is not being lifted. -1 until the
        // controller has placed it.
        private int restingSiblingIndex = -1;
        private Coroutine moveRoutine;
        private Coroutine punchRoutine;
        private Coroutine fadeRoutine;

        /// <summary>The rank this row is currently displaying - the controller compares against it to decide whether this row actually moved.</summary>
        public int Rank { get; private set; }

        /// <summary>
        /// Who this row is currently showing. A podium place keeps the same RANK forever, so rank
        /// alone cannot tell the controller that somebody new has taken it - these can.
        /// </summary>
        public string DisplayedName { get; private set; }

        public int DisplayedScore { get; private set; }

        /// <summary>
        /// Which of the two row prefabs this instance was spawned from - podium (top 3) or list.
        /// Set once at spawn and never changed: the two are separate prefabs with separate art, so
        /// an entry moving between the podium and the list has to be given a different row object
        /// rather than having this one re-skin itself. The controller uses this both to return the
        /// row to the right pool and to notice that swap.
        /// </summary>
        public bool IsPodiumRow { get; private set; }

        public void MarkAsPodiumRow(bool isPodiumRow)
        {
            IsPodiumRow = isPodiumRow;
        }

        public RectTransform Rect
        {
            get
            {
                if (rect == null)
                {
                    rect = transform as RectTransform;
                }

                return rect;
            }
        }

        private void Awake()
        {
            rect = transform as RectTransform;
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        [Header("Online Status")]
        // "Playing now" / "Online now" / "12m ago". Optional.
        [SerializeField] private TMPro.TMP_Text statusLabel;
        // The green dot. Shown for online AND playing - both mean "here right now".
        [SerializeField] private GameObject onlineIndicatorRoot;
        // Shown only while actively playing a level, for art that separates the two.
        [SerializeField] private GameObject playingIndicatorRoot;
        // Shown when they are not around at all, for art that has an explicit grey dot rather than
        // just an absent green one.
        [SerializeField] private GameObject offlineIndicatorRoot;

        /// <summary>
        /// Whether the rival in this row is around right now - online or actively playing.
        ///
        /// Read from the bound entry's status rather than from its label text, so it keeps working
        /// when that wording is changed or localised.
        /// </summary>
        public bool IsOnline => Status != Shared.Leaderboard.Bots.BotActivityStatus.Offline;

        /// <summary>True only while they are actually mid-level, which IsOnline also covers.</summary>
        public bool IsPlaying => Status == Shared.Leaderboard.Bots.BotActivityStatus.Playing;

        /// <summary>The bound entry's raw status, for anything that needs all three states.</summary>
        public Shared.Leaderboard.Bots.BotActivityStatus Status { get; private set; }

        /// <summary>
        /// Draws the online state. Rows are pooled, so every indicator is set explicitly on every
        /// bind - leaving one alone would inherit whoever last occupied this row.
        /// </summary>
        private void ApplyStatus(LeaderboardEntry entry)
        {
            Status = entry.Status;

            if (statusLabel != null)
            {
                statusLabel.text = entry.StatusLabel ?? string.Empty;
            }

            SetActive(onlineIndicatorRoot, IsOnline);
            SetActive(playingIndicatorRoot, IsPlaying);
            SetActive(offlineIndicatorRoot, !IsOnline);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        public void Bind(LeaderboardEntry entry)
        {
            Bind(entry, null);
        }

        /// <summary>
        /// giftSprite null hides the gift slot entirely - most rows win nothing, so that is the
        /// common case rather than an error.
        /// </summary>
        public void Bind(LeaderboardEntry entry, Sprite giftSprite)
        {
            Rank = entry.Rank;
            DisplayedName = entry.Name;
            DisplayedScore = entry.Score;

            if (rankLabel != null)
            {
                rankLabel.text = entry.Rank.ToString();
            }

            if (nameLabel != null)
            {
                nameLabel.text = entry.Name;
            }

            if (scoreLabel != null)
            {
                scoreLabel.text = entry.Score.ToString();
            }

            // Hidden rather than left on an old sprite: rows are pooled and reused, so an entry
            // with no art would otherwise inherit whoever last occupied this row.
            ApplyArt(avatarImage, entry.Avatar);
            ApplyArt(frameImage, entry.Frame);
            ApplyRankBadge(entry.Rank);
            ApplyStatus(entry);

            ApplyArt(giftImage, giftSprite);
            if (giftRoot != null)
            {
                giftRoot.SetActive(giftSprite != null);
            }

            if (playerHighlightRoot != null)
            {
                playerHighlightRoot.SetActive(entry.IsPlayer);
            }
        }

        /// <summary>
        /// Reparents without inheriting the old parent's world scale/position - worldPositionStays
        /// false, since every position here is expressed in the NEW parent's own anchored space
        /// (a podium slot and the scroll content have completely unrelated coordinate spaces).
        /// </summary>
        public void SetParent(RectTransform parent)
        {
            if (parent == null || Rect.parent == parent)
            {
                return;
            }

            Rect.SetParent(parent, worldPositionStays: false);
        }

        /// <summary>
        /// Podium layout, "the slot decides the size": fill the authored slot exactly, so both
        /// where that place sits and how big it reads come from the slot. Nothing here assumes the
        /// three slots match - rank 1 is usually larger and higher than 2 and 3.
        /// </summary>
        public void ApplyFillParentLayout()
        {
            Rect.anchorMin = Vector2.zero;
            Rect.anchorMax = Vector2.one;
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.offsetMin = Vector2.zero;
            Rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Podium layout, "the prefab decides the size": centre on the slot and keep the podium
        /// prefab's own authored width/height. Use this when all three places share one size and
        /// the slots exist purely to mark positions.
        /// </summary>
        public void ApplyCenteredInParentLayout()
        {
            Vector2 size = Rect.sizeDelta;
            Rect.anchorMin = new Vector2(0.5f, 0.5f);
            Rect.anchorMax = new Vector2(0.5f, 0.5f);
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.sizeDelta = size;
        }

        /// <summary>
        /// Scroll-list layout: full width of the content, fixed height, anchored to the content's
        /// TOP edge - so anchoredPosition.y is simply "how far down the list this row sits",
        /// negative and growing downward. Deliberately not a VerticalLayoutGroup: a layout group
        /// rewrites every child's position on the next layout pass, which would fight (and win
        /// against) the move animation below on every single frame.
        /// </summary>
        public void ApplyListLayout(float rowHeight)
        {
            Rect.anchorMin = new Vector2(0f, 1f);
            Rect.anchorMax = new Vector2(1f, 1f);
            Rect.pivot = new Vector2(0.5f, 1f);
            // Width stretches with the content (sizeDelta.x 0 = "parent width exactly"); height is
            // the fixed row height the controller lays out against.
            Rect.sizeDelta = new Vector2(0f, Mathf.Max(1f, rowHeight));
        }

        /// <summary>
        /// Sets the row's place in the sibling order and remembers it, so a climb can raise the row
        /// to the front for the journey and drop it back afterwards.
        ///
        /// Sibling order is draw order in Unity UI, and rows overlap while one is travelling past
        /// the others - a row moving up is moving to a LOWER index, so without the raise it slides
        /// underneath every row it passes.
        /// </summary>
        public void SetListSiblingIndex(int index)
        {
            restingSiblingIndex = index;
            Rect.SetSiblingIndex(index);
        }

        public void MoveInstant(Vector2 anchoredPosition)
        {
            StopMove();
            Rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>
        /// Slides to a new slot. delaySeconds staggers a whole board's worth of moves so rows
        /// travel as a wave rather than every row starting in the same frame.
        /// </summary>
        /// <summary>
        /// Slides to a new slot. delaySeconds staggers a whole board's worth of moves so rows
        /// travel as a wave rather than every row starting in the same frame.
        ///
        /// lift turns the plain slide into the climb sequence: swell to climbScale, travel at that
        /// size in front of everything else, then settle back down.
        /// </summary>
        public void MoveTo(Vector2 anchoredPosition, float durationSeconds, float delaySeconds = 0f, bool lift = false)
        {
            StopMove();
            if (!isActiveAndEnabled)
            {
                // Can't run a coroutine on an inactive object - land on the target rather than
                // silently staying wherever the row happened to be.
                Rect.anchoredPosition = anchoredPosition;
                return;
            }

            moveRoutine = StartCoroutine(MoveRoutine(anchoredPosition, durationSeconds, delaySeconds, lift));
        }

        /// <summary>
        /// How long MoveTo will take end to end, delay and climb included. The controller needs this
        /// to know how long to hold the scroll, and reading it from here means the two can never
        /// disagree about when the row has finished.
        /// </summary>
        public float GetMoveTotalSeconds(float durationSeconds, float delaySeconds, bool lift)
        {
            float total = Mathf.Max(0f, delaySeconds) + Mathf.Max(0.01f, durationSeconds);
            if (lift)
            {
                total += Mathf.Max(0f, climbScaleUpSeconds) + Mathf.Max(0f, climbSettleSeconds);
            }

            return total;
        }

        private IEnumerator MoveRoutine(Vector2 target, float durationSeconds, float delaySeconds, bool lift)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            float peak = Mathf.Max(1f, climbScale);

            if (lift)
            {
                // To the front for the whole journey, so the row passes OVER the rows it overtakes
                // instead of under them.
                Rect.SetAsLastSibling();
                yield return ScaleTo(1f, peak, climbScaleUpSeconds);
            }

            Vector2 start = Rect.anchoredPosition;
            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // Unscaled: the board is a popup and can be open while gameplay is paused.
                elapsed += Time.unscaledDeltaTime;
                // LerpUnclamped, so a curve that overshoots past 1 actually overshoots the slot
                // instead of being flattened against it.
                Rect.anchoredPosition = Vector2.LerpUnclamped(start, target, Evaluate(rowMoveCurve, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            Rect.anchoredPosition = target;

            if (lift)
            {
                yield return ScaleTo(peak, 1f, climbSettleSeconds);
                Rect.localScale = Vector3.one;
                // Back into the running order now that it has arrived and nothing is overlapping.
                if (restingSiblingIndex >= 0)
                {
                    Rect.SetSiblingIndex(restingSiblingIndex);
                }
            }

            moveRoutine = null;
        }

        /// <summary>
        /// Drops the row out of view, applies the new data down there, and brings it back up
        /// carrying it.
        ///
        /// applyData is invoked at the BOTTOM of the dip, never before it - that is the entire
        /// point. Called at the top, the name and score would visibly change on a card in plain
        /// sight and the whole move would read as a glitch instead of a hand-off.
        ///
        /// Shares the move coroutine slot, so anything that cancels a move cancels this too and the
        /// row cannot be left parked below its resting place.
        /// </summary>
        public void PlayPodiumDataSwap(System.Action applyData)
        {
            if (!isActiveAndEnabled)
            {
                // No coroutines on an inactive object - take the data immediately rather than
                // silently keeping the old occupant on screen.
                applyData?.Invoke();
                return;
            }

            StopMove();
            moveRoutine = StartCoroutine(PodiumDataSwapRoutine(applyData));
        }

        private IEnumerator PodiumDataSwapRoutine(System.Action applyData)
        {
            Vector2 rest = Rect.anchoredPosition;
            Vector2 bottom = rest + new Vector2(0f, -Mathf.Abs(podiumSwapDipDistance));

            yield return MoveBetween(rest, bottom, podiumSwapDownSeconds);

            applyData?.Invoke();

            if (podiumSwapHoldSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(podiumSwapHoldSeconds);
            }

            yield return MoveBetween(bottom, rest, podiumSwapUpSeconds);

            // Snapped back exactly, so repeated swaps cannot accumulate a drift away from the
            // authored position.
            Rect.anchoredPosition = rest;
            moveRoutine = null;
        }

        private IEnumerator MoveBetween(Vector2 from, Vector2 to, float durationSeconds)
        {
            float duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                Rect.anchoredPosition = to;
                yield break;
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                Rect.anchoredPosition = Vector2.LerpUnclamped(from, to, Evaluate(podiumSwapCurve, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            Rect.anchoredPosition = to;
        }

        private IEnumerator ScaleTo(float from, float to, float durationSeconds)
        {
            float duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                Rect.localScale = Vector3.one * to;
                yield break;
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float amount = Evaluate(climbScaleCurve, Mathf.Clamp01(elapsed / duration));
                Rect.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, amount);
                yield return null;
            }

            Rect.localScale = Vector3.one * to;
        }

        /// <summary>One-shot scale punch marking "this row's rank just changed", on top of whatever move is already running.</summary>
        public void PlayRankChangePunch(float delaySeconds = 0f)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
            }

            punchRoutine = StartCoroutine(PunchRoutine(delaySeconds));
        }

        private IEnumerator PunchRoutine(float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            float duration = Mathf.Max(0.01f, rankChangePunchDurationSeconds);
            Vector3 peak = Vector3.one * Mathf.Max(1f, rankChangePunchScale);

            // One pass driven by the curve rather than two hard-coded halves - that is what makes
            // the shape tunable, including an asymmetric snap-out-ease-back.
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float amount = Evaluate(rankChangePunchCurve, Mathf.Clamp01(elapsed / duration));
                Rect.localScale = Vector3.LerpUnclamped(Vector3.one, peak, amount);
                yield return null;
            }

            Rect.localScale = Vector3.one;
            punchRoutine = null;
        }

        public void PlayAppear()
        {
            if (canvasGroup == null)
            {
                return;
            }

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            if (!isActiveAndEnabled)
            {
                canvasGroup.alpha = 1f;
                return;
            }

            fadeRoutine = StartCoroutine(AppearRoutine());
        }

        private IEnumerator AppearRoutine()
        {
            float duration = Mathf.Max(0.01f, appearFadeDurationSeconds);
            float from = Mathf.Max(0.01f, appearScaleFrom);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                canvasGroup.alpha = progress;
                // Skipped entirely at 1, so a design that wants a pure fade pays nothing for it and
                // cannot have its scale stamped on by this.
                if (!Mathf.Approximately(from, 1f))
                {
                    Rect.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, progress);
                }

                yield return null;
            }

            canvasGroup.alpha = 1f;
            Rect.localScale = Vector3.one;
            fadeRoutine = null;
        }

        /// <summary>
        /// Full reset before this row goes back to the pool - every running animation stopped and
        /// every transform/alpha change undone, so the next entry it renders starts clean rather
        /// than inheriting a half-finished punch or fade from whoever used it last.
        /// </summary>
        public void ResetForPool()
        {
            StopMove();

            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
                punchRoutine = null;
            }

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            Rect.localScale = Vector3.one;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }

            // Forgotten along with everything else: the next entry to use this row gets its own
            // place in the order, and a stale index would drop it somewhere arbitrary.
            restingSiblingIndex = -1;
            Rank = 0;
            DisplayedName = null;
            DisplayedScore = 0;
        }

        private void StopMove()
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
        }

        /// <summary>
        /// Medal for a place the badge array covers, plain number for everything else. Driven off
        /// the rank NUMBER rather than a flag, so a row that slides from 4th to 3rd picks up its
        /// medal on the same rebind that moves it.
        /// </summary>
        private void ApplyRankBadge(int rank)
        {
            int badgeIndex = rank - 1;
            Sprite badge = topRankBadges != null && badgeIndex >= 0 && badgeIndex < topRankBadges.Length
                ? topRankBadges[badgeIndex]
                : null;

            ApplyArt(rankBadgeImage, badge);

            if (rankBadgeRoot != null)
            {
                rankBadgeRoot.SetActive(badge != null);
            }

            if (plainRankRoot != null)
            {
                plainRankRoot.SetActive(badge == null);
            }
        }

        private static void ApplyArt(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            // Disabled, not transparent: a null sprite on an enabled Image draws a white box the
            // size of the rect.
            image.enabled = sprite != null;
        }

        /// <summary>
        /// Samples a tuning curve, falling back to the old hard-coded ease-out when the curve has
        /// been cleared in the Inspector - an empty AnimationCurve evaluates to 0 at every time,
        /// which would silently freeze every row at its starting position.
        /// </summary>
        private static float Evaluate(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0)
            {
                return EaseOutCubic(t);
            }

            return curve.Evaluate(t);
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - inverse * inverse * inverse;
        }
    }
}
