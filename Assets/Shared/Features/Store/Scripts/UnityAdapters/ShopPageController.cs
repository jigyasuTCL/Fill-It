using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Currency;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Shared.Store.UnityAdapters
{
    public sealed class ShopPageController : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        // Gems -> Coins rename - FormerlySerializedAs preserves existing Inspector wiring.
        [FormerlySerializedAs("gemBalanceText")]
        [SerializeField] private TMP_Text coinBalanceText;
        [SerializeField] private CoinContainerView coinContainer;
        [SerializeField] private Transform fullWidthContent;
        [SerializeField] private Transform gridContent;
        [SerializeField] private GameObject RowContainerPrefab;
        [SerializeField] private StoreFullWidthCardView fullWidthTemplate;
        [SerializeField] private StoreGridPackCardView gridTemplate;
        [SerializeField] private float refreshIntervalSeconds = 1f;

        [Header("Entry Animation")]
        [Tooltip("Elements shown above the card list (e.g. coin container, settings button) that " +
                 "pop in first, staggered the same way as the cards below - see topBarEntryStagger/" +
                 "Duration/Ease. The card list (entryAnimationTargets) only starts popping in once " +
                 "every one of these has finished.")]
        [SerializeField] private List<GameObject> topBarEntryTargets = new List<GameObject>();
        [SerializeField] private float topBarEntryStaggerSeconds = 0.05f;
        [SerializeField] private float topBarEntryDurationSeconds = 0.25f;
        [SerializeField] private Ease topBarEntryEase = Ease.OutBack;

        [Tooltip("Delay added per card index in the staggered pop-in - e.g. the 4th card starts " +
                 "cardEntryStaggerSeconds * 3 after the 1st.")]
        [SerializeField] private float cardEntryStaggerSeconds = 0.05f;
        [SerializeField] private float cardEntryDurationSeconds = 0.25f;
        [SerializeField] private Ease cardEntryEase = Ease.OutBack;

        private const int GridCardsPerRow = 3;

        private readonly List<GameObject> spawnedCards = new List<GameObject>();
        private readonly List<GameObject> spawnedGridRows = new List<GameObject>();
        // Every individual card (fullWidthContent AND gridContent alike, in spawn order) that
        // should pop in one-by-one on the page's first Populate() - see PlayEntranceAnimation.
        private readonly List<Transform> entryAnimationTargets = new List<Transform>();
        private StoreManager storeManager;
        private Coroutine refreshRoutine;
        private Coroutine entryAnimationRoutine;
        private Transform currentGridRow;
        private int currentGridRowCardCount;

        public UnityEvent BackRequested = new UnityEvent();
        public StoreItemLifecycleEvent PurchaseRequested = new StoreItemLifecycleEvent();

        private void OnEnable()
        {
            BindButtons();
            Populate();
            StopRefreshLoop();
            refreshRoutine = StartCoroutine(RefreshLoop());
            PlayEntranceAnimation();
        }

        private void OnDisable()
        {
            StopRefreshLoop();
            UnbindButtons();
        }

        public void Configure(StoreManager manager)
        {
            storeManager = manager;
        }

        // Pops the top-bar elements (topBarEntryTargets) into view first, then every card under
        // fullWidthContent AND gridContent one after another (zero -> full scale, ease back out)
        // once the top bar has finished - see entryAnimationTargets. Only called from OnEnable:
        // RefreshLoop's periodic Populate() calls intentionally don't replay this, or the whole
        // page would re-stage itself every refresh tick while the shop stays open.
        private void PlayEntranceAnimation()
        {
            if (entryAnimationRoutine != null)
            {
                StopCoroutine(entryAnimationRoutine);
                entryAnimationRoutine = null;
            }

            for (int index = 0; index < topBarEntryTargets.Count; index++)
            {
                GameObject target = topBarEntryTargets[index];
                if (target != null)
                {
                    target.transform.DOKill();
                    target.transform.localScale = Vector3.zero;
                }
            }

            for (int index = 0; index < entryAnimationTargets.Count; index++)
            {
                Transform card = entryAnimationTargets[index];
                if (card != null)
                {
                    card.DOKill();
                    card.localScale = Vector3.zero;
                }
            }

            entryAnimationRoutine = StartCoroutine(PlayEntranceAnimationNextFrame());
        }

        // Deferred by one frame so each card's layout group has had a chance to position it
        // before it starts animating - same historical fix every other PopupScaleAnimator caller
        // relies on (see StorePopupController.PlayOpenNextFrame) for content not being fully
        // settled yet on the very first show.
        private IEnumerator PlayEntranceAnimationNextFrame()
        {
            yield return null;

            float topBarTotalDuration = 0f;

            for (int index = 0; index < topBarEntryTargets.Count; index++)
            {
                GameObject target = topBarEntryTargets[index];
                if (target == null)
                {
                    continue;
                }

                float delay = index * topBarEntryStaggerSeconds;

                target.transform.DOScale(Vector3.one, topBarEntryDurationSeconds)
                    .SetEase(topBarEntryEase)
                    .SetDelay(delay)
                    .SetUpdate(true);

                topBarTotalDuration = Mathf.Max(topBarTotalDuration, delay + topBarEntryDurationSeconds);
            }

            for (int index = 0; index < entryAnimationTargets.Count; index++)
            {
                Transform card = entryAnimationTargets[index];
                if (card == null)
                {
                    continue;
                }

                card.DOScale(Vector3.one, cardEntryDurationSeconds)
                    .SetEase(cardEntryEase)
                    .SetDelay(topBarTotalDuration + index * cardEntryStaggerSeconds)
                    .SetUpdate(true);
            }

            entryAnimationRoutine = null;
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, refreshIntervalSeconds));
                Populate();
            }
        }

        private void StopRefreshLoop()
        {
            if (refreshRoutine != null)
            {
                StopCoroutine(refreshRoutine);
                refreshRoutine = null;
            }
        }

        private void Populate()
        {
            if (storeManager == null)
            {
                return;
            }

            SetText(coinBalanceText, storeManager.CurrentCoinBalance().ToString());
            coinContainer?.Refresh();
            ClearSpawnedCards();

            IReadOnlyList<StoreItemDefinition> items = storeManager.Config.Items;
            for (int index = 0; index < items.Count; index++)
            {
                StoreItemDefinition item = items[index];
                if (item == null || !item.Enabled)
                {
                    continue;
                }

                StoreItemStatus status = storeManager.GetStatus(item);

                // Remove Ads is a permanent one-time unlock - once purchased, stop showing its
                // card in the shop entirely rather than leaving a disabled "Sold Out" row behind.
                if (item.Type == StoreItemType.RemoveAds && Shared.PlayerData.PlayerData.Instance.NoAdsPurchased)
                {
                    continue;
                }

                if (item.Type == StoreItemType.CoinPack)
                {
                    SpawnGridCard(item, status);
                }
                else
                {
                    SpawnFullWidthCard(item, status);
                }
            }
        }

        private void SpawnFullWidthCard(StoreItemDefinition item, StoreItemStatus status)
        {
            if (fullWidthTemplate == null || fullWidthContent == null)
            {
                return;
            }

            StoreFullWidthCardView card = Instantiate(fullWidthTemplate, fullWidthContent);
            card.gameObject.SetActive(true);
            card.Bind(item, status);
            card.ActionRequested.RemoveListener(HandleItemAction);
            card.ActionRequested.AddListener(HandleItemAction);
            spawnedCards.Add(card.gameObject);
            entryAnimationTargets.Add(card.transform);
        }

        private void SpawnGridCard(StoreItemDefinition item, StoreItemStatus status)
        {
            if (gridTemplate == null || gridContent == null)
            {
                return;
            }

            Transform rowParent = GetOrCreateGridRow();

            StoreGridPackCardView card = Instantiate(gridTemplate, rowParent);
            card.gameObject.SetActive(true);
            card.Bind(item, status);
            card.ActionRequested.RemoveListener(HandleItemAction);
            card.ActionRequested.AddListener(HandleItemAction);
            entryAnimationTargets.Add(card.transform);
        }

        // Groups grid cards into rows of GridCardsPerRow, spawning a new RowContainerPrefab
        // instance (parented under gridContent) as each row fills up. Falls back to gridContent
        // itself if RowContainerPrefab isn't assigned, so cards still spawn (ungrouped) rather
        // than silently disappearing.
        private Transform GetOrCreateGridRow()
        {
            if (RowContainerPrefab == null)
            {
                return gridContent;
            }

            if (currentGridRow == null || currentGridRowCardCount >= GridCardsPerRow)
            {
                GameObject rowInstance = Instantiate(RowContainerPrefab, gridContent);
                rowInstance.SetActive(true);
                spawnedGridRows.Add(rowInstance);

                currentGridRow = rowInstance.transform;
                currentGridRowCardCount = 0;
            }

            currentGridRowCardCount++;
            return currentGridRow;
        }

        private void ClearSpawnedCards()
        {
            for (int index = 0; index < spawnedCards.Count; index++)
            {
                if (spawnedCards[index] != null)
                {
                    Destroy(spawnedCards[index]);
                }
            }

            spawnedCards.Clear();

            // Destroying each row also destroys the (up to GridCardsPerRow) grid cards parented
            // under it - they're never added to spawnedCards individually.
            for (int index = 0; index < spawnedGridRows.Count; index++)
            {
                if (spawnedGridRows[index] != null)
                {
                    Destroy(spawnedGridRows[index]);
                }
            }

            spawnedGridRows.Clear();
            currentGridRow = null;
            currentGridRowCardCount = 0;

            // Destroyed cards above are the same Transforms tracked here - just drop the
            // (now-stale) references rather than re-killing tweens on already-destroyed objects.
            entryAnimationTargets.Clear();
        }

        private void HandleItemAction(string itemId)
        {
            StoreItemDefinition item = storeManager?.FindItem(itemId);
            if (item == null)
            {
                return;
            }

            if (item.Type == StoreItemType.FreeGrant)
            {
                storeManager.TryClaimFreeReward(itemId);
                Populate();
                return;
            }

            storeManager.RequestPurchase(itemId);
            PurchaseRequested.Invoke(itemId);
        }

        private void RequestBack()
        {
            // No manual PlayButtonSound() - backButton has no ButtonPressScaleFeedback of its
            // own, but it's still covered by the global ButtonClickSfxBinder (every Button under
            // CatFlapCanvas), which already played this on the same tap.
            BackRequested.Invoke();
        }

        private void BindButtons()
        {
            backButton?.onClick.RemoveListener(RequestBack);
            backButton?.onClick.AddListener(RequestBack);
        }

        private void UnbindButtons()
        {
            backButton?.onClick.RemoveListener(RequestBack);
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
