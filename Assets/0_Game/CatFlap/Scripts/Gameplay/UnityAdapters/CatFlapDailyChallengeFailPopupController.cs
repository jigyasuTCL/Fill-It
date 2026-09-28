using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Daily Challenge "Out of Lives" popup - shown when out of mistakes during a Daily Challenge
    /// sub-level. The bridge (CatFlapDailyChallengeBridge) decides labels/actions/whether Back
    /// shows; this component only presents them.
    ///
    /// Structural/animation clone of Shared.GameFlow.OutOfLivesPopupController - background fade,
    /// title/cat pop with the same looping cat-open VideoPlayer, typewriter progress message, AND
    /// (like that popup) a Revive option (ad/coin, mutually exclusive) alongside a separate
    /// Restart button - primaryButton IS that Restart button; its own ad requirement is enforced
    /// by DailyChallengeController's ad-gate (ShouldRequireAdForSelectedDate), not by this
    /// component or the bridge.
    /// </summary>
    public sealed class CatFlapDailyChallengeFailPopupController : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private GameObject root;
        [Tooltip("The challenge's date (e.g. \"September 3\"), shown at the top of the popup.")]
        [SerializeField] private TMP_Text dateText;

        [Header("Open Sequence")]
        [SerializeField] private CanvasGroup backgroundGroup;
        [Tooltip("Predefined title art baked into the prefab (e.g. \"Level Failed\") - this only " +
                 "scales it up, it does not set any text at runtime.")]
        [SerializeField] private RectTransform titleImage;
        [SerializeField] private RectTransform catTransform;
        // catTransform's asset is authored tiny relative to its sibling UI, so its open-sequence
        // scale-up target isn't Vector3.one like everything else - see ScaleUp's targetScale param.
        [SerializeField] private float catTargetScale = 300f;

        [Header("Cat Open Animation (VideoPlayer, plays once when this popup appears)")]
        [SerializeField] private SpriteRenderer catVideoRenderer;
        [SerializeField] private VideoClip catOpenVideoClip;

        private VideoPlayer catVideoPlayer;
        private RawImage catVideoImage;
        private RenderTexture catVideoRenderTexture;

        [Header("Restart Button - watches an ad (enforced by DailyChallengeController)")]
        [Tooltip("Predefined label baked into the prefab (\"Restart\") - always the same action, so " +
                 "this only needs a click handler, not a runtime-set label.")]
        [SerializeField] private Button primaryButton;
        [SerializeField] private Button backButton;

        [Header("Revive Button (ads/coins, mutually exclusive - mirrors Shared.GameFlow.OutOfLivesPopupController)")]
        [SerializeField] private Button watchAdButton;
        [SerializeField] private Button refillButton;
        [SerializeField] private Button freeReviveButton;
        [SerializeField] private GameObject watchAdRoot;
        [SerializeField] private GameObject refillRoot;
        [SerializeField] private GameObject freeReviveRoot;
        [SerializeField] private TMP_Text refillButtonText;
        [SerializeField] private string refillTextFormat = "Revive {0}";

        private int refillCoinCost;
        private bool canAffordRefill;
        private bool freeReviveAvailable;

        /// <summary>Fired when the ad-revive option is tapped - the bridge grants the reward.</summary>
        public UnityEvent WatchAdRequested = new UnityEvent();
        /// <summary>Fired when the coin-revive option is tapped - the bridge spends the coins and grants the reward.</summary>
        public UnityEvent RefillRequested = new UnityEvent();
        /// <summary>Fired when the free-revive option is tapped - the bridge grants the reward.</summary>
        public UnityEvent FreeReviveRequested = new UnityEvent();

        [Header("Progress Message")]
        [SerializeField] private TMP_Text progressLabel;

        [Header("Animation Timing")]
        // Entry animation tuned 2x faster than original (durations halved, typewriter speed
        // doubled) - see LevelWinPopupController for the matching change.
        [SerializeField] private float backgroundFadeDuration = 0.125f;
        [SerializeField] private float catScaleDuration = 0.175f;
        // Same default as Shared.GameFlow.OutOfLivesPopupController.buttonScaleDuration - the two
        // popups' button entry stagger is meant to read as the same beat.
        [SerializeField] private float buttonScaleDuration = 0.1f;
        [SerializeField] private float typewriterCharsPerSecond = 240f;

        private Action onBack;
        private Action onPrimary;
        private Coroutine openRoutine;

        private void Awake()
        {
            ResetVisualState();
            EnsureCatVideoPlayer();
        }

        private void OnEnable()
        {
            primaryButton?.onClick.AddListener(OnPrimaryClicked);
            backButton?.onClick.AddListener(OnBackClicked);
            watchAdButton?.onClick.AddListener(RequestWatchAd);
            refillButton?.onClick.AddListener(RequestRefill);
            freeReviveButton?.onClick.AddListener(RequestFreeRevive);
        }

        private void OnDisable()
        {
            primaryButton?.onClick.RemoveListener(OnPrimaryClicked);
            backButton?.onClick.RemoveListener(OnBackClicked);
            watchAdButton?.onClick.RemoveListener(RequestWatchAd);
            refillButton?.onClick.RemoveListener(RequestRefill);
            freeReviveButton?.onClick.RemoveListener(RequestFreeRevive);

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }
        }

        // VideoPlayer is added/configured in code rather than authored on the prefab, so this
        // works whether or not the prefab already carries one - see PlayCatOpenAnimation.
        //
        // MaterialOverride mode (feeding the video straight into catVideoRenderer's own material)
        // was tried first and rejected: a SpriteRenderer's mesh/UVs follow the SOURCE SPRITE's own
        // packed rect/silhouette (its "Tight" mesh), not a full 0-1 quad, so the video frame came
        // out cropped into that tiny sub-region and looked like a plain gray blob. A RawImage
        // always renders a full quad with 0-1 UVs regardless of what texture it holds, so instead
        // this creates one at runtime, sized/positioned to exactly match catVideoRenderer's own
        // RectTransform and parented as its sibling (so it inherits the SAME catTransform scale-up
        // animation), and points the VideoPlayer's RenderTexture output at it. The RawImage stays
        // hidden until PlayCatOpenAnimation actually plays, and catVideoRenderer (showing the
        // static poster sprite) is hidden only while the video is visible.
        private void EnsureCatVideoPlayer()
        {
            if (catVideoRenderer == null)
            {
                return;
            }

            if (catVideoPlayer == null)
            {
                catVideoPlayer = catVideoRenderer.GetComponent<VideoPlayer>();
                if (catVideoPlayer == null)
                {
                    catVideoPlayer = catVideoRenderer.gameObject.AddComponent<VideoPlayer>();
                }
            }

            if (catVideoImage == null)
            {
                var imageObject = new GameObject("CatOpenVideoImage", typeof(RectTransform), typeof(RawImage));
                RectTransform sourceRect = catVideoRenderer.GetComponent<RectTransform>();
                RectTransform imageRect = (RectTransform)imageObject.transform;
                imageRect.SetParent(sourceRect != null ? sourceRect.parent : catVideoRenderer.transform.parent, false);

                // catVideoRenderer's RectTransform.sizeDelta (800x800, authored purely because a
                // Canvas forces every descendant to carry a RectTransform) is NOT what actually
                // renders - a SpriteRenderer ignores RectTransform width/height entirely; its real
                // on-screen footprint is sprite.bounds x its own transform.localScale. Using
                // sizeDelta made the video balloon far past the sprite's real size once catTransform
                // (this GameObject's shared parent) scaled up to catTargetScale. Both the sprite and
                // this sibling RawImage live under the SAME parent, so deriving the size from the
                // sprite's own bounds (already excluding that shared parent scale) lines them up.
                Vector2 renderedSize = sourceRect != null ? sourceRect.sizeDelta : new Vector2(800f, 800f);
                if (catVideoRenderer.sprite != null)
                {
                    Vector3 spriteSize = catVideoRenderer.sprite.bounds.size;
                    Vector3 localScale = catVideoRenderer.transform.localScale;

                    // The poster sprite here isn't square - fine for a static sprite, but sizing
                    // the RawImage to that same non-square rect squashed the (square-ish) video
                    // horizontally to fit. Use the larger dimension for both axes so the video
                    // renders undistorted, centered on the same spot.
                    float uniformSize = Mathf.Max(spriteSize.x * localScale.x, spriteSize.y * localScale.y);
                    renderedSize = new Vector2(uniformSize, uniformSize);
                }

                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = renderedSize;
                imageRect.anchoredPosition = sourceRect != null ? sourceRect.anchoredPosition : Vector2.zero;
                if (sourceRect != null)
                {
                    imageRect.SetSiblingIndex(sourceRect.GetSiblingIndex() + 1);
                }

                catVideoImage = imageObject.GetComponent<RawImage>();
                catVideoImage.raycastTarget = false;
                imageObject.SetActive(false);
            }

            if (catVideoRenderTexture == null)
            {
                int width = catOpenVideoClip != null && catOpenVideoClip.width > 0 ? (int)catOpenVideoClip.width : 720;
                int height = catOpenVideoClip != null && catOpenVideoClip.height > 0 ? (int)catOpenVideoClip.height : 720;
                catVideoRenderTexture = new RenderTexture(width, height, 0);
                catVideoImage.texture = catVideoRenderTexture;
            }

            catVideoPlayer.playOnAwake = false;
            catVideoPlayer.isLooping = true;
            catVideoPlayer.waitForFirstFrame = true;
            catVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            catVideoPlayer.targetTexture = catVideoRenderTexture;
            catVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        // ---------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------

        private void RequestWatchAd()
        {
            WatchAdRequested.Invoke();
        }

        private void RequestRefill()
        {
            RefillRequested.Invoke();
        }

        private void RequestFreeRevive()
        {
            FreeReviveRequested.Invoke();
        }

        public void SetRefillCoinCost(int coinCost)
        {
            refillCoinCost = Mathf.Max(0, coinCost);
            RefreshRefillText();
        }

        /// <summary>Shows the coin refill option when affordable. Otherwise shows the watch-ad option. Overridden entirely by free revive - see SetFreeReviveAvailable.</summary>
        public void SetAffordableRefill(bool canAfford)
        {
            canAffordRefill = canAfford;
            RefreshButtonVisibility();
        }

        /// <summary>When true, shows ONLY the free-revive option (no coin cost, no ad) regardless of SetAffordableRefill - the caller decides eligibility.</summary>
        public void SetFreeReviveAvailable(bool available)
        {
            freeReviveAvailable = available;
            RefreshButtonVisibility();
        }

        // Exactly one of free/coin/ad revive is visible at a time - same priority as
        // Shared.GameFlow.OutOfLivesPopupController.RefreshButtonVisibility.
        private void RefreshButtonVisibility()
        {
            if (freeReviveRoot != null)
            {
                freeReviveRoot.SetActive(freeReviveAvailable);
            }

            if (refillRoot != null)
            {
                refillRoot.SetActive(!freeReviveAvailable && canAffordRefill);
            }

            if (watchAdRoot != null)
            {
                watchAdRoot.SetActive(!freeReviveAvailable && !canAffordRefill);
            }
        }

        private void RefreshRefillText()
        {
            SetText(refillButtonText, string.Format(refillTextFormat, refillCoinCost));
        }

        /// <summary>
        /// Force-hides all three revive options regardless of affordability/eligibility - use when
        /// this popup is shown as the FINAL lost-result screen (after Give Up) rather than the
        /// still-recoverable "out of lives" moment, where reviving isn't an available action at all.
        /// </summary>
        public void HideReviveOptions()
        {
            if (freeReviveRoot != null)
            {
                freeReviveRoot.SetActive(false);
            }

            if (refillRoot != null)
            {
                refillRoot.SetActive(false);
            }

            if (watchAdRoot != null)
            {
                watchAdRoot.SetActive(false);
            }
        }

        /// <summary>Sets the challenge date shown at the top of the popup (e.g. "September 3").</summary>
        public void SetDate(string date)
        {
            SetText(dateText, date);
        }

        // title is unused here - the Fail popup's title is predefined art baked into titleImage's
        // prefab content, not set at runtime (mirrors CatFlapDailyChallengeWinPopupController).
        // primaryButton's label is likewise predefined art/text ("Restart") baked into the prefab.
        public void Show(string title, string progressText, bool showBack, Action primaryAction, Action backAction)
        {
            if (root == null)
            {
                return;
            }

            backButton?.gameObject.SetActive(showBack);
            RefreshRefillText();
            RefreshButtonVisibility();

            onPrimary = primaryAction;
            onBack = backAction;

            root.SetActive(true);
            ResetVisualState();

            PlayPopupOpenSound();

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
            }
            openRoutine = StartCoroutine(PlayOpenSequence(progressText));
        }

        public void Hide()
        {
            if (root == null || !root.activeSelf)
            {
                return;
            }

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            // No close animation, same as the normal Win/Fail popups - GameFlowViewAdapter swaps
            // pages off with a direct SetActive(false) rather than an outro.
            root.SetActive(false);
        }

        // ---------------------------------------------------------
        // OPEN SEQUENCE
        // ---------------------------------------------------------

        private IEnumerator PlayOpenSequence(string progressText)
        {
            // 1. Background
            yield return StartCoroutine(FadeInBackground());

            // 2. Title + Cat together. Video prep/first-frame-decode starts NOW, alongside the
            // scale-up, rather than only after it finishes - otherwise the poster sprite sits
            // visible for the whole scale-up and only swaps to video right as it completes,
            // reading as a separate "then character animation plays" beat instead of one
            // continuous reveal.
            StartCoroutine(PlayCatOpenAnimation());
            yield return StartCoroutine(RunParallel(
                ScaleUp(titleImage, catScaleDuration),
                ScaleUp(catTransform, catScaleDuration, Vector3.one * catTargetScale)
            ));

            // 3. Cat scale-in is complete - type out the progress message.
            yield return StartCoroutine(Typewriter(progressLabel, progressText));

            // 4. Buttons - staggered one at a time, same beat as
            // Shared.GameFlow.OutOfLivesPopupController.PlayButtonsSequence: the currently-active
            // revive option (free/coin/ad, mutually exclusive), then Restart, then Give Up. Only
            // active buttons play, and only active buttons contribute to the total delay.
            yield return StartCoroutine(PlayButtonsSequence());

            openRoutine = null;
        }

        private IEnumerator PlayButtonsSequence()
        {
            float stagger = buttonScaleDuration * 0.85f;

            RectTransform[] slots =
            {
                GetActiveReviveButtonTransform(),
                GetActiveTransform(primaryButton),
                GetActiveTransform(backButton)
            };

            for (int i = 0; i < slots.Length; i++)
            {
                RectTransform slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                StartCoroutine(ScaleUp(slot, buttonScaleDuration));
                yield return new WaitForSeconds(stagger);
            }
        }

        private RectTransform GetActiveReviveButtonTransform()
        {
            if (freeReviveRoot != null && freeReviveRoot.activeSelf)
            {
                return GetActiveTransform(freeReviveButton);
            }

            if (refillRoot != null && refillRoot.activeSelf)
            {
                return GetActiveTransform(refillButton);
            }

            if (watchAdRoot != null && watchAdRoot.activeSelf)
            {
                return GetActiveTransform(watchAdButton);
            }

            return null;
        }

        private static RectTransform GetActiveTransform(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy)
            {
                return null;
            }

            return button.transform as RectTransform;
        }

        // ---------------------------------------------------------
        // INDIVIDUAL ANIMATIONS
        // ---------------------------------------------------------

        // Plays catOpenVideoClip on a loop for as long as this popup stays open (isLooping is set
        // in EnsureCatVideoPlayer) - never stops itself or reverts to the static poster sprite on
        // its own; ResetVisualState does that when the popup is closed/reopened.
        private IEnumerator PlayCatOpenAnimation()
        {
            EnsureCatVideoPlayer();
            if (catVideoPlayer == null || catOpenVideoClip == null || catVideoImage == null)
            {
                yield break;
            }

            catVideoPlayer.Stop();
            catVideoPlayer.clip = catOpenVideoClip;

            // Swapping to the RawImage/hiding the poster sprite BEFORE the video actually has a
            // decoded frame ready shows a blank/stale RenderTexture for a frame or two. Prepare +
            // wait for the first real frame first, so the swap only happens once there's real
            // video content to show.
            catVideoPlayer.Prepare();
            while (!catVideoPlayer.isPrepared)
            {
                yield return null;
            }

            catVideoPlayer.Play();
            while (!catVideoPlayer.isPlaying)
            {
                yield return null;
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = false;
            }

            catVideoImage.gameObject.SetActive(true);

            // Timed to the cat actually becoming visible, same as the shared
            // OutOfLivesPopupController's own PlayCatOpenAnimation - not to this coroutine
            // starting, which would land well before the video has a decoded frame ready.
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.LevelFail);
        }

        private IEnumerator FadeInBackground()
        {
            if (backgroundGroup == null)
            {
                yield break;
            }
            float elapsed = 0f;
            backgroundGroup.alpha = 0f;
            while (elapsed < backgroundFadeDuration)
            {
                elapsed += Time.deltaTime;
                backgroundGroup.alpha = Mathf.Clamp01(elapsed / backgroundFadeDuration);
                yield return null;
            }
            backgroundGroup.alpha = 1f;
        }

        private IEnumerator ScaleUp(RectTransform target, float duration, Vector3? targetScale = null)
        {
            if (target == null)
            {
                yield break;
            }
            Vector3 fullScale = targetScale ?? Vector3.one;
            target.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = fullScale * t;
                yield return null;
            }
            target.localScale = fullScale;
        }

        private IEnumerator Typewriter(TMP_Text label, string fullValue)
        {
            if (label == null)
            {
                yield break;
            }
            if (string.IsNullOrEmpty(fullValue))
            {
                label.text = string.Empty;
                yield break;
            }

            label.text = fullValue;
            label.ForceMeshUpdate();
            int totalVisibleCharacters = label.textInfo.characterCount;
            label.maxVisibleCharacters = 0;

            float secondsPerChar = 1f / Mathf.Max(1f, typewriterCharsPerSecond);
            for (int visible = 1; visible <= totalVisibleCharacters; visible++)
            {
                label.maxVisibleCharacters = visible;
                yield return new WaitForSeconds(secondsPerChar);
            }

            label.maxVisibleCharacters = int.MaxValue;
        }

        private IEnumerator RunParallel(params IEnumerator[] routines)
        {
            var running = new Coroutine[routines.Length];
            for (int index = 0; index < routines.Length; index++)
            {
                running[index] = StartCoroutine(routines[index]);
            }
            for (int index = 0; index < running.Length; index++)
            {
                if (running[index] != null)
                {
                    yield return running[index];
                }
            }
        }

        // ---------------------------------------------------------
        // BUTTONS
        // ---------------------------------------------------------

        private void OnPrimaryClicked()
        {
            // No manual PlayButtonSound() - primaryButton already has its own
            // ButtonPressScaleFeedback component playing the click sound on tap.
            Action action = onPrimary;
            onPrimary = null;
            onBack = null;
            Hide();
            action?.Invoke();
        }

        private void OnBackClicked()
        {
            // Back closes this popup without retrying, so it gets the back cue instead of the
            // generic click sound the retry button uses.
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ButtonBack);
            Action action = onBack;
            onPrimary = null;
            onBack = null;
            Hide();
            action?.Invoke();
        }

        // ---------------------------------------------------------
        // RESET / POSITIONS
        // ---------------------------------------------------------

        private void ResetVisualState()
        {
            // Guards against a popup closed mid-video (PlayCatOpenAnimation's own StartCoroutine
            // isn't tracked/cancelled by OnDisable) leaving the video image stuck visible and the
            // poster sprite stuck hidden the next time this popup opens.
            if (catVideoPlayer != null)
            {
                catVideoPlayer.Stop();
            }

            if (catVideoImage != null)
            {
                catVideoImage.gameObject.SetActive(false);
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = true;
            }

            if (backgroundGroup != null)
            {
                backgroundGroup.alpha = 0f;
            }

            if (titleImage != null)
            {
                titleImage.localScale = Vector3.zero;
            }

            if (catTransform != null)
            {
                catTransform.localScale = Vector3.zero;
            }

            // Scale each button entry slot back to zero, same as titleImage/catTransform above -
            // PlayButtonsSequence scales whichever ones are active back up to one. Root
            // active/inactive state itself is RefreshButtonVisibility's job, already applied
            // before this runs (see Show()), so it's safe to reset all of them unconditionally.
            ResetButtonScale(freeReviveButton);
            ResetButtonScale(refillButton);
            ResetButtonScale(watchAdButton);
            ResetButtonScale(primaryButton);
            ResetButtonScale(backButton);

            SetText(progressLabel, string.Empty);
        }

        private static void ResetButtonScale(Button button)
        {
            if (button != null)
            {
                button.transform.localScale = Vector3.zero;
            }
        }

        // ---------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
                label.maxVisibleCharacters = int.MaxValue;
            }
        }


        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.PopupOpen);
        }
    }
}
