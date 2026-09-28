using MangraGames;
using TMPro;
using Shared.Store;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Store.UnityAdapters
{
    public sealed class StoreFullWidthCardView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [Tooltip("Maps StoreItemDefinition.IconKey -> Sprite. Assign the shared IconDatabaseConfig asset here - falls back to item.Icon when no key/database or no match.")]
        [SerializeField] private IconDatabaseConfig iconDatabase;
        [SerializeField] private GameObject textRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private GameObject amountRoot;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text oldPriceText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionButtonText;
        [SerializeField] private GameObject badgeRoot;
        [SerializeField] private string freeActionLabel = "FREE";

        private string boundItemId;

        public StoreItemLifecycleEvent ActionRequested = new StoreItemLifecycleEvent();

        private void OnDestroy()
        {
            actionButton?.onClick.RemoveListener(RequestAction);
        }

        public void Bind(StoreItemDefinition item, StoreItemStatus status)
        {
            if (item == null)
            {
                return;
            }

            boundItemId = item.ItemId;
            bool isFreeStyle = item.Type == StoreItemType.FreeGrant;

            SetActive(amountRoot, isFreeStyle);
            SetActive(textRoot, !isFreeStyle);
            SetText(titleText, item.Title);
            SetText(descriptionText, item.Description);
            SetText(amountText, item.CoinAmount.ToString());

            Sprite icon = item.ResolveIcon(iconDatabase);
            if (iconImage != null && icon != null)
            {
                iconImage.sprite = icon;
            }

            bool showOldPrice = item.HasDiscount && !string.IsNullOrWhiteSpace(item.OldPriceLabel);
            SetActive(oldPriceText != null ? oldPriceText.gameObject : null, showOldPrice);
            if (showOldPrice)
            {
                SetText(oldPriceText, item.OldPriceLabel);
            }

            // Live store price when available (via InAppManager -> IInAppPurchasesService), falling
            // back to this item's own predefined PriceLabel from the Store Config asset otherwise
            // (Editor, IAP not yet initialized, or an unrecognized ProductId).
            SetText(actionButtonText, isFreeStyle
                ? (status.IsAvailable ? freeActionLabel : FormatCountdown(status.SecondsRemaining))
                : InAppManager.Instance.ResolveDisplayPrice(item.ProductId, item.PriceLabel));

            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(RequestAction);
                actionButton.onClick.AddListener(RequestAction);
                actionButton.interactable = status.IsAvailable && !status.IsSoldOut;
            }

            SetActive(badgeRoot, status.IsAvailable && item.IsTimerBased);
        }

        private void RequestAction()
        {
            if (!string.IsNullOrWhiteSpace(boundItemId))
            {
                ActionRequested.Invoke(boundItemId);
            }
        }

        private static string FormatCountdown(float secondsRemaining)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining));
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
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
