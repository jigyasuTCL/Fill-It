using System;
using System.Collections;
using System.Collections.Generic;
using CatFlap.Models;
using CatFlap.Views;
using Shared.GameConfig;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapSpriteBoardViewAdapter : MonoBehaviour, ICatFlapBoardView
    {
        [SerializeField] private Transform cellContainer;
        [SerializeField] private CatFlapSpriteCellViewAdapter cellPrefab;
        [SerializeField] private CatFlapTargetIndicatorViewAdapter infoCellPrefab;
        [SerializeField] private Camera inputCamera;

        [Header("Small Board Scale - shrinks for a 2x2 grid")]
        // Scales baseTransform ("Base", BoardRoot's first child) - NEVER BoardRoot itself. If
        // baseTransform is left unassigned, falls back to BoardRoot's (this component's own
        // transform's) first child.
        [SerializeField] private Transform baseTransform;
        [SerializeField] private int smallBoardSize = 2;
        [SerializeField] private float smallBoardScale = 0.7f;

        [Header("Size board area")]
        [SerializeField]
        private BoardSizeBounds[] sizeBounds =
        {
            new BoardSizeBounds { Size = 2, MinCellCenterX = -1f, MaxCellCenterX = 1f, YOffset = 0f },
            new BoardSizeBounds { Size = 4, MinCellCenterX = -2f, MaxCellCenterX = 2f, YOffset = 0f },
            new BoardSizeBounds { Size = 6, MinCellCenterX = -2f, MaxCellCenterX = 2f, YOffset = 0f },
            new BoardSizeBounds { Size = 8, MinCellCenterX = -2f, MaxCellCenterX = 2f, YOffset = 0f },
            new BoardSizeBounds { Size = 10, MinCellCenterX = -2f, MaxCellCenterX = 2f, YOffset = 0f },
        };

        // Used only if a level's size (rows/columns) has no matching entry above.
        [SerializeField] private float fallbackMinCellCenterX = -2f;
        [SerializeField] private float fallbackMaxCellCenterX = 2f;
        [SerializeField] private float fallbackYOffset = 0f;

        [SerializeField] private float cellFillPercent = 1.08f;
        [SerializeField] private LayerMask cellLayerMask = ~0;

        [Header("Board Enter Animation")]
        [SerializeField] private float enterCellDuration = 0.22f;
        [SerializeField] private float enterCellStagger = 0.025f;
        [SerializeField] private float enterGivenRevealDelay = 0.02f;
        [SerializeField] private float enterPlacedRevealDelay = 0.05f;

        [Header("Heart Break FX (Assets/Shared/Features/LevelMistakeLimit) - plays above the mistaken cell")]
        [SerializeField] private GameObject heartBreakPrefab;
        [SerializeField] private float heartBreakLifetimeSeconds = 1f;
        [SerializeField] private float heartBreakVerticalOffset = 1f;

        [Header("Level Win Board Animation - plays alongside PlayHappyMoodOnAllCells")]
        // 1.5x faster than the original 0.9s/0.075s pace (duration and stagger both divided by
        // 1.5), so the whole per-cell pulse wave finishes noticeably sooner without changing the
        // scale multiplier (still 1.5x, unrelated - that's how big each tile pulses, not how fast).
        [Tooltip("Total time each tile spends pulsing (grow-to-peak + ease back to original), same wave order as PlayEnterAnimation.")]
        [SerializeField] private float winPulseCellDuration = 0.6f;
        [SerializeField] private float winPulseCellStagger = 0.05f;
        [SerializeField] private float winPulseScaleMultiplier = 1.5f;
        [SerializeField] private ParticleSystem leftWinConfetti;
        [SerializeField] private ParticleSystem rightWinConfetti;

        // FTUE-only: Level 4's last step shows this sprite in place of FtueSpotlightController's
        // usual dim/cross-highlight overlay (see CatFlapFtueDriver) - starts inactive in the scene.
        [SerializeField] private GameObject ftueLastStepOverlay;

        public void SetFtueLastStepOverlayActive(bool active)
        {
            if (ftueLastStepOverlay != null)
            {
                ftueLastStepOverlay.SetActive(active);
            }
        }

        [Serializable]
        private sealed class BoardSizeBounds
        {
            public int Size;
            public float MinCellCenterX;
            public float MaxCellCenterX;
            public float YOffset;
        }

        private readonly List<CatFlapSpriteCellViewAdapter> cells = new List<CatFlapSpriteCellViewAdapter>();
        private Vector3? baseOriginalScale;
        private readonly List<CatFlapTargetIndicatorViewAdapter> rowIndicators = new List<CatFlapTargetIndicatorViewAdapter>();
        private readonly List<CatFlapTargetIndicatorViewAdapter> columnIndicators = new List<CatFlapTargetIndicatorViewAdapter>();
        private CatFlapTargetIndicatorViewAdapter cornerIndicator;
        private GameRulesConfig rulesConfig = new GameRulesConfig().CloneNormalized();
        private CatFlapCellValue[,] renderedValues = new CatFlapCellValue[0, 0];
        private bool[,] renderedNotes = new bool[0, 0];
        private bool[,] renderedLocked = new bool[0, 0];
        private int columnCount;
        private int rowCount;
        private int lastSelectionFrame = -1;
        private Coroutine enterAnimationRoutine;

        public event Action<int, int> CellSelected = delegate { };

        /// <summary>
        /// Plays the level-start reveal: cell boxes scale in one-by-one in a wave from the
        /// bottom-left corner. As soon as each individual cell's own box entry finishes, that
        /// same cell (after its configured reveal delay - given cells reveal sooner than
        /// player-placed ones) pops in its value AND its row/column completed/wrong indication
        /// (already computed by the caller via CatFlapRuntimeController.RefreshValidationVisuals
        /// before this runs) at the same time - not as separate global waves. Call once, right
        /// after the board has been fully built/rendered for the level.
        /// </summary>
        public void PlayEnterAnimation(Action onComplete = null)
        {
            if (enterAnimationRoutine != null)
            {
                StopCoroutine(enterAnimationRoutine);
            }

            enterAnimationRoutine = StartCoroutine(EnterAnimationRoutine(onComplete));
        }

        private IEnumerator EnterAnimationRoutine(Action onComplete)
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.BoardTilesIntro);

            int activeCount = Mathf.Min(rowCount * columnCount, cells.Count);
            if (activeCount <= 0 || columnCount <= 0)
            {
                enterAnimationRoutine = null;
                onComplete?.Invoke();
                yield break;
            }

            float maxTotalDelay = 0f;
            for (int index = 0; index < activeCount; index++)
            {
                CatFlapSpriteCellViewAdapter cell = cells[index];
                if (cell == null)
                {
                    continue;
                }

                cell.PrepareForEnterAnimation();
                Vector3 targetScale = cell.transform.localScale;
                cell.transform.localScale = Vector3.zero;

                float entryDelay = WaveDistance(index) * Mathf.Max(0f, enterCellStagger);
                float revealDelay = cell.IsGiven ? Mathf.Max(0f, enterGivenRevealDelay) : Mathf.Max(0f, enterPlacedRevealDelay);
                float totalDelay = entryDelay + enterCellDuration + revealDelay;
                maxTotalDelay = Mathf.Max(maxTotalDelay, totalDelay);

                StartCoroutine(ScaleCellRoutine(cell.transform, targetScale, entryDelay));
                StartCoroutine(RevealCellAfterDelay(cell, totalDelay));

                // Alternate tiles only (every other cell in the entry wave) - a haptic pulse on
                // every single tile would feel like a constant buzz; every other tile instead
                // reads as a light rhythmic pulse synced to the scale-in wave.
                if (index % 2 == 0)
                {
                    StartCoroutine(PlayEntryHapticRoutine(entryDelay));
                }
            }

            yield return new WaitForSecondsRealtime(maxTotalDelay);

            enterAnimationRoutine = null;
            onComplete?.Invoke();
        }

        // Bottom-left origin: highest row index, left-most column - shared by the cell scale-in
        // wave and the value-reveal wave so every stage sweeps the board in the same order.
        private float WaveDistance(int index)
        {
            int row = index / columnCount;
            int column = index % columnCount;
            return (rowCount - 1 - row) + column;
        }

        // Reveals this cell's value and its row/column result highlight together, once this
        // specific cell's own box entry (plus its reveal delay) has finished.
        private IEnumerator RevealCellAfterDelay(CatFlapSpriteCellViewAdapter cell, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            cell.RevealValue();
            cell.RevealResultHighlight();
            // global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.BoardTileAppear);
        }

        private IEnumerator PlayEntryHapticRoutine(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
        }

        private IEnumerator ScaleCellRoutine(Transform target, Vector3 to, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            float seconds = Mathf.Max(0.01f, enterCellDuration);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseBackOut(Mathf.Clamp01(elapsed / seconds));
                target.localScale = Vector3.LerpUnclamped(Vector3.zero, to, t);
                yield return null;
            }

            target.localScale = to;
        }

        private static float EaseBackOut(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }

        public void ApplyRulesConfig(GameRulesConfig config)
        {
            rulesConfig = (config ?? new GameRulesConfig()).CloneNormalized();
            ApplyRulesConfig(cells);
            ApplyIndicatorRulesConfig(rowIndicators);
            ApplyIndicatorRulesConfig(columnIndicators);
            cornerIndicator?.ApplyRulesConfig(rulesConfig);
        }

        private void Awake()
        {
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!Shared.GameFlow.InputManager.GamePlayInputEnabled)
            {
                return;
            }

            if (TryGetReleasedPointerPosition(out Vector2 screenPosition))
            {
                TrySelectAtScreenPosition(screenPosition);
            }
        }

        public void Build(int rows, int columns)
        {
            rowCount = rows;
            columnCount = columns;

            // Scales the idle-mood concurrency floor (2-5 characters) to this level's board size -
            // see CatFlapCharacterIdleThrottle.
            CatFlapCharacterIdleThrottle.SetBoardLength(Mathf.Max(rows, columns));

            Transform boardBase = ResolveBoardBase();
            if (boardBase != null)
            {
                // Cache the authored scale on first use - Build() can run again later (level
                // restart/resize), and by then localScale may already be the shrunk value from a
                // previous call, so re-reading it here would compound the shrink instead of always
                // being a fixed 70% of the ORIGINAL authored scale.
                if (!baseOriginalScale.HasValue)
                {
                    baseOriginalScale = boardBase.localScale;
                }

                boardBase.localScale = Mathf.Max(rows, columns) == smallBoardSize
                    ? baseOriginalScale.Value * smallBoardScale
                    : baseOriginalScale.Value;
            }

            int required = rows * columns;
            EnsureCellCount(required);
            EnsureIndicatorCount(rowIndicators, rows);
            EnsureIndicatorCount(columnIndicators, columns);
            EnsureCornerIndicator();
            EnsureRenderedState(rows, columns);

            GetBoundsForSize(Mathf.Max(rows, columns), out float minX, out float maxX, out float yOffset);
            float horizontalSpacing = CalculateSpacing(columns, minX, maxX);
            float verticalSpacing = horizontalSpacing;
            float cellScale = CalculateCellScale(horizontalSpacing);
            Vector2 origin = new Vector2(
                CalculateLeftMostCellCenterX(minX, horizontalSpacing),
                CalculateTopMostCellCenterY(rows, verticalSpacing, yOffset));

            for (int index = 0; index < cells.Count; index++)
            {
                bool active = index < required;
                cells[index].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                int row = index / columns;
                int column = index % columns;
                cells[index].transform.localScale = new Vector3(cellScale, cellScale, 1f);
                cells[index].transform.localPosition = new Vector3(
                    origin.x + column * horizontalSpacing,
                    origin.y - row * verticalSpacing,
                    0f);
                cells[index].Bind(row, column, HandleCellSelected);
            }

            PositionIndicators(rows, columns, origin, horizontalSpacing, verticalSpacing, cellScale);
            PositionCornerIndicator(origin, horizontalSpacing, verticalSpacing, cellScale);
            RefreshIndicators();

            if (cornerIndicator != null)
            {
                cornerIndicator.SetTitleState();
                cornerIndicator.SetIndicatorState(columns / 2, columns / 2, false);
            }
        }

        // NEVER returns this component's own transform (BoardRoot) - only baseTransform if
        // explicitly assigned, or else BoardRoot's first child ("Base"). BoardRoot itself must
        // never be scaled.
        private Transform ResolveBoardBase()
        {
            if (baseTransform != null)
            {
                return baseTransform;
            }

            return transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        // Looks up the hand-tuned min/max X and YOffset for this exact board size (2x2, 4x4, ...)
        // from sizeBounds. Falls back to fallbackMinCellCenterX/fallbackMaxCellCenterX/
        // fallbackYOffset for any size that doesn't have its own entry configured. Vertical
        // spacing itself is derived from the resulting horizontal spacing (see Build()), so
        // there's no separate Y bound - YOffset only nudges the whole grid up/down afterward.
        private void GetBoundsForSize(int size, out float minX, out float maxX, out float yOffset)
        {
            if (sizeBounds != null)
            {
                for (int index = 0; index < sizeBounds.Length; index++)
                {
                    BoardSizeBounds bounds = sizeBounds[index];
                    if (bounds != null && bounds.Size == size)
                    {
                        minX = bounds.MinCellCenterX;
                        maxX = bounds.MaxCellCenterX;
                        yOffset = bounds.YOffset;
                        return;
                    }
                }
            }

            minX = fallbackMinCellCenterX;
            maxX = fallbackMaxCellCenterX;
            yOffset = fallbackYOffset;
        }

        private float CalculateSpacing(int count, float min, float max)
        {
            if (count <= 0)
            {
                return 0f;
            }

            return (max - min) / count;
        }

        private float CalculateLeftMostCellCenterX(float minX, float horizontalSpacing)
        {
            return minX + horizontalSpacing;
        }

        private float CalculateTopMostCellCenterY(int rows, float verticalSpacing, float yOffset)
        {
            return (rows - 1) * verticalSpacing * 0.5f + yOffset;
        }

        private float CalculateCellScale(float spacing)
        {
            if (spacing <= 0f)
            {
                return 1f;
            }

            float baseCellSize = GetBaseCellWorldSize();
            if (baseCellSize <= 0f)
            {
                return 1f;
            }

            return spacing * Mathf.Clamp(cellFillPercent, 0.1f, 1.2f) / baseCellSize;
        }

        private float GetBaseCellWorldSize()
        {
            if (cells.Count <= 0 || cells[0] == null)
            {
                return 1f;
            }

            Transform cellTransform = cells[0].transform;
            Vector3 originalScale = cellTransform.localScale;
            bool wasActive = cells[0].gameObject.activeSelf;
            cells[0].gameObject.SetActive(true);
            cellTransform.localScale = Vector3.one;

            float size = 1f;
            Renderer renderer = cells[0].GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                size = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.y);
            }
            else
            {
                Collider2D collider = cells[0].GetComponentInChildren<Collider2D>();
                if (collider != null)
                {
                    size = Mathf.Max(collider.bounds.size.x, collider.bounds.size.y);
                }
            }

            cellTransform.localScale = originalScale;
            cells[0].gameObject.SetActive(wasActive);
            return Mathf.Max(0.0001f, size);
        }

        public void SetCell(int row, int column, CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor, CatFlapCellVisualState visualState, bool isGiven, bool suppressValueChangeAnimation = false, bool forcePlayFlip = false, float speedMultiplier = 1f, bool suppressPostFlipPop = false)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            if (row >= 0 && row < rowCount && column >= 0 && column < columnCount)
            {
                renderedValues[row, column] = value;
                renderedNotes[row, column] = isNote;
                renderedLocked[row, column] = isLocked;
            }

            cells[index].SetState(value, isLocked, isNote, useLockedColor, visualState, isGiven, suppressValueChangeAnimation, forcePlayFlip, speedMultiplier, suppressPostFlipPop);
            RefreshIndicators();
        }

        // Every concurrently-in-flight reveal (one entry per PlayStaggeredReveal call that hasn't
        // finished yet) - NOT a single slot. Two separate moves that each complete a different
        // line must be able to play their reveals in parallel instead of the second cancelling the
        // first, since they touch entirely different cells.
        private readonly List<Coroutine> activeStaggeredRevealRoutines = new List<Coroutine>();

        public void PlayStaggeredReveal(IReadOnlyList<IReadOnlyList<CatFlapCellPosition>> groups, float staggerSeconds, float speedIncreasePerStep, float maxSpeedMultiplier, Action<CatFlapCellPosition, float> revealCell, Action onComplete)
        {
            if (groups == null || groups.Count == 0 || revealCell == null)
            {
                onComplete?.Invoke();
                return;
            }

            // The coroutine always awaits at least one cell's own tile-flip animation before it
            // can finish (see AnimateStaggeredReveal's IsAnyEntryAnimationPlaying wait), so it can
            // never complete synchronously within this same StartCoroutine call - safe to capture
            // and add `routine` to the active list only after StartCoroutine returns, and to read
            // it (via this closure) from inside the completion callback that fires later.
            Coroutine routine = null;
            routine = StartCoroutine(AnimateStaggeredReveal(groups, staggerSeconds, speedIncreasePerStep, maxSpeedMultiplier, revealCell, () =>
            {
                activeStaggeredRevealRoutines.Remove(routine);
                onComplete?.Invoke();
            }));
            activeStaggeredRevealRoutines.Add(routine);
        }

        public void CancelStaggeredReveal()
        {
            for (int index = 0; index < activeStaggeredRevealRoutines.Count; index++)
            {
                if (activeStaggeredRevealRoutines[index] != null)
                {
                    StopCoroutine(activeStaggeredRevealRoutines[index]);
                }
            }

            activeStaggeredRevealRoutines.Clear();
        }

        private IEnumerator AnimateStaggeredReveal(IReadOnlyList<IReadOnlyList<CatFlapCellPosition>> groups, float staggerSeconds, float speedIncreasePerStep, float maxSpeedMultiplier, Action<CatFlapCellPosition, float> revealCell, Action onComplete)
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                // Every cell in this group reveals together, in the same instant - e.g. a row and a
                // column completed by the same move are grouped cell-for-cell (see
                // CatFlapRuntimeController.GetCompletedLineRevealGroups) so they visibly flip in
                // lockstep instead of the whole row finishing before the column even starts.
                // staggerSeconds only pauses BETWEEN groups, never within one.
                //
                // Each successive group also flips faster than the last - group 0 at 1x, group 1 at
                // (1 + speedIncreasePerStep)x, and so on, capped at maxSpeedMultiplier - so a long
                // completed line visibly speeds up as it reveals instead of ticking through at one
                // flat pace throughout.
                float speedMultiplier = Mathf.Min(1f + groupIndex * Mathf.Max(0f, speedIncreasePerStep), Mathf.Max(1f, maxSpeedMultiplier));
                IReadOnlyList<CatFlapCellPosition> group = groups[groupIndex];
                for (int cellIndex = 0; cellIndex < group.Count; cellIndex++)
                {
                    // An unhandled exception inside revealCell (e.g. a per-cell side effect like the
                    // completed particle/mood) would otherwise silently kill this coroutine mid-loop -
                    // Unity gives no console crash, just every remaining cell in the line never
                    // revealing/flipping. Isolate each cell's reveal so one failure can't swallow the
                    // rest of the line.
                    try
                    {
                        revealCell(group[cellIndex], speedMultiplier);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }

                if (staggerSeconds > 0f && groupIndex < groups.Count - 1)
                {
                    yield return new WaitForSeconds(staggerSeconds);
                }
            }

            // The loop above only waits staggerSeconds BETWEEN starting each cell's flip - it
            // never waits for the LAST (or even second-to-last, if staggerSeconds is shorter than
            // a single flip's own duration) cell's flip to actually finish playing before this
            // coroutine reports done. onComplete (RenderAll + the completed-line appreciation text
            // + PlayPageState - see CatFlapRuntimeController.PlayLockedCellsThenPageState) firing
            // that early was cutting those still-mid-flight flips short: RenderAll's own instant
            // repaint stops whatever tileFlipAnimation coroutine was still running (see
            // CatFlapSpriteCellViewAdapter.SetState's interruptedFlip handling) well before it
            // visually settled, right as the appreciation text popped in - reading as "the text
            // appearing stopped the remaining flips". Wait for every cell's own flip to genuinely
            // finish first, same pattern as the level-win celebration's own
            // IsAnyEntryAnimationPlaying wait (CatFlapGameplayManager.PlayLevelWinCelebrationThenShowPopup).
            while (IsAnyEntryAnimationPlaying())
            {
                yield return null;
            }

            onComplete?.Invoke();
        }

        public void PlayCompletedParticle(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].PlayCompletedParticle();
        }

        // See CatFlapSpriteCellViewAdapter.PlayHintParticle - called from
        // CatFlapGameplayManager.ApplyActiveHint right after the Hint booster places its value.
        public void PlayHintParticle(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].PlayHintParticle();
        }

        // Plays every cell's own completed-line character pop (see
        // CatFlapSpriteCellViewAdapter.PlayCompletedLinePop) across an entire row at once - the
        // SAME scale up/hold/settle beat normal gameplay already plays per-cell right after a
        // real row/column locks in, just without the tile-flip reveal that normally precedes it
        // (these tiles already show their correct value). Used by Cat Flap's Level 4 FTUE rules
        // walkthrough right after the player places the first guided tile, in place of a
        // single-cell celebration - this reads as "this whole row is on its way to being
        // correct" rather than celebrating just the one tile that was tapped.
        public void PlayRowCompletePulse(int row)
        {
            for (int column = 0; column < columnCount; column++)
            {
                int index = row * columnCount + column;
                if (index < 0 || index >= cells.Count || cells[index] == null)
                {
                    continue;
                }

                cells[index].PlayCompletedLinePop();
            }
        }

        // Level-win celebration: every eligible cell (real, non-note cat/fish values - empty and
        // note cells are silently skipped by PlayCompletedParticle/CanAnimateCharacter) plays its
        // Happy mood at once, rather than the usual per-line completion beat. playParticles false
        // (FTUE levels - see CatFlapGameplayManager.PlayLevelWinCelebrationThenShowPopup) skips
        // just each cell's own completedParticle burst; the mood animation itself always plays.
        public void PlayHappyMoodOnAllCells(bool playParticles = true)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].PlayLevelWinCelebration(playParticles);
            }
        }

        // Level-win board flourish, played alongside PlayHappyMoodOnAllCells (not instead of it):
        // every tile's BACKGROUND (not the whole cell, and NOT its character icon - see
        // CatFlapSpriteCellViewAdapter.PlayLevelWinPulse) pulses up past its normal size then eases
        // back down, swept in the same bottom-left-to-top-right wave order as PlayEnterAnimation
        // (see WaveDistance), plus a confetti burst on each side of the board. The character's own
        // Happy mood loop (already playing via PlayHappyMoodOnAllCells) is left completely
        // untouched - scaling the whole cell used to visibly fight with that animation.
        // playParticles false (FTUE levels) skips just the left/right confetti bursts; the
        // background pulse itself always plays.
        public void PlayLevelWinBoardAnimation(bool playParticles = true)
        {
            if (playParticles && leftWinConfetti != null)
            {
                leftWinConfetti.Play();
            }

            if (playParticles && rightWinConfetti != null)
            {
                rightWinConfetti.Play();
            }

            int activeCount = Mathf.Min(rowCount * columnCount, cells.Count);
            if (activeCount <= 0 || columnCount <= 0)
            {
                return;
            }

            for (int index = 0; index < activeCount; index++)
            {
                CatFlapSpriteCellViewAdapter cell = cells[index];
                if (cell == null)
                {
                    continue;
                }

                float delay = WaveDistance(index) * Mathf.Max(0f, winPulseCellStagger);
                cell.PlayLevelWinPulse(delay, winPulseCellDuration, winPulseScaleMultiplier);
            }
        }

        // True while any cell's level-win background pulse (PlayLevelWinBoardAnimation) is still
        // mid-play, including cells still waiting out their own wave-position delay before they
        // even start - lets the win-celebration coroutine wait for the whole pulse wave to finish
        // (not just be triggered) before the result popup appears.
        public bool IsAnyLevelWinPulsePlaying()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsPlayingLevelWinPulse)
                {
                    return true;
                }
            }

            return false;
        }

        // True while any cell's tap-driven value-change flip is still mid-play - the winning tap
        // itself included. Lets the level-win celebration wait for that placement to actually
        // finish landing before playing on top of it.
        public bool IsAnyEntryAnimationPlaying()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsPlayingEntryAnimation)
                {
                    return true;
                }
            }

            return false;
        }

        public void PlayMistakeBlink(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].PlayMistakeBlink();
        }

        public void PlayRuleBreakBlink(IReadOnlyList<CatFlapCellPosition> cellsToBlink)
        {
            if (cellsToBlink == null)
            {
                return;
            }

            for (int i = 0; i < cellsToBlink.Count; i++)
            {
                int index = cellsToBlink[i].Row * columnCount + cellsToBlink[i].Column;
                if (index < 0 || index >= cells.Count)
                {
                    continue;
                }

                cells[index].PlayRuleBreakBlink();
            }
        }

        public void PlayWarningBlink(IReadOnlyList<CatFlapCellPosition> cellsToBlink, bool playShake = true)
        {
            if (cellsToBlink == null)
            {
                return;
            }

            for (int i = 0; i < cellsToBlink.Count; i++)
            {
                int index = cellsToBlink[i].Row * columnCount + cellsToBlink[i].Column;
                if (index < 0 || index >= cells.Count)
                {
                    continue;
                }

                cells[index].PlayWarningBlink(playShake);
            }
        }

        public void StopWarningBlink(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].StopWarningBlink();
        }

        /// <summary>Shows/hides a row's target-indicator Wrong Solution icon - see CatFlapTargetIndicatorViewAdapter.SetWarningState.</summary>
        public void SetRowWarningState(int row, bool isWrong)
        {
            if (row < 0 || row >= rowIndicators.Count)
            {
                return;
            }

            rowIndicators[row].SetWarningState(isWrong);
        }

        /// <summary>Shows/hides a column's target-indicator Wrong Solution icon - see CatFlapTargetIndicatorViewAdapter.SetWarningState.</summary>
        public void SetColumnWarningState(int column, bool isWrong)
        {
            if (column < 0 || column >= columnIndicators.Count)
            {
                return;
            }

            columnIndicators[column].SetWarningState(isWrong);
        }

        /// <summary>True while any cell's rule-break pulse (see PlayRuleBreakBlink) is still actively running board-wide.</summary>
        public bool IsAnyRuleBreakBlinking()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsRuleBreakBlinking)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True while any cell's Wrong Solution blink (see PlayWarningBlink) is still actively running board-wide.</summary>
        public bool IsAnyWarningBlinking()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsWarningBlinking)
                {
                    return true;
                }
            }

            return false;
        }

        public void PlayLockedTapShake(int row, int column)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].PlayLockedTapShake();
        }

        // Plays just above the specific cell where the mistake happened, rather than a single
        // board-wide beat, so it reads as tied to that mistake.
        public void PlayHeartBreak(int row, int column)
        {
            if (heartBreakPrefab == null)
            {
                return;
            }

            int index = row * columnCount + column;
            Transform anchor = index >= 0 && index < cells.Count ? cells[index].transform : cellContainer;
            if (anchor == null)
            {
                return;
            }

            // +Y lifts it above the cell; -1 on Z pulls it a step closer to camera than the board
            // plane, so it draws above the cell sprite instead of behind it.
            Vector3 spawnPosition = anchor.position + new Vector3(0f, heartBreakVerticalOffset, -1f);
            GameObject instance = Instantiate(heartBreakPrefab, spawnPosition, heartBreakPrefab.transform.rotation);
            Destroy(instance, heartBreakLifetimeSeconds);
        }

        public bool TryGetCellWorldPosition(int row, int column, out Vector3 position)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                position = default;
                return false;
            }

            position = cells[index].transform.position;
            return true;
        }

        private int hintHighlightedIndex = -1;

        public void ShowHintTarget(int row, int column, CatFlapCellValue value)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            if (hintHighlightedIndex >= 0 && hintHighlightedIndex != index && hintHighlightedIndex < cells.Count)
            {
                cells[hintHighlightedIndex].SetHintHighlighted(false);
                cells[hintHighlightedIndex].ClearHintPreview();
            }

            hintHighlightedIndex = index;
            cells[index].SetHintHighlighted(true);
            cells[index].SetHintPreview(value);
        }

        public void ClearHintTarget()
        {
            if (hintHighlightedIndex >= 0 && hintHighlightedIndex < cells.Count)
            {
                cells[hintHighlightedIndex].SetHintHighlighted(false);
                cells[hintHighlightedIndex].ClearHintPreview();
            }

            hintHighlightedIndex = -1;
        }

        public Camera InputCamera => inputCamera != null ? inputCamera : Camera.main;

        // World-space X distance guaranteed to fully clear the camera's viewport on either side -
        // used by CatFlapGameplayManager's FTUE level-to-level slide transition to know how far
        // left/right to move the whole board off-screen before repositioning/reloading it. Based
        // on the input camera's own orthographic viewport width (this board is world-space
        // SpriteRenderer gameplay, always viewed through an orthographic camera - falls back to a
        // generous fixed distance if that assumption ever doesn't hold).
        public float GetOffscreenSlideDistance()
        {
            Camera camera = InputCamera;
            if (camera != null && camera.orthographic)
            {
                return camera.orthographicSize * 2f * Mathf.Max(1f, camera.aspect);
            }

            return 20f;
        }

        // Animates this board's own root transform (this component's GameObject - never touched
        // by Build/EnsureCellCount, which only ever position individual cells, see
        // ResolveBoardBase) from its current localPosition to targetLocalPosition. Used by
        // CatFlapGameplayManager's FTUE level-to-level slide transition to move the whole board
        // off/on screen, with a normal level load/rebuild happening in between while it's
        // positioned off-screen and so invisible.
        public IEnumerator SlideLocalPosition(Vector3 targetLocalPosition, float durationSeconds)
        {
            Vector3 start = transform.localPosition;
            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseInOutSlide(Mathf.Clamp01(elapsed / duration));
                transform.localPosition = Vector3.LerpUnclamped(start, targetLocalPosition, t);
                yield return null;
            }

            transform.localPosition = targetLocalPosition;
        }

        private static float EaseInOutSlide(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        public bool TryGetCellWorldBounds(int row, int column, out Bounds bounds)
        {
            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                bounds = default;
                return false;
            }

            Renderer cellRenderer = cells[index].GetComponentInChildren<Renderer>();
            if (cellRenderer == null)
            {
                bounds = default;
                return false;
            }

            bounds = cellRenderer.bounds;
            return true;
        }

        // Union of every currently-active cell's world bounds, plus the row/column target
        // indicators and corner indicator (all visually part of "the board") - used by
        // CatFlapFtueDriver to scope the FTUE dim overlay to just the board area instead of the
        // whole screen (see FtueSpotlightController.SetBoardClipBounds).
        public bool TryGetBoardWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            for (int index = 0; index < rowCount * columnCount && index < cells.Count; index++)
            {
                if (!cells[index].gameObject.activeInHierarchy)
                {
                    continue;
                }

                Renderer cellRenderer = cells[index].GetComponentInChildren<Renderer>();
                if (cellRenderer == null)
                {
                    continue;
                }

                EncapsulateBounds(ref bounds, ref hasBounds, cellRenderer.bounds);
            }

            EncapsulateIndicatorBounds(rowIndicators, ref bounds, ref hasBounds);
            EncapsulateIndicatorBounds(columnIndicators, ref bounds, ref hasBounds);

            if (cornerIndicator != null && cornerIndicator.gameObject.activeInHierarchy)
            {
                Renderer cornerRenderer = cornerIndicator.GetComponentInChildren<Renderer>();
                if (cornerRenderer != null)
                {
                    EncapsulateBounds(ref bounds, ref hasBounds, cornerRenderer.bounds);
                }
            }

            return hasBounds;
        }

        private static void EncapsulateIndicatorBounds(List<CatFlapTargetIndicatorViewAdapter> indicators, ref Bounds bounds, ref bool hasBounds)
        {
            for (int index = 0; index < indicators.Count; index++)
            {
                if (indicators[index] == null || !indicators[index].gameObject.activeInHierarchy)
                {
                    continue;
                }

                Renderer indicatorRenderer = indicators[index].GetComponentInChildren<Renderer>();
                if (indicatorRenderer == null)
                {
                    continue;
                }

                EncapsulateBounds(ref bounds, ref hasBounds, indicatorRenderer.bounds);
            }
        }

        private static void EncapsulateBounds(ref Bounds bounds, ref bool hasBounds, Bounds add)
        {
            if (!hasBounds)
            {
                bounds = add;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(add);
        }

        public void Clear()
        {
            ClearHintTarget();
            rowCount = 0;
            columnCount = 0;
            renderedValues = new CatFlapCellValue[0, 0];
            renderedNotes = new bool[0, 0];
            renderedLocked = new bool[0, 0];
            SetActive(cells, false);
            SetActive(rowIndicators, false);
            SetActive(columnIndicators, false);
            if (cornerIndicator != null)
            {
                cornerIndicator.gameObject.SetActive(false);
            }
        }

        private void EnsureCellCount(int required)
        {
            if (cellContainer == null || cellPrefab == null)
            {
                return;
            }

            while (cells.Count < required)
            {
                CatFlapSpriteCellViewAdapter cell = Instantiate(cellPrefab, cellContainer);
                cell.ApplyRulesConfig(rulesConfig);
                cells.Add(cell);
            }
        }

        private void EnsureIndicatorCount(List<CatFlapTargetIndicatorViewAdapter> indicators, int required)
        {
            if (cellContainer == null || infoCellPrefab == null)
            {
                return;
            }

            while (indicators.Count < required)
            {
                CatFlapTargetIndicatorViewAdapter indicator = Instantiate(infoCellPrefab, cellContainer);
                indicator.ApplyRulesConfig(rulesConfig);
                indicator.transform.SetAsFirstSibling();
                indicators.Add(indicator);
            }

            for (int index = 0; index < indicators.Count; index++)
            {
                indicators[index].gameObject.SetActive(index < required);
            }
        }

        private void EnsureCornerIndicator()
        {
            if (cornerIndicator != null || cellContainer == null || infoCellPrefab == null)
            {
                return;
            }

            cornerIndicator = Instantiate(infoCellPrefab, cellContainer);
            cornerIndicator.ApplyRulesConfig(rulesConfig);
            cornerIndicator.transform.SetAsFirstSibling();
        }

        private void EnsureRenderedState(int rows, int columns)
        {
            if (renderedValues.GetLength(0) == rows && renderedValues.GetLength(1) == columns)
            {
                return;
            }

            renderedValues = new CatFlapCellValue[rows, columns];
            renderedNotes = new bool[rows, columns];
            renderedLocked = new bool[rows, columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    renderedValues[row, column] = CatFlapCellValue.Empty;
                }
            }
        }

        private void PositionIndicators(
            int rows,
            int columns,
            Vector2 origin,
            float horizontalSpacing,
            float verticalSpacing,
            float cellScale)
        {
            for (int row = 0; row < rowIndicators.Count; row++)
            {
                bool active = row < rows;
                rowIndicators[row].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                rowIndicators[row].SetRootScale(new Vector3(cellScale * 0.5f, cellScale, 1f));
                rowIndicators[row].transform.localPosition = new Vector3(
                    origin.x - CalculateIndicatorOffset(horizontalSpacing),
                    origin.y - row * verticalSpacing,
                    0f);
            }

            for (int column = 0; column < columnIndicators.Count; column++)
            {
                bool active = column < columns;
                columnIndicators[column].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                columnIndicators[column].SetRootScale(new Vector3(cellScale, cellScale * 0.5f, 1f));
                columnIndicators[column].transform.localPosition = new Vector3(
                    origin.x + column * horizontalSpacing,
                    origin.y + CalculateIndicatorOffset(verticalSpacing),
                    0f);
            }
        }

        private void PositionCornerIndicator(Vector2 origin, float horizontalSpacing, float verticalSpacing, float cellScale)
        {
            if (cornerIndicator == null)
            {
                return;
            }

            cornerIndicator.gameObject.SetActive(true);
            cornerIndicator.SetRootScale(new Vector3(cellScale * 0.5f, cellScale * 0.5f, 1f));
            cornerIndicator.transform.localPosition = new Vector3(
                origin.x - CalculateIndicatorOffset(horizontalSpacing),
                origin.y + CalculateIndicatorOffset(verticalSpacing),
                0f);
        }

        // Row/column/corner indicator sprites render at half the normal cell's size on the axis
        // that faces the grid (see the 0.5f scale factors above), so their center can't simply
        // sit a full "spacing" away like two normal cells do - that would leave a bigger visible
        // gap on the indicator's (smaller) side. This solves for the center-to-center distance
        // that makes the edge-to-edge gap match a normal cell-to-cell gap exactly:
        //   gapNormal = spacing - normalCellSize
        //   distance  = gapNormal + normalCellSize / 2 + indicatorSize / 2
        // and indicatorSize = normalCellSize / 2 (the 0.5f scale), which simplifies to:
        //   distance = spacing - normalCellSize / 4
        private float CalculateIndicatorOffset(float spacing)
        {
            float fill = Mathf.Clamp(cellFillPercent, 0.1f, 1.2f);
            float normalCellSize = spacing * fill;
            return spacing - normalCellSize * 0.25f;
        }

        private void RefreshIndicators()
        {
            for (int row = 0; row < rowCount && row < rowIndicators.Count; row++)
            {
                CountRow(row, out int fishCount, out int catCount, out bool isCompleted);
                rowIndicators[row].SetIndicatorState(catCount, fishCount, isCompleted);
            }

            for (int column = 0; column < columnCount && column < columnIndicators.Count; column++)
            {
                CountColumn(column, out int fishCount, out int catCount, out bool isCompleted);
                columnIndicators[column].SetIndicatorState(catCount, fishCount, isCompleted);
            }
        }

        private void CountRow(int row, out int fishCount, out int catCount, out bool isCompleted)
        {
            fishCount = 0;
            catCount = 0;
            isCompleted = columnCount > 0;
            for (int column = 0; column < columnCount; column++)
            {
                CountValue(renderedValues[row, column], renderedNotes[row, column], ref fishCount, ref catCount);
                isCompleted &= renderedLocked[row, column];
            }
        }

        private void CountColumn(int column, out int fishCount, out int catCount, out bool isCompleted)
        {
            fishCount = 0;
            catCount = 0;
            isCompleted = rowCount > 0;
            for (int row = 0; row < rowCount; row++)
            {
                CountValue(renderedValues[row, column], renderedNotes[row, column], ref fishCount, ref catCount);
                isCompleted &= renderedLocked[row, column];
            }
        }

        private static void CountValue(CatFlapCellValue value, bool isNote, ref int fishCount, ref int catCount)
        {
            if (isNote)
            {
                return;
            }

            if (value == CatFlapCellValue.Fish)
            {
                fishCount++;
            }
            else if (value == CatFlapCellValue.Cat)
            {
                catCount++;
            }
        }

        private void HandleCellSelected(int row, int column)
        {
            CellSelected?.Invoke(row, column);
        }

        private void TrySelectAtScreenPosition(Vector2 screenPosition)
        {
            if (lastSelectionFrame == Time.frameCount)
            {
                return;
            }

            Camera cameraToUse = inputCamera != null ? inputCamera : Camera.main;
            if (cameraToUse == null)
            {
                return;
            }

            Vector3 worldPosition = cameraToUse.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -cameraToUse.transform.position.z));
            Vector2 worldPoint = new Vector2(worldPosition.x, worldPosition.y);
            RaycastHit2D hit = Physics2D.Raycast(worldPoint, Vector2.zero, 0f, cellLayerMask);
            if (hit.collider == null)
            {
                return;
            }

            CatFlapSpriteCellViewAdapter cell = hit.collider.GetComponentInParent<CatFlapSpriteCellViewAdapter>();
            if (cell != null)
            {
                lastSelectionFrame = Time.frameCount;
                cell.TrySelect();
            }
        }

        private static bool TryGetReleasedPointerPosition(out Vector2 screenPosition)
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                foreach (TouchControl touch in Touchscreen.current.touches)
                {
                    if (touch.press.wasReleasedThisFrame)
                    {
                        screenPosition = touch.position.ReadValue();
                        return true;
                    }
                }
            }

            screenPosition = default;
            return false;
        }

        private static void SetActive(List<CatFlapSpriteCellViewAdapter> views, bool active)
        {
            for (int index = 0; index < views.Count; index++)
            {
                if (views[index] != null)
                {
                    views[index].gameObject.SetActive(active);
                }
            }
        }

        private static void SetActive(List<CatFlapTargetIndicatorViewAdapter> views, bool active)
        {
            for (int index = 0; index < views.Count; index++)
            {
                if (views[index] != null)
                {
                    views[index].gameObject.SetActive(active);
                }
            }
        }

        private void ApplyRulesConfig(List<CatFlapSpriteCellViewAdapter> views)
        {
            for (int index = 0; index < views.Count; index++)
            {
                views[index]?.ApplyRulesConfig(rulesConfig);
            }
        }

        private void ApplyIndicatorRulesConfig(List<CatFlapTargetIndicatorViewAdapter> views)
        {
            for (int index = 0; index < views.Count; index++)
            {
                views[index]?.ApplyRulesConfig(rulesConfig);
            }
        }
    }
}
