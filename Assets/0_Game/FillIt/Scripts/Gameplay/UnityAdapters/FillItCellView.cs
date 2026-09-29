using System;
using System.Collections;
using CatFlap.Models;
using CatFlap.Views;
using Shared.GameConfig;
using TLC.SpriteAnimation;
using TMPro;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapSpriteCellViewAdapter : MonoBehaviour
    {
        [Header("Core Refs")]
        [SerializeField] private SpriteRenderer background;
        // Animated character (Idle/Happy/Sad via characterAnimator) - shown only for a real,
        // committed (non-note) Cat/Fish value.
        [SerializeField] private SpriteRenderer valueIcon;
        // Static character art - shown only while this cell holds a NOTE Cat/Fish value. Never
        // animated; exactly one of valueIcon/notesIcon is active at a time.
        [SerializeField] private SpriteRenderer notesIcon;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Collider2D touchCollider;

        [Header("Value Icons (Cat / Fish)")]
        [SerializeField] private Sprite fishSprite;
        [SerializeField] private Sprite catSprite;
        [SerializeField] private Sprite noteFishSprite;
        [SerializeField] private Sprite noteCatSprite;

        [Header("Character Mood Animation (Idle/Happy/Sad) - plays only on valueIcon, never notesIcon")]
        // Idle: code owns ALL pacing, not the animator. Each cycle: pick one random clip from the
        // matching array, Play() it, wait exactly clip.Duration (one play-through), explicitly
        // Stop() the animator so its own built-in auto-repeat can never fire, then wait
        // idleMinDelaySeconds..idleMaxDelaySeconds before picking the next clip and repeating. See
        // IdleMoodRoutine. IMPORTANT: idle SpriteAnimationSO assets should have restBetweenPlays
        // set to (0, 0) - that field is unused now that code decides the gap between plays, and a
        // nonzero value would add its own extra (and here, invisible) delay before each play
        // starts, on top of the delay already applied in code.
        // Happy: plays once when this cell's row/column completes (PlayCompletedParticle), for as
        // long as the completed-line particle keeps playing, capped at happyMoodDurationSeconds -
        // never loops on its own, and never gets stuck running if the particle is unassigned or
        // finishes instantly.
        // Sad: plays once whenever this cell is part of a broken rule (PlayRuleBreakBlink), for
        // the same duration as that blink.
        // Any mood change (state change) restarts from scratch - see UpdateCharacterIdleAnimation.
        [SerializeField] private SpriteAnimator characterAnimator;
        [SerializeField] private SpriteAnimationSO[] catIdleAnimations;
        [SerializeField] private SpriteAnimationSO[] catHappyAnimations;
        [SerializeField] private SpriteAnimationSO[] catSadAnimations;
        // Level-win only (see PlayLevelWinCelebration/LevelWinThrilledMoodRoutine) - plays once,
        // then hands off straight to Idle. Distinct from catHappyAnimations, which is the
        // per-line-completion reaction used everywhere else.
        [SerializeField] private SpriteAnimationSO[] catThrilledAnimations;
        [SerializeField] private SpriteAnimationSO[] fishIdleAnimations;
        [SerializeField] private SpriteAnimationSO[] fishHappyAnimations;
        [SerializeField] private SpriteAnimationSO[] fishSadAnimations;
        [SerializeField] private SpriteAnimationSO[] fishThrilledAnimations;
        [SerializeField] private float idleMinDelaySeconds = 1.5f;
        [SerializeField] private float idleMaxDelaySeconds = 4f;
        // Only the very first idle play of a level-start/bulk reveal (playFirstImmediately=false)
        // uses this range instead of idleMinDelaySeconds/idleMaxDelaySeconds above - every play
        // after that first one, and every tap-driven reveal, uses the normal Inspector values.
        [SerializeField] private float levelStartFirstIdleDelayMinSeconds = 2f;
        [SerializeField] private float levelStartFirstIdleDelayMaxSeconds = 5f;
        [SerializeField] private float happyMoodDurationSeconds = 1f;

        [Header("Background Sprites - Empty")]
        [SerializeField] private Sprite emptyBackgroundSprite;

        [Header("Background Sprites - Locked (given at level start OR row/column completed)")]
        [SerializeField] private Sprite catBackgroundSprite;
        [SerializeField] private Sprite fishBackgroundSprite;

        [Header("Background Sprites - Placed (player-placed, not locked yet)")]
        [SerializeField] private Sprite catPlacedBackgroundSprite;
        [SerializeField] private Sprite fishPlacedBackgroundSprite;

        [Header("Background Sprites - Note (cell holds a note value, not committed yet)")]
        [SerializeField] private Sprite noteBackgroundSprite;

        [Header("Result Highlight (Correct / Wrong / Warning)")]
        // Sits above the background, below the hint-only ring. Shows the player whether the
        // committed answer in this cell is currently correct, wrong, or part of a
        // completed-but-mismatched line - see UpdateResultHighlight. Hidden for Normal.
        [SerializeField] private SpriteRenderer resultHighlightRenderer;
        [SerializeField] private Sprite correctHighlightSprite;
        [SerializeField] private Sprite wrongHighlightSprite;
        [SerializeField] private Sprite warningHighlightSprite;
        // Plays once whenever this cell transitions into Completed (correct/green) - not
        // replayed on every SetState call while it stays Completed.
        [SerializeField] private ParticleSystem completedParticle;
        // Plays once when the Hint booster places its target value into this cell - see
        // PlayHintParticle, called from CatFlapGameplayManager.ApplyActiveHint.
        [SerializeField] private ParticleSystem hintParticle;
        // Fallback tint used only when a dedicated sprite for that state (above) isn't assigned.
        [SerializeField] private Color completedColor = new Color(0.35f, 0.85f, 0.4f, 1f);
        [SerializeField] private Color errorColor = new Color(0.95f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.72f, 0.15f, 1f);
        [SerializeField] private Color neutralColor = new Color(1f, 1f, 1f, 1f);

        [Header("Mistake Blink (Result Highlight visibility flicker)")]
        [SerializeField] private float mistakeBlinkDurationSeconds = 0.45f;
        [SerializeField] private int mistakeBlinkCount = 3;

        [Header("Rule Break Blink (scale pulse - not opacity)")]
        // Plays on every cell that belongs to a broken rule, alongside the heart-break FX -
        // distinct from the mistake blink above, which only flickers the tapped cell's highlight.
        [SerializeField] private float ruleBreakBlinkDurationSeconds = 0.45f;
        [SerializeField] private int ruleBreakBlinkCount = 3;
        [SerializeField] private float ruleBreakBlinkScale = 1.15f;

        [Header("Wrong Solution Blink (Result Highlight fade in/out, loops)")]
        // Plays while a line is flagged wrong-sequence (complete, no rule violation, but doesn't
        // match the level's canonical order) - the highlight fades in/out on a loop until
        // StopWarningBlink is called (line edited/fixed). One full in+out cycle takes
        // 2x this value - tweak this to change blink speed (larger = slower).
        [SerializeField] private float warningBlinkStepSeconds = 0.45f;
        [SerializeField] private int warningBlinkCycles = 5;

        [Header("Wrong Solution Shake (one-shot, plays warningShakeCycles times when the blink starts)")]
        [SerializeField] private int warningShakeCycles = 3;
        [SerializeField] private float warningShakeDurationSeconds = 0.3f;
        [SerializeField] private float warningShakeMagnitude = 2f;

        [Header("Hint-Only Highlight (gold ring pulse)")]
        [SerializeField] private SpriteRenderer hintHighlightRenderer;
        [SerializeField] private float hintPulseDurationSeconds = 0.6f;
        [SerializeField] private float hintPulseMinScale = 0.92f;
        [SerializeField] private float hintPulseMaxScale = 1.08f;

        [Header("Icon Change Animation - X-axis card flip")]
        [SerializeField] private float iconChangeAnimationSeconds = 0.16f;
        // X scale at the flip's midpoint, as a fraction of full width - 0 collapses to an edge-on
        // sliver (a true flip) before the new sprite appears and it expands back out.
        [SerializeField] private float iconChangeMinScale = 0f;

        [Header("Post-Flip Character Pop (completed row/column reveal flips only)")]
        // The character icon (not the background/highlight) eases up past full size, holds
        // briefly at the peak, then drops back to full size faster than it rose - only plays
        // after a completed-line reveal's flip (playCompletedParticleOnFinish), never a plain
        // tap. This is the single merged replacement for what used to be two separate effects
        // (a quick background+icon overshoot right after the flip, plus a bigger icon-only pop).
        // Runs independently of the flip itself (see PlayCharacterPostFlipPop) so it never holds
        // up tileFlipAnimation/IsPlayingEntryAnimation, which the completed-line reveal cascade
        // and win-celebration wait loop both key off of.
        [SerializeField] private float characterPostFlipPeakScale = 1.15f;
        [SerializeField] private float characterPostFlipScaleUpDurationSeconds = 0.35f;
        [SerializeField] private float characterPostFlipHoldSeconds = 0.25f;
        [SerializeField] private float characterPostFlipScaleDownDurationSeconds = 0.1f;
        private Coroutine characterPostFlipRoutine;

        [Header("Fallback Text Label Colors (used only when neither icon renderer is assigned)")]
        [SerializeField] private Color catTextColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        [SerializeField] private Color fishTextColor = new Color(1f, 0.6f, 0.15f, 1f);

        private int row;
        private int column;
        private bool isLocked;
        private bool isGiven;
        private CatFlapCellVisualState currentVisualState = CatFlapCellVisualState.Normal;
        private Action<int, int> selected;
        private Coroutine iconAnimation;
        private Coroutine mistakeBlinkRoutine;
        private Coroutine ruleBreakBlinkRoutine;
        private Coroutine warningBlinkRoutine;
        private Coroutine warningShakeRoutine;
        private Vector3 warningBlinkHomeLocalPosition;
        private Coroutine lockedTapShakeRoutine;
        private Coroutine hintPulseRoutine;
        private SpriteRenderer hintPreviewIcon;
        private Sprite currentIconSprite;
        private bool currentIconNote;
        private CatFlapCellValue currentIconValue = CatFlapCellValue.Empty;
        private Vector3 iconFullScale = Vector3.one;
        private Vector3 notesIconFullScale = Vector3.one;
        private Vector3 backgroundFullScale = Vector3.one;
        private Color currentStateColor;
        private Coroutine tileFlipAnimation;

        /// <summary>True while this cell's tap-driven value-change flip (background+icon squash/
        /// expand, see PlayTileFlip/AnimateTileFlip) is still mid-play.</summary>
        public bool IsPlayingEntryAnimation => tileFlipAnimation != null;

        private Coroutine characterMoodRoutine;
        private CatFlapCellValue previousMoodValue = CatFlapCellValue.Empty;
        private bool previousMoodNote;
        private CatFlapCellVisualState previousMoodVisualState = CatFlapCellVisualState.Normal;

        private void Awake()
        {
            if (valueIcon != null)
            {
                iconFullScale = valueIcon.transform.localScale;
            }

            if (notesIcon != null)
            {
                notesIconFullScale = notesIcon.transform.localScale;
            }

            if (background != null)
            {
                backgroundFullScale = background.transform.localScale;
            }

            if (completedParticle == null)
            {
                completedParticle = GetComponentInChildren<ParticleSystem>(true);
            }

            currentStateColor = neutralColor;
        }

        // Safety net for the idle concurrency budget: covers pooled-cell deactivation (board
        // shrinking on a smaller level) and scene teardown, neither of which routes through
        // UpdateCharacterIdleAnimation's own eligible/ineligible transition.
        private void OnDisable()
        {
            CatFlapCharacterIdleThrottle.UnregisterEligible(this);
        }

        public void Bind(int row, int column, Action<int, int> selected)
        {
            this.row = row;
            this.column = column;
            this.selected = selected;
        }

        public void ApplyRulesConfig(GameRulesConfig rulesConfig)
        {
            if (rulesConfig == null)
            {
                return;
            }

            GameRulesConfig config = rulesConfig.CloneNormalized();
            completedColor = config.CellCompletedColor;
            errorColor = config.CellErrorColor;
            warningColor = config.CellWarningColor;
            mistakeBlinkDurationSeconds = config.MistakeBlinkDurationSeconds;
            mistakeBlinkCount = config.MistakeBlinkCount;
        }

        public void SetState(CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor, CatFlapCellVisualState visualState, bool isGiven = false, bool suppressValueChangeAnimation = false, bool forcePlayFlip = false, float speedMultiplier = 1f, bool suppressPostFlipPop = false)
        {
            this.isLocked = isLocked;
            this.isGiven = isGiven;
            // touchCollider is left enabled regardless of isLocked - a tap on a locked cell (a
            // pre-placed clue, or one whose row/column has since completed) still needs to reach
            // TrySelect -> CatFlapGameplayManager.HandleCellSelected, which is what plays the
            // locked-tap sound and (for a given-but-not-yet-green clue cell) the negative
            // shake/haptic/SFX feedback. Disabling the collider here silently swallowed those taps
            // before they ever reached that logic.

            // The background reflects value + locked state, and now also the completed-line
            // green (useLockedColor - see ResolveBackgroundSprite) - the wrong/warning overlay on
            // resultHighlightRenderer below is independent of it, so a cell that's both part of a
            // completed-correct line AND currently part of a broken rule in its other axis (e.g.
            // its row just completed but it's also part of a broken column) shows BOTH: the green
            // base and the red outline on top, instead of the red overlay hiding the green
            // entirely. isLocked is true both for a clue given at level start AND for a cell whose
            // row/column has since completed; a player-placed cell that isn't locked yet uses the
            // separate "placed" sprite.
            Sprite targetBackgroundSprite = ResolveBackgroundSprite(value, isLocked, isNote, useLockedColor);
            Sprite targetIconSprite = ResolveSprite(value, isNote);
            bool valueChanged = targetIconSprite != currentIconSprite || isNote != currentIconNote;
            bool previousIsNote = currentIconNote;

            // UnityEngine.Debug.Log($"CatFlapSpriteCellViewAdapter.SetState: ({row},{column}) value={value}, isNote={isNote}, valueChanged={valueChanged}, forcePlayFlip={forcePlayFlip}, suppressValueChangeAnimation={suppressValueChangeAnimation}, willAnimate={(valueChanged || forcePlayFlip) && !suppressValueChangeAnimation}, gameObject.activeInHierarchy={gameObject.activeInHierarchy}, valueIcon.active={(valueIcon != null ? valueIcon.gameObject.activeSelf : (bool?)null)}, valueIcon.scale={(valueIcon != null ? valueIcon.transform.localScale.ToString() : "null")}, tileFlipRunning={tileFlipAnimation != null}.");

            // forcePlayFlip covers a lock-only transition (a completed row/column turning green) -
            // the icon/value itself doesn't change, so valueChanged alone would take the instant
            // ApplyBackgroundSpriteInstant path below; forcing the flip here reuses the same
            // tap-driven tile-flip animation for that reveal instead. See
            // CatFlapRuntimeController.PlayStaggeredReveal.
            if ((valueChanged || forcePlayFlip) && !suppressValueChangeAnimation)
            {
                // Any actual value/note change stops the mood loop - it's restarted once the
                // change settles (on flip completion), so it never fights the tile flip for
                // ownership of the icon renderers.
                StopCharacterMood();

                // The full tile (background box + character icon) flips together on the X axis -
                // this is the tap-driven change animation. suppressValueChangeAnimation is set by
                // bulk/initial board renders (level load, restart, clear, resume) so those apply
                // instantly instead - see CatFlapRuntimeController.RenderAll.
                currentIconSprite = targetIconSprite;
                currentIconNote = isNote;
                currentIconValue = value;
                PlayTileFlip(targetBackgroundSprite, targetIconSprite, value, isNote, visualState, previousIsNote, forcePlayFlip, speedMultiplier, suppressPostFlipPop);
            }
            else
            {
                currentIconValue = value;

                if (valueChanged)
                {
                    // suppressValueChangeAnimation bulk/instant path - a real value/note change,
                    // just applied without the flip. Still owns the icon renderers outright, so
                    // any leftover in-flight flip from an earlier tap must be stopped first (same
                    // as before) - this genuinely IS a different end state now.
                    StopCharacterMood();

                    if (tileFlipAnimation != null)
                    {
                        StopCoroutine(tileFlipAnimation);
                        tileFlipAnimation = null;
                    }

                    currentIconSprite = targetIconSprite;
                    currentIconNote = isNote;

                    // Exactly one of valueIcon/notesIcon is ever active - the other is fully
                    // hidden.
                    ApplyIconSprite(valueIcon, isNote ? null : targetIconSprite, iconFullScale);
                    ApplyIconSprite(notesIcon, isNote ? targetIconSprite : null, notesIconFullScale);

                    ApplyBackgroundSpriteInstant(targetBackgroundSprite);
                    UpdateCharacterIdleAnimation(value, isNote, visualState, playFirstImmediately: true, useLevelStartFirstDelay: suppressValueChangeAnimation);
                }
                else
                {
                    // Nothing about the value/note actually changed - e.g. a validation-only
                    // refresh that only touches visualState/highlight, or an unrelated cell's
                    // validation timer triggering a board-wide/scoped render that happens to
                    // sweep over this cell too (see CatFlapRuntimeController.RenderLines/
                    // RenderAll). Must not force the icon renderers' sprite/scale back to their
                    // static full scale here, or every such refresh would stomp a mid-flight or
                    // held character animation's own SO-normalised scale (see SpriteAnimator).
                    currentIconSprite = targetIconSprite;
                    currentIconNote = isNote;

                    // A tile flip already in flight right now was started by an earlier, GENUINE
                    // change (a flip only ever starts from valueChanged/forcePlayFlip - see the
                    // branch above) - and since NOTHING has changed since that flip started, its
                    // own target background/icon is necessarily the exact same end state this
                    // passive call would apply anyway. Stopping it here to instantly re-apply
                    // that same end state can only ever cut the animation short early, never
                    // change what it settles on - so leave it completely alone and let it finish
                    // on its own, instead of "gracefully recovering" from an interruption that
                    // serves no purpose (this used to happen unconditionally here, snapping a
                    // cell's own flip short the instant an unrelated render swept over it).
                    if (tileFlipAnimation == null)
                    {
                        ApplyBackgroundSpriteInstant(targetBackgroundSprite);
                        UpdateCharacterIdleAnimation(value, isNote, visualState, playFirstImmediately: false, useLevelStartFirstDelay: suppressValueChangeAnimation);
                    }
                }

                // The flip branch above applies this itself, in sync with the background's own
                // sprite swap/scale (see AnimateTileFlip) - only the instant/passive paths need
                // it here. Always safe even while a flip is in progress - a separate renderer.
                UpdateResultHighlight(visualState);
            }

            if (label != null)
            {
                label.text = valueIcon == null && notesIcon == null ? ResolveLabel(value, isNote) : string.Empty;
            }
        }

        private void ApplyBackgroundSpriteInstant(Sprite sprite)
        {
            if (background == null)
            {
                return;
            }

            background.sprite = sprite;
            background.color = neutralColor;
            // Guards against this being applied while a tile flip is mid-way through squashing
            // the background - e.g. a validation refresh landing between the flip's two halves.
            background.transform.localScale = backgroundFullScale;

            // resultHighlightRenderer is scaled in lockstep with background during AnimateTileFlip
            // (see there) - if this instant path lands while that coroutine was mid-collapse (it's
            // stopped just before this call in SetState), the highlight would otherwise be left
            // stuck squashed to a thin sliver instead of snapping back to full size with the
            // background it's guarding against above.
            if (resultHighlightRenderer != null)
            {
                resultHighlightRenderer.transform.localScale = backgroundFullScale*1.07f;
            }
        }

        public void SetIndicatorState(int catCount, int fishCount, bool isCompleted = false)
        {
            isLocked = true;
            if (touchCollider != null)
            {
                touchCollider.enabled = false;
            }

            currentStateColor = isCompleted ? completedColor : neutralColor;

            if (background != null)
            {
                background.sprite = ResolveBackgroundSprite(CatFlapCellValue.Empty, true, false);
                background.color = currentStateColor;
            }

            if (label != null)
            {
                Color catColor = catTextColor;
                Color fishColor = fishTextColor;
                label.richText = true;
                label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(catColor)}>{catCount}</color> <color=#{ColorUtility.ToHtmlStringRGB(fishColor)}>{fishCount}</color>";
            }

            if (valueIcon != null)
            {
                valueIcon.gameObject.SetActive(false);
            }

            if (notesIcon != null)
            {
                notesIcon.gameObject.SetActive(false);
            }
        }

        public bool IsGiven => isGiven;

        /// <summary>
        /// Hides this cell's value icon and result highlight (background/box shape is left alone)
        /// so the board's enter animation can reveal them later, in stages, instead of them
        /// popping in the instant SetState first applied them.
        /// </summary>
        public void PrepareForEnterAnimation()
        {
            // A stray reveal or tile-flip animation left running would re-activate an icon (and
            // squash background) a few frames later regardless of the resets below, making the
            // icon flash visible mid-wave and then pop again when RevealValue() runs. Must be
            // stopped, not just hidden.
            if (iconAnimation != null)
            {
                StopCoroutine(iconAnimation);
                iconAnimation = null;
            }

            if (tileFlipAnimation != null)
            {
                StopCoroutine(tileFlipAnimation);
                tileFlipAnimation = null;
            }

            StopCharacterMood();

            if (background != null)
            {
                background.transform.localScale = backgroundFullScale;
            }

            if (valueIcon != null)
            {
                valueIcon.transform.localScale = iconFullScale;
                valueIcon.gameObject.SetActive(false);
            }

            if (notesIcon != null)
            {
                notesIcon.transform.localScale = notesIconFullScale;
                notesIcon.gameObject.SetActive(false);
            }

            if (resultHighlightRenderer != null)
            {
                resultHighlightRenderer.gameObject.SetActive(false);
                // A stopped-mid-flight tile flip (see AnimateTileFlip) can leave this squashed to a
                // sliver - reset it so RevealResultHighlight() doesn't later reactivate it stuck at
                // that scale instead of full size.
                resultHighlightRenderer.transform.localScale = backgroundFullScale * 1.07f;
            }
        }

        /// <summary>Re-plays the existing icon-change pop animation for whatever value this cell already holds.</summary>
        public void RevealValue()
        {
            if (currentIconSprite != null)
            {
                PlayIconChange(currentIconSprite);
            }
        }

        /// <summary>Restores the result highlight (wrong/warning only - never Completed/Normal) computed by the last SetState call.</summary>
        public void RevealResultHighlight()
        {
            if (resultHighlightRenderer != null)
            {
                // Must match UpdateResultHighlight's own "visible" gate exactly - Completed is
                // also != Normal, so using that comparison here (as before) wrongly turned the
                // red/orange wrong-solution overlay back on for every completed (green) cell once
                // the level-enter reveal wave reached it.
                resultHighlightRenderer.gameObject.SetActive(
                    currentVisualState == CatFlapCellVisualState.Error ||
                    currentVisualState == CatFlapCellVisualState.Warning);
            }
        }

        /// <summary>
        /// Plays the completed-line particle once, and (if this cell holds a real, non-note
        /// Cat/Fish) the Happy mood for exactly as long as that particle is playing. Called
        /// explicitly by the controller only when this cell's row or column just newly validated
        /// as complete/correct - never inferred from visual-state changes, so it can't misfire on
        /// a wrong-to-correct fix that doesn't complete a line, or on level load/resume re-renders.
        /// </summary>
        public void PlayCompletedParticle()
        {
            if (completedParticle != null)
            {
                completedParticle.Play(true);
            }

            PlayHappyMood();
        }

        /// <summary>
        /// Plays once when the Hint booster places its target value into this cell (see
        /// CatFlapGameplayManager.ApplyActiveHint). Purely a VFX cue - unlike
        /// PlayCompletedParticle/PlayLevelWinCelebration, it doesn't touch the character mood.
        /// </summary>
        public void PlayHintParticle()
        {
            if (hintParticle != null)
            {
                hintParticle.Play(true);
            }
        }

        /// <summary>
        /// Level-win celebration beat: same particle as <see cref="PlayCompletedParticle"/>, but
        /// plays the dedicated Thrilled mood once (see PlayLevelWinThrilledMood) instead of Happy,
        /// and forces a clean restart of the character animator first rather than relying on
        /// SpriteAnimator.Play()'s own early-return checks. By the time the board wins, most
        /// cells' Happy mood already ran once (for their own row/column) and ended paused/frozen
        /// on that reaction's last frame - see StopCharacterMood's preserveScale path - and
        /// re-Play()ing on top of that paused state can silently no-op instead of visibly
        /// restarting. An explicit Stop() first guarantees this always actually plays.
        /// </summary>
        public void PlayLevelWinCelebration(bool playParticle = true)
        {
            if (playParticle && completedParticle != null)
            {
                completedParticle.Play(true);
            }

            if (!CanAnimateCharacter(currentIconValue, currentIconNote))
            {
                return;
            }

            if (characterMoodRoutine != null)
            {
                StopCoroutine(characterMoodRoutine);
                characterMoodRoutine = null;
            }

            if (characterAnimator != null)
            {
                characterAnimator.Stop();
            }

            PlayLevelWinThrilledMood();
        }

        private Coroutine levelWinPulseRoutine;

        /// <summary>True while this cell's level-win background pulse (see PlayLevelWinPulse) is
        /// still mid-play - lets the board/manager wait for the whole win-pulse wave to finish
        /// before showing the win popup.</summary>
        public bool IsPlayingLevelWinPulse => levelWinPulseRoutine != null;

        /// <summary>
        /// Level-win board flourish (see CatFlapSpriteBoardViewAdapter.PlayLevelWinBoardAnimation):
        /// pulses ONLY this cell's background sprite up past its normal size then eases back down -
        /// deliberately never touches valueIcon/characterAnimator, since PlayLevelWinCelebration
        /// plays the character's own Happy mood loop at the same time, and scaling the character on
        /// top of its own animation visibly fought with it.
        /// </summary>
        public void PlayLevelWinPulse(float delay, float duration, float peakScaleMultiplier)
        {
            if (background == null)
            {
                return;
            }

            if (levelWinPulseRoutine != null)
            {
                StopCoroutine(levelWinPulseRoutine);
            }

            levelWinPulseRoutine = StartCoroutine(AnimateLevelWinPulse(delay, duration, peakScaleMultiplier));
        }

        private IEnumerator AnimateLevelWinPulse(float delay, float duration, float peakScaleMultiplier)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            Vector3 peakScale = backgroundFullScale * Mathf.Max(1f, peakScaleMultiplier);
            float totalSeconds = Mathf.Max(0.01f, duration);
            float growSeconds = totalSeconds * 0.4f;
            float settleSeconds = totalSeconds - growSeconds;

            float elapsed = 0f;
            while (elapsed < growSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutCubicForPulse(Mathf.Clamp01(elapsed / growSeconds));
                background.transform.localScale = Vector3.LerpUnclamped(backgroundFullScale, peakScale, t);
                yield return null;
            }

            background.transform.localScale = peakScale;

            elapsed = 0f;
            while (elapsed < settleSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutBackForPulse(Mathf.Clamp01(elapsed / settleSeconds));
                background.transform.localScale = Vector3.LerpUnclamped(peakScale, backgroundFullScale, t);
                yield return null;
            }

            background.transform.localScale = backgroundFullScale;
            levelWinPulseRoutine = null;
        }

        private static float EaseOutCubicForPulse(float t)
        {
            float x = 1f - t;
            return 1f - x * x * x;
        }

        private static float EaseOutBackForPulse(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }

        // Always invokes, regardless of isLocked - CatFlapGameplayManager.HandleCellSelected
        // (via CatFlapRuntimeController.SelectCell, which already returns moveChanged=false for a
        // locked cell without mutating the board) is what decides what a locked-cell tap means
        // (locked-tap sound, plus negative shake/haptic/SFX for a still-wrong given clue cell) -
        // this method must not pre-emptively swallow the tap before that logic ever sees it.
        public void TrySelect()
        {
            selected?.Invoke(row, column);
        }

        public void PlayMistakeBlink()
        {
            if (resultHighlightRenderer == null)
            {
                return;
            }

            if (mistakeBlinkRoutine != null)
            {
                StopCoroutine(mistakeBlinkRoutine);
            }

            mistakeBlinkRoutine = StartCoroutine(AnimateMistakeBlink());
        }

        /// <summary>
        /// Pulses this cell's background + icon scale up and down a few times - the "which rule is
        /// broken" tell, played on every cell belonging to a broken rule (see
        /// CatFlapRuntimeController.ResolveMistake/ApplyAllNotes -> boardView.PlayRuleBreakBlink).
        /// Deliberately a scale pulse, not an opacity flicker - keep it distinct from
        /// PlayMistakeBlink. Also plays the Sad mood for the same duration, if this cell holds a
        /// real, non-note Cat/Fish.
        /// </summary>
        public void PlayRuleBreakBlink()
        {
            if (ruleBreakBlinkRoutine != null)
            {
                StopCoroutine(ruleBreakBlinkRoutine);
            }

            if (tileFlipAnimation != null)
            {
            }

            ruleBreakBlinkRoutine = StartCoroutine(AnimateRuleBreakBlink());
            PlaySadMood();
        }

        /// <summary>
        /// Starts an indefinite fade in/out on the amber highlight for this cell's row/column,
        /// newly flagged "wrong solution" (complete, no rule violation, but doesn't match the
        /// level's canonical order). When playShake is true, also plays a one-shot shake right as
        /// it starts (not repeated on every fade cycle) - pass false for a silent load/resume
        /// refresh so the shake doesn't replay for a line that was already wrong before this
        /// session started. The fade keeps looping regardless, until StopWarningBlink is called
        /// (when the line is edited/fixed - see CatFlapRuntimeController.RemoveWarningLine).
        /// Restarting is safe/idempotent.
        /// </summary>
        public void PlayWarningBlink(bool playShake = true)
        {
            if (resultHighlightRenderer == null)
            {
                return;
            }

            if (warningBlinkRoutine == null && warningShakeRoutine == null)
            {
                // Only captured when nothing is already running - the cell is guaranteed to be at
                // rest at that point (both routines always return to home when they finish/stop),
                // so this is always the correct position to return to.
                warningBlinkHomeLocalPosition = transform.localPosition;
            }

            if (warningBlinkRoutine != null)
            {
                StopCoroutine(warningBlinkRoutine);
            }

            warningBlinkRoutine = StartCoroutine(AnimateWarningBlink());

            if (playShake)
            {
                if (warningShakeRoutine != null)
                {
                    StopCoroutine(warningShakeRoutine);
                }

                warningShakeRoutine = StartCoroutine(AnimateWarningShake());
            }
        }

        /// <summary>
        /// Ends an in-progress PlayWarningBlink (and its one-shot shake, if still playing), leaving
        /// the highlight fully visible and the cell back at its home position. Safe to call even if
        /// nothing is currently blinking.
        /// </summary>
        public void StopWarningBlink()
        {
            if (warningBlinkRoutine == null && warningShakeRoutine == null)
            {
                return;
            }

            if (warningBlinkRoutine != null)
            {
                StopCoroutine(warningBlinkRoutine);
                warningBlinkRoutine = null;
            }

            if (warningShakeRoutine != null)
            {
                StopCoroutine(warningShakeRoutine);
                warningShakeRoutine = null;
            }

            if (resultHighlightRenderer != null)
            {
                Color fullColor = resultHighlightRenderer.color;
                fullColor.a = 1f;
                resultHighlightRenderer.color = fullColor;
                resultHighlightRenderer.enabled = true;
            }

            transform.localPosition = warningBlinkHomeLocalPosition;
        }

        /// <summary>
        /// One-shot shake (same visual technique as the Wrong Solution shake, but always exactly
        /// one cycle regardless of warningShakeCycles) - played when the player taps a locked
        /// pre-placed/clue cell that isn't part of a completed line yet (see
        /// CatFlapGameplayManager.HandleCellSelected). Independent of warningShakeRoutine so it
        /// can't interfere with an in-progress Wrong Solution blink/shake on this same cell.
        /// </summary>
        public void PlayLockedTapShake()
        {
            if (warningShakeRoutine != null)
            {
                StopCoroutine(warningShakeRoutine);
                warningShakeRoutine = null;
                transform.localPosition = warningBlinkHomeLocalPosition;
            }

            if (lockedTapShakeRoutine != null)
            {
                StopCoroutine(lockedTapShakeRoutine);
            }

            lockedTapShakeRoutine = StartCoroutine(AnimateLockedTapShake());
        }

        private IEnumerator AnimateLockedTapShake()
        {
            Vector3 home = transform.localPosition;
            // Same full right -> center -> left -> center oscillation (sin over 2*PI) and 3x speed
            // as AnimateWarningShake, for the same "actually reads as a shake" reason.
            float duration = Mathf.Max(0.01f, warningShakeDurationSeconds) / 3f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float shakeT = Mathf.Sin(t * Mathf.PI * 2f);
                transform.localPosition = home + new Vector3(warningShakeMagnitude * shakeT, 0f, 0f);
                yield return null;
            }

            transform.localPosition = home;
            lockedTapShakeRoutine = null;
        }

        /// <summary>
        /// Toggles the hint-only highlight ring on/off (see <c>HintHighlight</c> child added by
        /// the "Add Hint Highlight To Cell Prefab" editor utility). Never mutates the cell's
        /// value/state - this is purely a visual pointer telling the player which cell to tap.
        /// </summary>
        public void SetHintHighlighted(bool active)
        {
            if (hintHighlightRenderer == null)
            {
                return;
            }

            if (!active)
            {
                if (hintPulseRoutine != null)
                {
                    StopCoroutine(hintPulseRoutine);
                    hintPulseRoutine = null;
                }

                // Matches the full-cell scale the orange Warning highlight (resultHighlightRenderer)
                // uses, not a plain Vector3.one - the hint ring's own authored sprite scale reads
                // much smaller than the cell otherwise.
                hintHighlightRenderer.transform.localScale = backgroundFullScale;
                hintHighlightRenderer.gameObject.SetActive(false);
                return;
            }

            hintHighlightRenderer.gameObject.SetActive(true);
            if (hintPulseRoutine == null)
            {
                hintPulseRoutine = StartCoroutine(AnimateHintPulse());
            }
        }

        /// <summary>
        /// Shows a note-style preview icon (same sprite/scale ResolveSprite/notesIconFullScale
        /// would use for a real note) of the character that belongs in this cell, on a dedicated
        /// renderer created lazily at runtime - deliberately NOT routed through SetState/the real
        /// notesIcon renderer, so it can never be stomped by or conflict with the cell's actual
        /// model value/note (which SetState re-derives from CatFlapCellModel on every render,
        /// regardless of what this preview is showing). ClearHintPreview hides it again.
        /// </summary>
        public void SetHintPreview(CatFlapCellValue value)
        {
            EnsureHintPreviewIcon();
            if (hintPreviewIcon == null)
            {
                return;
            }

            Sprite sprite = ResolveSprite(value, isNote: true);
            hintPreviewIcon.sprite = sprite;
            hintPreviewIcon.color = Color.white;
            hintPreviewIcon.transform.localScale = notesIconFullScale;
            hintPreviewIcon.gameObject.SetActive(sprite != null);
        }

        public void ClearHintPreview()
        {
            if (hintPreviewIcon != null)
            {
                hintPreviewIcon.gameObject.SetActive(false);
            }
        }

        private void EnsureHintPreviewIcon()
        {
            if (hintPreviewIcon != null)
            {
                return;
            }

            SpriteRenderer sourceRenderer = notesIcon != null ? notesIcon : valueIcon;
            if (sourceRenderer == null)
            {
                return;
            }

            var previewObject = new GameObject("HintPreviewIcon", typeof(SpriteRenderer));
            previewObject.transform.SetParent(sourceRenderer.transform.parent, false);
            previewObject.transform.localPosition = sourceRenderer.transform.localPosition;
            previewObject.transform.localRotation = sourceRenderer.transform.localRotation;

            hintPreviewIcon = previewObject.GetComponent<SpriteRenderer>();
            hintPreviewIcon.sortingLayerID = sourceRenderer.sortingLayerID;
            // Just above the notes/value icon layer, so it's never hidden behind whatever the cell
            // is actually showing.
            hintPreviewIcon.sortingOrder = sourceRenderer.sortingOrder + 1;
            previewObject.SetActive(true);
            hintPreviewIcon.gameObject.SetActive(false);
        }

        private IEnumerator AnimateHintPulse()
        {
            float halfDuration = Mathf.Max(0.01f, hintPulseDurationSeconds * 0.5f);
            // Pulses around the same full-cell base scale as the orange Warning highlight, not
            // Vector3.one - see SetHintHighlighted.
            Vector3 minScale = backgroundFullScale * hintPulseMinScale;
            Vector3 maxScale = backgroundFullScale * hintPulseMaxScale;
            while (true)
            {
                for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
                {
                    float t = Mathf.Clamp01(elapsed / halfDuration);
                    hintHighlightRenderer.transform.localScale = Vector3.Lerp(minScale, maxScale, t);
                    yield return null;
                }

                for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
                {
                    float t = Mathf.Clamp01(elapsed / halfDuration);
                    hintHighlightRenderer.transform.localScale = Vector3.Lerp(maxScale, minScale, t);
                    yield return null;
                }
            }
        }

        // Blinks by toggling the highlight sprite's visibility on/off - no color animation
        // involved. The sprite/color set by UpdateResultHighlight just before this runs (see
        // ResolveMistake -> RenderAll -> PlayMistakeBlink) is left untouched; this only flickers
        // whether that sprite is currently showing.
        private IEnumerator AnimateMistakeBlink()
        {
            int blinkCount = Mathf.Max(1, mistakeBlinkCount);
            float stepDuration = Mathf.Max(0.01f, mistakeBlinkDurationSeconds / (blinkCount * 2f));
            for (int index = 0; index < blinkCount; index++)
            {
                resultHighlightRenderer.enabled = false;
                yield return new WaitForSeconds(stepDuration);
                resultHighlightRenderer.enabled = true;
                yield return new WaitForSeconds(stepDuration);
            }

            resultHighlightRenderer.enabled = true;
            mistakeBlinkRoutine = null;
        }

        /// <summary>True while the Wrong Solution blink (see PlayWarningBlink) is still actively running on this cell.</summary>
        public bool IsWarningBlinking => warningBlinkRoutine != null;

        /// <summary>True while this cell's rule-break pulse (see PlayRuleBreakBlink) is still actively running.</summary>
        public bool IsRuleBreakBlinking => ruleBreakBlinkRoutine != null;

        // Fades resultHighlightRenderer's alpha between its full (already-applied Warning) color
        // and transparent, warningBlinkCycles times (a smooth fade, unlike AnimateMistakeBlink's
        // hard on/off toggle), then leaves it fully visible and stops on its own - unlike before,
        // this no longer runs forever. Ended EARLY instead, before it naturally finishes, only by
        // StopWarningBlink (when this cell's line is edited/fixed). The shake (see
        // AnimateWarningShake) is a separate one-shot that plays once at the start, not on every
        // fade cycle.
        private IEnumerator AnimateWarningBlink()
        {
            resultHighlightRenderer.enabled = true;
            Color fullColor = resultHighlightRenderer.color;
            fullColor.a = 1f;
            Color fadedColor = fullColor;
            fadedColor.a = 0f;

            float stepDuration = Mathf.Max(0.01f, warningBlinkStepSeconds);
            int cycles = Mathf.Max(1, warningBlinkCycles);

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                yield return AnimateFadeStep(fullColor, fadedColor, stepDuration);
                yield return AnimateFadeStep(fadedColor, fullColor, stepDuration);
            }

            resultHighlightRenderer.color = fullColor;
            resultHighlightRenderer.enabled = true;
            warningBlinkRoutine = null;
        }

        private IEnumerator AnimateFadeStep(Color fromColor, Color toColor, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                resultHighlightRenderer.color = Color.Lerp(fromColor, toColor, t);
                yield return null;
            }

            resultHighlightRenderer.color = toColor;
        }

        // One-shot shake, played once when the wrong-solution blink starts (not repeated on every
        // fade cycle) - rises from and returns to zero offset (sine, not linear) so the cell always
        // ends back exactly at its home position.
        private IEnumerator AnimateWarningShake()
        {
            Vector3 home = warningBlinkHomeLocalPosition;
            // 3x faster than a plain reading of warningShakeDurationSeconds. Each cycle is a full
            // right -> center -> left -> center oscillation (sin over a full 2*PI, not just PI) -
            // not just a one-directional bump - so a single cycle alone already reads as an actual
            // shake.
            float duration = Mathf.Max(0.01f, warningShakeDurationSeconds) / 3f;
            int cycles = Mathf.Max(1, warningShakeCycles);

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
                {
                    float t = Mathf.Clamp01(elapsed / duration);
                    float shakeT = Mathf.Sin(t * Mathf.PI * 2f);
                    transform.localPosition = home + new Vector3(warningShakeMagnitude * shakeT, 0f, 0f);
                    yield return null;
                }
            }

            transform.localPosition = home;
            warningShakeRoutine = null;
        }

        // Scales background + icon up and down together a few times, always returning to their
        // full/authored scale - a pulse, not a fade. Distinct from AnimateMistakeBlink above.
        private IEnumerator AnimateRuleBreakBlink()
        {
            int blinkCount = Mathf.Max(1, ruleBreakBlinkCount);
            float halfStepDuration = Mathf.Max(0.01f, ruleBreakBlinkDurationSeconds / (blinkCount * 2f));
            SpriteRenderer icon = ResolveIconRenderer(currentIconNote);
            Vector3 iconBaseScale = ResolveIconFullScale(currentIconNote);
            Vector3 iconPulseScale = iconBaseScale * ruleBreakBlinkScale;
            Vector3 backgroundPulseScale = backgroundFullScale * ruleBreakBlinkScale;

            for (int index = 0; index < blinkCount; index++)
            {
                yield return ScaleBoth(icon, iconBaseScale, iconPulseScale, backgroundPulseScale, halfStepDuration);
                yield return ScaleBoth(icon, iconPulseScale, iconBaseScale, backgroundFullScale, halfStepDuration);
            }

            if (icon != null)
            {
                icon.transform.localScale = iconBaseScale;
            }

            if (background != null)
            {
                background.transform.localScale = backgroundFullScale;
            }

            if (tileFlipAnimation != null || icon != ResolveIconRenderer(currentIconNote))
            {
            }

            ruleBreakBlinkRoutine = null;
        }

        private IEnumerator ScaleBoth(SpriteRenderer icon, Vector3 iconFrom, Vector3 iconTo, Vector3 backgroundTo, float duration)
        {
            Vector3 backgroundFrom = background != null ? background.transform.localScale : Vector3.one;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                if (icon != null)
                {
                    icon.transform.localScale = Vector3.Lerp(iconFrom, iconTo, t);
                }

                if (background != null)
                {
                    background.transform.localScale = Vector3.Lerp(backgroundFrom, backgroundTo, t);
                }

                yield return null;
            }
        }

        // Drives the wrong/mismatched-line outline overlay from the cell's validated state - Error
        // (wrong), Warning (complete line that doesn't match the solution but isn't a rule
        // violation). Hidden for Normal AND Completed - the green "correct" look is now the
        // BACKGROUND's own sprite (see ResolveBackgroundSprite/useLockedColor) so it keeps showing
        // underneath this overlay instead of being replaced by it; this renderer only ever adds
        // the red/orange outline on top. Never touches the background itself. Prefers a dedicated
        // sprite per state (assigned in the Inspector); falls back to tinting the
        // currently-assigned sprite with a plain color only if no dedicated sprite has been set
        // for that state yet.
        private void UpdateResultHighlight(CatFlapCellVisualState visualState)
        {
            currentVisualState = visualState;

            if (resultHighlightRenderer == null)
            {
                return;
            }

            Sprite sprite;
            Color fallbackTint;
            switch (visualState)
            {
                case CatFlapCellVisualState.Error:
                    sprite = wrongHighlightSprite;
                    fallbackTint = errorColor;
                    break;
                case CatFlapCellVisualState.Warning:
                    sprite = warningHighlightSprite;
                    fallbackTint = warningColor;
                    break;
                default:
                    sprite = null;
                    fallbackTint = neutralColor;
                    break;
            }

            bool visible = visualState == CatFlapCellVisualState.Error || visualState == CatFlapCellVisualState.Warning;
            if (sprite != null)
            {
                resultHighlightRenderer.sprite = sprite;
                resultHighlightRenderer.color = Color.white;
            }
            else if (visible)
            {
                resultHighlightRenderer.color = fallbackTint;
            }

            resultHighlightRenderer.gameObject.SetActive(visible);
        }

        private Sprite ResolveBackgroundSprite(CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor = false)
        {
            if (isNote && value != CatFlapCellValue.Empty && noteBackgroundSprite != null)
            {
                return noteBackgroundSprite;
            }

            // useLockedColor (see CatFlapRuntimeController.VisualState.ShouldUseLockedColor) means
            // this cell is a given clue or part of a validated-complete row/column - show the
            // green "complete" sprite as the base image itself rather than as an overlay, so a
            // separate red wrong/warning overlay (resultHighlightRenderer) can still show on top
            // of it for a cell that's complete on one axis but currently broken on the other.
            if (useLockedColor && correctHighlightSprite != null)
            {
                return correctHighlightSprite;
            }

            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return !isLocked && fishPlacedBackgroundSprite != null ? fishPlacedBackgroundSprite : fishBackgroundSprite;
                case CatFlapCellValue.Cat:
                    return !isLocked && catPlacedBackgroundSprite != null ? catPlacedBackgroundSprite : catBackgroundSprite;
                default:
                    return emptyBackgroundSprite;
            }
        }

        private Sprite ResolveSprite(CatFlapCellValue value, bool isNote)
        {
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return isNote && noteFishSprite != null ? noteFishSprite : fishSprite;
                case CatFlapCellValue.Cat:
                    return isNote && noteCatSprite != null ? noteCatSprite : catSprite;
                default:
                    return null;
            }
        }

        private SpriteRenderer ResolveIconRenderer(bool isNote)
        {
            return isNote ? notesIcon : valueIcon;
        }

        private Vector3 ResolveIconFullScale(bool isNote)
        {
            return isNote ? notesIconFullScale : iconFullScale;
        }

        // ---------- Character mood (Idle / Happy / Sad) ----------

        // Eligible only for a real (non-note) Cat/Fish value, with both the animator and its
        // renderer assigned - notesIcon never animates, and note cells never animate at all.
        private bool CanAnimateCharacter(CatFlapCellValue value, bool isNote)
        {
            return !isNote &&
                (value == CatFlapCellValue.Cat || value == CatFlapCellValue.Fish) &&
                valueIcon != null &&
                characterAnimator != null &&
                gameObject.activeInHierarchy;
        }

        // Starts/stops the Idle loop - eligible only while a real (non-note) cat/fish sits in this
        // cell AND its visual state is Normal, Completed, OR Warning. Error cells still just rest
        // on their static sprite - Sad's own flow is unchanged there: once Sad has played for an
        // Error cell, Idle stays off until the cell is back to Normal/Completed. Completed and
        // Warning cells ARE allowed to idle (unlike Error) - Completed so Happy hands back to Idle
        // instead of freezing forever once it finishes, and Warning so the Wrong Solution blink
        // (see PlayWarningBlink, which only ever touches resultHighlightRenderer/transform, never
        // the character) doesn't leave the character frozen mid-idle for the whole time it plays.
        // Any actual change in value/note/visual-state restarts Idle from scratch (resets its
        // random-delay timer); a redundant call with no real change leaves an already-running mood
        // alone.
        //
        // playFirstImmediately controls whether the FIRST play of this fresh Idle entry skips the
        // usual delay wait entirely:
        //   true  - a tap just placed/revealed this character (AnimateTileFlip) - play one idle
        //           animation immediately as it appears, then hand off to normal delayed pacing.
        //   false - everything else - waits before the first play too, so nothing animates the
        //           instant this mood starts.
        //
        // useLevelStartFirstDelay (only meaningful when playFirstImmediately is false) picks which
        // range that first wait uses:
        //   true  - the level-start/board-enter reveal (RevealValue -> PlayIconChange/
        //           AnimateIconReveal) or a bulk/suppressed board render (SetState with
        //           suppressValueChangeAnimation) - uses the fixed levelStartFirstIdleDelay
        //           Min/MaxSeconds range, not the Inspector idleMinDelaySeconds/idleMaxDelaySeconds.
        //   false - e.g. handing back to Idle after Happy/Sad ends - uses the normal Inspector
        //           idleMinDelaySeconds/idleMaxDelaySeconds range, same as every later play.
        // Used by the reveal/completed-particle path when it deliberately skips calling
        // UpdateCharacterIdleAnimation (Happy is about to play instead of Idle) - without this, the
        // previousMood* fields would stay stale at their pre-reveal value, so the very next
        // redundant SetState/RenderAll pass (same value/isNote/visualState, nothing actually
        // changed for the player) would compute stateChanged == true purely from that staleness,
        // bypass the "still mid-play, nothing changed" guard, and rip the just-started Happy
        // routine out from under itself to force-restart Idle instead - visibly cutting Happy short
        // and making it look faster/inconsistent.
        private void RecordMoodState(CatFlapCellValue value, bool isNote, CatFlapCellVisualState visualState)
        {
            previousMoodValue = value;
            previousMoodNote = isNote;
            previousMoodVisualState = visualState;
        }

        // bypassIdleThrottle: true skips CatFlapCharacterIdleThrottle's board-wide wave gating
        // entirely for THIS cell's whole Idle run (every play, not just the first) - used for the
        // level-win handoff (see LevelWinThrilledMoodRoutine) so every character on the board keeps
        // idling continuously instead of only ~20-25% of them at a time waiting their turn. Purely
        // a per-call parameter, not persisted state - the very next ordinary
        // UpdateCharacterIdleAnimation call (e.g. the next level load) defaults back to false and
        // restores normal wave-gated pacing automatically.
        private void UpdateCharacterIdleAnimation(CatFlapCellValue value, bool isNote, CatFlapCellVisualState visualState, bool playFirstImmediately = false, bool useLevelStartFirstDelay = false, bool preserveScaleIfStopping = false, bool bypassIdleThrottle = false)
        {
            bool eligible = CanAnimateCharacter(value, isNote) &&
                (visualState == CatFlapCellVisualState.Normal ||
                 visualState == CatFlapCellVisualState.Completed ||
                 visualState == CatFlapCellVisualState.Warning);

            bool stateChanged = value != previousMoodValue || isNote != previousMoodNote || visualState != previousMoodVisualState;
            previousMoodValue = value;
            previousMoodNote = isNote;
            previousMoodVisualState = visualState;

            if (!eligible)
            {
                CatFlapCharacterIdleThrottle.UnregisterEligible(this);

                // A rule-broken cell (Error) - as opposed to simply not being a real character at
                // all - reacts Sad once on the transition into Error (level load/resume onto an
                // already-broken save, or a live mistake), then holds on Sad's last frame rather
                // than repeating - SadMoodRoutine's own hand-back at the end of that one play finds
                // stateChanged false here and falls through without restarting it.
                bool isRuleBreak = visualState == CatFlapCellVisualState.Error && CanAnimateCharacter(value, isNote);

                // Only actually touch the animator when something genuinely changed for THIS
                // cell. A redundant call (e.g. a board-wide re-render triggered by tapping a
                // different cell) must never stop it just because a mood routine happens to still
                // be running - that would cut off a still-playing Happy/Sad reaction (or an
                // already-paused, already-frozen animator's scale) every time any other cell is
                // tapped. A routine that's actually still running belongs to a value/note/
                // visualState combo that hasn't changed since it started (stateChanged would be
                // false for it too), so it's left to finish and clean itself up naturally
                // (HappyMoodRoutine/SadMoodRoutine call back into this method when they end).
                if (stateChanged)
                {
                    StopCharacterMood(preserveScaleIfStopping);
                    if (isRuleBreak)
                    {
                        PlaySadMood();
                    }
                }

                return;
            }

            CatFlapCharacterIdleThrottle.RegisterEligible(this);

            if (characterMoodRoutine != null && !stateChanged)
            {
                return;
            }

            // IdleMoodRoutine waits its own random idleMinDelaySeconds/idleMaxDelaySeconds before
            // its first Play()/Stop() ever touches the renderer - without this, a cell coming off
            // a held Sad/Happy last frame (e.g. a rule violation that just got fixed elsewhere in
            // the same row/column) would keep visibly showing that mood for the whole idle delay
            // instead of returning to its neutral sprite the instant the state actually changed.
            if (stateChanged && valueIcon != null)
            {
                valueIcon.sprite = currentIconSprite;
            }

            StartCharacterMoodRoutine(IdleMoodRoutine(value, playFirstImmediately, useLevelStartFirstDelay, bypassIdleThrottle));
        }

        private const float IdleSlotRetryMinSeconds = 0.2f;
        private const float IdleSlotRetryMaxSeconds = 0.5f;

        // preserveScale keeps the animator frozen on whatever sprite/scale it was last showing
        // instead of resetting to the pre-animation base scale - used when a Happy/Sad reaction
        // ends on a terminal (Completed/Error/Warning) state, so its final frame keeps rendering
        // at the SO's own fitted scale rather than snapping back to the plain icon's scale.
        private void StopCharacterMood(bool preserveScale = false)
        {
            if (characterMoodRoutine != null)
            {
                // StopCoroutine kills the routine mid-yield with no cleanup, so a slot it was
                // holding (or waiting to acquire) must be released explicitly here - otherwise an
                // interrupted Idle play would leak its slot forever.
                StopCoroutine(characterMoodRoutine);
                characterMoodRoutine = null;
            }

            CatFlapCharacterIdleThrottle.ReleaseFromWave(this, idleMinDelaySeconds, idleMaxDelaySeconds);

            if (characterAnimator != null)
            {
                if (preserveScale)
                {
                    characterAnimator.Pause();
                }
                else
                {
                    characterAnimator.Stop();
                }
            }
        }

        private void StartCharacterMoodRoutine(IEnumerator routine)
        {
            if (characterMoodRoutine != null)
            {
                // Same reasoning as StopCharacterMood - StopCoroutine kills the routine mid-yield
                // with no cleanup, so a slot it was holding (or waiting to acquire) must be
                // released explicitly here, otherwise an interrupted Idle play (e.g. Happy
                // pre-empting it) leaks its slot forever.
                StopCoroutine(characterMoodRoutine);
                CatFlapCharacterIdleThrottle.ReleaseFromWave(this, idleMinDelaySeconds, idleMaxDelaySeconds);
            }

            characterMoodRoutine = StartCoroutine(routine);
        }

        // The board plays idle in WAVES (see CatFlapCharacterIdleThrottle): a random subset of
        // eligible cells plays together, then the whole board goes quiet for a shared random
        // delay before the next wave is picked - never each cell independently rolling its own
        // delay and playing whenever it happens to be ready. playFirstImmediately (a tap-driven
        // reveal) is the one exception - it always plays right away, bypassing wave gating
        // entirely for that first play, so placing a character never waits on an unrelated
        // ambient wave for its own immediate confirmatory play; it falls into the normal
        // wave-following cadence below afterwards.
        private IEnumerator IdleMoodRoutine(CatFlapCellValue value, bool playFirstImmediately, bool useLevelStartFirstDelay, bool bypassIdleThrottle = false)
        {
            SpriteAnimationSO[] clips = value == CatFlapCellValue.Cat ? catIdleAnimations : fishIdleAnimations;
            characterAnimator.SetSizeReference(currentIconSprite);

            if (playFirstImmediately)
            {
                yield return PlayOneIdleClip(clips);
            }
            else
            {
                float minDelay;
                float maxDelay;
                if (useLevelStartFirstDelay)
                {
                    // Level-start/bulk reveal's first play only - a fixed 2-5s range,
                    // regardless of the Inspector idleMinDelaySeconds/idleMaxDelaySeconds.
                    minDelay = Mathf.Max(0f, levelStartFirstIdleDelayMinSeconds);
                    maxDelay = Mathf.Max(minDelay, levelStartFirstIdleDelayMaxSeconds);
                }
                else
                {
                    minDelay = Mathf.Max(0f, idleMinDelaySeconds);
                    maxDelay = Mathf.Max(minDelay, idleMaxDelaySeconds);
                }

                float delay = UnityEngine.Random.Range(minDelay, maxDelay);
                if (delay > 0f)
                {
                    yield return new WaitForSeconds(delay);
                }
            }

            while (true)
            {
                if (bypassIdleThrottle)
                {
                    // Skips the board-wide wave gating below entirely - this cell is always free
                    // to play right away, run as its own independent "wave of one" instead of
                    // waiting a turn alongside every other cell.
                    yield return PlayOneIdleClip(clips);

                    // The normal wave-following path below gets its inter-play pacing for free from
                    // CatFlapCharacterIdleThrottle's own inter-wave quiet period (armed by
                    // ReleaseFromWave using these same two values) - bypassing that here means this
                    // cell needs its own explicit pacing instead, or it would play back-to-back
                    // with no gap at all.
                    float delay = UnityEngine.Random.Range(Mathf.Max(0f, idleMinDelaySeconds), Mathf.Max(idleMinDelaySeconds, idleMaxDelaySeconds));
                    if (delay > 0f)
                    {
                        yield return new WaitForSeconds(delay);
                    }

                    continue;
                }

                // Waits to be picked for the board's next wave - keep retrying rather than playing
                // anyway or giving up. The retry wait is jittered per attempt (not a fixed
                // interval) - many cells polling at once (e.g. right at level start, when most of
                // the board becomes eligible in the same frame) would otherwise all retry on the
                // exact same cadence, so Unity resumes them in the same relative order every cycle
                // and the same cells keep winning the pick first.
                while (!CatFlapCharacterIdleThrottle.TryJoinWave(this))
                {
                    yield return new WaitForSeconds(UnityEngine.Random.Range(IdleSlotRetryMinSeconds, IdleSlotRetryMaxSeconds));
                }

                yield return PlayOneIdleClip(clips);
            }
        }

        // Plays exactly one random idle clip through to completion, then releases this cell from
        // the current wave (see CatFlapCharacterIdleThrottle.ReleaseFromWave - once every member of
        // the wave has done the same, the board-wide inter-wave delay is armed using this cell's
        // own idleMinDelaySeconds/idleMaxDelaySeconds) and resets to the plain static sprite for
        // the gap until this cell's next play. Also used by IdleMoodRoutine's playFirstImmediately
        // path, which never joined a wave in the first place - ReleaseFromWave is a harmless no-op
        // there.
        private IEnumerator PlayOneIdleClip(SpriteAnimationSO[] clips)
        {
            SpriteAnimationSO clip = PickRandomClip(clips);
            if (clip == null)
            {
                CatFlapCharacterIdleThrottle.ReleaseFromWave(this, idleMinDelaySeconds, idleMaxDelaySeconds);
                yield break;
            }

            // Idle/Happy/Sad clips are both authored with Hold Last Frame on, which - per
            // SpriteAnimator.Play - makes switching straight from one to a DIFFERENT one
            // (e.g. resuming Idle right after Happy ended paused on its own held last frame)
            // reverse-scrub backwards through the OLD clip's frames first, then jump to the
            // new one. That reverse sweep is meant for look-at poses turning back to neutral,
            // not mood switches - here it visibly replayed the previous mood's frames (at the
            // reverse transition's own faster multiplier) right before the new clip started.
            // An explicit Stop() first clears SpriteAnimator's current clip so Play() always
            // takes its plain immediate-start path instead.
            characterAnimator.Stop();
            characterAnimator.Play(clip);
            yield return new WaitForSeconds(clip.Duration);
            characterAnimator.Stop();
            CatFlapCharacterIdleThrottle.ReleaseFromWave(this, idleMinDelaySeconds, idleMaxDelaySeconds);

            // Stop() leaves the renderer showing whatever frame it was last on - reset to the
            // plain static sprite (and base scale) for the gap between plays, never mid-play
            // or holding on the clip's last frame.
            if (valueIcon != null)
            {
                valueIcon.sprite = currentIconSprite;
            }
        }

        // One-shot, tied to completedParticle's own lifetime rather than the clip's - never
        // loops, and stops as soon as the particle finishes even if the clip is still mid-play.
        private void PlayHappyMood()
        {
            if (!CanAnimateCharacter(currentIconValue, currentIconNote))
            {
                return;
            }

            SpriteAnimationSO[] clips = currentIconValue == CatFlapCellValue.Cat ? catHappyAnimations : fishHappyAnimations;
            SpriteAnimationSO clip = PickRandomClip(clips);
            if (clip == null)
            {
                return;
            }

            WarnIfNotImmediateStart(clip, "Happy");
            StartCharacterMoodRoutine(HappyMoodRoutine(clip));
        }

        // Plays Thrilled exactly once, then hands off to Idle immediately (no random delay) -
        // unlike PlayHappyMood's own one-shot-then-Idle handoff (used for a per-line completion
        // mid-level), which keeps the Inspector's normal random delay before Idle's first play.
        private void PlayLevelWinThrilledMood()
        {
            if (!CanAnimateCharacter(currentIconValue, currentIconNote))
            {
                return;
            }

            SpriteAnimationSO clip = PickRandomClip(currentIconValue == CatFlapCellValue.Cat ? catThrilledAnimations : fishThrilledAnimations);
            if (clip == null)
            {
                return;
            }

            WarnIfNotImmediateStart(clip, "Thrilled");
            StartCharacterMoodRoutine(LevelWinThrilledMoodRoutine(clip));
        }

        private IEnumerator LevelWinThrilledMoodRoutine(SpriteAnimationSO clip)
        {
            characterAnimator.SetSizeReference(currentIconSprite);
            characterAnimator.Stop();
            characterAnimator.Play(clip);

            yield return new WaitForSeconds(Mathf.Max(0.01f, clip.Duration));

            // Same "clear the field before handing off" pattern HappyMoodRoutine uses - StartCharacterMoodRoutine
            // would otherwise try to StopCoroutine the very routine that's currently calling it.
            // playFirstImmediately: true is what actually skips Idle's normal random start delay.
            // bypassIdleThrottle: true keeps EVERY character on the board idling continuously for
            // the win celebration, instead of the usual ~20-25%-at-a-time wave.
            characterMoodRoutine = null;
            UpdateCharacterIdleAnimation(
                currentIconValue,
                currentIconNote,
                currentVisualState,
                playFirstImmediately: true,
                useLevelStartFirstDelay: false,
                preserveScaleIfStopping: true,
                bypassIdleThrottle: true);
        }

        // SpriteAnimator only starts animating the instant Play() is called for a continuous or
        // hold-pose clip. A plain "Interval" clip (playContinuously=false, holdLastFrame=false) -
        // the idle default - instead shows its rest frame and waits a random delay (from that
        // clip's own restBetweenPlays) before the first frame ever advances. For a one-shot
        // reaction capped at a short fixed window (happyMoodDurationSeconds /
        // ruleBreakBlinkDurationSeconds) that hidden delay can easily outlast the whole window, so
        // the reaction gets Stop()'d before it ever visibly plays - looking exactly like "nothing
        // happened". Happy/Sad clips must be authored with Play Continuously (or Hold Last Frame)
        // enabled so they start immediately; this just surfaces the mistake instead of leaving it
        // silent.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void WarnIfNotImmediateStart(SpriteAnimationSO clip, string moodName)
        {
            if (clip.playContinuously || clip.holdLastFrame)
            {
                return;
            }

            Debug.LogWarning(
                $"CatFlapSpriteCellViewAdapter: {moodName} clip '{clip.name}' is authored in Interval " +
                "mode (Play Continuously and Hold Last Frame both off). SpriteAnimator will rest for a " +
                "random restBetweenPlays delay before it starts animating, which can make this mood " +
                "appear to never play within its short fixed window. Enable Play Continuously (or Hold " +
                "Last Frame) on this SpriteAnimationSO so it starts immediately.",
                clip);
        }

        private IEnumerator HappyMoodRoutine(SpriteAnimationSO clip)
        {
            characterAnimator.SetSizeReference(currentIconSprite);

            // See the matching comment in IdleMoodRoutine - Stop() first so a still-held Idle (or
            // a previous Happy/Sad) never triggers SpriteAnimator's reverse-to-neutral hold
            // transition instead of starting Happy immediately. No artificial ease-in scale
            // envelope here - the character snaps straight to Play()'s own clip-normalised scale
            // (see SpriteAnimator.NormaliseScaleTo); any smoothing comes from the clip's own
            // authored frames, not a synthetic transform-scale animation on top of them.
            characterAnimator.Stop();
            characterAnimator.Play(clip);

            // Tracks completedParticle.isPlaying when there's a real particle to follow, but is
            // always capped at happyMoodDurationSeconds - so a missing/instant/misconfigured
            // particle system can never cut Happy off after a single frame (or leave it stuck on
            // forever if the particle system loops).
            float elapsed = 0f;
            while (elapsed < happyMoodDurationSeconds)
            {
                if (completedParticle != null && !completedParticle.isPlaying && elapsed > 0f)
                {
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            characterMoodRoutine = null;
            UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, preserveScaleIfStopping: true);
        }

        // One-shot, tied to the rule-break blink's own duration.
        private void PlaySadMood()
        {
            if (!CanAnimateCharacter(currentIconValue, currentIconNote))
            {
                return;
            }

            SpriteAnimationSO[] clips = currentIconValue == CatFlapCellValue.Cat ? catSadAnimations : fishSadAnimations;
            SpriteAnimationSO clip = PickRandomClip(clips);
            if (clip == null)
            {
                return;
            }

            WarnIfNotImmediateStart(clip, "Sad");
            StartCharacterMoodRoutine(SadMoodRoutine(clip));
        }

        private IEnumerator SadMoodRoutine(SpriteAnimationSO clip)
        {
            characterAnimator.SetSizeReference(currentIconSprite);

            // See the matching comment in IdleMoodRoutine - Stop() first so a still-held Idle (or
            // a previous Happy/Sad) never triggers SpriteAnimator's reverse-to-neutral hold
            // transition instead of starting Sad immediately. No artificial ease-in scale
            // envelope here - see the matching comment in HappyMoodRoutine.
            characterAnimator.Stop();
            characterAnimator.Play(clip);

            if (ruleBreakBlinkDurationSeconds > 0f)
            {
                yield return new WaitForSeconds(ruleBreakBlinkDurationSeconds);
            }

            characterMoodRoutine = null;
            UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, preserveScaleIfStopping: true);
        }

        // Only ever hands back a clip with frames assigned. SpriteAnimator.Play() calls Stop()
        // (disabling itself) whenever given an invalid clip, so a null/empty entry in one of the
        // Inspector arrays must never reach Play() - better to skip that pick (and leave whatever
        // mood was already running alone) than to silently disable the animator.
        private static SpriteAnimationSO PickRandomClip(SpriteAnimationSO[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            int startIndex = UnityEngine.Random.Range(0, clips.Length);
            for (int offset = 0; offset < clips.Length; offset++)
            {
                SpriteAnimationSO candidate = clips[(startIndex + offset) % clips.Length];
                if (candidate != null && candidate.IsValid)
                {
                    return candidate;
                }
            }

            return null;
        }

        // Level-entry reveal pop only (RevealValue) - a plain uniform scale-in of the icon, since
        // the value is already correct and there's no sprite swap to hide mid-animation. The
        // tap-driven tile flip below is a separate, distinct animation.
        private void PlayIconChange(Sprite sprite)
        {
            SpriteRenderer renderer = ResolveIconRenderer(currentIconNote);

            if (iconAnimation != null)
            {
                StopCoroutine(iconAnimation);
                iconAnimation = null;
            }

            if (!gameObject.activeInHierarchy || iconChangeAnimationSeconds <= 0f || renderer == null)
            {
                ApplyIconSprite(renderer, sprite, ResolveIconFullScale(currentIconNote));
                UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, useLevelStartFirstDelay: true);
                return;
            }

            iconAnimation = StartCoroutine(AnimateIconReveal(renderer, sprite));
        }

        private IEnumerator AnimateIconReveal(SpriteRenderer renderer, Sprite sprite)
        {
            Vector3 fullScale = ResolveIconFullScale(currentIconNote);
            ApplyIconSprite(renderer, sprite, Vector3.zero);

            float seconds = Mathf.Max(0.01f, iconChangeAnimationSeconds);
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
            {
                float t = EaseOut(Mathf.Clamp01(elapsed / seconds));
                renderer.transform.localScale = Vector3.LerpUnclamped(Vector3.zero, fullScale, t);
                yield return null;
            }

            renderer.transform.localScale = fullScale;
            iconAnimation = null;
            UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, useLevelStartFirstDelay: true);
        }

        private static void ApplyIconSprite(SpriteRenderer renderer, Sprite sprite, Vector3 scale)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.transform.localScale = scale;
            renderer.gameObject.SetActive(sprite != null);
        }

        // Tap-driven value change: the whole tile (background box + character icon) flips
        // together on the X axis only (Y stays full height) - collapses to a thin edge, swaps
        // both sprites at the midpoint, then expands back out. Never used for bulk/initial board
        // renders (level load, restart, clear, resume) - those apply instantly, see SetState.
        // previousIsNote/isNote can differ (e.g. a note promoted to a real value), in which case
        // the renderer showing the icon switches from notesIcon to valueIcon (or vice versa) at
        // the flip's midpoint, same as the sprite swap.
        private void PlayTileFlip(Sprite backgroundSprite, Sprite iconSprite, CatFlapCellValue value, bool isNote, CatFlapCellVisualState visualState, bool previousIsNote, bool playCompletedParticleOnFinish = false, float speedMultiplier = 1f, bool suppressPostFlipPop = false)
        {
            if (tileFlipAnimation != null)
            {
                StopCoroutine(tileFlipAnimation);
                tileFlipAnimation = null;
            }

            if (iconAnimation != null)
            {
                StopCoroutine(iconAnimation);
                iconAnimation = null;
            }

            if (characterPostFlipRoutine != null)
            {
                StopCoroutine(characterPostFlipRoutine);
                characterPostFlipRoutine = null;
                // Interrupted mid-pop by a new flip landing on this same cell before it eased back
                // down - reset both possible icon renderers to their real full scale rather than
                // leave whichever one was popping stuck at some in-between scale.
                if (valueIcon != null)
                {
                    valueIcon.transform.localScale = iconFullScale;
                }

                if (notesIcon != null)
                {
                    notesIcon.transform.localScale = notesIconFullScale;
                }
            }

            SpriteRenderer collapsingRenderer = ResolveIconRenderer(previousIsNote);
            SpriteRenderer expandingRenderer = ResolveIconRenderer(isNote);

            if (!gameObject.activeInHierarchy || iconChangeAnimationSeconds <= 0f)
            {
                ApplyBackgroundSpriteInstant(backgroundSprite);
                if (collapsingRenderer != expandingRenderer)
                {
                    ApplyIconSprite(collapsingRenderer, null, ResolveIconFullScale(previousIsNote));
                }

                ApplyIconSprite(expandingRenderer, iconSprite, ResolveIconFullScale(isNote));
                UpdateResultHighlight(visualState);

                // When a completed-particle celebration is about to play, Happy would
                // immediately override an Idle play started here anyway - starting Idle first
                // just piles a wasted SpriteAnimator.Play()/StartClip() (plus the coroutine churn
                // in StartCharacterMoodRoutine) onto this same Update, which was making the very
                // first Happy play visibly faster (see PlayCompletedParticle/PlayHappyMood).
                if (playCompletedParticleOnFinish)
                {
                    RecordMoodState(value, isNote, visualState);
                    PlayCompletedParticle();
                }
                else
                {
                    UpdateCharacterIdleAnimation(value, isNote, visualState, playFirstImmediately: true);
                }

                return;
            }

            tileFlipAnimation = StartCoroutine(AnimateTileFlip(backgroundSprite, iconSprite, value, isNote, visualState, collapsingRenderer, expandingRenderer, playCompletedParticleOnFinish, speedMultiplier, suppressPostFlipPop));
        }

        private IEnumerator AnimateTileFlip(
            Sprite backgroundSprite,
            Sprite iconSprite,
            CatFlapCellValue value,
            bool isNote,
            CatFlapCellVisualState visualState,
            SpriteRenderer collapsingRenderer,
            SpriteRenderer expandingRenderer,
            bool playCompletedParticleOnFinish = false,
            float speedMultiplier = 1f,
            bool suppressPostFlipPop = false)
        {
            // speedMultiplier only ever differs from 1x for a completed-line reveal step (see
            // CatFlapSpriteBoardViewAdapter.AnimateStaggeredReveal) - a normal tap always flips at
            // the authored iconChangeAnimationSeconds pace.
            float halfDuration = Mathf.Max(0.01f, (iconChangeAnimationSeconds * 0.5f) / Mathf.Max(0.01f, speedMultiplier));
            float minScaleFactor = Mathf.Clamp01(iconChangeMinScale);
            Vector3 backgroundMinScale = new Vector3(backgroundFullScale.x * minScaleFactor, backgroundFullScale.y, backgroundFullScale.z);

            Vector3 collapsingFullScale = collapsingRenderer == valueIcon ? iconFullScale : notesIconFullScale;
            Vector3 collapsingMinScale = new Vector3(collapsingFullScale.x * minScaleFactor, collapsingFullScale.y, collapsingFullScale.z);
            Vector3 expandingFullScale = expandingRenderer == valueIcon ? iconFullScale : notesIconFullScale;
            Vector3 expandingMinScale = new Vector3(expandingFullScale.x * minScaleFactor, expandingFullScale.y, expandingFullScale.z);

            // Collapse from wherever the background/icon actually are right now, not the fixed
            // authored full scale - a fast second tap can restart this flip while the previous
            // one is still mid-collapse, and starting from the authored scale would snap it back
            // out to full size for one frame before re-collapsing.
            Vector3 backgroundCollapseStart = background != null ? background.transform.localScale : backgroundFullScale;
            Vector3 collapsingCollapseStart = collapsingRenderer != null ? collapsingRenderer.transform.localScale : collapsingFullScale;

            // resultHighlightRenderer sits directly above the background as a full-size overlay
            // (see UpdateResultHighlight) - it must scale down/up in lockstep with the background,
            // or a completed/error/warning highlight that's already showing sits frozen at full
            // size on top of the collapsing background, and a newly-applied one (see below) pops
            // in unanimated instead of flipping in with the rest of the tile.
            Vector3 highlightCollapseStart = resultHighlightRenderer != null ? resultHighlightRenderer.transform.localScale : backgroundFullScale;

            for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
            {
                float t = EaseIn(Mathf.Clamp01(elapsed / halfDuration));
                if (background != null)
                {
                    background.transform.localScale = Vector3.Lerp(backgroundCollapseStart, backgroundMinScale, t);
                }

                if (resultHighlightRenderer != null)
                {
                    resultHighlightRenderer.transform.localScale = Vector3.Lerp(highlightCollapseStart, backgroundMinScale, t);
                }

                if (collapsingRenderer != null)
                {
                    collapsingRenderer.transform.localScale = Vector3.Lerp(collapsingCollapseStart, collapsingMinScale, t);
                }

                yield return null;
            }

            if (background != null)
            {
                background.sprite = backgroundSprite;
                background.color = neutralColor;
            }

            // Applied at the same midpoint as the background sprite swap above, so the
            // correct/wrong/warning overlay changes together with the tile's own art rather than
            // before or after the flip visually settles.
            UpdateResultHighlight(visualState);

            if (collapsingRenderer != null && collapsingRenderer != expandingRenderer)
            {
                collapsingRenderer.gameObject.SetActive(false);
            }

            ApplyIconSprite(expandingRenderer, iconSprite, expandingMinScale);

            for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
            {
                float t = EaseOut(Mathf.Clamp01(elapsed / halfDuration));
                if (background != null)
                {
                    background.transform.localScale = Vector3.Lerp(backgroundMinScale, backgroundFullScale, t);
                }

                if (resultHighlightRenderer != null)
                {
                    resultHighlightRenderer.transform.localScale = Vector3.Lerp(backgroundMinScale, backgroundFullScale, t);
                }

                if (expandingRenderer != null)
                {
                    expandingRenderer.transform.localScale = Vector3.Lerp(expandingMinScale, expandingFullScale, t);
                }

                yield return null;
            }

            if (background != null)
            {
                background.transform.localScale = backgroundFullScale;
            }

            if (resultHighlightRenderer != null)
            {
                resultHighlightRenderer.transform.localScale = backgroundFullScale;
            }

            if (expandingRenderer != null)
            {
                expandingRenderer.transform.localScale = expandingFullScale;
            }

            tileFlipAnimation = null;

            // UnityEngine.Debug.Log($"CatFlapSpriteCellViewAdapter.AnimateTileFlip: ({row},{column}) ran to completion. expandingRenderer={(expandingRenderer == valueIcon ? "valueIcon" : expandingRenderer == notesIcon ? "notesIcon" : "other")}, active={(expandingRenderer != null ? expandingRenderer.gameObject.activeSelf : (bool?)null)}, scale={(expandingRenderer != null ? expandingRenderer.transform.localScale.ToString() : "null")}, collapsingRenderer={(collapsingRenderer == valueIcon ? "valueIcon" : collapsingRenderer == notesIcon ? "notesIcon" : "other")}, collapsingActive={(collapsingRenderer != null ? collapsingRenderer.gameObject.activeSelf : (bool?)null)}.");

            // Fires right as the flip finishes, not before - see
            // CatFlapRuntimeController.RevealCompletedCell, the only
            // caller that sets playCompletedParticleOnFinish (via forcePlayFlip). Skip starting
            // Idle here when a celebration is about to play - Happy would immediately override it
            // anyway, and starting Idle first just piles a wasted Play()/StartClip() onto this same
            // Update (every cell in a completed-line reveal group hits this on the same frame),
            // which was making the very first Happy play visibly faster.
            if (playCompletedParticleOnFinish)
            {
                RecordMoodState(value, isNote, visualState);
                PlayCompletedParticle();

                // suppressPostFlipPop is only ever true for the level-winning move's own reveal
                // (see CatFlapRuntimeController.PlayWinningLineReveal) - that flip goes straight
                // into the board-wide win celebration instead of playing this pop first.
                if (!suppressPostFlipPop)
                {
                    // Only after PlayCompletedParticle()'s PlayHappyMood() has already started the
                    // Happy clip (characterAnimator.Play -> SpriteAnimator.NormaliseScaleTo, which
                    // just set the renderer to ITS OWN correct per-clip-normalised scale) - see
                    // PlayCharacterPostFlipPop for why capturing that scale fresh, rather than
                    // trusting a fixed value computed earlier, is required.
                    PlayCharacterPostFlipPop(expandingRenderer);
                }
            }
            else
            {
                UpdateCharacterIdleAnimation(value, isNote, visualState, playFirstImmediately: true);
            }
        }

        // Plays just the completed-line character pop (scale up, hold, settle back down - see
        // AnimateCharacterPostFlipPop) on this cell's CURRENT icon, with no tile flip beforehand -
        // used by Cat Flap's Level 4 FTUE rules walkthrough to celebrate a whole row at once
        // right after the first guided tile lands, without re-flipping tiles that are already
        // showing their correct value.
        //
        // A cell that was just tapped already has its Idle mood stopped (SetState's own flip path
        // calls StopCharacterMood before it flips) and hasn't restarted it yet, so its pop plays
        // untouched. A cell that's simply been sitting there since level load (e.g. a pre-given
        // clue never tapped this level) has its Idle mood loop actively running - and
        // PlayOneIdleClip calls characterAnimator.Stop() at the start AND end of every single idle
        // clip play, which force-resets the renderer's transform.localScale straight back to its
        // pre-animation base scale (see SpriteAnimator.Stop). Idle loops continuously with random
        // delays between plays, so one of those Stop() calls landing anywhere inside this pop's
        // ~0.7s duration will silently snap the scale back mid-pop, reading as "never scaled at
        // all". Stopping mood first (and restarting it once the pop settles) removes the entire
        // class of interruption instead of just reducing how often it's hit.
        public void PlayCompletedLinePop()
        {
            StopCharacterMood(preserveScale: true);

            SpriteRenderer characterRenderer = ResolveIconRenderer(currentIconNote);
            if (characterRenderer == null)
            {
                UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, preserveScaleIfStopping: true);
                return;
            }

            if (characterPostFlipRoutine != null)
            {
                StopCoroutine(characterPostFlipRoutine);
            }

            characterPostFlipRoutine = StartCoroutine(AnimateCompletedLinePopThenResumeIdle(characterRenderer));
        }

        private IEnumerator AnimateCompletedLinePopThenResumeIdle(SpriteRenderer characterRenderer)
        {
            yield return AnimateCharacterPostFlipPop(characterRenderer);
            UpdateCharacterIdleAnimation(currentIconValue, currentIconNote, currentVisualState, preserveScaleIfStopping: true);
        }

        // ---------- Post-flip character pop (completed-line reveal flips only) ----------

        // Independent of tileFlipAnimation on purpose - IsPlayingEntryAnimation (which the
        // completed-line reveal cascade and the win-celebration wait loop both key off of) must
        // clear the instant the flip itself settles, not a full second-plus later once this pop's
        // hold finishes, or every reveal/win would grind to a halt waiting on it.
        private void PlayCharacterPostFlipPop(SpriteRenderer characterRenderer)
        {
            if (characterRenderer == null)
            {
                return;
            }

            if (characterPostFlipRoutine != null)
            {
                StopCoroutine(characterPostFlipRoutine);
            }

            characterPostFlipRoutine = StartCoroutine(AnimateCharacterPostFlipPop(characterRenderer));
        }

        // Reads the renderer's CURRENT scale as the baseline, rather than trusting a value
        // computed earlier by the caller - by the time this runs (right after
        // PlayCompletedParticle/PlayHappyMood has already called characterAnimator.Play), that
        // scale is whatever SpriteAnimator.NormaliseScaleTo just set for THIS SPECIFIC clip/sprite,
        // which can legitimately differ per character (e.g. Cat vs Fish sheets needing different
        // art-fit correction). Using a fixed generic scale here instead stomped that per-character
        // normalisation the moment this pop reverted - Cat (whose normalised scale differs from
        // the generic baseline) ended up visibly undersized after the pop, while Fish (whose
        // normalised scale happened to match the generic baseline) looked fine. Safe to read
        // immediately now (no waiting out an ease-in dip first) - HappyMoodRoutine no longer
        // applies one on top of Play()'s own normalised scale.
        private IEnumerator AnimateCharacterPostFlipPop(SpriteRenderer characterRenderer)
        {
            Vector3 fullScale = characterRenderer.transform.localScale;
            Vector3 peakScale = fullScale * characterPostFlipPeakScale;

            float scaleUpDuration = Mathf.Max(0.01f, characterPostFlipScaleUpDurationSeconds);
            for (float elapsed = 0f; elapsed < scaleUpDuration; elapsed += Time.deltaTime)
            {
                float t = EaseOut(Mathf.Clamp01(elapsed / scaleUpDuration));
                characterRenderer.transform.localScale = Vector3.LerpUnclamped(fullScale, peakScale, t);
                yield return null;
            }

            characterRenderer.transform.localScale = peakScale;

            if (characterPostFlipHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(characterPostFlipHoldSeconds);
            }

            float scaleDownDuration = Mathf.Max(0.01f, characterPostFlipScaleDownDurationSeconds);
            for (float elapsed = 0f; elapsed < scaleDownDuration; elapsed += Time.deltaTime)
            {
                float t = EaseIn(Mathf.Clamp01(elapsed / scaleDownDuration));
                characterRenderer.transform.localScale = Vector3.LerpUnclamped(peakScale, fullScale, t);
                yield return null;
            }

            characterRenderer.transform.localScale = fullScale;
            characterPostFlipRoutine = null;
        }

        private static float EaseIn(float t)
        {
            return t * t;
        }

        private static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        private static string ResolveLabel(CatFlapCellValue value, bool isNote)
        {
            string prefix = isNote ? "?" : string.Empty;
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return prefix + "F";
                case CatFlapCellValue.Cat:
                    return prefix + "C";
                default:
                    return string.Empty;
            }
        }
    }
}
