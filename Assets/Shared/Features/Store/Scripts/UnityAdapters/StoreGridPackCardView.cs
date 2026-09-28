using MangraGames;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.Store.UnityAdapters
{
    public sealed class StoreGridPackCardView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [Tooltip("Maps StoreItemDefinition.IconKey -> Sprite. Assign the shared IconDatabaseConfig asset here - falls back to item.Icon when no key/database or no match.")]
        [SerializeField] private IconDatabaseConfig iconDatabase;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text oldPriceText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionButtonText;
        [SerializeField] private GameObject badgeRoot;
        [SerializeField] private TMP_Text badgeText;

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

            Sprite icon = item.ResolveIcon(iconDatabase);
            if (iconImage != null && icon != null)
            {
                iconImage.sprite = icon;
            }

            SetText(amountText, item.CoinAmount.ToString());

            // Live store price when available (via InAppManager -> IInAppPurchasesService), falling
            // back to this item's own predefined PriceLabel from the Store Config asset otherwise
            // (Editor, IAP not yet initialized, or an unrecognized ProductId).
            SetText(actionButtonText, status.IsSoldOut ? "Sold Out" : InAppManager.Instance.ResolveDisplayPrice(item.ProductId, item.PriceLabel));

            bool showOldPrice = item.HasDiscount && !string.IsNullOrWhiteSpace(item.OldPriceLabel);
            SetActive(oldPriceText != null ? oldPriceText.gameObject : null, showOldPrice);
            if (showOldPrice)
            {
                SetText(oldPriceText, item.OldPriceLabel);
            }

            bool showBadge = !status.IsAvailable && !status.IsSoldOut && status.SecondsRemaining > 0f;
            SetActive(badgeRoot, showBadge);
            if (showBadge)
            {
                SetText(badgeText, FormatCountdown(status.SecondsRemaining));
            }

            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(RequestAction);
                actionButton.onClick.AddListener(RequestAction);
                actionButton.interactable = status.IsAvailable;
            }
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
