using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.Purchase
{
    /// <summary>
    /// Generic "purchase failed" dialog - shown whenever an IAP request (InAppManager.Purchase)
    /// resolves as a failure. Single Close button; the caller decides what happens next.
    /// </summary>
    public sealed class PurchaseFailedPopupController : MonoBehaviour
    {
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private string message = "Sorry! We couldn't process your purchase. Please try again.";

        private UnityAction pendingOnClosed;

        public UnityEvent Closed = new UnityEvent();

        private void OnDisable()
        {
            popupAnimator?.SnapTo(false);
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        // Same fix as NoAdsPopupController/NoInternetPopupController.EnsureTopmostSorting - this
        // popup has no Canvas/override-sorting of its own, so it only renders "on top"/receives
        // touches correctly by relying on activation order relative to whatever it's covering.
        private const int TopmostSortingOrder = 1000;

        public void Show(UnityAction onClosed = null)
        {
            pendingOnClosed = onClosed;
            BindButtons();
            // SetText(messageText, message);
            PlayPopupOpenSound();
            // EnsureTopmostSorting();

            // Open() activates rootToToggle synchronously as its first step, so it must be
            // called directly (not from inside a coroutine) - StartCoroutine throws immediately
            // if this GameObject is still inactive at this point.
            popupAnimator.Open();
        }

        // Parameterless so it can be wired straight into a UnityEvent (e.g. CheatPanelController's
        // TestPurchaseFailedRequested) from the Inspector, the same way WinLevelRequested/
        // FailLevelRequested target CatFlapGameplayManager.CheatWinLevel/CheatFailLevel.
        [ContextMenu("Test Show")]
        public void ShowTestPreview()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(PurchaseFailedPopupController)}.{nameof(ShowTestPreview)} requires Play Mode.");
                return;
            }

            Show();
        }

        private void EnsureTopmostSorting()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = TopmostSortingOrder;
        }

        private void RequestClose()
        {
            PlayButtonSound();
            UnityAction callback = pendingOnClosed;
            pendingOnClosed = null;

            popupAnimator.Close(() =>
            {
                Closed.Invoke();
                callback?.Invoke();
            });
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

        private static void SetText(TMP_Text label, string value)
        {
            return; // Disabled for now - the message is already set in the Inspector and doesn't need to be changed at runtime.   
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
