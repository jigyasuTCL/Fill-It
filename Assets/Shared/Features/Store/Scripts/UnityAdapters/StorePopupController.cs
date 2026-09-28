using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Shared.GameFlow;

namespace Shared.Store.UnityAdapters
{
    public sealed class StorePopupController : MonoBehaviour
    {
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private PopupScaleAnimator popupAnimator;
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private Button closeButton;
        // Gems -> Coins rename - FormerlySerializedAs preserves existing Inspector wiring.
        [FormerlySerializedAs("gemBalanceText")]
        [SerializeField] private TMP_Text coinBalanceText;
        [SerializeField] private Transform fullWidthContent;
        [SerializeField] private Transform gridContent;
        [SerializeField] private StoreFullWidthCardView fullWidthTemplate;
        [SerializeField] private StoreGridPackCardView gridTemplate;
        [SerializeField] private float refreshIntervalSeconds = 1f;

        private readonly List<GameObject> spawnedCards = new List<GameObject>();
        private StoreManager storeManager;
        private bool isClosing;
        private Coroutine openRoutine;
        private Coroutine refreshRoutine;

        public UnityEvent CloseRequested = new UnityEvent();
        public StoreItemLifecycleEvent PurchaseRequested = new StoreItemLifecycleEvent();

        private void OnDestroy()
        {
            UnbindButtons();
        }

        public void Configure(StoreManager manager)
        {
            storeManager = manager;
        }

        public void Show()
        {
            BindButtons();
            Populate();
            isClosing = false;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            SetVisible(true);
            if (popupPanel != null)
            {
                popupPanel.gameObject.SetActive(true);
                popupPanel.localScale = Vector3.zero;
            }

            InputManager.BlockAllButtonsExcept(popupPanel);

            PlayPopupOpenSound();
            openRoutine = StartCoroutine(PlayOpenNextFrame());
            StopRefreshLoop();
            refreshRoutine = StartCoroutine(RefreshLoop());
        }

        private IEnumerator PlayOpenNextFrame()
        {
            yield return null;
            if (popupAnimator != null)
            {
                popupAnimator.PlayOpen();
            }

            openRoutine = null;
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

        public void Close(UnityAction onClosed = null)
        {
            if (isClosing)
            {
                return;
            }

            if (!gameObject.activeInHierarchy)
            {
                onClosed?.Invoke();
                return;
            }

            isClosing = true;
            StopRefreshLoop();
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            if (popupAnimator != null)
            {
                popupAnimator.PlayClose(() =>
                {
                    SetVisible(false);
                    isClosing = false;
                    InputManager.UnblockAllButtons();
                    onClosed?.Invoke();
                });
                return;
            }

            SetVisible(false);
            isClosing = false;
            InputManager.UnblockAllButtons();
            onClosed?.Invoke();
        }

        private void Populate()
        {
            if (storeManager == null)
            {
                return;
            }

            SetText(coinBalanceText, storeManager.CurrentCoinBalance().ToString());
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
                // card here entirely rather than leaving a disabled "Sold Out" row behind (same
                // as ShopPageController.Populate).
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

            // No full-width cards actually landed in the container this pass (no template
            // assigned, or every full-width item skipped/filtered above) - hide it instead of
            // leaving an empty section (background/header art with no cards) visible in the store.
            fullWidthContent?.gameObject.SetActive(fullWidthContent.childCount > 0);
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
        }

        private void SpawnGridCard(StoreItemDefinition item, StoreItemStatus status)
        {
            if (gridTemplate == null || gridContent == null)
            {
                return;
            }

            StoreGridPackCardView card = Instantiate(gridTemplate, gridContent);
            card.gameObject.SetActive(true);
            card.Bind(item, status);
            card.ActionRequested.RemoveListener(HandleItemAction);
            card.ActionRequested.AddListener(HandleItemAction);
            spawnedCards.Add(card.gameObject);
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

        private void RequestClose()
        {
            PlayButtonSound();
            CloseRequested.Invoke();
        }

        private void BindButtons()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
            closeButton?.onClick.AddListener(RequestClose);
        }

        private void UnbindButtons()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
        }

        private void SetVisible(bool visible)
        {
            if (popupRoot != null)
            {
                popupRoot.SetActive(visible);
            }

            gameObject.SetActive(visible);
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private static void PlayButtonSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ButtonBack);
        }

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.PopupOpen);
        }
    }
}
