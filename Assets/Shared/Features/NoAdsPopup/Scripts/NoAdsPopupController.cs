using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.NoAdsPopup
{
    /// <summary>
    /// Generic "no ad available" dialog - shown whenever a rewarded-ad request comes back empty
    /// (currently the fake rewarded-ad flow). Single Close button; the caller decides what
    /// happens next (typically the same as declining the ad).
    /// </summary>
    public sealed class NoAdsPopupController : MonoBehaviour
    {
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private string message = "No ads available right now. Please try again later.";

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

        // Same fix as FakeAdViewerController.EnsureTopmostSorting - this popup (like most modal
        // popups in this project) has no Canvas/override-sorting of its own, so it only renders
        // "on top"/receives touches correctly by relying on activation order relative to whatever
        // it's covering. That silently breaks - touches pass through to whatever's actually on top
        // - once ANY other active content uses an overridden nested Canvas with a high
        // sortingOrder (e.g. the Level Win/Out Of Lives character video, or the Fake Ads dialog
        // this popup is shown from). Forcing a decisively high override here fixes it
        // unconditionally.
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

        public void Close()
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
            closeButton?.onClick.RemoveListener(Close);
            closeButton?.onClick.AddListener(Close);
        }

        private void UnbindButtons()
        {
            closeButton?.onClick.RemoveListener(Close);
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
