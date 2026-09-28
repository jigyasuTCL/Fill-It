using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Ftue;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    public sealed class InfoPanelController : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [Header("Panel")]
        [SerializeField] private Button closeButton;
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private GameObject panelRoot;

        [Header("Pages")]
        [SerializeField] private RectTransform pageContainer;
        [SerializeField] private List<GameObject> pages = new List<GameObject>();

        // Data-driven page text - index-matched to pages. Reuses FtueStepsAsset (Assets/Shared/Features/Ftue)
        // rather than a new type since it already models "an ordered list of authored description text".
        // Assign Rule1..Rule5 (Assets/Resources/Configs/Info) here; entry i overrides the TMP text found
        // on pages[i]. Leave an entry empty/null to keep whatever text is already on that page in the prefab.
        [Header("Info Text (data-driven, index-matched to Pages)")]
        [SerializeField] private List<FtueStepsAsset> infoTextAssets = new List<FtueStepsAsset>();

        [SerializeField] private float swipeThreshold = 80f;
        [SerializeField] private float pageAnimationDuration = 0.35f;
        [SerializeField] private Ease pageEase = Ease.OutCubic;

        [Header("Page Indicators")]
        [SerializeField] private List<Image> pageIndicators = new List<Image>();

        [SerializeField] private float activeIndicatorWidth = 180f;
        [SerializeField] private float inactiveIndicatorWidth = 128f;

        [SerializeField] private Color activeIndicatorColor = Color.white;
        [SerializeField] private Color inactiveIndicatorColor = Color.gray;

        [SerializeField] private float indicatorAnimationDuration = 0.25f;
        [SerializeField] private Ease indicatorEase = Ease.OutCubic;

        [Header("Bottom UI")]
        [SerializeField] private RectTransform bottomUI;
        [SerializeField] private float bottomAnimationDuration = 0.35f;
        [SerializeField] private float bottomStartOffset = 100f;
        [SerializeField] private Ease bottomEase = Ease.OutBack;

        private int currentPageIndex;
        private Vector2 dragStartPosition;

        private bool isAnimating;

        private Vector2 bottomDefaultPosition;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (bottomUI != null)
                bottomDefaultPosition = bottomUI.anchoredPosition;

            ApplyInfoTexts();
            ShowPage(0);
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);

            KillPageTweens();
            KillIndicatorTweens();

            if (bottomUI != null)
                bottomUI.DOKill();
        }

        // =========================================================
        // OPEN / CLOSE
        // =========================================================

        // Saved/restored (not just forced back to true) rather than always re-enabling on close -
        // same convention as SettingsPopupController.Open/Close - so this never wrongly turns
        // gameplay input back on if something else had already disabled it before this panel
        // opened (e.g. this panel opening on top of another popup).
        private bool gameplayInputWasEnabled;

        public void Open()
        {
            ShowPage(0);

            gameplayInputWasEnabled = Shared.GameFlow.InputManager.GamePlayInputEnabled;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;

            if (popupAnimator != null)
            {
                popupAnimator.Open();
            }
            else if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            PlayBottomAnimation();
        }

        public void Close()
        {
            KillPageTweens();
            KillIndicatorTweens();

            if (bottomUI != null)
                bottomUI.DOKill();

            if (popupAnimator != null)
            {
                // Input stays blocked until the close animation actually finishes.
                popupAnimator.Close(() => RestoreGameplayInputNextFrame());
            }
            else
            {
                if (panelRoot != null)
                {
                    panelRoot.SetActive(false);
                }

                RestoreGameplayInputNextFrame();
            }
        }

        // Waits one frame before restoring input - guards against the same tap that closed this
        // panel also landing on the board underneath in the same frame.
        private void RestoreGameplayInputNextFrame()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = gameplayInputWasEnabled;
        }

        // =========================================================
        // SWIPE
        // =========================================================

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (isAnimating)
                return;

            dragStartPosition = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Required for Unity UI drag handling.
            // Page movement happens on OnEndDrag.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (isAnimating)
                return;

            float deltaX =
                eventData.position.x -
                dragStartPosition.x;

            if (Mathf.Abs(deltaX) < swipeThreshold)
                return;

            bool swipeLeft = deltaX < 0f;

            int targetIndex =
                currentPageIndex +
                (swipeLeft ? 1 : -1);

            AnimateToPage(targetIndex, swipeLeft);
        }

        // =========================================================
        // PAGE ANIMATION
        // =========================================================

        private void AnimateToPage(
            int targetIndex,
            bool swipeLeft)
        {
            if (isAnimating)
                return;

            if (targetIndex < 0 ||
                targetIndex >= pages.Count)
                return;

            GameObject currentPage =
                pages[currentPageIndex];

            GameObject nextPage =
                pages[targetIndex];

            if (currentPage == null ||
                nextPage == null)
                return;

            RectTransform currentRect =
                currentPage.GetComponent<RectTransform>();

            RectTransform nextRect =
                nextPage.GetComponent<RectTransform>();

            if (currentRect == null ||
                nextRect == null)
                return;

            isAnimating = true;

            KillPageTweens();

            float width = GetPageWidth();

            if (width <= 0f)
                width = Screen.width;

            float direction =
                swipeLeft ? 1f : -1f;

            // -----------------------------------------------------
            // Position both pages BEFORE starting animation.
            // -----------------------------------------------------

            currentRect.anchoredPosition =
                Vector2.zero;

            nextRect.anchoredPosition =
                new Vector2(
                    width * direction,
                    0f
                );

            currentPage.SetActive(true);
            nextPage.SetActive(true);

            // -----------------------------------------------------
            // Animate both pages at exactly the same time.
            // -----------------------------------------------------

            Sequence sequence = DOTween.Sequence();

            sequence.Join(
                currentRect
                    .DOAnchorPos(
                        new Vector2(
                            -width * direction,
                            0f
                        ),
                        pageAnimationDuration
                    )
                    .SetEase(pageEase)
            );

            sequence.Join(
                nextRect
                    .DOAnchorPos(
                        Vector2.zero,
                        pageAnimationDuration
                    )
                    .SetEase(pageEase)
            );

            // -----------------------------------------------------
            // Animate the page indicator at the same time.
            // -----------------------------------------------------

            AnimatePageIndicators(targetIndex);

            sequence.OnComplete(() =>
            {
                nextRect.anchoredPosition =
                    Vector2.zero;

                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages[i] == null)
                        continue;

                    pages[i].SetActive(
                        i == targetIndex
                    );
                }

                currentPageIndex =
                    targetIndex;

                isAnimating = false;
            });
        }

        // =========================================================
        // PAGE TEXT (data-driven)
        // =========================================================

        private void ApplyInfoTexts()
        {
            if (pages == null || infoTextAssets == null)
                return;

            for (int i = 0; i < pages.Count && i < infoTextAssets.Count; i++)
            {
                FtueStepsAsset infoAsset = infoTextAssets[i];

                if (infoAsset == null ||
                    infoAsset.Steps == null ||
                    infoAsset.Steps.Count == 0)
                    continue;

                GameObject page = pages[i];

                if (page == null)
                    continue;

                InfoPageView pageView =
                    page.GetComponent<InfoPageView>();

                if (pageView != null)
                {
                    pageView.SetText(infoAsset.Steps[0].Description);
                    continue;
                }

                // Fallback for pages not yet wired with InfoPageView.
                TMP_Text pageText =
                    page.GetComponentInChildren<TMP_Text>(true);

                if (pageText != null)
                    pageText.text = infoAsset.Steps[0].Description;
            }
        }

        // =========================================================
        // PAGE SETUP
        // =========================================================

        private void ShowPage(int index)
        {
            if (pages == null ||
                pages.Count == 0)
                return;

            KillPageTweens();

            currentPageIndex =
                Mathf.Clamp(
                    index,
                    0,
                    pages.Count - 1
                );

            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] == null)
                    continue;

                RectTransform rect =
                    pages[i].GetComponent<RectTransform>();

                if (rect != null)
                    rect.anchoredPosition =
                        Vector2.zero;

                pages[i].SetActive(
                    i == currentPageIndex
                );
            }

            // Update indicators immediately.
            SetPageIndicatorsImmediate(currentPageIndex);

            isAnimating = false;
        }

        // =========================================================
        // PAGE INDICATORS
        // =========================================================

        private void AnimatePageIndicators(int activeIndex)
        {
            if (pageIndicators == null ||
                pageIndicators.Count == 0)
                return;

            KillIndicatorTweens();

            for (int i = 0; i < pageIndicators.Count; i++)
            {
                Image indicator =
                    pageIndicators[i];

                if (indicator == null)
                    continue;

                RectTransform rect =
                    indicator.rectTransform;

                bool isActive =
                    i == activeIndex;

                float targetWidth =
                    isActive
                        ? activeIndicatorWidth
                        : inactiveIndicatorWidth;

                Color targetColor =
                    isActive
                        ? activeIndicatorColor
                        : inactiveIndicatorColor;

                // Animate width.
                rect
                    .DOSizeDelta(
                        new Vector2(
                            targetWidth,
                            rect.sizeDelta.y
                        ),
                        indicatorAnimationDuration
                    )
                    .SetEase(indicatorEase);

                // Animate color.
                indicator
                    .DOColor(
                        targetColor,
                        indicatorAnimationDuration
                    )
                    .SetEase(indicatorEase);
            }
        }

        private void SetPageIndicatorsImmediate(int activeIndex)
        {
            if (pageIndicators == null ||
                pageIndicators.Count == 0)
                return;

            for (int i = 0; i < pageIndicators.Count; i++)
            {
                Image indicator =
                    pageIndicators[i];

                if (indicator == null)
                    continue;

                RectTransform rect =
                    indicator.rectTransform;

                bool isActive =
                    i == activeIndex;

                float targetWidth =
                    isActive
                        ? activeIndicatorWidth
                        : inactiveIndicatorWidth;

                Color targetColor =
                    isActive
                        ? activeIndicatorColor
                        : inactiveIndicatorColor;

                rect.sizeDelta =
                    new Vector2(
                        targetWidth,
                        rect.sizeDelta.y
                    );

                indicator.color =
                    targetColor;
            }
        }

        private void KillIndicatorTweens()
        {
            if (pageIndicators == null)
                return;

            for (int i = 0; i < pageIndicators.Count; i++)
            {
                if (pageIndicators[i] == null)
                    continue;

                pageIndicators[i]
                    .rectTransform
                    .DOKill();

                pageIndicators[i].DOKill();
            }
        }

        // =========================================================
        // PAGE WIDTH
        // =========================================================

        private float GetPageWidth()
        {
            if (pageContainer != null)
            {
                float width =
                    pageContainer.rect.width;

                if (width > 0f)
                    return width;
            }

            if (pages != null &&
                pages.Count > 0)
            {
                GameObject page =
                    pages[currentPageIndex];

                if (page != null)
                {
                    RectTransform rect =
                        page.GetComponent<RectTransform>();

                    if (rect != null &&
                        rect.rect.width > 0f)
                    {
                        return rect.rect.width;
                    }
                }
            }

            return 0f;
        }

        // =========================================================
        // KILL PAGE TWEENS
        // =========================================================

        private void KillPageTweens()
        {
            if (pages == null)
                return;

            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] == null)
                    continue;

                RectTransform rect =
                    pages[i].GetComponent<RectTransform>();

                if (rect != null)
                    rect.DOKill();
            }
        }

        // =========================================================
        // BOTTOM UI ANIMATION
        // =========================================================

        private void PlayBottomAnimation()
        {
            if (bottomUI == null)
                return;

            bottomUI.DOKill();

            bottomUI.anchoredPosition =
                bottomDefaultPosition +
                Vector2.down *
                bottomStartOffset;

            bottomUI
                .DOAnchorPos(
                    bottomDefaultPosition,
                    bottomAnimationDuration
                )
                .SetEase(bottomEase);
        }
    }
}