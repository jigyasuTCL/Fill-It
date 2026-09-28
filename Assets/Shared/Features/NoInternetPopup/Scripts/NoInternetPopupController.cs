using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.NoInternetPopup
{
    /// <summary>
    /// Generic "no internet connection" dialog - shown whenever a network-dependent action
    /// (currently the fake rewarded-ad flow) can't reach the network. Single Close/Retry button;
    /// the caller decides what retrying actually means.
    /// </summary>
    public sealed class NoInternetPopupController : MonoBehaviour
    {
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private string message = "No internet connection. Please check your connection and try again.";

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

        // Same fix as FakeAdViewerController.EnsureTopmostSorting/NoAdsPopupController - this
        // popup has no Canvas/override-sorting of its own, so it only renders "on top"/receives
        // touches correctly by relying on activation order relative to whatever it's covering.
        // That silently breaks - touches pass through to whatever's actually on top - once ANY
        // other active content uses an overridden nested Canvas with a high sortingOrder (e.g. the
        // Level Win/Out Of Lives character video, or the Fake Ads dialog this popup is shown
        // from). Forcing a decisively high override here fixes it unconditionally.
        private const int TopmostSortingOrder = 1000;

        public void Show(UnityAction onClosed = null)
        {
            pendingOnClosed = onClosed;
            BindButtons();
            SetText(messageText, message);
            PlayPopupOpenSound();
            EnsureTopmostSorting();

            // Open() activates rootToToggle synchronously as its first step, so it must be
            // called directly (not from inside a coroutine) - StartCoroutine throws immediately
            // if this GameObject is still inactive at this point.
            popupAnimator.Open();
        }

        private void EnsureTopmostSorting()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            // A nested Canvas doesn't strictly need its own GraphicRaycaster (raycasting normally
            // routes through the root Canvas's), but Unity flags any Canvas without one with a
            // "missing Graphic Raycaster" warning - adding one here is harmless and silences it.
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
