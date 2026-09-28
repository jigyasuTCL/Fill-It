using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Ftue
{
    // Dims the whole screen except a window over a given area, using four opaque strips sized
    // from that area's on-screen rect - same technique as Cat Flap's own
    // CatFlapHintSpotlightController, but generic: no knowledge of boards, cells, levels, or UI
    // hierarchies beyond "here is a screen-pixel rect to leave uncovered". Optionally centers a
    // hand-pointer image in the highlighted window while shown.
    public sealed class FtueSpotlightController : MonoBehaviour
    {
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private RectTransform topStrip;
        [SerializeField] private RectTransform bottomStrip;
        [SerializeField] private RectTransform leftStrip;
        [SerializeField] private RectTransform rightStrip;
        [SerializeField] private RectTransform handPointer;
        // Drives the hand pointer's Animator between its "SingleTap"/"DoubleTap" clips via the
        // IsDoubleTap bool parameter - optional, left unassigned if the hand pointer has no such
        // Animator (e.g. before the art/controller existed).
        [SerializeField] private Animator handPointerAnimator;
        private static readonly int IsDoubleTapHash = Animator.StringToHash("IsDoubleTap");
        // Must match the AnimatorState names in FtueHand.controller's Base Layer exactly.
        private const string SingleTapStateName = "SingleTap";
        private const string DoubleTapStateName = "DoubleTap";
        // The double-tap clip is only forced once the hand pointer's own GameObject/Animator is
        // actually active in the hierarchy, rather than in the same synchronous call that
        // activates it - see TriggerDoubleTapWhenActive.
        private Coroutine doubleTapTriggerRoutine;
        private bool doubleTapPending;
        [SerializeField] private float screenPaddingPixels = 14f;

        // Extra margin (screen pixels) added around the board's own tight bounds when computing
        // boardClipScreenRect below - a clip region sized EXACTLY to the board looked visibly too
        // tight (dimming crept right up against the board's edge/base art). Purely cosmetic, has
        // no bearing on the per-cell hole's own screenPaddingPixels above.
        [SerializeField] private float boardClipMarginPixels = 40f;

        // Normalized (0-1 of Screen.width/height) region the dim overlay is confined to for the
        // board-highlight case (Show(Bounds, ...)) - defaults to the whole screen until
        // SetBoardClipBounds narrows it to the board's own on-screen rect, so anything outside
        // the board (HUD, rules panel, booster panel) never gets dimmed. ShowUI's full-screen
        // dismiss-anywhere case (e.g. the "Beware!" hearts tip) always ignores this and covers the
        // whole screen regardless, since it isn't pointing at the board at all.
        private Rect boardClipScreenRect = new Rect(0f, 0f, 1f, 1f);

        // Called once per FTUE-active level (see CatFlapFtueDriver.Configure) with the board's
        // own overall world bounds - narrows every subsequent Show(Bounds, ...) call's dim
        // overlay to just that on-screen area instead of the whole screen. Pass a null camera (or
        // call ClearBoardClipBounds) to fall back to full-screen dimming again.
        public void SetBoardClipBounds(Bounds worldBounds, Camera camera)
        {
            if (camera == null)
            {
                ClearBoardClipBounds();
                return;
            }

            Vector3 screenMin = camera.WorldToScreenPoint(worldBounds.min);
            Vector3 screenMax = camera.WorldToScreenPoint(worldBounds.max);
            float minXPixels = Mathf.Min(screenMin.x, screenMax.x) - boardClipMarginPixels;
            float maxXPixels = Mathf.Max(screenMin.x, screenMax.x) + boardClipMarginPixels;
            float minYPixels = Mathf.Min(screenMin.y, screenMax.y) - boardClipMarginPixels;
            float maxYPixels = Mathf.Max(screenMin.y, screenMax.y) + boardClipMarginPixels;

            float left = Mathf.Clamp01(minXPixels / Mathf.Max(1f, Screen.width));
            float right = Mathf.Clamp01(maxXPixels / Mathf.Max(1f, Screen.width));
            float bottom = Mathf.Clamp01(minYPixels / Mathf.Max(1f, Screen.height));
            float top = Mathf.Clamp01(maxYPixels / Mathf.Max(1f, Screen.height));
            boardClipScreenRect = Rect.MinMaxRect(left, bottom, right, top);
        }

        public void ClearBoardClipBounds()
        {
            boardClipScreenRect = new Rect(0f, 0f, 1f, 1f);
        }

        // Scale-up-from-zero pop whenever the hand pointer (re)appears at a NEW target position -
        // not replayed on every Refresh() call that leaves it pointing at the same spot (e.g. a
        // still-waiting re-validation tick), only when it was hidden and reappears, or moves from
        // one target to another. See ShowScreenRect/PlayHandAppearAnimation.
        [SerializeField] private float handAppearDurationSeconds = 0.75f;
        // Waited out BEFORE the pop starts (hand stays at zero scale/invisible this whole time) -
        // without this, the pop starts in the same instant as the spotlight window and FTUE banner
        // both animating in, and gets visually lost among them. Delaying it a beat after those
        // land makes it read as its own distinct, noticeable moment instead.
        [SerializeField] private float handAppearDelaySeconds = 0.3f;
        private Vector3? handPointerOriginalScale;
        private Vector2? lastHandAnchor;
        private Coroutine handAppearRoutine;

        // Rounds the cutout window's 4 corners: a circle sprite centered exactly on each corner
        // point, same dim color as the strips. The quadrant of each circle pointing INTO the
        // window correctly bites a rounded notch into what would otherwise be a sharp corner; the
        // other 3 quadrants just overlap the already-dim strips (harmless). All 4 are optional -
        // leave them unassigned to fall back to the old sharp-corner look.
        [SerializeField] private RectTransform topLeftCorner;
        [SerializeField] private RectTransform topRightCorner;
        [SerializeField] private RectTransform bottomLeftCorner;
        [SerializeField] private RectTransform bottomRightCorner;
        [SerializeField] private float cornerRadiusPixels = 24f;

        // Boundary accents for ShowCross's "+" window only: thin bars marking where the
        // highlighted row/column strip actually TERMINATES (the row's left/right ends, the
        // column's top/bottom ends), so the cross reads as a bounded highlight instead of just
        // fading into the board clip edge. All 4 are optional, same as the corner accents above -
        // leave unassigned to fall back to the old look (no end markers). Unused by the
        // single-hole Show(Bounds, ...)/ShowUI cases, which explicitly hide them.
        [SerializeField] private RectTransform rowLeftEndCap;
        [SerializeField] private RectTransform rowRightEndCap;
        [SerializeField] private RectTransform columnTopEndCap;
        [SerializeField] private RectTransform columnBottomEndCap;
        // How far past the clip's own outer edge each cap bleeds - the clip edge already lines up
        // with the board's outer frame art (see boardClipMarginPixels), so this only needs to be
        // a couple of pixels (covers antialiasing/rounding at the seam) - a large value visibly
        // pokes the cap out past the frame into whatever UI sits above/around the board. See the
        // big comment in ShowCrossScreenRect for why this is a bleed past the edge rather than a
        // fixed size centered on it.
        [SerializeField] private float endCapOuterBleedPixels = 2f;
        // Scales the bottom cap's own thickness relative to the other 3 (1 = same span as top/
        // left/right, 0.5 = half as thick) - the bottom cap otherwise reads visually heavier than
        // the rest once everything else lines up correctly.
        [SerializeField] private float bottomEndCapThicknessScale = 0.6f;
        // Extra downward shift (screen pixels) applied to the bottom cap's own anchor range - both
        // its edges move down together by this amount, so its span/thickness stays the same, it's
        // just relocated. A manual visual offset for whatever residual misalignment the anchoring
        // math above doesn't otherwise account for.
        [SerializeField] private float bottomEndCapOffsetPixels = 15f;

        [SerializeField] private float overlayFadeDurationSeconds = 0.2f;
        // Auto-resolved (and added if missing) from overlayRoot in Awake - no prefab wiring
        // needed for the fade to work.
        private CanvasGroup overlayCanvasGroup;
        private Coroutine overlayFadeRoutine;

        // The dim strips'/corners' own authored alpha (e.g. black @ 0.72) - captured once, on
        // first use, so SetOverlayDimAlpha/ResetOverlayDimAlpha always have the real original to
        // restore, regardless of how many times either is called. Left null until then.
        private float? defaultOverlayDimAlpha;
        private List<Image> dimImages;

        // Dims the screen OUTSIDE the board clip rect (see boardClipScreenRect) - the strips
        // above only ever cover the confined clip area itself (e.g. just the board, or a target
        // hole within it), so before this existed, anything beyond that clip (HUD, rules panel,
        // screen edges) was left completely undimmed. Built at runtime (not prefab-authored) as
        // children of overlayRoot, cloned from the same 4 strip Images so they match visually and
        // automatically share overlayRoot's own fade/show/hide lifecycle. Their coverage area
        // (full screen minus the clip rect) never overlaps the clip-confined strips' own coverage
        // area (full clip rect minus whatever hole is currently punched into it), so which set
        // renders "on top" never matters - see PositionOuterStrips.
        private RectTransform outerTopStrip;
        private RectTransform outerBottomStrip;
        private RectTransform outerLeftStrip;
        private RectTransform outerRightStrip;
        // Round the OUTER hole's own 4 corners (where the board's clip rect meets the outer dim) -
        // a genuine rectangular hole, unlike the inner cross case's T-junctions, so corner
        // accents apply here even while the inner corners are hidden (showSpotlight: false).
        private RectTransform outerTopLeftCorner;
        private RectTransform outerTopRightCorner;
        private RectTransform outerBottomLeftCorner;
        private RectTransform outerBottomRightCorner;
        private bool outerStripsBuilt;

        private void Awake()
        {
            if (overlayRoot == null)
            {
                return;
            }

            overlayCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();
            if (overlayCanvasGroup == null)
            {
                overlayCanvasGroup = overlayRoot.AddComponent<CanvasGroup>();
            }
        }

        private void EnsureOuterStripsBuilt()
        {
            if (outerStripsBuilt || overlayRoot == null)
            {
                return;
            }

            outerStripsBuilt = true;
            outerTopStrip = CloneStrip(topStrip, "FtueOuterDimTop");
            outerBottomStrip = CloneStrip(bottomStrip, "FtueOuterDimBottom");
            outerLeftStrip = CloneStrip(leftStrip, "FtueOuterDimLeft");
            outerRightStrip = CloneStrip(rightStrip, "FtueOuterDimRight");
            outerTopLeftCorner = CloneStrip(topLeftCorner, "FtueOuterDimTopLeftCorner");
            outerTopRightCorner = CloneStrip(topRightCorner, "FtueOuterDimTopRightCorner");
            outerBottomLeftCorner = CloneStrip(bottomLeftCorner, "FtueOuterDimBottomLeftCorner");
            outerBottomRightCorner = CloneStrip(bottomRightCorner, "FtueOuterDimBottomRightCorner");
        }

        private RectTransform CloneStrip(RectTransform source, string cloneName)
        {
            if (source == null)
            {
                return null;
            }

            GameObject clone = Instantiate(source.gameObject, overlayRoot.transform, worldPositionStays: false);
            clone.name = cloneName;
            return clone.transform as RectTransform;
        }

        // Positions the 4 outer strips to cover the full screen minus clipRect, expressed as
        // anchors LOCAL to overlayRoot (whose own rect spans exactly clipRect on screen) rather
        // than the normal 0-1 range - a screen-fraction outside clipRect maps to a local anchor
        // fraction outside 0-1, which Unity's RectTransform anchoring supports natively
        // (extrapolated from the parent rect's own corners), so these strips can visually extend
        // beyond overlayRoot's own rect without needing a separate full-screen parent/CanvasGroup.
        // When clipRect already covers the whole screen (no active board clip - e.g. ShowUI's
        // dismiss-anywhere case), every strip collapses to zero size - harmless, and needs no
        // separate visibility toggle. The 4 corner accents DO need an explicit toggle instead
        // (see hasClip below): unlike a strip, a corner is a fixed-radius circle that doesn't
        // shrink away to nothing on its own, so left enabled with no real clip it would show as a
        // small stray circle sitting in the screen's own corner.
        private void PositionOuterStrips(Rect clipRect, bool hasClip)
        {
            EnsureOuterStripsBuilt();

            float ToLocal(float screenFraction, float clipMin, float clipSize)
            {
                return (screenFraction - clipMin) / Mathf.Max(0.0001f, clipSize);
            }

            float localZero = ToLocal(0f, clipRect.xMin, clipRect.width);
            float localOne = ToLocal(1f, clipRect.xMin, clipRect.width);
            float localXMin = ToLocal(clipRect.xMin, clipRect.xMin, clipRect.width);
            float localXMax = ToLocal(clipRect.xMax, clipRect.xMin, clipRect.width);
            float localBottom = ToLocal(0f, clipRect.yMin, clipRect.height);
            float localTop = ToLocal(1f, clipRect.yMin, clipRect.height);
            float localYMin = ToLocal(clipRect.yMin, clipRect.yMin, clipRect.height);
            float localYMax = ToLocal(clipRect.yMax, clipRect.yMin, clipRect.height);

            SetStripAnchors(outerLeftStrip, new Vector2(localZero, localBottom), new Vector2(localXMin, localTop));
            SetStripAnchors(outerRightStrip, new Vector2(localXMax, localBottom), new Vector2(localOne, localTop));
            SetStripAnchors(outerBottomStrip, new Vector2(localXMin, localBottom), new Vector2(localXMax, localYMin));
            SetStripAnchors(outerTopStrip, new Vector2(localXMin, localYMax), new Vector2(localXMax, localTop));

            if (hasClip)
            {
                SetCorner(outerTopLeftCorner, localXMin, localYMax);
                SetCorner(outerTopRightCorner, localXMax, localYMax);
                SetCorner(outerBottomLeftCorner, localXMin, localYMin);
                SetCorner(outerBottomRightCorner, localXMax, localYMin);
            }
            else
            {
                if (outerTopLeftCorner != null) outerTopLeftCorner.gameObject.SetActive(false);
                if (outerTopRightCorner != null) outerTopRightCorner.gameObject.SetActive(false);
                if (outerBottomLeftCorner != null) outerBottomLeftCorner.gameObject.SetActive(false);
                if (outerBottomRightCorner != null) outerBottomRightCorner.gameObject.SetActive(false);
            }
        }

        // Overrides how dark the dim overlay (every strip + rounded corner) reads, on top of
        // whatever's authored in the prefab - e.g. Cat Flap's Level 4 rules walkthrough wants a
        // noticeably darker overlay than every other FTUE level. Only the alpha channel changes;
        // RGB (the dim color itself) is left exactly as authored. Persists across Show/ShowCross/
        // Hide calls until ResetOverlayDimAlpha() is called - callers must reset it themselves
        // once whatever needed the darker look ends, or every later FTUE level would inherit it.
        public void SetOverlayDimAlpha(float alpha)
        {
            EnsureDimImagesResolved();
            foreach (Image image in dimImages)
            {
                if (image == null)
                {
                    continue;
                }

                Color color = image.color;
                color.a = alpha;
                image.color = color;
            }
        }

        // Restores every dim strip/corner to its own originally-authored alpha - see
        // SetOverlayDimAlpha.
        public void ResetOverlayDimAlpha()
        {
            EnsureDimImagesResolved();
            if (defaultOverlayDimAlpha.HasValue)
            {
                SetOverlayDimAlpha(defaultOverlayDimAlpha.Value);
            }
        }

        private void EnsureDimImagesResolved()
        {
            if (dimImages != null)
            {
                return;
            }

            // Built before caching (not after) so the outer strips - clones of topStrip/etc,
            // matching their alpha too - are already there to be picked up below, rather than
            // permanently missed because they didn't exist yet on this first resolve.
            EnsureOuterStripsBuilt();

            dimImages = new List<Image>();
            foreach (RectTransform strip in new[]
                     {
                         topStrip, bottomStrip, leftStrip, rightStrip,
                         outerTopStrip, outerBottomStrip, outerLeftStrip, outerRightStrip,
                         topLeftCorner, topRightCorner, bottomLeftCorner, bottomRightCorner,
                         outerTopLeftCorner, outerTopRightCorner, outerBottomLeftCorner, outerBottomRightCorner,
                         rowLeftEndCap, rowRightEndCap, columnTopEndCap, columnBottomEndCap
                     })
            {
                if (strip == null)
                {
                    continue;
                }

                // GetComponentsInChildren (not just GetComponent on the RectTransform itself) -
                // a rounded/"curvy" accent piece (e.g. the corner accents) is often authored as a
                // parent object with the actual Image(s) one level down, so a same-object-only
                // lookup silently missed them: SetOverlayDimAlpha/ResetOverlayDimAlpha only ever
                // touched the flat strip fills, never those nested curvy overlay images.
                foreach (Image image in strip.GetComponentsInChildren<Image>(true))
                {
                    if (!dimImages.Contains(image))
                    {
                        dimImages.Add(image);
                    }
                }
            }

            if (!defaultOverlayDimAlpha.HasValue && dimImages.Count > 0)
            {
                defaultOverlayDimAlpha = dimImages[0].color.a;
            }
        }

        // Highlights a world-space area (e.g. a board cell), converted to screen space via the
        // given camera. isDoubleTap picks which hand-pointer animation clip plays (ignored if no
        // handPointerAnimator is assigned). showHandPointer defaults to true (the normal
        // "waiting for a tap here" case) but is set to false while a target's correct value has
        // already been tapped in and is only waiting to lock/turn green - the spotlight/banner
        // stay up, but there's nothing left to point the hand at.
        // handWorldBounds (optional): points the hand pointer at a DIFFERENT area than the one
        // being dimmed/highlighted - e.g. Cat Flap's Level 4 rules walkthrough highlights a whole
        // row but wants the hand pointing at just one cell within it. Defaults to null, which
        // keeps the hand centered on worldBounds exactly as before.
        public void Show(Bounds worldBounds, Camera camera, bool isDoubleTap = false, bool showHandPointer = true, Bounds? handWorldBounds = null)
        {
            if (overlayRoot == null || camera == null)
            {
                Hide();
                return;
            }

            Vector3 screenMin = camera.WorldToScreenPoint(worldBounds.min);
            Vector3 screenMax = camera.WorldToScreenPoint(worldBounds.max);

            Vector2? handScreenCenter = null;
            if (handWorldBounds.HasValue)
            {
                Vector3 handScreenMin = camera.WorldToScreenPoint(handWorldBounds.Value.min);
                Vector3 handScreenMax = camera.WorldToScreenPoint(handWorldBounds.Value.max);
                handScreenCenter = new Vector2((handScreenMin.x + handScreenMax.x) * 0.5f, (handScreenMin.y + handScreenMax.y) * 0.5f);
            }

            ShowScreenRect(
                Mathf.Min(screenMin.x, screenMax.x), Mathf.Max(screenMin.x, screenMax.x),
                Mathf.Min(screenMin.y, screenMax.y), Mathf.Max(screenMin.y, screenMax.y),
                showHandPointer, isDoubleTap, useBoardClip: true, handScreenCenterOverride: handScreenCenter);
        }

        // Highlights TWO areas at once - a full row and a full column, forming a "+" (cross)
        // window - by dimming exactly the 4 corner quadrants OUTSIDE their union, rather than one
        // rectangular hole. A single hole can't do this: a full row's bounds already touch the
        // left/right screen edges and a full column's bounds already touch top/bottom, so
        // encapsulating both into ONE bounds (as Show(Bounds, ...) does) degenerates to the whole
        // clip area - see CatFlapFtueDriver's own note on why this exists. Reuses the same 4
        // opaque rect objects as Show(Bounds, ...) (repurposed here as quadrant fills instead of
        // edge strips - SetStripAnchors just stretches a rect to match whatever anchor box it's
        // given, it has no inherent "edge" shape - so the next normal Show() call resets them back
        // to normal automatically) and hides the 4 rounded-corner accent pieces, since a cross's
        // T-junctions have no single rectangular corner for those to round off.
        // showSpotlight = false skips the dim strips/corners/end-caps entirely (left fully
        // inactive) while still activating the overlay canvas and positioning the hand pointer as
        // normal - e.g. Cat Flap's Level 4 last step, which shows its own dedicated overlay sprite
        // (see CatFlapSpriteBoardViewAdapter) in place of this dim/cross-highlight visual, but
        // still wants the hand pointer (and, via FtueController.ShowCross, the banner) exactly as
        // before.
        public void ShowCross(Bounds rowWorldBounds, Bounds columnWorldBounds, Camera camera, bool isDoubleTap = false, bool showHandPointer = true, Bounds? handWorldBounds = null, bool showSpotlight = true)
        {
            if (overlayRoot == null || camera == null)
            {
                Hide();
                return;
            }

            // Only the row's Y-extent (its own thin thickness) and the column's X-extent (its own
            // thin thickness) are actually needed below - a full row's X-extent and a full
            // column's Y-extent are always the WHOLE clip area by definition, not measured.
            Vector3 rowScreenMin = camera.WorldToScreenPoint(rowWorldBounds.min);
            Vector3 rowScreenMax = camera.WorldToScreenPoint(rowWorldBounds.max);
            Vector3 columnScreenMin = camera.WorldToScreenPoint(columnWorldBounds.min);
            Vector3 columnScreenMax = camera.WorldToScreenPoint(columnWorldBounds.max);

            Vector2? handScreenCenter = null;
            if (handWorldBounds.HasValue)
            {
                Vector3 handScreenMin = camera.WorldToScreenPoint(handWorldBounds.Value.min);
                Vector3 handScreenMax = camera.WorldToScreenPoint(handWorldBounds.Value.max);
                handScreenCenter = new Vector2((handScreenMin.x + handScreenMax.x) * 0.5f, (handScreenMin.y + handScreenMax.y) * 0.5f);
            }

            ShowCrossScreenRect(
                Mathf.Min(rowScreenMin.y, rowScreenMax.y), Mathf.Max(rowScreenMin.y, rowScreenMax.y),
                Mathf.Min(columnScreenMin.x, columnScreenMax.x), Mathf.Max(columnScreenMin.x, columnScreenMax.x),
                showHandPointer, isDoubleTap, handScreenCenter, showSpotlight);
        }

        // Highlights a UI element directly. Its RectTransform's world corners are only already
        // screen-pixel coordinates when the owning Canvas is Screen Space - Overlay; in Screen
        // Space - Camera (or World Space) they're real world-space positions that still need
        // projecting through that canvas's own camera - RectTransformUtility.WorldToScreenPoint
        // handles all three render modes correctly (a null camera means "already screen space",
        // exactly matching Overlay).
        // showHandPointer defaults to false here - the UI-target case (e.g. the "Beware!" hearts
        // tip) reads as pointing at a whole panel, not a single tappable spot, so the pointer
        // icon doesn't apply the way it does for a specific board cell.
        public void ShowUI(RectTransform targetRect, bool showHandPointer = false)
        {
            if (overlayRoot == null || targetRect == null)
            {
                Hide();
                return;
            }

            Canvas canvas = targetRect.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            var corners = new Vector3[4];
            targetRect.GetWorldCorners(corners);
            Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
            Vector2 screenMax = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
            ShowScreenRect(
                Mathf.Min(screenMin.x, screenMax.x), Mathf.Max(screenMin.x, screenMax.x),
                Mathf.Min(screenMin.y, screenMax.y), Mathf.Max(screenMin.y, screenMax.y),
                showHandPointer, isDoubleTap: false, useBoardClip: false);
        }

        public void Hide()
        {
            if (handPointer != null)
            {
                handPointer.gameObject.SetActive(false);
            }

            if (doubleTapTriggerRoutine != null)
            {
                StopCoroutine(doubleTapTriggerRoutine);
                doubleTapTriggerRoutine = null;
            }

            if (handAppearRoutine != null)
            {
                StopCoroutine(handAppearRoutine);
                handAppearRoutine = null;
            }

            doubleTapPending = false;
            lastHandAnchor = null;

            if (overlayRoot == null || !overlayRoot.activeSelf)
            {
                return;
            }

            if (overlayCanvasGroup == null)
            {
                overlayRoot.SetActive(false);
                return;
            }

            // This component sits on overlayRoot itself, so the fade coroutine (started below)
            // must finish BEFORE the GameObject deactivates - Unity stops every coroutine on a
            // disabled GameObject, so SetActive(false) only happens as the fade's own completion
            // callback, once the alpha has actually reached 0.
            FadeOverlay(overlayCanvasGroup.alpha, 0f, () => overlayRoot.SetActive(false));
        }

        // Instant variant of Hide() - skips the fade-out entirely. Use only when the caller is
        // about to immediately move/hide whatever this was highlighting itself (e.g. Cat Flap's
        // Skip Tutorial button, which starts sliding the board away right after this call) - the
        // normal animated Hide() takes overlayFadeDurationSeconds to fully fade, and since this
        // dim overlay is a screen-anchored UI element (not a child of a world-space board), it
        // would otherwise visibly hang in its old on-screen position while the board it was
        // highlighting is already sliding out from underneath it.
        public void HideImmediate()
        {
            if (handPointer != null)
            {
                handPointer.gameObject.SetActive(false);
            }

            if (doubleTapTriggerRoutine != null)
            {
                StopCoroutine(doubleTapTriggerRoutine);
                doubleTapTriggerRoutine = null;
            }

            if (handAppearRoutine != null)
            {
                StopCoroutine(handAppearRoutine);
                handAppearRoutine = null;
            }

            doubleTapPending = false;
            lastHandAnchor = null;

            if (overlayFadeRoutine != null)
            {
                StopCoroutine(overlayFadeRoutine);
                overlayFadeRoutine = null;
            }

            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }
        }

        private void FadeOverlay(float from, float to, System.Action onComplete)
        {
            if (overlayCanvasGroup == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (overlayFadeRoutine != null)
            {
                StopCoroutine(overlayFadeRoutine);
            }

            overlayFadeRoutine = StartCoroutine(FadeOverlayRoutine(from, to, onComplete));
        }

        private IEnumerator FadeOverlayRoutine(float from, float to, System.Action onComplete)
        {
            overlayCanvasGroup.alpha = from;
            float duration = Mathf.Max(0.01f, overlayFadeDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                overlayCanvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            overlayCanvasGroup.alpha = to;
            overlayFadeRoutine = null;
            onComplete?.Invoke();
        }

        private void ShowScreenRect(float rawMinX, float rawMaxX, float rawMinY, float rawMaxY, bool showHandPointer, bool isDoubleTap, bool useBoardClip, Vector2? handScreenCenterOverride = null)
        {
            // No outer corner accents here: the inner strips fill the ENTIRE clip except this
            // call's own target hole, so the clip/outer boundary is just more of the same flat
            // dim color on both sides - no visible seam to round off (unlike ShowCrossScreenRect's
            // showSpotlight: false case, where the board's own separate sprite overlay meets this
            // canvas dim at a real, visually distinct edge).
            Rect clip = PrepareOverlay(useBoardClip, showOuterCornerAccents: false);

            float minX = rawMinX - screenPaddingPixels;
            float maxX = rawMaxX + screenPaddingPixels;
            float minY = rawMinY - screenPaddingPixels;
            float maxY = rawMaxY + screenPaddingPixels;

            // Normalized against the ACTIVE clip region in screen pixels (not the raw full
            // screen) - since the strips anchor relative to overlayRoot, and overlayRoot itself
            // was just resized to that same clip region above, this keeps the hole lined up with
            // its real on-screen position regardless of whether overlayRoot currently spans the
            // whole screen or just the board.
            float clipMinXPixels = clip.xMin * Screen.width;
            float clipWidthPixels = Mathf.Max(1f, clip.width * Screen.width);
            float clipMinYPixels = clip.yMin * Screen.height;
            float clipHeightPixels = Mathf.Max(1f, clip.height * Screen.height);

            float left = Mathf.Clamp01((minX - clipMinXPixels) / clipWidthPixels);
            float right = Mathf.Clamp01((maxX - clipMinXPixels) / clipWidthPixels);
            float bottom = Mathf.Clamp01((minY - clipMinYPixels) / clipHeightPixels);
            float top = Mathf.Clamp01((maxY - clipMinYPixels) / clipHeightPixels);

            SetStripAnchors(leftStrip, new Vector2(0f, 0f), new Vector2(left, 1f));
            SetStripAnchors(rightStrip, new Vector2(right, 0f), new Vector2(1f, 1f));
            SetStripAnchors(bottomStrip, new Vector2(left, 0f), new Vector2(right, bottom));
            SetStripAnchors(topStrip, new Vector2(left, top), new Vector2(right, 1f));

            SetCorner(topLeftCorner, left, top);
            SetCorner(topRightCorner, right, top);
            SetCorner(bottomLeftCorner, left, bottom);
            SetCorner(bottomRightCorner, right, bottom);
            SetEndCapActive(rowLeftEndCap, false);
            SetEndCapActive(rowRightEndCap, false);
            SetEndCapActive(columnTopEndCap, false);
            SetEndCapActive(columnBottomEndCap, false);

            ApplyHandPointer(showHandPointer, isDoubleTap, new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f),
                handScreenCenterOverride, clipMinXPixels, clipWidthPixels, clipMinYPixels, clipHeightPixels);
        }

        // Same corner-quadrant-dimming shape described on ShowCross's own comment - see there for
        // why a single rectangular hole can't do this. rowMinY/rowMaxY is the row band's own
        // thickness (it always spans the whole clip width, so no X range is needed for it);
        // colMinX/colMaxX is the column band's own thickness (it always spans the whole clip
        // height, so no Y range is needed for it). Always uses the board clip (this is only ever
        // a board-highlight case), unlike ShowScreenRect's useBoardClip parameter.
        private void ShowCrossScreenRect(float rowMinY, float rowMaxY, float colMinX, float colMaxX,
            bool showHandPointer, bool isDoubleTap, Vector2? handScreenCenterOverride, bool showSpotlight = true)
        {
            // Outer corner accents only matter for the showSpotlight: false case (Cat Flap's
            // Level 4 last step): that's the only time this canvas dim's board-clip edge meets a
            // visually DISTINCT element (the board's own separate sprite overlay) instead of more
            // of this same flat dim color, so it's the only case with a real seam to round off -
            // see PositionOuterStrips.
            Rect clip = PrepareOverlay(useBoardClip: true, showOuterCornerAccents: !showSpotlight);

            float clipMinXPixels = clip.xMin * Screen.width;
            float clipWidthPixels = Mathf.Max(1f, clip.width * Screen.width);
            float clipMinYPixels = clip.yMin * Screen.height;
            float clipHeightPixels = Mathf.Max(1f, clip.height * Screen.height);

            float rowBottom = Mathf.Clamp01((rowMinY - screenPaddingPixels - clipMinYPixels) / clipHeightPixels);
            float rowTop = Mathf.Clamp01((rowMaxY + screenPaddingPixels - clipMinYPixels) / clipHeightPixels);

            float colLeft = Mathf.Clamp01((colMinX - screenPaddingPixels - clipMinXPixels) / clipWidthPixels);
            float colRight = Mathf.Clamp01((colMaxX + screenPaddingPixels - clipMinXPixels) / clipWidthPixels);

            if (!showSpotlight)
            {
                // Caller (Cat Flap's Level 4 last step) shows its own dedicated overlay sprite in
                // place of this dim/cross-highlight - leave every dim visual fully inactive rather
                // than positioning it, but still resolve the hand pointer/clip math below exactly
                // as normal so the pointer keeps working.
                HideAllDimVisuals();
            }
            else
            {
                // The 4 corner quadrants OUTSIDE the row-band/column-band union - the only regions
                // that actually need dimming for a cross-shaped window.
                SetStripAnchors(leftStrip, new Vector2(0f, rowTop), new Vector2(colLeft, 1f));
                SetStripAnchors(rightStrip, new Vector2(colRight, rowTop), new Vector2(1f, 1f));
                SetStripAnchors(topStrip, new Vector2(0f, 0f), new Vector2(colLeft, rowBottom));
                SetStripAnchors(bottomStrip, new Vector2(colRight, 0f), new Vector2(1f, rowBottom));

                if (topLeftCorner != null)
                {
                    topLeftCorner.gameObject.SetActive(false);
                }

                if (topRightCorner != null)
                {
                    topRightCorner.gameObject.SetActive(false);
                }

                if (bottomLeftCorner != null)
                {
                    bottomLeftCorner.gameObject.SetActive(false);
                }

                if (bottomRightCorner != null)
                {
                    bottomRightCorner.gameObject.SetActive(false);
                }

                // Row/column bands are measured against the board's own tight bounds, but the CLIP
                // (0/1 here) is padded out by boardClipMarginPixels beyond that, and the pre-existing
                // board frame art sits further out still, past even the clip's own edge. A cap
                // centered as a fixed-size block (the old approach) could only ever line up with ONE
                // of "flush against the highlighted cell" or "flush against the outer frame" at a
                // time - whichever margin/frame width didn't match its fixed size left a visible gap
                // on the other side. Instead, each cap now STRETCHES from the board's real tight edge
                // (flush with the cell/legend it's terminating next to) all the way out past the
                // clip's own edge by endCapOuterBleedPixels (comfortably past any outer frame border,
                // whatever its exact width), so it always bridges the whole gap on both ends
                // regardless of either margin's exact size.
                float marginFracX = Mathf.Clamp01(boardClipMarginPixels / clipWidthPixels);
                float marginFracY = Mathf.Clamp01(boardClipMarginPixels / clipHeightPixels);
                float outerBleedFracX = endCapOuterBleedPixels / clipWidthPixels;
                float outerBleedFracY = endCapOuterBleedPixels / clipHeightPixels;
                SetVerticalEndCap(rowLeftEndCap, -outerBleedFracX, marginFracX, rowBottom, rowTop);
                SetVerticalEndCap(rowRightEndCap, 1f - marginFracX, 1f + outerBleedFracX, rowBottom, rowTop);
                SetHorizontalEndCap(columnTopEndCap, 1f - marginFracY, 1f + outerBleedFracY, colLeft, colRight);
                // Scaled down from the flush edge (marginFracY), not from the far/bled edge - so the
                // bottom cap still starts exactly flush with the cell, it just doesn't reach as far
                // past it as the other 3 caps do.
                float bottomSpan = (marginFracY + outerBleedFracY) * Mathf.Clamp01(bottomEndCapThicknessScale);
                // Moved down via the anchor range itself (not a post-hoc anchoredPosition nudge) -
                // both edges shift down together by the same offset, so the cap's own span/thickness
                // is preserved, just relocated.
                float bottomOffsetFracY = bottomEndCapOffsetPixels / clipHeightPixels;
                SetHorizontalEndCap(columnBottomEndCap, marginFracY - bottomSpan - bottomOffsetFracY, marginFracY - bottomOffsetFracY, colLeft, colRight);
            }

            Vector2 defaultAnchor = new Vector2((colLeft + colRight) * 0.5f, (rowBottom + rowTop) * 0.5f);
            ApplyHandPointer(showHandPointer, isDoubleTap, defaultAnchor, handScreenCenterOverride, clipMinXPixels, clipWidthPixels, clipMinYPixels, clipHeightPixels);
        }

        // Deactivates every dim-visual element (strips, corners, end-caps) without touching
        // overlayRoot/overlayCanvasGroup - used when a caller wants the hand pointer positioned and
        // active (overlayRoot stays active for that) but none of the actual dimming/cross-highlight
        // shown, e.g. Cat Flap's Level 4 last step which shows its own overlay sprite instead.
        private void HideAllDimVisuals()
        {
            SetStripAnchors(leftStrip, Vector2.zero, Vector2.zero);
            SetStripAnchors(rightStrip, Vector2.zero, Vector2.zero);
            SetStripAnchors(topStrip, Vector2.zero, Vector2.zero);
            SetStripAnchors(bottomStrip, Vector2.zero, Vector2.zero);

            if (topLeftCorner != null) topLeftCorner.gameObject.SetActive(false);
            if (topRightCorner != null) topRightCorner.gameObject.SetActive(false);
            if (bottomLeftCorner != null) bottomLeftCorner.gameObject.SetActive(false);
            if (bottomRightCorner != null) bottomRightCorner.gameObject.SetActive(false);

            SetEndCapActive(rowLeftEndCap, false);
            SetEndCapActive(rowRightEndCap, false);
            SetEndCapActive(columnTopEndCap, false);
            SetEndCapActive(columnBottomEndCap, false);
        }

        // Activates/fades in the overlay and confines it to the board clip region (or the whole
        // screen) - shared setup between the single-hole (ShowScreenRect) and cross (
        // ShowCrossScreenRect) cases. Returns the active clip Rect so callers can normalize their
        // own screen-pixel coordinates against it. showOuterCornerAccents - see
        // PositionOuterStrips/ShowCrossScreenRect's own comment - only true for the one case with
        // a real visual seam to round off (the board-sprite-overlay last step).
        private Rect PrepareOverlay(bool useBoardClip, bool showOuterCornerAccents = false)
        {
            // Activated FIRST, not last: this component sits on overlayRoot itself, and
            // StartCoroutine (used below for the fade-in) throws if called while its own
            // GameObject is still inactive.
            bool wasHidden = overlayRoot != null && !overlayRoot.activeSelf;
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(true);
            }

            if (wasHidden)
            {
                // Fresh appearance - fade in from fully transparent.
                FadeOverlay(0f, 1f, null);
            }
            else if (overlayCanvasGroup != null)
            {
                // Already showing (e.g. Refresh() re-targeting the same/a new spot without ever
                // hiding in between) - stays fully opaque rather than replaying the fade-in.
                overlayCanvasGroup.alpha = 1f;
            }

            // The board-highlight case confines the WHOLE dim overlay (all 4 strips, which anchor
            // relative to overlayRoot itself) to the board's own on-screen rect instead of the
            // full screen - see SetBoardClipBounds. ShowUI's full-screen dismiss-anywhere case
            // always resets back to the whole screen regardless of whatever board clip is stored.
            Rect clip = useBoardClip ? boardClipScreenRect : new Rect(0f, 0f, 1f, 1f);
            if (overlayRoot != null && overlayRoot.transform is RectTransform overlayRect)
            {
                overlayRect.anchorMin = new Vector2(clip.xMin, clip.yMin);
                overlayRect.anchorMax = new Vector2(clip.xMax, clip.yMax);
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
            }

            // Dims the screen OUTSIDE clip (e.g. outside the board) - kept in sync with clip every
            // call; the 4 corner accents are gated separately by showOuterCornerAccents (see
            // PositionOuterStrips/ShowCrossScreenRect's own comment on when a real seam exists).
            PositionOuterStrips(clip, useBoardClip && showOuterCornerAccents);

            return clip;
        }

        // Positions/shows (or hides) the hand pointer and its gesture animation - shared between
        // ShowScreenRect (single hole, default anchor = its own center) and ShowCrossScreenRect
        // (cross, default anchor = the row/column intersection).
        private void ApplyHandPointer(bool showHandPointer, bool isDoubleTap, Vector2 defaultAnchor, Vector2? handScreenCenterOverride,
            float clipMinXPixels, float clipWidthPixels, float clipMinYPixels, float clipHeightPixels)
        {
            bool isNewHandPosition = false;

            if (handPointer != null)
            {
                if (showHandPointer)
                {
                    Vector2 newAnchor;
                    if (handScreenCenterOverride.HasValue)
                    {
                        // Normalized against the same active clip region as the strips above, so
                        // the override lines up correctly whether or not a board clip is active.
                        float overrideX = Mathf.Clamp01((handScreenCenterOverride.Value.x - clipMinXPixels) / clipWidthPixels);
                        float overrideY = Mathf.Clamp01((handScreenCenterOverride.Value.y - clipMinYPixels) / clipHeightPixels);
                        newAnchor = new Vector2(overrideX, overrideY);
                    }
                    else
                    {
                        newAnchor = defaultAnchor;
                    }

                    // "New position" = wasn't already shown, or was shown but pointing somewhere
                    // else - a Refresh() call that leaves it on the SAME target doesn't replay the
                    // pop again.
                    isNewHandPosition = !handPointer.gameObject.activeSelf ||
                        lastHandAnchor == null || lastHandAnchor.Value != newAnchor;

                    handPointer.anchorMin = newAnchor;
                    handPointer.anchorMax = newAnchor;
                    handPointer.anchoredPosition = Vector2.zero;
                    lastHandAnchor = newAnchor;
                }
                else
                {
                    lastHandAnchor = null;

                    // Hidden mid-pop (target reached before the pop/delay finished) - stop it
                    // rather than let it finish later and fire its queued gesture-start callback
                    // on a hand that's no longer shown.
                    if (handAppearRoutine != null)
                    {
                        StopCoroutine(handAppearRoutine);
                        handAppearRoutine = null;
                    }
                }

                handPointer.gameObject.SetActive(showHandPointer);
            }

            if (showHandPointer && isNewHandPosition)
            {
                // Soft haptic cue for the hand pointer appearing or moving to a new target -
                // never replayed on a Refresh() call that leaves it pointing at the same spot.
                Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);

                // Hold off starting the tap/click gesture clip until the appear pop has actually
                // finished landing - starting both at once buried the click gesture underneath the
                // pop's own scale motion. Not new position (still pointing at the same target)
                // skips the pop entirely, so the gesture starts immediately below instead.
                PlayHandAppearAnimation(() => StartHandGestureAnimation(isDoubleTap));
            }
            else if (showHandPointer)
            {
                StartHandGestureAnimation(isDoubleTap);
            }
        }

        private void StartHandGestureAnimation(bool isDoubleTap)
        {
            if (handPointerAnimator == null)
            {
                return;
            }

            if (isDoubleTap)
            {
                // Don't restart this on every ordinary Refresh() call while still waiting on
                // (or already showing) the SAME double-tap target - only a freshly requested
                // double-tap (single-tap target, or nothing shown yet) should trigger it.
                if (!doubleTapPending)
                {
                    doubleTapPending = true;
                    if (doubleTapTriggerRoutine != null)
                    {
                        StopCoroutine(doubleTapTriggerRoutine);
                    }

                    doubleTapTriggerRoutine = StartCoroutine(TriggerDoubleTapWhenActive());
                }
            }
            else
            {
                doubleTapPending = false;
                if (doubleTapTriggerRoutine != null)
                {
                    StopCoroutine(doubleTapTriggerRoutine);
                    doubleTapTriggerRoutine = null;
                }

                PlayHandState(SingleTapStateName, isDoubleTap: false);
            }
        }

        // Waits for the hand pointer's own GameObject/Animator to actually be active in the
        // hierarchy before forcing the DoubleTap state - forcing it in the SAME synchronous call
        // that activates the GameObject risks racing the Animator's own OnEnable/initial-state
        // evaluation, which is what caused the double-tap clip to not stick on first appearance.
        private IEnumerator TriggerDoubleTapWhenActive()
        {
            while (handPointerAnimator == null || !handPointerAnimator.gameObject.activeInHierarchy)
            {
                yield return null;
            }

            doubleTapTriggerRoutine = null;

            // Could have been hidden, or switched to a single-tap target, while this was waiting.
            if (!doubleTapPending)
            {
                yield break;
            }

            PlayHandState(DoubleTapStateName, isDoubleTap: true);
        }

        // SetBool alone isn't enough to guarantee the right pose the FIRST time a state is
        // entered: the controller's state machine always initializes into its authored default
        // state (SingleTap) and only transitions off it on a LATER Update, via the ordinary
        // SingleTap<->DoubleTap transitions - so forcing Play() directly is what actually
        // guarantees the correct clip is showing, done only when it isn't ALREADY in that state
        // so this doesn't restart the clip (a visible pop back to frame 0) redundantly.
        private void PlayHandState(string targetStateName, bool isDoubleTap)
        {
            handPointerAnimator.SetBool(IsDoubleTapHash, isDoubleTap);

            if (!handPointerAnimator.GetCurrentAnimatorStateInfo(0).IsName(targetStateName))
            {
                handPointerAnimator.Play(targetStateName, 0, 0f);
            }
        }

        // Pops handPointer in from zero scale, same ease-out-back curve as
        // CatFlapSpriteBoardViewAdapter's own board-tile entrance (ScaleCellRoutine/EaseBackOut) -
        // captures its authored scale once on first use so repeated pops always animate back up to
        // that same size rather than whatever scale a previous, possibly-interrupted pop left it at.
        private void PlayHandAppearAnimation(System.Action onComplete = null)
        {
            if (handPointer == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (handAppearRoutine != null)
            {
                StopCoroutine(handAppearRoutine);
            }

            handPointerOriginalScale ??= handPointer.localScale;
            handAppearRoutine = StartCoroutine(HandAppearRoutine(handPointerOriginalScale.Value, onComplete));
        }

        private IEnumerator HandAppearRoutine(Vector3 targetScale, System.Action onComplete)
        {
            handPointer.localScale = Vector3.zero;

            if (handAppearDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(handAppearDelaySeconds);
            }

            float seconds = Mathf.Max(0.01f, handAppearDurationSeconds);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseBackOut(Mathf.Clamp01(elapsed / seconds));
                handPointer.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, t);
                yield return null;
            }

            handPointer.localScale = targetScale;
            handAppearRoutine = null;
            onComplete?.Invoke();
        }

        private static float EaseBackOut(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }

        private void SetCorner(RectTransform corner, float anchorX, float anchorY)
        {
            if (corner == null)
            {
                return;
            }

            corner.anchorMin = new Vector2(anchorX, anchorY);
            corner.anchorMax = corner.anchorMin;
            float diameter = cornerRadiusPixels * 2f;
            corner.sizeDelta = new Vector2(diameter, diameter);
            corner.anchoredPosition = Vector2.zero;
            corner.gameObject.SetActive(true);
        }

        private void SetEndCapActive(RectTransform cap, bool active)
        {
            if (cap != null)
            {
                cap.gameObject.SetActive(active);
            }
        }

        // A vertical bar stretched between xMin and xMax (one of them the board's real tight
        // edge, the other bled past the clip's own outer edge - see the caller) and across the
        // row band's own Y range (bandBottom/bandTop) - bridges the whole gap between the
        // highlighted row and the board's outer frame, whatever either gap's exact width is.
        private void SetVerticalEndCap(RectTransform cap, float xMin, float xMax, float bandBottom, float bandTop)
        {
            if (cap == null)
            {
                return;
            }

            cap.anchorMin = new Vector2(xMin, bandBottom);
            cap.anchorMax = new Vector2(xMax, bandTop);
            cap.sizeDelta = Vector2.zero;
            cap.anchoredPosition = Vector2.zero;
            cap.gameObject.SetActive(true);
        }

        // Same idea as SetVerticalEndCap, rotated 90 degrees: a horizontal bar stretched between
        // yMin and yMax, across the column band's own X range (bandLeft/bandRight).
        private void SetHorizontalEndCap(RectTransform cap, float yMin, float yMax, float bandLeft, float bandRight)
        {
            if (cap == null)
            {
                return;
            }

            cap.anchorMin = new Vector2(bandLeft, yMin);
            cap.anchorMax = new Vector2(bandRight, yMax);
            cap.sizeDelta = Vector2.zero;
            cap.anchoredPosition = Vector2.zero;
            cap.gameObject.SetActive(true);
        }


        private static void SetStripAnchors(RectTransform strip, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (strip == null)
            {
                return;
            }

            strip.anchorMin = anchorMin;
            strip.anchorMax = anchorMax;
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = Vector2.zero;
        }
    }
}
