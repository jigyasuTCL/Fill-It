using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Shared.GameFlow
{
    public sealed class OutOfLivesPopupController : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private RectTransform popupPanel;

        [Header("Open Sequence")]
        [SerializeField] private CanvasGroup backgroundGroup;
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

        [Header("Button Objects")]
        [SerializeField] private Button watchAdButton;
        [SerializeField] private Button refillButton;
        [SerializeField] private Button giveUpButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button freeReviveButton;

        [Header("Button Roots")]
        [SerializeField] private GameObject watchAdRoot;
        [SerializeField] private GameObject refillRoot;
        [SerializeField] private GameObject freeReviveRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text refillButtonText;
        [SerializeField] private TMP_Text TotalCoinButtonText;
        [SerializeField] private string refillTextFormat = "Refill Lives - {0}";

        [Header("Cat Message")]
        [SerializeField] private TMP_Text catMessageText;
        [SerializeField] private Color catMessageHighlightColor = new Color(1f, 0.54f, 0.4f);

        private string pendingCatMessageValue = string.Empty;

        [Header("Animation Timing")]
        // Entry animation tuned 2x faster than original (durations halved, typewriter speed
        // doubled) - see LevelWinPopupController for the matching change.
        [SerializeField] private float backgroundFadeDuration = 0.125f;
        [SerializeField] private float catScaleDuration = 0.175f;
        [SerializeField] private float buttonScaleDuration = 0.1f;
        [SerializeField] private float typewriterCharsPerSecond = 240f;

        private bool isClosing;
        private bool hasPlayedOpen;

        private int refillCoinCost;
        private bool canAffordRefill;
        private bool freeReviveAvailable;

        private Coroutine openRoutine;

        public UnityEvent WatchAdRequested = new UnityEvent();
        public UnityEvent RefillRequested = new UnityEvent();
        public UnityEvent GiveUpRequested = new UnityEvent();
        public UnityEvent RestartRequested = new UnityEvent();
        public UnityEvent FreeReviveRequested = new UnityEvent();

        int CurrentCoins;
        private void Awake()
        {
            ResetVisualState();
            EnsureCatVideoPlayer();
        }

        // GameObject name of the RawImage that "MangraGames > Bake Cat Video Image Into Prefabs"
        // (Assets/Shared/Features/GameFlow/Editor) permanently authors as catVideoRenderer's
        // sibling - this is only ever LOOKED UP by name at runtime, never built here.
        private const string CatVideoImageName = "CatOpenVideoImage";

        public VideoClip CatOpenVideoClip => catOpenVideoClip;

        // Pure runtime lookup - the RawImage, VideoPlayer, RenderTexture, and clip assignment are
        // all baked into the prefab ahead of time (see BakeCatVideoSetup below, run once via the
        // editor bake command); nothing is created or configured here.
        public void EnsureCatVideoPlayer()
        {
            if (catVideoRenderer == null)
            {
                return;
            }

            if (catVideoPlayer == null)
            {
                catVideoPlayer = catVideoRenderer.GetComponent<VideoPlayer>();
            }

            if (catVideoImage == null && catVideoRenderer.transform.parent != null)
            {
                Transform existing = catVideoRenderer.transform.parent.Find(CatVideoImageName);
                if (existing != null)
                {
                    catVideoImage = existing.GetComponent<RawImage>();
                }
            }
        }

        // Editor-only setup step - called once, at edit time, by CatVideoImagePrefabBaker (via
        // PrefabUtility.LoadPrefabContents) to permanently build the RawImage sibling and
        // configure the VideoPlayer against an already-saved RenderTexture project asset. Takes
        // the RenderTexture as a parameter (rather than creating one itself) so this method never
        // touches UnityEditor.AssetDatabase and stays safe to compile into player builds.
        //
        // MaterialOverride mode (feeding the video straight into catVideoRenderer's own material)
        // was tried first and rejected: a SpriteRenderer's mesh/UVs follow the SOURCE SPRITE's own
        // packed rect/silhouette (its "Tight" mesh), not a full 0-1 quad, so the video frame came
        // out cropped into that tiny sub-region and looked like a plain gray blob. A RawImage
        // always renders a full quad with 0-1 UVs regardless of what texture it holds, so instead
        // this builds one, sized/positioned to exactly match catVideoRenderer's own RectTransform
        // and parented as its sibling (so it inherits the SAME catTransform scale-up animation).
        public void BakeCatVideoSetup(RenderTexture renderTexture)
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

            if (catVideoImage == null && catVideoRenderer.transform.parent != null)
            {
                Transform existing = catVideoRenderer.transform.parent.Find(CatVideoImageName);
                if (existing != null)
                {
                    catVideoImage = existing.GetComponent<RawImage>();
                }
            }

            if (catVideoImage == null)
            {
                var imageObject = new GameObject(CatVideoImageName, typeof(RectTransform), typeof(RawImage));
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

                    // The poster sprite here isn't square (e.g. ~2.7 x 3.7) - fine for a static
                    // sprite, but sizing the RawImage to that same non-square rect squashed the
                    // (square-ish) video horizontally to fit. Use the larger dimension for both
                    // axes so the video renders undistorted, centered on the same spot.
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

            catVideoImage.texture = renderTexture;

            catVideoPlayer.clip = catOpenVideoClip;
            catVideoPlayer.playOnAwake = false;
            catVideoPlayer.isLooping = true;
            catVideoPlayer.waitForFirstFrame = true;
            catVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            catVideoPlayer.targetTexture = renderTexture;
            catVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        // ---------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------
        public void SetCurretCoin(int coinCost) {
            CurrentCoins=coinCost;
            RefreshTexts();
        }
        public void SetRefillCoinCost(int coinCost)
        {
            refillCoinCost = Mathf.Max(0, coinCost);
            RefreshTexts();
        }

        private string defaultTitleText;
        private string defaultRefillTextFormat;

        /// <summary>
        /// Lets a game reuse this popup for another "can't continue" reason (e.g. a timer running
        /// out): replaces the title text and the refill button's format ({0} = coin cost). Pass
        /// null for either to put back what the prefab was authored with.
        /// </summary>
        public void SetTexts(string title, string refillFormat)
        {
            TMP_Text titleLabel = titleImage != null ? titleImage.GetComponentInChildren<TMP_Text>(true) : null;
            if (titleLabel != null)
            {
                if (defaultTitleText == null)
                {
                    defaultTitleText = titleLabel.text;
                }

                titleLabel.text = string.IsNullOrEmpty(title) ? defaultTitleText : title;
            }

            if (defaultRefillTextFormat == null)
            {
                defaultRefillTextFormat = refillTextFormat;
            }

            refillTextFormat = string.IsNullOrEmpty(refillFormat) ? defaultRefillTextFormat : refillFormat;
            RefreshTexts();
        }

        /// <summary>
        /// Levels 1..N get that level's own CatFlapLevelResultTextConfig fail line, in order; any
        /// level past the last configured entry gets a random one from the same list. Called by
        /// CatFlapGameplayManager before this popup is shown.
        /// </summary>
        public void SetLevelResultText(int levelNumber)
        {
            string rawText = CatFlapLevelResultTextConfig.Load()?.GetFailText(levelNumber);
            pendingCatMessageValue = CatFlapLevelResultTextConfig.ApplyHighlight(rawText, catMessageHighlightColor);
        }

        /// <summary>
        /// Shows the coin refill option when affordable.
        /// Otherwise shows the watch-ad option. Overridden entirely by free revive - see
        /// SetFreeReviveAvailable.
        /// </summary>
        public void SetAffordableRefill(bool canAfford)
        {
            canAffordRefill = canAfford;
            RefreshButtonVisibility();
        }

        /// <summary>
        /// When true, shows ONLY the free-revive option (no coin cost, no ad) regardless of
        /// SetAffordableRefill - the caller decides eligibility (e.g. "never used it before").
        /// </summary>
        public void SetFreeReviveAvailable(bool available)
        {
            freeReviveAvailable = available;
            RefreshButtonVisibility();
        }

        public void Show()
        {
            BindButtons();

            RefreshTexts();
            RefreshButtonVisibility();

            isClosing = false;
            hasPlayedOpen = false;

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            SetVisible(true);

            if (popupPanel != null)
            {
                popupPanel.gameObject.SetActive(true);
            }

            ResetVisualState();

            PlayPopupOpenSound();

            openRoutine = StartCoroutine(PlayOpenSequence());
        }

        public void Close(UnityAction onClosed = null)
        {
            if (isClosing)
            {
                return;
            }

            isClosing = true;

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            SetVisible(false);

            isClosing = false;
            hasPlayedOpen = false;

            onClosed?.Invoke();
        }

        // ---------------------------------------------------------
        // OPEN SEQUENCE
        // ---------------------------------------------------------

        private IEnumerator PlayOpenSequence()
        {
            hasPlayedOpen = true;
            refillButtonText.text = string.Format(
                        refillTextFormat,
                        refillCoinCost
                    );
            // 1. Background
            yield return StartCoroutine(
                FadeInBackground()
            );

            // 2. Title, then Cat with a short stagger (not fully parallel, not fully sequential -
            // same 0.85x-duration stagger PlayButtonsSequence uses). The video starts playing
            // immediately (no wait), alongside the title scale-up.
            PlayCatOpenAnimation();
            StartCoroutine(ScaleUp(titleImage, catScaleDuration));
            yield return new WaitForSeconds(catScaleDuration * 0.85f);
            yield return StartCoroutine(
                ScaleUp(catTransform, catScaleDuration, Vector3.one * catTargetScale)
            );

            // 3. Cat scale-in is complete - play the cat message.
            StartCoroutine(PlayCatMessage());

            // 4. Buttons - staggered one at a time, same beat as LevelWinPopupController's
            // ads/continue/home sequence. Only active buttons play, and only active buttons
            // contribute to the total delay - see PlayButtonsSequence.
            yield return StartCoroutine(PlayButtonsSequence());

            openRoutine = null;
        }

        // Same visual slots/order as LevelWinPopupController.PlayButtonsSequence (revive option →
        // Restart → Give Up, standing in for that popup's ads → continue → home), but unlike that
        // one, a hidden slot here doesn't still consume its stagger delay - only buttons that are
        // actually active play AND advance the timing, so e.g. Give Up alone (if it were ever the
        // only active button) pops in immediately with no wasted wait for the other two.
        private IEnumerator PlayButtonsSequence()
        {
            float stagger = buttonScaleDuration * 0.85f;

            RectTransform[] slots =
            {
                GetActiveReviveButtonTransform(),
                GetActiveTransform(restartButton),
                GetActiveTransform(giveUpButton)
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

        // Exactly one of free/coin/ad revive is active at a time (see RefreshButtonVisibility) -
        // this is the "2x button" equivalent slot from LevelWinPopupController, just resolved by
        // which revive option is currently showing instead of a single fixed button.
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
        // CAT MESSAGE
        // ---------------------------------------------------------

        private IEnumerator PlayCatMessage()
        {
            if (catMessageText == null)
            {
                yield break;
            }

            yield return StartCoroutine(
                Typewriter(
                    catMessageText,
                    pendingCatMessageValue
                )
            );
        }

        // ---------------------------------------------------------
        // INDIVIDUAL ANIMATIONS
        // ---------------------------------------------------------

        // Plays the already-baked catOpenVideoClip on a loop for as long as this popup stays open
        // (isLooping/clip/targetTexture are all baked in - see BakeCatVideoSetup) - unlike the old
        // one-shot SpriteAnimator version, this never stops itself or reverts to the static poster
        // sprite on its own; ResetVisualState does that when the popup is closed/reopened.
        // No poster fallback/wait - the RawImage is active and the poster disabled immediately;
        // Play() auto-prepares internally if needed, and waitForFirstFrame keeps whatever's shown
        // held on the last/first decoded frame instead of flashing anything else.
        private void PlayCatOpenAnimation()
        {
            EnsureCatVideoPlayer();

            // Timed to this cat entry animation specifically, not the popup Show()/open call
            // itself (see PlayOpenSequence) - Show() also plays the background fade and title
            // stagger first, so playing it any earlier would land well before the cat is visible.
            // Deliberately NOT gated on catVideoPlayer/catVideoImage below - those depend on the
            // one-time "MangraGames > Bake Cat Video Image Into Prefabs" editor step having
            // already run for this prefab; if it hasn't, the video silently no-ops, but the sound
            // cue for the cat "appearing" still needs to play regardless.
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.LevelFail);

            if (catVideoPlayer == null || catVideoImage == null)
            {
                return;
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = false;
            }

            catVideoImage.gameObject.SetActive(true);
            catVideoPlayer.Play();
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

                float t = Mathf.Clamp01(
                    elapsed / Mathf.Max(0.0001f, backgroundFadeDuration)
                );

                backgroundGroup.alpha = t;

                yield return null;
            }

            backgroundGroup.alpha = 1f;
        }

        private IEnumerator ScaleUp(
            RectTransform target,
            float duration,
            Vector3? targetScale = null)
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

                float t = EaseOutBack(
                    Mathf.Clamp01(
                        elapsed / Mathf.Max(0.0001f, duration)
                    )
                );

                target.localScale = fullScale * t;

                yield return null;
            }

            target.localScale = fullScale;
        }

        private IEnumerator Typewriter(
            TMP_Text label,
            string fullValue)
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

            int totalVisibleCharacters =
                label.textInfo.characterCount;

            label.maxVisibleCharacters = 0;

            float secondsPerChar =
                1f / Mathf.Max(
                    1f,
                    typewriterCharsPerSecond
                );

            for (
                int visible = 1;
                visible <= totalVisibleCharacters;
                visible++)
            {
                label.maxVisibleCharacters = visible;

                yield return new WaitForSeconds(
                    secondsPerChar
                );
            }

            label.maxVisibleCharacters =
                int.MaxValue;
        }

        // ---------------------------------------------------------
        // BUTTONS
        // ---------------------------------------------------------

        // No manual PlayButtonSound() in these four - watchAdButton/refillButton/restartButton/
        // freeReviveButton all already have their own ButtonPressScaleFeedback component playing
        // the click sound on tap; calling this too played it twice on every tap.
        private void RequestWatchAd()
        {
            WatchAdRequested.Invoke();
        }

        private void RequestRefill()
        {
            RefillRequested.Invoke();
        }

        private void RequestGiveUp()
        {
            // Give Up closes this popup (into level fail) rather than granting anything, so it
            // gets the back cue instead of the generic click sound the other buttons use.
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ButtonBack);
            GiveUpRequested.Invoke();
        }

        private void RequestRestart()
        {
            RestartRequested.Invoke();
        }

        private void RequestFreeRevive()
        {
            FreeReviveRequested.Invoke();
        }

        private void BindButtons()
        {
            UnbindButtons();

            watchAdButton?.onClick.AddListener(
                RequestWatchAd
            );

            refillButton?.onClick.AddListener(
                RequestRefill
            );

            giveUpButton?.onClick.AddListener(
                RequestGiveUp
            );

            restartButton?.onClick.AddListener(
                RequestRestart
            );

            freeReviveButton?.onClick.AddListener(
                RequestFreeRevive
            );
        }

        private void UnbindButtons()
        {
            watchAdButton?.onClick.RemoveListener(
                RequestWatchAd
            );

            refillButton?.onClick.RemoveListener(
                RequestRefill
            );

            giveUpButton?.onClick.RemoveListener(
                RequestGiveUp
            );

            restartButton?.onClick.RemoveListener(
                RequestRestart
            );

            freeReviveButton?.onClick.RemoveListener(
                RequestFreeRevive
            );
        }

        // ---------------------------------------------------------
        // UI REFRESH
        // ---------------------------------------------------------

        // Exactly one of free revive / coin revive / ad revive is visible at a time: free revive
        // takes priority (once used, it's gone for good - see the caller's PlayerData flag),
        // otherwise coin refill if affordable, otherwise watch-ad.
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

        private void RefreshTexts()
        {
            SetText(catMessageText, string.Empty);
            SetText(
                refillButtonText,
                string.Format(
                    refillTextFormat,
                    refillCoinCost
                )
            );
            SetText(TotalCoinButtonText,CurrentCoins.ToString()) ;
        }

        // ---------------------------------------------------------
        // RESET / POSITIONS
        // ---------------------------------------------------------

        private void ResetVisualState()
        {
            // Resets both video image and poster before each open - PlayCatOpenAnimation only
            // hides the poster/shows the video when catVideoPlayer/catVideoImage are both present
            // (baked in ahead of time), so this is the fallback state if either is missing.
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
            // before this runs (see Show()), so it's safe to reset all five unconditionally here.
            ResetButtonScale(freeReviveButton);
            ResetButtonScale(refillButton);
            ResetButtonScale(watchAdButton);
            ResetButtonScale(restartButton);
            ResetButtonScale(giveUpButton);

            if (refillButtonText != null)
            {
                refillButtonText.maxVisibleCharacters = int.MaxValue;
            }

            if (catMessageText != null)
            {
                catMessageText.maxVisibleCharacters = int.MaxValue;
            }
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

        private void SetVisible(bool visible)
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(visible);
            }

            gameObject.SetActive(visible);
        }

        private static void SetText(
            TMP_Text label,
            string value)
        {
            if (label != null)
            {
                label.text = value;
                label.maxVisibleCharacters =
                    int.MaxValue;
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;

            float f = t - 1f;

            return 1f +
                   c3 * f * f * f +
                   c1 * f * f;
        }

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance
                .PlaySoundAction?
                .Invoke(global::SoundName.PopupOpen);
        }
    }
}