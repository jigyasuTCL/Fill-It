using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Shared.Boosters;

namespace Shared.GameFlow
{
    public sealed class BuyBoosterPopupController : MonoBehaviour
    {
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private PopupScaleAnimator popupAnimator;
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private Button buyButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button watchAdButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text buyCountText;
        [SerializeField] private TMP_Text watchAdCountText;
        [SerializeField] private string titleFormat = "Need More {0}?";
        [SerializeField] private string descriptionFormat = "Get 3 {0} to help solve tricky situations!";
        [SerializeField] private string costTextFormat = "{0}";
        [SerializeField] private string countTextFormat = "x{0} {1}";

        private bool isClosing;
        private BoosterType boosterType;
        private int buyCount;
        private int adBuyCount;
        private int coinCost;
        private Coroutine openRoutine;

        public UnityEvent BuyRequested = new UnityEvent();
        public UnityEvent CancelRequested = new UnityEvent();
        public UnityEvent WatchAdRequested = new UnityEvent();

        private void OnDestroy()
        {
            UnbindButtons();
        }

        public void SetContent(BoosterType type, int count, int adCount, int cost)
        {
            boosterType = type;
            buyCount = Mathf.Max(0, count);
            adBuyCount = Mathf.Max(0, adCount);
            coinCost = Mathf.Max(0, cost);
            RefreshTexts();
        }

        public void Show()
        {
            BindButtons();
            RefreshTexts();
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

        private void RequestBuy()
        {
            // No manual PlayButtonSound() here - buyButton already has its own
            // ButtonPressScaleFeedback component playing the click sound on tap; calling this too
            // played it twice on every Buy tap.
            BuyRequested.Invoke();
        }

        private void RequestCancel()
        {
            // Cancel is this popup's only close/back action (no separate X button) - it gets the
            // back cue instead of the generic click sound the buy/watch-ad buttons use.
            CancelRequested.Invoke();
        }

        private void RequestWatchAd()
        {
            // Same reasoning as RequestBuy - watchAdButton's own ButtonPressScaleFeedback already
            // plays the click sound.
            WatchAdRequested.Invoke();
        }

        private void BindButtons()
        {
            buyButton?.onClick.RemoveListener(RequestBuy);
            cancelButton?.onClick.RemoveListener(RequestCancel);
            watchAdButton?.onClick.RemoveListener(RequestWatchAd);
            buyButton?.onClick.AddListener(RequestBuy);
            cancelButton?.onClick.AddListener(RequestCancel);
            watchAdButton?.onClick.AddListener(RequestWatchAd);
        }

        private void UnbindButtons()
        {
            buyButton?.onClick.RemoveListener(RequestBuy);
            cancelButton?.onClick.RemoveListener(RequestCancel);
            watchAdButton?.onClick.RemoveListener(RequestWatchAd);
        }

        private void RefreshTexts()
        {
            string boosterName = boosterType.ToString();
            SetText(titleText, string.Format(titleFormat, boosterName));
            SetText(descriptionText, string.Format(descriptionFormat, boosterName));
            SetText(costText, string.Format(costTextFormat, coinCost));

            string countLabel = string.Format(countTextFormat, buyCount, boosterName);
            SetText(buyCountText, countLabel);
            SetText(watchAdCountText, string.Format(countTextFormat, adBuyCount, boosterName));
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

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.PopupOpen);
        }
    }
}
