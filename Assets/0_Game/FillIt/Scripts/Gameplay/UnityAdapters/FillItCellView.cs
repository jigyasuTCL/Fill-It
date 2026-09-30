using System.Collections;
using FillIt.Rules;
using FillIt.Views;
using TMPro;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// One Fillomino tile on the world-space board. Finished regions take their number's colour
    /// and are bridged to same-region neighbours so each region reads as one solid shape; regions
    /// still being built show a softer tint; broken regions go red. Given (clue) numbers are bold.
    /// </summary>
    public sealed class FillItCellView : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private SpriteRenderer bridgeRight;
        [SerializeField] private SpriteRenderer bridgeDown;
        [SerializeField] private SpriteRenderer selectionOutline;
        [SerializeField] private SpriteRenderer hintOutline;
        [SerializeField] private SpriteRenderer hintedDot;
        [SerializeField] private TMP_Text valueLabel;
        [SerializeField] private TMP_Text hintPreviewLabel;
        [SerializeField] private ParticleSystem completeParticle;
        [SerializeField] private ParticleSystem hintParticle;

        [Header("Sprites")]
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Sprite filledSprite;

        [Header("Fonts")]
        [SerializeField] private TMP_FontAsset playerFont;
        [SerializeField] private TMP_FontAsset givenFont;

        [Header("Colours - index = number (0 unused)")]
        [SerializeField] private Color[] numberColors =
        {
            Color.white,
            new Color32(0xFF, 0xE0, 0xB2, 0xFF),
            new Color32(0xFF, 0xC9, 0x4D, 0xFF),
            new Color32(0xB5, 0xE8, 0x6A, 0xFF),
            new Color32(0x5F, 0xD3, 0xA7, 0xFF),
            new Color32(0x8E, 0xE3, 0xF0, 0xFF),
            new Color32(0x86, 0xC5, 0xFF, 0xFF),
            new Color32(0xB9, 0xA5, 0xFF, 0xFF),
            new Color32(0xFF, 0x9C, 0xC2, 0xFF),
            new Color32(0xFF, 0xB3, 0x6B, 0xFF),
        };
        [SerializeField] private Color emptyTileColor = Color.white;
        [Tooltip("How far a region-in-progress tile's colour is blended toward white.")]
        [Range(0f, 1f)] [SerializeField] private float openTintWhiteBlend = 0.62f;
        [Range(0f, 1f)] [SerializeField] private float givenOpenTintWhiteBlend = 0.4f;
        [SerializeField] private Color errorTileColor = new Color32(0xFF, 0x8F, 0x8F, 0xFF);
        [SerializeField] private Color givenTextColor = new Color32(0x2B, 0x33, 0x5E, 0xFF);
        [SerializeField] private Color playerTextColor = new Color32(0x3F, 0x5F, 0xC8, 0xFF);
        [SerializeField] private Color doneTextColor = new Color32(0x2B, 0x33, 0x5E, 0xFF);
        [SerializeField] private Color errorTextColor = new Color32(0x9B, 0x16, 0x2C, 0xFF);
        [SerializeField] private Color selectionColor = new Color32(0x3F, 0xB8, 0xFF, 0xFF);
        [SerializeField] private Color chainColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        [SerializeField] private Color hintColor = new Color32(0xFF, 0x9E, 0x3D, 0xFF);

        [Header("Animation")]
        [SerializeField] private float popDurationSeconds = 0.16f;
        [SerializeField] private float popScale = 1.18f;
        [SerializeField] private float completeDurationSeconds = 0.32f;
        [SerializeField] private float completeScale = 1.14f;
        [SerializeField] private float shakeDurationSeconds = 0.35f;
        [SerializeField] private float shakeDistance = 0.08f;

        private FillItCellVisualState state;
        private Coroutine scaleRoutine;
        private Coroutine shakeRoutine;
        private Coroutine winPulseRoutine;
        private Coroutine enterRoutine;
        private Vector3 visualBaseLocalPosition;
        private bool hintTargetActive;
        private float bridgeLength;
        private float hintPulseTime;

        public int Index { get; private set; } = -1;
        public bool IsGiven => state.IsGiven;
        public bool IsAnimating => scaleRoutine != null || shakeRoutine != null || enterRoutine != null;
        public bool IsPlayingLevelWinPulse => winPulseRoutine != null;

        private Transform Visual => visualRoot != null ? visualRoot : transform;

        private void Awake()
        {
            visualBaseLocalPosition = Visual.localPosition;
            SetRendererActive(selectionOutline, false);
            SetRendererActive(hintOutline, false);
            SetRendererActive(hintedDot, false);
            if (hintPreviewLabel != null)
            {
                hintPreviewLabel.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            scaleRoutine = null;
            shakeRoutine = null;
            winPulseRoutine = null;
            enterRoutine = null;
            Visual.localScale = Vector3.one;
            Visual.localPosition = visualBaseLocalPosition;
        }

        private void Update()
        {
            if (!hintTargetActive || hintOutline == null)
            {
                return;
            }

            hintPulseTime += Time.unscaledDeltaTime;
            Color color = hintColor;
            color.a = 0.55f + 0.45f * Mathf.Sin(hintPulseTime * 6f);
            hintOutline.color = color;
        }

        /// <summary>The tile's own size at scale 1, in the cell's local units - the board scales cells from this.</summary>
        public float TileSize => background != null && background.sprite != null
            ? background.sprite.bounds.size.x * background.transform.localScale.x
            : 1f;

        public Color GetNumberColor(int value)
        {
            return numberColors != null && value > 0 && value < numberColors.Length ? numberColors[value] : Color.white;
        }

        /// <param name="spacingInCellUnits">Centre-to-centre distance to the next cell, in this cell's local units - sizes the region bridges.</param>
        public void Bind(int index, float spacingInCellUnits)
        {
            Index = index;
            bridgeLength = Mathf.Max(0f, spacingInCellUnits);
            LayoutBridge(bridgeRight, horizontal: true);
            LayoutBridge(bridgeDown, horizontal: false);
        }

        public void SetState(FillItCellVisualState newState, bool animateValueChange)
        {
            bool valueChanged = newState.Value != state.Value;
            state = newState;
            Paint();
            if (animateValueChange && valueChanged && !state.IsEmpty)
            {
                PlayScalePunch(popDurationSeconds, popScale);
            }
        }

        public void ShowHintTarget(int value)
        {
            hintTargetActive = true;
            hintPulseTime = 0f;
            SetRendererActive(hintOutline, true);
            if (hintPreviewLabel != null)
            {
                hintPreviewLabel.text = value.ToString();
                hintPreviewLabel.color = new Color(hintColor.r, hintColor.g, hintColor.b, 0.85f);
                hintPreviewLabel.gameObject.SetActive(true);
            }

            if (valueLabel != null)
            {
                valueLabel.gameObject.SetActive(false);
            }
        }

        public void ClearHintTarget()
        {
            hintTargetActive = false;
            SetRendererActive(hintOutline, false);
            if (hintPreviewLabel != null)
            {
                hintPreviewLabel.gameObject.SetActive(false);
            }

            Paint();
        }

        public void PlayRegionComplete(float delaySeconds)
        {
            StartCoroutine(RegionCompleteRoutine(delaySeconds));
        }

        public void PlayMistakeShake()
        {
            if (shakeRoutine != null)
            {
                StopCoroutine(shakeRoutine);
            }

            shakeRoutine = StartCoroutine(ShakeRoutine());
        }

        public void PlayHintParticle()
        {
            if (hintParticle != null)
            {
                hintParticle.Play();
            }
        }

        public void PrepareForEnterAnimation()
        {
            Visual.localScale = Vector3.zero;
        }

        public void PlayEnterAnimation(float delaySeconds, float durationSeconds)
        {
            if (enterRoutine != null)
            {
                StopCoroutine(enterRoutine);
            }

            enterRoutine = StartCoroutine(EnterRoutine(delaySeconds, durationSeconds));
        }

        public void PlayLevelWinPulse(float delaySeconds, float durationSeconds, float scaleMultiplier)
        {
            if (winPulseRoutine != null)
            {
                StopCoroutine(winPulseRoutine);
            }

            winPulseRoutine = StartCoroutine(WinPulseRoutine(delaySeconds, durationSeconds, scaleMultiplier));
        }

        private void Paint()
        {
            bool empty = state.IsEmpty;
            Color numberColor = GetNumberColor(state.Value);
            Color tile;
            Color text;
            if (empty)
            {
                tile = emptyTileColor;
                text = playerTextColor;
            }
            else if (state.ShowError)
            {
                tile = errorTileColor;
                text = errorTextColor;
            }
            else if (state.IsDone)
            {
                tile = numberColor;
                text = doneTextColor;
            }
            else
            {
                tile = Color.Lerp(numberColor, Color.white, state.IsGiven ? givenOpenTintWhiteBlend : openTintWhiteBlend);
                text = state.IsGiven ? givenTextColor : playerTextColor;
            }

            if (background != null)
            {
                Sprite sprite = empty ? emptySprite : filledSprite;
                if (sprite != null)
                {
                    background.sprite = sprite;
                }

                background.color = tile;
            }

            // Bridges share the tile colour so a region reads as one connected shape.
            PaintBridge(bridgeRight, state.ConnectRight && !empty, tile);
            PaintBridge(bridgeDown, state.ConnectDown && !empty, tile);

            if (valueLabel != null)
            {
                valueLabel.gameObject.SetActive(!empty && !hintTargetActive);
                valueLabel.text = empty ? string.Empty : state.Value.ToString();
                valueLabel.color = text;
                TMP_FontAsset font = state.IsGiven ? givenFont : playerFont;
                if (font != null && valueLabel.font != font)
                {
                    valueLabel.font = font;
                }
            }

            SetRendererActive(hintedDot, state.IsHinted && !empty);

            if (selectionOutline != null)
            {
                bool showOutline = state.IsInDragChain || state.IsSelected;
                SetRendererActive(selectionOutline, showOutline);
                if (showOutline)
                {
                    selectionOutline.color = state.IsInDragChain ? chainColor : selectionColor;
                }
            }
        }

        private static void PaintBridge(SpriteRenderer bridge, bool visible, Color color)
        {
            if (bridge == null)
            {
                return;
            }

            bridge.enabled = visible;
            bridge.color = color;
        }

        private void LayoutBridge(SpriteRenderer bridge, bool horizontal)
        {
            if (bridge == null || bridge.sprite == null)
            {
                return;
            }

            // Spans from this cell's centre to the neighbour's centre, slightly narrower than the
            // tile so the tiles' rounded corners still show at the join.
            Vector2 spriteSize = bridge.sprite.bounds.size;
            float tileSize = background != null && background.sprite != null ? background.sprite.bounds.size.x : 1f;
            float thickness = tileSize * 0.62f;
            float length = bridgeLength;
            Transform bridgeTransform = bridge.transform;
            if (horizontal)
            {
                bridgeTransform.localPosition = new Vector3(length * 0.5f, 0f, 0.01f);
                bridgeTransform.localScale = new Vector3(length / Mathf.Max(0.0001f, spriteSize.x), thickness / Mathf.Max(0.0001f, spriteSize.y), 1f);
            }
            else
            {
                bridgeTransform.localPosition = new Vector3(0f, -length * 0.5f, 0.01f);
                bridgeTransform.localScale = new Vector3(thickness / Mathf.Max(0.0001f, spriteSize.x), length / Mathf.Max(0.0001f, spriteSize.y), 1f);
            }
        }

        private void PlayScalePunch(float duration, float peak)
        {
            if (scaleRoutine != null)
            {
                StopCoroutine(scaleRoutine);
            }

            scaleRoutine = StartCoroutine(ScalePunchRoutine(duration, peak));
        }

        private IEnumerator ScalePunchRoutine(float duration, float peak)
        {
            Transform target = Visual;
            float half = Mathf.Max(0.01f, duration) * 0.5f;
            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                target.localScale = Vector3.one * Mathf.Lerp(1f, peak, elapsed / half);
                yield return null;
            }

            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                target.localScale = Vector3.one * Mathf.Lerp(peak, 1f, EaseOut(elapsed / half));
                yield return null;
            }

            target.localScale = Vector3.one;
            scaleRoutine = null;
        }

        private IEnumerator RegionCompleteRoutine(float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            if (completeParticle != null)
            {
                completeParticle.Play();
            }

            PlayScalePunch(completeDurationSeconds, completeScale);
        }

        private IEnumerator ShakeRoutine()
        {
            Transform target = Visual;
            float duration = Mathf.Max(0.01f, shakeDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / duration;
                float offset = Mathf.Sin(t * Mathf.PI * 8f) * shakeDistance * (1f - t);
                target.localPosition = visualBaseLocalPosition + new Vector3(offset, 0f, 0f);
                yield return null;
            }

            target.localPosition = visualBaseLocalPosition;
            shakeRoutine = null;
        }

        private IEnumerator EnterRoutine(float delaySeconds, float durationSeconds)
        {
            Transform target = Visual;
            target.localScale = Vector3.zero;
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            float duration = Mathf.Max(0.01f, durationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                target.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            target.localScale = Vector3.one;
            enterRoutine = null;
        }

        private IEnumerator WinPulseRoutine(float delaySeconds, float durationSeconds, float scaleMultiplier)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            if (completeParticle != null)
            {
                completeParticle.Play();
            }

            Transform target = Visual;
            float half = Mathf.Max(0.01f, durationSeconds) * 0.5f;
            float peak = Mathf.Max(1f, scaleMultiplier);
            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                target.localScale = Vector3.one * Mathf.Lerp(1f, peak, EaseOut(elapsed / half));
                yield return null;
            }

            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                target.localScale = Vector3.one * Mathf.Lerp(peak, 1f, EaseOut(elapsed / half));
                yield return null;
            }

            target.localScale = Vector3.one;
            winPulseRoutine = null;
        }

        private static void SetRendererActive(SpriteRenderer renderer, bool active)
        {
            if (renderer != null)
            {
                renderer.enabled = active;
            }
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }
    }
}
