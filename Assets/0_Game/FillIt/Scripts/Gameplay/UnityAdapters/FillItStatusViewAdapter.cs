using System.Collections;
using System.Collections.Generic;
using System.Text;
using CatFlap.Boosters;
using CatFlap.Models;
using CatFlap.Progress;
using CatFlap.Validation;
using CatFlap.Views;
using Shared.Boosters;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapStatusViewAdapter : MonoBehaviour, ICatFlapStatusView
    {
        [SerializeField] private TMP_Text levelLabel;
        private System.DateTime? dailyChallengeDate;
        // Last level handed to SetLevel, plus whether Enable/DisableHardLevelUI last marked it
        // hard - together these let RefreshLevelLabel rebuild "Hard Level - X"/"Level - X" from
        // either side, whichever updates last.
        private string lastLevelId;
        private string lastLevelName;
        private bool isHardLevel;
        // The player's own progression number (CatFlapGameplayManager.CurrentLevelNumber). Past
        // GlobalConfig.TotalLevels that keeps counting up (101, 102, ...) while the level ASSET
        // actually loaded is a reshuffled 1-100 loop level (see CatFlapLoopLevelSequencer) - and
        // SetLevel's id/name carry that looped asset's number, not the player's. Without this the
        // HUD tells a player on level 105 they're on "Level - 25". 0 = never set, fall back to
        // parsing id/name as before.
        private int displayLevelNumber;
        // Cached alongside lastLevelId/lastLevelName for the same reason - RefreshLevelLabel can
        // fire later (from Enable/DisableHardLevelUI) and needs to rebuild the exact same "Level
        // 1-4" text for an FTUE level without SetLevel having just been called again.
        private bool lastIsFtueLevel;
        private Coroutine hardLevelPanelEntryRoutine;
        private Coroutine levelLabelEntryRoutine;
        [SerializeField] private TMP_Text livesLabel;
        [SerializeField] private TMP_Text stateLabel;
        [SerializeField] private TMP_Text movesLabel;
        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private TMP_Text completionLabel;
        // Gems -> Coins rename - FormerlySerializedAs preserves this field's existing Inspector
        // wiring in every prefab that already has it assigned under the old name.
        [FormerlySerializedAs("gemsLabel")]
        [SerializeField] private TMP_Text coinsLabel;
        [SerializeField] private TMP_Text notesLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        // The Settings button only - hidden during the board-tap FTUE tutorial (levels 1-3, see
        // CatFlapGameplayManager.ApplyFtueState) while coins/level number stay visible, separate
        // from the per-feature attemptsHudRoot toggle below.
        [SerializeField] private GameObject settingsButtonRoot;
        [SerializeField] private Image[] attemptPips;
        [SerializeField] private Sprite attemptFilledSprite;
        [SerializeField] private Sprite attemptConsumedSprite;
        // The pips' shared parent container (has its own background pill Image) - hiding just the
        // pip Images still leaves this background visible, so this needs to be toggled too.
        [SerializeField] private GameObject attemptsHudRoot;

        [Header("Heart Break FX (plays on a pip's filled -> consumed transition)")]
        [SerializeField] private Sprite heartBreakLeftSprite;
        [SerializeField] private Sprite heartBreakRightSprite;
        [SerializeField] private float heartBreakScaleUpDurationSeconds = 0.12f;
        [SerializeField] private float heartBreakScaleUpAmount = 1.2f;
        [SerializeField] private float heartBreakFadeDurationSeconds = 0.35f;
        [SerializeField] private float heartBreakSplitOffset = 18f;

        [Header("Revive Fill Animation (attempt pips fill one-by-one back to full)")]
        [Tooltip("Requires each attemptPips Image's own Image Type set to Filled in the Inspector - this only drives fillAmount, it does not set Image Type itself.")]
        [SerializeField] private float reviveFillDurationSeconds = 0.25f;
        [SerializeField] private float reviveScaleUpDurationSeconds = 0.12f;
        [SerializeField] private float reviveScaleUpAmount = 1.25f;
        // Assets/Shared/Features/LevelMistakeLimit/Prefab/Heart_Break_vfx.prefab - a looping
        // ParticleSystem with no stop action of its own, so it's explicitly destroyed after
        // heartBreakVfxLifetimeSeconds rather than left to stop itself.
        [SerializeField] private ParticleSystem heartBreakVfxPrefab;
        [SerializeField] private float heartBreakVfxLifetimeSeconds = 1f;
        [SerializeField] private TMP_Text equalCountsRuleIcon;
        [SerializeField] private TMP_Text maxTwoAdjacentRuleIcon;
        [SerializeField] private TMP_Text uniqueLinesRuleIcon;
        [SerializeField] private GameObject equalCountsRuleOutline;
        [SerializeField] private GameObject maxTwoAdjacentRuleOutline;
        [SerializeField] private GameObject uniqueLinesRuleOutline;
        [SerializeField] private RectTransform equalCountsRuleRoot;
        [SerializeField] private RectTransform maxTwoAdjacentRuleRoot;
        [SerializeField] private RectTransform uniqueLinesRuleRoot;

        [Header("FTUE Level Indicator (FtueLevelsIndicator - 4 step icons + 3 connecting linkers, one step per FTUE level)")]
        // Root of the whole widget (FtueLevelsIndicator) - active only for FTUE levels (see
        // SetFtueLevelIndicator); everything else here is a child of it.
        [SerializeField] private GameObject ftueLevelIndicatorRoot;
        // One per FTUE level, in level order (index 0 = level 1) - reused from the existing
        // AttemptPip1..4 GameObjects under FtueLevelsIndicator/FtueIndication.
        [SerializeField] private Image[] ftueLevelStepImages;
        // One fewer than ftueLevelStepImages - connector[i] sits between step[i] and step[i + 1].
        [SerializeField] private Image[] ftueLevelConnectorImages;
        [SerializeField] private Sprite ftueLevelStepReachedSprite;
        [SerializeField] private Sprite ftueLevelStepLockedSprite;
        [SerializeField] private Color ftueLevelConnectorPendingColor = Color.white;
        [SerializeField] private Color ftueLevelConnectorPassedColor = new Color(0.31190506f, 0.8352941f, 0.2516465f);
        [Tooltip("Punch scale played on a step icon/connector the instant IT (not the others) newly changes state - see SetFtueLevelIndicator.")]
        [SerializeField] private float ftueLevelIndicatorPunchDurationSeconds = 0.25f;
        [SerializeField] private float ftueLevelIndicatorPunchScale = 1.3f;
        // -1 = no FTUE level rendered yet this session - see SetFtueLevelIndicator's own comment
        // on why nothing punches on that very first call.
        private int ftueLevelIndicatorLastLevelNumber = -1;

        [Header("Rule Break Blink (scale pulse on the TopHUD rule chip, not the board cells)")]
        [SerializeField] private float ruleBreakBlinkDurationSeconds = 0.45f;
        [SerializeField] private int ruleBreakBlinkCount = 3;
        [SerializeField] private float ruleBreakBlinkScale = 1.15f;

        [Header("Wrong Solution Text (pops in, then fades in/out on a loop while any line is flagged wrong)")]
        // No longer a TMP_Text - the Wrong Solution warning is now its own GameObject (background +
        // icon art, see Assets/0_Game/CatFlap/Art/Sprites/wrong_solution_bg.png/wrong_solution_icon.png)
        // that's enabled/disabled directly, with wrongSolutionCanvasGroup driving its fade (auto-
        // resolved via GetComponent if left unassigned) and its own Transform driving the scale pop.
        [SerializeField] private GameObject wrongSolutionRoot;
        [SerializeField] private CanvasGroup wrongSolutionCanvasGroup;
        [SerializeField] private float wrongSolutionPopDurationSeconds = 0.25f;
        [SerializeField] private float wrongSolutionFadeStepSeconds = 0.5f;

        [Header("HUD Entrance Animation (level start)")]
        // TopHUDRoot - the coins/level-number/settings group, slid down together from above.
        [SerializeField] private RectTransform topHudGroupRoot;
        // RulesPanelRoot - scales up in place (its anchored position never moves).
        [SerializeField] private RectTransform rulesPanelGroupRoot;
        [SerializeField] private float topHudSlideDurationSeconds = 0.35f;
        [SerializeField] private float topHudSlideDistance = 220f;
        [SerializeField] private float lifeBaseSlideDurationSeconds = 0.3f;
        [SerializeField] private float lifeBaseSlideDistance = 160f;
        [SerializeField] private float pipScaleDurationSeconds = 0.2f;
        [SerializeField] private float pipScaleStaggerSeconds = 0.05f;
        [SerializeField] private float rulesScaleDurationSeconds = 0.3f;
        // Gap between each group's own START time (not between one finishing and the next
        // starting) - top HUD starts immediately, life panel starts one gap later, rules panel
        // two gaps later, all without waiting for an earlier group to finish. The booster panel
        // (owned by CatFlapGameplayManager) continues this same sequence at three gaps in - see
        // EntranceGroupStaggerSeconds.
        [SerializeField] private float entranceGroupStaggerSeconds = 0.12f;

        private bool[] pipWasFilled;
        private Coroutine[] pipBreakRoutines;
        private Coroutine reviveAnimationRoutine;

        private bool equalCountsWasFailed;
        private bool maxTwoAdjacentWasFailed;
        private bool uniqueLinesWasFailed;
        private Coroutine equalCountsBlinkRoutine;
        private Coroutine maxTwoAdjacentBlinkRoutine;
        private Coroutine uniqueLinesBlinkRoutine;

        private Coroutine wrongSolutionFadeRoutine;

        private static readonly Color RuleNeutralColor = new Color(1f, 1f, 1f, 0.96f);
        private static readonly Color RuleValidColor = new Color(0.83f, 0.97f, 0.89f, 1f);
        private static readonly Color RuleInvalidColor = new Color(0.95f, 0.45f, 0.45f, 1f);
        private static readonly Color RuleValidIconColor = new Color(0.21f, 0.78f, 0.54f, 1f);
        private static readonly Color RuleInvalidIconColor = new Color(0.86f, 0.16f, 0.15f, 1f);
        private static readonly Color RuleNeutralIconColor = new Color(0.72f, 0.76f, 0.8f, 1f);
        [Header("Hard level UI")]
        [SerializeField] private Color hardLevelColor = Color.white;
        [SerializeField] private Color NormalLevelColor = Color.white;
        [Tooltip("Font asset levelLabel switches to on a hard level (alongside hardLevelColor). Leave unassigned to keep whatever font the label already has.")]
        [SerializeField] private TMP_FontAsset hardLevelFont = null;
        [Tooltip("Font asset levelLabel switches back to on a normal level (alongside NormalLevelColor). Leave unassigned to keep whatever font the label already has.")]
        [SerializeField] private TMP_FontAsset normalLevelFont = null;
        [SerializeField] private GameObject hardLevelUI = null;
        [SerializeField] private GameObject hardLevelPanel = null;
        [Tooltip("How long hardLevelPanel/levelLabel take to pop in (scale 0 -> 1) when EnableHardLevelUI runs - same ease-out-back shape the HUD's own startup animation uses.")]
        [SerializeField] private float hardLevelEntryDurationSeconds = 0.25f;
        [SerializeField] private Button infoButton = null;

        // Coins panel's own root (icon + coinsLabel together) - separate from coinsLabel itself
        // since SetCoinsHudVisible needs to hide the whole panel, not just clear its text.
        [SerializeField] private GameObject coinsHudRoot = null;

        // FTUE-only visibility toggles (see CatFlapGameplayManager.ApplyFtueState) - Settings
        // already has its own SetSettingsButtonVisible above this file; these three follow the
        // exact same "just SetActive the root" pattern for the remaining HUD elements FTUE hides.
        public void SetRulesPanelVisible(bool visible)
        {
            if (rulesPanelGroupRoot != null)
            {
                rulesPanelGroupRoot.gameObject.SetActive(visible);
            }
        }

        // RulesPanelRoot carries its own overriding Canvas (sortingOrder above the FTUE dim
        // overlay) so it still reads on top of the dim during Level 4's rules walkthrough - see
        // CatFlapGameplayManager.ApplyFtueState's isRulesWalkthroughLevel. That override must be
        // OFF everywhere else: RulesPanelRoot is the same shared prefab used in normal (non-FTUE)
        // gameplay, where it needs to sort exactly as before (plain sibling order) so it doesn't
        // bleed on top of other popups (level win/fail, settings, out-of-lives, ...) that sit
        // above it in the hierarchy but carry no such override themselves. Lazily resolved (not
        // serialized) since the Canvas lives on the same GameObject as rulesPanelGroupRoot itself.
        private Canvas rulesPanelOverlayCanvas;
        private bool rulesPanelOverlayCanvasResolved;

        public void SetRulesPanelOverlaySortingActive(bool active)
        {
            if (!rulesPanelOverlayCanvasResolved)
            {
                rulesPanelOverlayCanvasResolved = true;
                if (rulesPanelGroupRoot != null)
                {
                    rulesPanelOverlayCanvas = rulesPanelGroupRoot.GetComponent<Canvas>();
                }
            }

            if (rulesPanelOverlayCanvas != null)
            {
                rulesPanelOverlayCanvas.overrideSorting = active;
            }
        }

        public void SetInfoButtonVisible(bool visible)
        {
            if (infoButton != null)
            {
                infoButton.gameObject.SetActive(visible);
            }
        }

        public void SetCoinsHudVisible(bool visible)
        {
            if (coinsHudRoot != null)
            {
                coinsHudRoot.SetActive(visible);
            }
        }

        // Level 4's FTUE rules walkthrough (see CatFlapFtueDriver.ConfigureRulesWalkthrough) hides
        // each rule row individually and reveals them one at a time as the player earns them,
        // rather than showing/hiding the whole rulesPanelGroupRoot at once like every other FTUE
        // level does.
        // Screen-space RectTransform of one rule's own chip - the Level 4 FTUE popup animates onto
        // this exact spot (see FtueController.MoveBannerToTarget) right before that rule reveals.
        public RectTransform GetRuleRowRect(CatFlapRule rule)
        {
            return rule switch
            {
                CatFlapRule.EqualCounts => equalCountsRuleRoot,
                CatFlapRule.MaxTwoAdjacent => maxTwoAdjacentRuleRoot,
                CatFlapRule.UniqueLines => uniqueLinesRuleRoot,
                _ => null
            };
        }

        // Total duration for a rule row's own punch-pop when it's first revealed (see
        // SetRuleRowVisible) - fast and snappy, not a slow ease.
        [SerializeField] private float ruleRevealPunchDurationSeconds = 0.22f;
        // How far past full size the pop overshoots before settling - same "punch" feel as
        // ftueLevelIndicatorPunchScale/ScalePunchRoutine elsewhere on this HUD.
        [SerializeField] private float ruleRevealPunchScale = 1.25f;

        public void SetRuleRowVisible(CatFlapRule rule, bool visible)
        {
            RectTransform ruleRect = GetRuleRowRect(rule);
            if (ruleRect == null)
            {
                return;
            }

            ruleRect.gameObject.SetActive(visible);
            if (visible)
            {
                // Punch pop-in: grows from zero straight past full size to ruleRevealPunchScale,
                // then settles back down to 1 - a much more visible "arrival" beat than a plain
                // ease-in-to-1, and quick rather than a slow reveal. See
                // CatFlapFtueDriver.RevealRuleThenAdvance, which reveals a rule row right as the
                // FTUE banner arrives on it.
                StartCoroutine(ScalePunchInRoutine(ruleRect, ruleRevealPunchDurationSeconds, ruleRevealPunchScale));
            }
        }

        // Same overall shape as ScalePunchRoutine below, but starting from zero (a reveal) rather
        // than from full size (a punctuation on something already visible) - grows past full size
        // to punchScale using an ease-out-back curve, then eases back down to exactly 1.
        private static IEnumerator ScalePunchInRoutine(RectTransform target, float durationSeconds, float punchScale)
        {
            target.localScale = Vector3.zero;
            Vector3 overshootScale = Vector3.one * Mathf.Max(1f, punchScale);
            float growDuration = Mathf.Max(0.01f, durationSeconds) * 0.65f;
            float settleDuration = Mathf.Max(0.01f, durationSeconds) * 0.35f;

            for (float elapsed = 0f; elapsed < growDuration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / growDuration));
                target.localScale = Vector3.LerpUnclamped(Vector3.zero, overshootScale, t);
                yield return null;
            }

            for (float elapsed = 0f; elapsed < settleDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / settleDuration);
                target.localScale = Vector3.LerpUnclamped(overshootScale, Vector3.one, t);
                yield return null;
            }

            target.localScale = Vector3.one;
        }

        /// <summary>
        /// Drives the FTUE-only level-progress widget (FtueLevelsIndicator) - active only while
        /// isFtueLevel is true, hidden outright for every level past the FTUE range. levelNumber
        /// is 1-based (level 1 fills step index 0). Step index i is shown "reached" (filled
        /// sprite) once levelNumber has gotten at least that far (i.e. i &lt; levelNumber, so
        /// being ON level 1 already fills step 0); connector i (between step i and step i + 1)
        /// only turns from its pending white to the passed green once the step BEFORE it is
        /// truly behind the player - i.e. once levelNumber has advanced past i + 1, not merely
        /// reached it - see the "index i &lt; levelNumber - 1" check below.
        /// Whichever step icon(s)/connector(s) actually flip state compared to the last call get
        /// a one-shot punch-scale pop (see ScalePunchRoutine) - nothing already in its current
        /// state re-punches on every relaod/restart of the same level.
        /// </summary>
        public void SetFtueLevelIndicator(int levelNumber, bool isFtueLevel)
        {
            if (ftueLevelIndicatorRoot != null)
            {
                ftueLevelIndicatorRoot.SetActive(isFtueLevel);
            }

            if (!isFtueLevel)
            {
                // Leaving the FTUE range entirely - next time it's re-entered (a fresh play
                // session, say) there's no meaningful "previous" state to diff against, so don't
                // punch on that first re-render either.
                ftueLevelIndicatorLastLevelNumber = -1;
                return;
            }

            // -1 (never rendered before) intentionally never punches - only a genuine change
            // FROM a previously-rendered level does.
            int previousLevelNumber = ftueLevelIndicatorLastLevelNumber;
            bool canPunch = previousLevelNumber >= 0 && previousLevelNumber != levelNumber;

            if (ftueLevelStepImages != null)
            {
                for (int index = 0; index < ftueLevelStepImages.Length; index++)
                {
                    if (ftueLevelStepImages[index] == null)
                    {
                        continue;
                    }

                    bool reached = index < levelNumber;
                    ftueLevelStepImages[index].sprite = reached ? ftueLevelStepReachedSprite : ftueLevelStepLockedSprite;

                    if (canPunch && reached != index < previousLevelNumber)
                    {
                        StartCoroutine(ScalePunchRoutine(ftueLevelStepImages[index].transform, ftueLevelIndicatorPunchDurationSeconds, ftueLevelIndicatorPunchScale));
                    }
                }
            }

            if (ftueLevelConnectorImages != null)
            {
                for (int index = 0; index < ftueLevelConnectorImages.Length; index++)
                {
                    if (ftueLevelConnectorImages[index] == null)
                    {
                        continue;
                    }

                    bool passed = index < levelNumber - 1;
                    ftueLevelConnectorImages[index].color = passed ? ftueLevelConnectorPassedColor : ftueLevelConnectorPendingColor;

                    if (canPunch && passed != index < previousLevelNumber - 1)
                    {
                        StartCoroutine(ScalePunchRoutine(ftueLevelConnectorImages[index].transform, ftueLevelIndicatorPunchDurationSeconds, ftueLevelIndicatorPunchScale));
                    }
                }
            }

            ftueLevelIndicatorLastLevelNumber = levelNumber;
        }

        /// <summary>
        /// Enables the Hard Level HUD indicator - called once
        /// HardLevelPopupController.OnMoveToTargetArrived fires (the popup's flying badge has
        /// just "arrived" here), see CatFlapGameplayManager's wiring.
        /// </summary>
        public void EnableHardLevelUI()
        {
            if (hardLevelUI != null)
            {
                hardLevelUI.SetActive(false);
            }
            if (hardLevelPanel != null)
            {
                hardLevelPanel.SetActive(false);
            }
            Camera.main.backgroundColor=new Color(1f, 0.8588f, 0.8157f, 1f);
            levelLabel.color = hardLevelColor;
            ApplyLevelLabelFont(hardLevelFont);
            isHardLevel = true;
            RefreshLevelLabel();
            PlayHardLevelEntryAnimation();
        }

        // Pops hardLevelPanel and levelLabel in with the same ease-out-back scale the HUD's own
        // startup animation uses (ScaleUpEntryItem), so the badge/label turning hard mid-level -
        // right as HardLevelPopupController finishes its close - reads as part of that same
        // pop-in language instead of snapping on.
        private void PlayHardLevelEntryAnimation()
        {
            float duration = Mathf.Max(0.01f, hardLevelEntryDurationSeconds);

            if (hardLevelPanel != null)
            {
                RestartHardLevelEntryRoutine(ref hardLevelPanelEntryRoutine, hardLevelPanel.transform, duration);
            }

            if (levelLabel != null)
            {
                RestartHardLevelEntryRoutine(ref levelLabelEntryRoutine, levelLabel.transform, duration);
            }
        }

        private void RestartHardLevelEntryRoutine(ref Coroutine routine, Transform target, float duration)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(ScaleUpEntryItem(target, duration));
        }

        // Anything still mid-pop when the level stops being hard would otherwise be left stuck at
        // a partial scale - levelLabel especially, since it stays on screen either way.
        private void StopHardLevelEntryAnimation()
        {
            if (hardLevelPanelEntryRoutine != null)
            {
                StopCoroutine(hardLevelPanelEntryRoutine);
                hardLevelPanelEntryRoutine = null;
            }

            if (levelLabelEntryRoutine != null)
            {
                StopCoroutine(levelLabelEntryRoutine);
                levelLabelEntryRoutine = null;
            }

            if (hardLevelPanel != null)
            {
                hardLevelPanel.transform.localScale = Vector3.one;
            }

            if (levelLabel != null)
            {
                levelLabel.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Instantly hides the Hard Level HUD indicator - called at the start of every
        /// LoadLevel/Clear/Restart (see CatFlapRuntimeController.VisualState.ClearErrorCells), so
        /// it never lingers on screen from a previous hard level once EnableHardLevelUI turns it on.
        /// </summary>
        public void DisableHardLevelUI()
        {
            if (hardLevelUI != null)
            {
                hardLevelUI.SetActive(false);
            }
            if (hardLevelPanel != null)
            {
                hardLevelPanel.SetActive(false);
            }
            Camera.main.backgroundColor=new Color(0.9921569f, 0.9490196f, 0.9294118f, 1f);
            levelLabel.color = NormalLevelColor;
            ApplyLevelLabelFont(normalLevelFont);
            isHardLevel = false;
            RefreshLevelLabel();
            StopHardLevelEntryAnimation();
        }

        // Unassigned means "this project isn't swapping fonts for hard levels" - leave whatever
        // font the label already has rather than blanking it, same convention as the optional
        // hardLevelUI/hardLevelPanel references above.
        private void ApplyLevelLabelFont(TMP_FontAsset font)
        {
            if (levelLabel != null && font != null)
            {
                levelLabel.font = font;
            }
        }

        /// <summary>
        /// Disables the Info button - called the instant a win or loss is recorded
        /// (CatFlapGameplayManager.HandleLevelWon/HandleLevelFailed), before any of their
        /// celebration/fail animations or popups start, so it can't be tapped mid-resolution.
        /// </summary>
        public void DisableInfoButton()
        {
            if (infoButton != null)
            {
                infoButton.interactable = false;
            }
        }

        /// <summary>
        /// Re-enables the Info button - called at the start of every LoadLevel/Clear/Restart (see
        /// CatFlapRuntimeController.VisualState.ClearErrorCells), same convention as
        /// DisableHardLevelUI/ForceHideWrongSolutionWarning, so a fresh level never starts with it
        /// still disabled from the previous level's win/loss.
        /// </summary>
        public void EnableInfoButton()
        {
            if (infoButton != null)
            {
                infoButton.interactable = true;
            }
        }

        private void Awake()
        {
            // Forces a clean, code-owned starting state regardless of whatever active/inactive
            // state the prefab was left authored as - ShowWrongSolutionWarning/HideWrongSolutionWarning
            // both trust wrongSolutionRoot.activeSelf as ground truth from here on, so it must
            // start correctly hidden rather than "shown by default" if left active in the Editor.
            if (wrongSolutionRoot != null)
            {
                if (wrongSolutionCanvasGroup == null)
                {
                    wrongSolutionCanvasGroup = wrongSolutionRoot.GetComponent<CanvasGroup>();
                }

                wrongSolutionRoot.SetActive(false);
            }
        }

        #region Entry Animation

        [Header("Entry Animation")]
        public List<Transform> EntryAnimationObject = new List<Transform>();
        [SerializeField] private float entryAnimationDurationSeconds = 0.25f;
        [SerializeField] private float entryAnimationStaggerSeconds = 0.03f;

        private Coroutine entryAnimationRoutine;

        /// <summary>
        /// Scales every entry in EntryAnimationObject from 0 -> 1 (ease-out-back, same easing as
        /// the Wrong Solution pop-in below), staggered slightly so the HUD reads as one continuous
        /// pop-in instead of every element snapping into place on the same frame. Called by
        /// CatFlapGameplayManager.PlayBoardEnterAnimation alongside the board's own enter
        /// animation - every regular level load/restart and Daily Challenge load/restart funnels
        /// through that one method, so this replays every time the board does, not just once.
        /// Not wired to SetLevel/RefreshViews - those also fire for unrelated mid-session updates
        /// (cheat panel edits, booster refreshes, etc.) that must NOT replay this pop-in.
        /// </summary>
        public void PlayEntryAnimation()
        {
            if (EntryAnimationObject == null || EntryAnimationObject.Count == 0)
            {
                return;
            }

            if (entryAnimationRoutine != null)
            {
                StopCoroutine(entryAnimationRoutine);
            }

            foreach (Transform item in EntryAnimationObject)
            {
                if (item != null)
                {
                    item.localScale = Vector3.zero;
                }
            }

            entryAnimationRoutine = StartCoroutine(AnimateEntryStagger());
        }

        private IEnumerator AnimateEntryStagger()
        {
            float duration = Mathf.Max(0.01f, entryAnimationDurationSeconds);
            foreach (Transform item in EntryAnimationObject)
            {
                if (item == null)
                {
                    continue;
                }

                StartCoroutine(ScaleUpEntryItem(item, duration));
                if (entryAnimationStaggerSeconds > 0f)
                {
                    yield return new WaitForSeconds(entryAnimationStaggerSeconds);
                }
            }

            entryAnimationRoutine = null;
        }

        private IEnumerator ScaleUpEntryItem(Transform target, float duration)
        {
            Vector3 fullScale = Vector3.one;
            target.localScale = Vector3.zero;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = fullScale * t;
                yield return null;
            }

            target.localScale = fullScale;
        }

        #endregion

        public void SetLevel(string id, string name, string difficulty, bool isFtueLevel = false)
        {
            // Cached so Enable/DisableHardLevelUI can re-render the label with the right
            // "Hard Level - X"/"Level - X" prefix - they fire both before this (DisableHardLevelUI,
            // from ClearErrorCells at load) and after it (EnableHardLevelUI, once the hard-level
            // badge lands), so whichever runs last has to be able to rebuild the same text.
            lastLevelId = id;
            lastLevelName = name;
            lastIsFtueLevel = isFtueLevel;
            RefreshLevelLabel();
        }

        private void RefreshLevelLabel()
        {
            if (string.IsNullOrEmpty(lastLevelId) && string.IsNullOrEmpty(lastLevelName))
            {
                return;
            }

            SetText(levelLabel, dailyChallengeDate.HasValue
                ? FormatDailyChallengeDate(dailyChallengeDate.Value)
                : FormatLevelNumber(lastLevelId, lastLevelName, isHardLevel, lastIsFtueLevel, displayLevelNumber));
        }

        // Called by CatFlapGameplayManager with its own CurrentLevelNumber - see
        // displayLevelNumber for why the label can't just read the loaded level's id/name.
        public void SetDisplayLevelNumber(int levelNumber)
        {
            displayLevelNumber = Mathf.Max(0, levelNumber);
            RefreshLevelLabel();
        }

        public void SetDailyChallengeDate(System.DateTime? date)
        {
            dailyChallengeDate = date;
        }

        // The attempts/hearts HUD's own RectTransform - used to point the "Beware! Life gets
        // reduced on wrong moves" one-time FTUE tip at it (see
        // CatFlapGameplayManager.ShowLevelAttemptTipIfNeeded).
        public RectTransform AttemptsHudRectTransform => attemptsHudRoot != null ? attemptsHudRoot.transform as RectTransform : null;

        public void SetSettingsButtonVisible(bool visible)
        {
            settingsButtonRoot?.SetActive(visible);
        }

        // Lets CatFlapGameplayManager continue this same start-time stagger for the booster
        // panel (the 4th group in the sequence, owned outside this class) - its start delay is
        // 3 * this gap, keeping "top HUD -> life -> rules -> booster" evenly spaced.
        public float EntranceGroupStaggerSeconds => entranceGroupStaggerSeconds;

        /// <summary>
        /// Plays the in-game HUD's level-start entrance: after initialDelaySeconds (a shared
        /// head-start before any HUD element moves - the board's own entrance animation is not
        /// held back by this, see CatFlapGameplayManager.PlayLevelStartEntranceSequence), the top
        /// HUD group (coins/level number/settings) starts sliding down from above, the life
        /// pips' base starts sliding in one stagger gap later, and the rules panel starts scaling
        /// up in place two gaps later - each group starts without waiting for an earlier one to
        /// finish (the same sequence continues with the booster panel, owned outside this class).
        /// Calls onComplete once every assigned group has finished; a group left unassigned in
        /// the Inspector is simply skipped rather than blocking the others.
        /// </summary>
        public void PlayHudEntranceAnimation(float initialDelaySeconds, System.Action onComplete)
        {
            int pendingGroups = 0;
            if (topHudGroupRoot != null)
            {
                pendingGroups++;
            }

            if (attemptsHudRoot != null)
            {
                pendingGroups++;
            }

            if (rulesPanelGroupRoot != null)
            {
                pendingGroups++;
            }

            if (pendingGroups == 0)
            {
                onComplete?.Invoke();
                return;
            }

            int remaining = pendingGroups;
            void GroupComplete()
            {
                remaining--;
                if (remaining <= 0)
                {
                    onComplete?.Invoke();
                }
            }

            float initialDelay = Mathf.Max(0f, initialDelaySeconds);
            float stagger = Mathf.Max(0f, entranceGroupStaggerSeconds);

            if (topHudGroupRoot != null)
            {
                StartCoroutine(SlideInRoutine(topHudGroupRoot, new Vector2(0f, topHudSlideDistance), initialDelay, topHudSlideDurationSeconds, GroupComplete));
            }

            if (attemptsHudRoot != null)
            {
                StartCoroutine(AnimateLifeEntranceRoutine(initialDelay + stagger * 1f, GroupComplete));
            }

            if (rulesPanelGroupRoot != null)
            {
                StartCoroutine(ScaleInRoutine(rulesPanelGroupRoot, initialDelay + stagger * 2f, rulesScaleDurationSeconds, GroupComplete));
            }
        }

        // Slides the life pips' base in from above, then scales each currently-visible pip up in
        // place once the base finishes - mirrors the "base slides in, then each life object scales
        // up" order from the feature spec. Only completes once every pip has finished popping in.
        private IEnumerator AnimateLifeEntranceRoutine(float startDelay, System.Action onComplete)
        {
            RectTransform baseRect = attemptsHudRoot.transform as RectTransform;
            if (baseRect == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            // Set the base's off-screen start position and hide the pips immediately (before the
            // stagger delay below), so nothing flashes at its resting scale/position while this
            // group is waiting for its turn to start.
            for (int index = 0; index < attemptPips.Length; index++)
            {
                if (attemptPips[index] != null)
                {
                    attemptPips[index].transform.localScale = Vector3.zero;
                }
            }

            Vector2 restPosition = baseRect.anchoredPosition;
            Vector2 startPosition = restPosition + new Vector2(0f, lifeBaseSlideDistance);
            baseRect.anchoredPosition = startPosition;

            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            float slideDuration = Mathf.Max(0.01f, lifeBaseSlideDurationSeconds);
            for (float elapsed = 0f; elapsed < slideDuration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / slideDuration));
                baseRect.anchoredPosition = Vector2.LerpUnclamped(startPosition, restPosition, t);
                yield return null;
            }

            baseRect.anchoredPosition = restPosition;

            int pendingPips = 0;
            for (int index = 0; index < attemptPips.Length; index++)
            {
                if (attemptPips[index] != null && attemptPips[index].gameObject.activeSelf)
                {
                    pendingPips++;
                }
            }

            if (pendingPips == 0)
            {
                onComplete?.Invoke();
                yield break;
            }

            int remainingPips = pendingPips;
            void PipComplete()
            {
                remainingPips--;
                if (remainingPips <= 0)
                {
                    onComplete?.Invoke();
                }
            }

            for (int index = 0; index < attemptPips.Length; index++)
            {
                if (attemptPips[index] != null && attemptPips[index].gameObject.activeSelf)
                {
                    StartCoroutine(ScalePipRoutine(attemptPips[index].transform, index * pipScaleStaggerSeconds, PipComplete));
                }
            }
        }

        private IEnumerator ScalePipRoutine(Transform pip, float delay, System.Action onComplete)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            float duration = Mathf.Max(0.01f, pipScaleDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                pip.localScale = Vector3.LerpUnclamped(Vector3.zero, Vector3.one, t);
                yield return null;
            }

            pip.localScale = Vector3.one;
            onComplete?.Invoke();
        }

        private static IEnumerator SlideInRoutine(RectTransform target, Vector2 offset, float startDelay, float duration, System.Action onComplete)
        {
            // Positioned off-screen immediately, before the stagger delay, so it never flashes at
            // its resting position while waiting for this group's turn to start.
            Vector2 restPosition = target.anchoredPosition;
            Vector2 startPosition = restPosition + offset;
            target.anchoredPosition = startPosition;

            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            duration = Mathf.Max(0.01f, duration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.anchoredPosition = Vector2.LerpUnclamped(startPosition, restPosition, t);
                yield return null;
            }

            target.anchoredPosition = restPosition;
            onComplete?.Invoke();
        }

        private static IEnumerator ScaleInRoutine(RectTransform target, float startDelay, float duration, System.Action onComplete)
        {
            // Scaled to zero immediately, before the stagger delay, so it never flashes at full
            // size while waiting for this group's turn to start.
            target.localScale = Vector3.zero;

            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            duration = Mathf.Max(0.01f, duration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = Vector3.LerpUnclamped(Vector3.zero, Vector3.one, t);
                yield return null;
            }

            target.localScale = Vector3.one;
            onComplete?.Invoke();
        }

        public void SetLevelMistakes(int remainingMistakes, int maxMistakes, bool animateHeartLoss = false, bool featureVisible = true)
        {
            SetText(livesLabel, $"{remainingMistakes}/{maxMistakes}");
            RenderAttempts(remainingMistakes, maxMistakes, animateHeartLoss, featureVisible);
        }

        public void SetState(CatFlapGameState state)
        {
            SetText(stateLabel, state.ToString());
        }

        public void SetProgress(CatFlapProgressSnapshot progress)
        {
            SetText(movesLabel, progress.Moves.ToString());
            SetText(timerLabel, FormatTime(progress.ElapsedSeconds));
            SetText(completionLabel, $"{progress.CompletionPercent:0}%");
        }

        public void SetBoosterEconomy(int coins, int undoCount, int undoCost, int hintCount, int hintCost, int fixCount, int fixCost)
        {
            SetText(coinsLabel, coins.ToString());
        }

        public void SetNotesMode(bool enabled)
        {
            SetText(notesLabel, enabled ? "Notes On" : "Notes Off");
        }

        public void ShowMistake(int row, int column)
        {
            SetText(feedbackLabel, $"Mistake at {row},{column}");
        }

        public void ShowRuleResult(CatFlapRuleValidationResult result, bool boardComplete)
        {
            if (result == null || result.IsValid)
            {
                SetText(feedbackLabel, string.Empty);
            }
            else
            {
                SetText(feedbackLabel, result.Violations[0].Message);
            }

            RenderRule(equalCountsRuleIcon, equalCountsRuleOutline, equalCountsRuleRoot, result, CatFlapRule.EqualCounts, boardComplete, ref equalCountsWasFailed, ref equalCountsBlinkRoutine);
            RenderRule(maxTwoAdjacentRuleIcon, maxTwoAdjacentRuleOutline, maxTwoAdjacentRuleRoot, result, CatFlapRule.MaxTwoAdjacent, boardComplete, ref maxTwoAdjacentWasFailed, ref maxTwoAdjacentBlinkRoutine);
            RenderRule(uniqueLinesRuleIcon, uniqueLinesRuleOutline, uniqueLinesRuleRoot, result, CatFlapRule.UniqueLines, boardComplete, ref uniqueLinesWasFailed, ref uniqueLinesBlinkRoutine);
        }

        public void ShowBoosterResult(CatFlapCellRevealResult result)
        {
            if (string.IsNullOrWhiteSpace(result.Message))
            {
                return;
            }

            SetText(feedbackLabel, result.Success ? $"{result.Message} ({result.Row},{result.Column})" : result.Message);
        }

        /// <summary>
        /// Hint-specific feedback with a star rating (1-3, from <see cref="CatFlap.Boosters.CatFlapBoardDifficultyScorer"/>
        /// via CatFlapRuntimeSession.LastHintStars).
        /// NOTE: hintStarsLabel is not yet wired to a prefab element - falls back to feedbackLabel
        /// until a developer assigns a dedicated stars/hint-card Text field in the Inspector.
        /// </summary>
        public void ShowHintResult(CatFlapCellRevealResult result, int stars, int difficultyRemoved)
        {
            if (string.IsNullOrWhiteSpace(result.Message))
            {
                return;
            }

            if (!result.Success)
            {
                SetText(feedbackLabel, result.Message);
                return;
            }

            string message = CatFlapHintTextConfig.Load()?.TryBuildMessage(result.ReasonKey, result.Value, out string configuredMessage) == true
                ? configuredMessage
                : result.Message;

            string starsText = new string('*', Mathf.Clamp(stars, 0, 3));
            SetText(feedbackLabel, $"{message} {starsText}");
        }

        public void ShowBoosterResult(BoosterUseResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                SetText(feedbackLabel, result.Message);
            }
        }

        public void ShowLoadErrors(IReadOnlyList<string> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                SetText(feedbackLabel, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            for (int index = 0; index < errors.Count; index++)
            {
                if (index > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(errors[index]);
            }

            SetText(feedbackLabel, builder.ToString());
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private void RenderAttempts(int remainingMistakes, int maxMistakes, bool animateHeartLoss, bool featureVisible = true)
        {
            if (attemptPips == null || attemptPips.Length == 0)
            {
                return;
            }

            if (!featureVisible)
            {
                if (attemptsHudRoot != null)
                {
                    attemptsHudRoot.SetActive(false);
                }

                for (int index = 0; index < attemptPips.Length; index++)
                {
                    attemptPips[index].gameObject.SetActive(false);
                }

                return;
            }

            if (attemptsHudRoot != null)
            {
                attemptsHudRoot.SetActive(true);
            }

            if (pipWasFilled == null || pipWasFilled.Length != attemptPips.Length)
            {
                pipWasFilled = new bool[attemptPips.Length];
                pipBreakRoutines = new Coroutine[attemptPips.Length];
                for (int index = 0; index < attemptPips.Length; index++)
                {
                    pipWasFilled[index] = true;
                }
            }

            int visibleCount = Mathf.Clamp(maxMistakes, 0, attemptPips.Length);
            int remaining = Mathf.Clamp(remainingMistakes, 0, visibleCount);
            for (int index = 0; index < attemptPips.Length; index++)
            {
                bool visible = index < visibleCount;
                attemptPips[index].gameObject.SetActive(visible);
                if (!visible)
                {
                    pipWasFilled[index] = true;
                    continue;
                }

                bool filled = index < remaining;
                bool justLost = animateHeartLoss && pipWasFilled[index] && !filled;
                pipWasFilled[index] = filled;

                if (justLost)
                {
                    // Background gray heart shows immediately (same Image, same position); the
                    // break FX plays on top of it and fades out, revealing the gray heart beneath.
                    attemptPips[index].sprite = attemptConsumedSprite;
                    TriggerHeartBreak(index);
                }
                else if (!filled)
                {
                    attemptPips[index].sprite = attemptConsumedSprite;
                }
                else
                {
                    attemptPips[index].sprite = attemptFilledSprite;
                }
            }
        }

        // Spawns Heart_Break_vfx as a child of the pip (so it moves/scales with the HUD like the
        // sprite halves do) and plays it. The prefab loops with no stop action of its own, so it's
        // explicitly destroyed after heartBreakVfxLifetimeSeconds rather than left running forever.
        private void PlayHeartBreakVfx(RectTransform pipRect)
        {
            if (heartBreakVfxPrefab == null || pipRect == null)
            {
                return;
            }

            ParticleSystem instance = Instantiate(heartBreakVfxPrefab, pipRect);
            instance.transform.localPosition = Vector3.zero;
            instance.Play();
            Destroy(instance.gameObject, Mathf.Max(0.01f, heartBreakVfxLifetimeSeconds));
        }

        private void TriggerHeartBreak(int pipIndex)
        {
            if (pipBreakRoutines[pipIndex] != null)
            {
                StopCoroutine(pipBreakRoutines[pipIndex]);
            }

            pipBreakRoutines[pipIndex] = StartCoroutine(AnimateHeartBreak(attemptPips[pipIndex]));
        }

        // Scale the heart up briefly, then split it into its two broken-heart halves and fade
        // them out, revealing the gray "consumed" sprite already set on the pip beneath.
        private IEnumerator AnimateHeartBreak(Image pip)
        {
            RectTransform pipRect = pip.transform as RectTransform;
            PlayHeartBreakVfx(pipRect);
            RectTransform overlayRect = CreateOverlayHeart(pipRect, attemptFilledSprite, out Image overlayImage);

            float scaleUpDuration = Mathf.Max(0.01f, heartBreakScaleUpDurationSeconds);
            Vector3 fullScale = Vector3.one;
            Vector3 poppedScale = Vector3.one * heartBreakScaleUpAmount;
            for (float elapsed = 0f; elapsed < scaleUpDuration; elapsed += Time.deltaTime)
            {
                overlayRect.localScale = Vector3.Lerp(fullScale, poppedScale, Mathf.Clamp01(elapsed / scaleUpDuration));
                yield return null;
            }

            overlayRect.localScale = poppedScale;
            Object.Destroy(overlayImage.gameObject);

            RectTransform leftHalfRect = CreateOverlayHeart(pipRect, heartBreakLeftSprite, out Image leftHalfImage);
            RectTransform rightHalfRect = CreateOverlayHeart(pipRect, heartBreakRightSprite, out Image rightHalfImage);
            leftHalfRect.localScale = poppedScale;
            rightHalfRect.localScale = poppedScale;

            Vector2 leftStart = leftHalfRect.anchoredPosition;
            Vector2 rightStart = rightHalfRect.anchoredPosition;
            Vector2 leftEnd = leftStart + new Vector2(-heartBreakSplitOffset, 0f);
            Vector2 rightEnd = rightStart + new Vector2(heartBreakSplitOffset, 0f);

            float fadeDuration = Mathf.Max(0.01f, heartBreakFadeDurationSeconds);
            for (float elapsed = 0f; elapsed < fadeDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                leftHalfRect.anchoredPosition = Vector2.Lerp(leftStart, leftEnd, t);
                rightHalfRect.anchoredPosition = Vector2.Lerp(rightStart, rightEnd, t);
                float alpha = 1f - t;
                SetImageAlpha(leftHalfImage, alpha);
                SetImageAlpha(rightHalfImage, alpha);
                yield return null;
            }

            Object.Destroy(leftHalfImage.gameObject);
            Object.Destroy(rightHalfImage.gameObject);
        }

        private static RectTransform CreateOverlayHeart(RectTransform parent, Sprite sprite, out Image image)
        {
            var overlay = new GameObject("HeartBreakFX", typeof(RectTransform), typeof(Image));
            var overlayRect = (RectTransform)overlay.transform;
            overlayRect.SetParent(parent, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.anchoredPosition = Vector2.zero;

            image = overlay.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;

            return overlayRect;
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        #region Revive Fill Animation

        /// <summary>
        /// Revive/refill flourish: fills each restored attempt pip's fillAmount from 0 to 1 one
        /// heart at a time (not all at once) up to remainingMistakes (NOT always up to
        /// maxMistakes - e.g. the watch-ad grant only restores 1), popping that pip with a brief
        /// scale-up-then-settle bounce the instant IT reaches full before the next heart starts
        /// filling. Pips beyond remainingMistakes (but still within maxMistakes) are left showing
        /// consumed/empty, since they weren't actually restored. Requires each attemptPips Image
        /// already set to Image Type = Filled in the Inspector - this only drives fillAmount, it
        /// never changes Image Type itself. Stops/restarts cleanly if called again before a
        /// previous run finishes. Purely visual - callers still own calling SetLevelMistakes with
        /// the real post-revive values (e.g. for livesLabel).
        /// </summary>
        public void PlayReviveAnimation(int remainingMistakes, int maxMistakes, System.Action onComplete = null)
        {
            if (attemptPips == null || attemptPips.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            if (reviveAnimationRoutine != null)
            {
                StopCoroutine(reviveAnimationRoutine);
            }

            reviveAnimationRoutine = StartCoroutine(AnimateReviveFill(remainingMistakes, maxMistakes, onComplete));
        }

        private IEnumerator AnimateReviveFill(int remainingMistakes, int maxMistakes, System.Action onComplete)
        {
            if (attemptsHudRoot != null)
            {
                attemptsHudRoot.SetActive(true);
            }

            if (pipWasFilled == null || pipWasFilled.Length != attemptPips.Length)
            {
                pipWasFilled = new bool[attemptPips.Length];
                pipBreakRoutines = new Coroutine[attemptPips.Length];
            }

            int visibleCount = Mathf.Clamp(maxMistakes, 0, attemptPips.Length);
            int fillCount = Mathf.Clamp(remainingMistakes, 0, visibleCount);

            // Every visible pip starts at its correct pre-animation look (empty for the ones
            // about to fill, consumed for the ones that weren't actually restored) before any of
            // them starts filling, so nothing flashes wrong while an earlier pip in the sequence
            // is still animating.
            for (int index = 0; index < attemptPips.Length; index++)
            {
                if (attemptPips[index] == null)
                {
                    continue;
                }

                bool visible = index < visibleCount;
                attemptPips[index].gameObject.SetActive(visible);
                if (!visible)
                {
                    pipWasFilled[index] = true;
                    continue;
                }

                attemptPips[index].transform.localScale = Vector3.one;
                pipWasFilled[index] = false;

                if (index < fillCount)
                {
                    attemptPips[index].sprite = attemptFilledSprite;
                    attemptPips[index].fillAmount = 0f;
                }
                else
                {
                    attemptPips[index].sprite = attemptConsumedSprite;
                    attemptPips[index].fillAmount = 0f;
                }
            }

            for (int index = 0; index < fillCount; index++)
            {
                Image pip = attemptPips[index];
                if (pip == null)
                {
                    continue;
                }

                yield return FillPipRoutine(pip);
                yield return ScalePunchRoutine(pip.transform, reviveScaleUpDurationSeconds, reviveScaleUpAmount);
                pipWasFilled[index] = true;
            }

            reviveAnimationRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator FillPipRoutine(Image pip)
        {
            float duration = Mathf.Max(0.01f, reviveFillDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                pip.fillAmount = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }

            pip.fillAmount = 1f;
        }

        // Quick scale-up-then-settle "pop" - same shake shape as AnimateRuleBreakBlink's single
        // pulse, played once a pip finishes filling to punctuate it as the completed/full heart.
        private static IEnumerator ScalePunchRoutine(Transform target, float durationSeconds, float scaleUpAmount)
        {
            Vector3 fullScale = Vector3.one;
            Vector3 poppedScale = Vector3.one * scaleUpAmount;
            float halfDuration = Mathf.Max(0.01f, durationSeconds) * 0.5f;

            for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
            {
                target.localScale = Vector3.LerpUnclamped(fullScale, poppedScale, Mathf.Clamp01(elapsed / halfDuration));
                yield return null;
            }

            for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
            {
                target.localScale = Vector3.LerpUnclamped(poppedScale, fullScale, Mathf.Clamp01(elapsed / halfDuration));
                yield return null;
            }

            target.localScale = fullScale;
        }

        #endregion

        private void RenderRule(
            TMP_Text icon,
            GameObject outline,
            RectTransform ruleRoot,
            CatFlapRuleValidationResult result,
            CatFlapRule rule,
            bool boardComplete,
            ref bool wasFailed,
            ref Coroutine blinkRoutine)
        {
            bool valid = result == null || result.IsRuleValid(rule);
            bool failed = !valid;
            Color backgroundColor = RuleNeutralColor;
            Color iconColor = RuleNeutralIconColor;
            string iconText = string.Empty;

            if (failed)
            {
                backgroundColor = RuleInvalidColor;
                iconColor = RuleInvalidIconColor;
                iconText = "X";
            }
            else if (boardComplete)
            {
                backgroundColor = RuleValidColor;
                iconColor = RuleValidIconColor;
                iconText = "V";
            }

            if (icon != null)
            {
                icon.text = iconText;
                icon.color = iconColor;
            }

            if (outline != null)
            {
                outline.SetActive(failed);
            }

            // Pulse only on the transition into a broken state - never re-trigger on every
            // ShowRuleResult call while the same rule stays broken. (A later PlayRuleBreakBlink
            // call re-triggers this same pulse on every heart-break, regardless of transition.)
            if (failed && !wasFailed)
            {
                TriggerRuleBlink(ruleRoot, ref blinkRoutine);
            }

            wasFailed = failed;
        }

        /// <summary>
        /// Re-pulses the whole chip of every currently-broken rule - called alongside the
        /// HeartBreak FX so losing a life always reinforces which rule caused it, even if that
        /// rule has been broken for a while (RenderRule's own pulse only fires on the transition).
        /// </summary>
        public void PlayRuleBreakBlink()
        {
            if (equalCountsWasFailed)
            {
                TriggerRuleBlink(equalCountsRuleRoot, ref equalCountsBlinkRoutine);
            }

            if (maxTwoAdjacentWasFailed)
            {
                TriggerRuleBlink(maxTwoAdjacentRuleRoot, ref maxTwoAdjacentBlinkRoutine);
            }

            if (uniqueLinesWasFailed)
            {
                TriggerRuleBlink(uniqueLinesRuleRoot, ref uniqueLinesBlinkRoutine);
            }
        }

        /// <summary>
        /// Shows the shared "Wrong Solution" HUD indicator: pops in (scale 0 -> 1) first, then
        /// starts its indefinite fade in/out loop once the pop finishes. Idempotent - calling this
        /// again while already showing does nothing (doesn't restart mid-pop/mid-fade). Stops only
        /// via HideWrongSolutionWarning. wrongSolutionRoot.activeSelf (not a separately-tracked
        /// bool) is the single source of truth for whether this is currently showing - so this
        /// method's own idea of "already showing" can never drift out of sync with what the
        /// GameObject actually is, regardless of what the prefab was left authored as or what else
        /// touches its active state.
        /// </summary>
        public void ShowWrongSolutionWarning()
        {
            if (wrongSolutionRoot == null || wrongSolutionRoot.activeSelf)
            {
                return;
            }

            wrongSolutionRoot.SetActive(true);
            if (wrongSolutionCanvasGroup != null)
            {
                wrongSolutionCanvasGroup.alpha = 1f;
            }

            wrongSolutionRoot.transform.localScale = Vector3.zero;
            if (wrongSolutionFadeRoutine != null)
            {
                StopCoroutine(wrongSolutionFadeRoutine);
            }

            wrongSolutionFadeRoutine = StartCoroutine(AnimateWrongSolutionPopThenFadeLoop());
        }

        /// <summary>
        /// Screen-pixel position of the "Wrong Solution" indicator's own on-screen spot, regardless
        /// of whether it's currently shown - lets other world-space popups (e.g. the appreciation
        /// text) anchor at the exact same visual spot. Uses RectTransformUtility so it resolves
        /// correctly whether the owning Canvas is Screen Space - Overlay or Camera.
        /// </summary>
        public bool TryGetWrongSolutionScreenPosition(out Vector2 screenPosition)
        {
            if (wrongSolutionRoot == null || wrongSolutionRoot.transform is not RectTransform rect)
            {
                screenPosition = default;
                return false;
            }

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            screenPosition = RectTransformUtility.WorldToScreenPoint(canvasCamera, rect.position);
            return true;
        }

        /// <summary>
        /// Fades the "Wrong Solution" indicator out from its current alpha, then disables it. Safe
        /// to call even if it's not currently showing.
        /// </summary>
        public void HideWrongSolutionWarning()
        {
            if (wrongSolutionRoot == null || !wrongSolutionRoot.activeSelf)
            {
                return;
            }

            if (wrongSolutionFadeRoutine != null)
            {
                StopCoroutine(wrongSolutionFadeRoutine);
                wrongSolutionFadeRoutine = null;
            }

            wrongSolutionFadeRoutine = StartCoroutine(AnimateWrongSolutionFadeOut());
        }

        /// <summary>
        /// Instantly disables the Wrong Solution indicator - no fade, no coroutine. Use at level
        /// load so a level that starts clean never plays HideWrongSolutionWarning's fade-out just
        /// because it happened to still be showing from before this level loaded.
        /// </summary>
        public void ForceHideWrongSolutionWarning()
        {
            if (wrongSolutionFadeRoutine != null)
            {
                StopCoroutine(wrongSolutionFadeRoutine);
                wrongSolutionFadeRoutine = null;
            }

            if (wrongSolutionRoot != null)
            {
                wrongSolutionRoot.SetActive(false);
            }
        }

        // Pop (scale 0 -> 1, ease-out-back) plays once, fully opaque; only once that finishes does
        // the indefinite fade in/out loop take over. wrongSolutionFadeRoutine (the Coroutine handle
        // StartCoroutine returns for THIS whole method) covers both beats, so
        // StopCoroutine(wrongSolutionFadeRoutine) from HideWrongSolutionWarning cleanly cancels
        // whichever beat is currently running.
        private IEnumerator AnimateWrongSolutionPopThenFadeLoop()
        {
            yield return AnimateWrongSolutionScale(Vector3.zero, Vector3.one, Mathf.Max(0.01f, wrongSolutionPopDurationSeconds));

            // float stepDuration = Mathf.Max(0.01f, wrongSolutionFadeStepSeconds);
            // while (true)
            // {
            //     yield return AnimateWrongSolutionAlpha(1f, 0f, stepDuration);
            //     yield return AnimateWrongSolutionAlpha(0f, 1f, stepDuration);
            // }
        }

        private IEnumerator AnimateWrongSolutionScale(Vector3 from, Vector3 to, float duration)
        {
            Transform target = wrongSolutionRoot.transform;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.localScale = to;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }

        // Deactivates wrongSolutionRoot only at the very end, same as before - but since
        // HideWrongSolutionWarning's own re-entrancy guard now reads wrongSolutionRoot.activeSelf
        // directly rather than a separate bool, an interrupted fade (StopCoroutine, e.g. a second
        // Hide call or this component/GameObject being disabled mid-fade) can never leave that
        // guard desynced from reality - activeSelf itself IS the state.
        private IEnumerator AnimateWrongSolutionFadeOut()
        {
            float startAlpha = wrongSolutionCanvasGroup != null ? wrongSolutionCanvasGroup.alpha : 1f;
            yield return AnimateWrongSolutionAlpha(startAlpha, 0f, Mathf.Max(0.01f, wrongSolutionFadeStepSeconds));
            wrongSolutionRoot.SetActive(false);
            wrongSolutionFadeRoutine = null;
        }

        private IEnumerator AnimateWrongSolutionAlpha(float from, float to, float duration)
        {
            if (wrongSolutionCanvasGroup == null)
            {
                yield break;
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                wrongSolutionCanvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            wrongSolutionCanvasGroup.alpha = to;
        }

        private void TriggerRuleBlink(RectTransform blinkTarget, ref Coroutine blinkRoutine)
        {
            if (blinkTarget == null)
            {
                return;
            }

            if (blinkRoutine != null)
            {
                StopCoroutine(blinkRoutine);
            }

            blinkRoutine = StartCoroutine(AnimateRuleBreakBlink(blinkTarget));
        }

        // Scale pulse on the rule's outline GameObject - deliberately not an opacity fade, to
        // read as a distinct "this rule just broke" beat.
        private IEnumerator AnimateRuleBreakBlink(RectTransform target)
        {
            Vector3 fullScale = Vector3.one;
            Vector3 pulseScale = Vector3.one * ruleBreakBlinkScale;
            int blinkCount = Mathf.Max(1, ruleBreakBlinkCount);
            float halfStepDuration = Mathf.Max(0.01f, ruleBreakBlinkDurationSeconds / (blinkCount * 2f));

            for (int index = 0; index < blinkCount; index++)
            {
                yield return ScaleRuleChip(target, fullScale, pulseScale, halfStepDuration);
                yield return ScaleRuleChip(target, pulseScale, fullScale, halfStepDuration);
            }

            target.localScale = fullScale;
        }

        private static IEnumerator ScaleRuleChip(RectTransform target, Vector3 from, Vector3 to, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                target.localScale = Vector3.Lerp(from, to, t);
                yield return null;
            }

            target.localScale = to;
        }
        // Levels 1-4 are the FTUE tutorial sequence - shown as one combined "Level 1-4" range
        // rather than each level's own individual number, since they read as a single onboarding
        // unit to the player, not 4 separate levels. Takes priority over the Hard Level prefix
        // below - the two never actually coincide in practice, but FTUE is the broader case.
        private static string FormatLevelNumber(string id, string name, bool isHardLevel, bool isFtueLevel, int progressionNumber)
        {
            if (isFtueLevel)
            {
                return "Level 1-4";
            }

            // The player's progression number wins whenever it's known - id/name describe the
            // level ASSET that loaded, which is the wrong number once loop levels start replaying
            // the 1-100 pack (see displayLevelNumber).
            int number = progressionNumber;
            if (number <= 0)
            {
                number = ExtractLastNumber(id);
            }

            if (number <= 0)
            {
                number = ExtractLastNumber(name);
            }

            string prefix = isHardLevel ? "Hard Level" : "Level";
            return number > 0 ? $"{prefix} - {number}" : prefix;
        }

        private static string FormatDailyChallengeDate(System.DateTime date)
        {
            return date.ToString("MMM d");
        }

        private static int ExtractLastNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            int multiplier = 1;
            int number = 0;
            bool found = false;
            for (int index = value.Length - 1; index >= 0; index--)
            {
                char character = value[index];
                if (character >= '0' && character <= '9')
                {
                    found = true;
                    number += (character - '0') * multiplier;
                    multiplier *= 10;
                }
                else if (found)
                {
                    break;
                }
            }

            return number;
        }

        private static string FormatTime(float elapsedSeconds)
        {
            int totalSeconds = elapsedSeconds < 0f ? 0 : (int)elapsedSeconds;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:00}:{seconds:00}";
        }
    }
}
