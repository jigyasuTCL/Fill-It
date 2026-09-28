using System;
using UnityEngine;
using UnityEngine.UI;
using Shared.NoInternetPopup;
using Shared.NoAdsPopup;

namespace Shared.GameFlow
{
    /// <summary>
    /// Generic "watch an ad to continue?" confirmation dialog - part of the shared GameFlow
    /// module so any feature that needs a rewarded-ad gate (Daily Challenge today, potentially
    /// others later) can reuse the same one instead of each owning its own ad-panel/button
    /// plumbing. No real ad SDK exists in this project yet - HandleWatch/HandleSkip are the exact
    /// two places to wire in real ad network calls later; callers never need to change.
    /// noInternetButton/noAdsButton simulate the two ways a real ad request can fail, surfacing
    /// NoInternetPopupController/NoAdsPopupController instead of just silently skipping.
    /// </summary>
    public sealed class FakeAdViewerController : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button watchButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button noInternetButton;
        [SerializeField] private Button noAdsButton;
        [SerializeField] private NoInternetPopupController noInternetPopup;
        [SerializeField] private NoAdsPopupController noAdsPopup;

        private Action onWatched;
        private Action onSkipped;

        private void OnEnable()
        {
            watchButton?.onClick.AddListener(HandleWatch);
            skipButton?.onClick.AddListener(HandleSkip);
            noInternetButton?.onClick.AddListener(HandleNoInternet);
            noAdsButton?.onClick.AddListener(HandleNoAds);
        }

        private void OnDisable()
        {
            watchButton?.onClick.RemoveListener(HandleWatch);
            skipButton?.onClick.RemoveListener(HandleSkip);
            noInternetButton?.onClick.RemoveListener(HandleNoInternet);
            noAdsButton?.onClick.RemoveListener(HandleNoAds);
        }

        /// <summary>
        /// Shows the dialog and waits for a tap. onWatched fires if the (fake) ad was watched;
        /// onSkipped fires if the player declined - both optional, callers typically only care
        /// about onWatched.
        /// </summary>
        // Modal popups in this project (this one included) are plain UI panels with no Canvas/
        // override-sorting of their own - they only render "on top" by relying on being later in
        // hierarchy/activated later than whatever they're covering. That assumption breaks for
        // content that DOES use an overridden nested Canvas with a high sortingOrder (e.g. the
        // Level Win/Out Of Lives character video - see LevelWinPopupController.EnsureCatVideoPlayer),
        // since a Canvas with overrideSorting participates in an explicit numeric sort that beats
        // any non-overridden sibling regardless of activation order. Giving this popup its own
        // override, set high enough to beat any known overridden content, fixes that unconditionally
        // rather than needing every future "topmost" popup to guess a safe number relative to it.
        private const int TopmostSortingOrder = 1000;

        public void Show(Action onWatched, Action onSkipped = null)
        {
            this.onWatched = onWatched;
            this.onSkipped = onSkipped;

            if (root != null)
            {
                root.SetActive(true);
                EnsureTopmostSorting(root);
            }
        }

        private static void EnsureTopmostSorting(GameObject target)
        {
            Canvas canvas = target.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = target.AddComponent<Canvas>();
            }

            // A nested Canvas doesn't strictly need its own GraphicRaycaster (raycasting normally
            // routes through the root Canvas's), but Unity flags any Canvas without one with a
            // "missing Graphic Raycaster" warning - adding one here is harmless and silences it.
            if (target.GetComponent<GraphicRaycaster>() == null)
            {
                target.AddComponent<GraphicRaycaster>();
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = TopmostSortingOrder;
        }

        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private void HandleWatch()
        {
            Action callback = onWatched;
            onWatched = null;
            onSkipped = null;
            Hide();
            callback?.Invoke();
        }

        private void HandleSkip()
        {
            Action callback = onSkipped;
            onWatched = null;
            onSkipped = null;
            Hide();
            callback?.Invoke();
        }
        private void HandleNoInternet()
        {
            Action callback = onSkipped;
            onWatched = null;
            onSkipped = null;
            Hide();

            if (noInternetPopup != null)
            {
                noInternetPopup.Show(() => callback?.Invoke());
                return;
            }

            callback?.Invoke();
        }

        private void HandleNoAds()
        {
            Action callback = onSkipped;
            onWatched = null;
            onSkipped = null;
            Hide();

            if (noAdsPopup != null)
            {
                noAdsPopup.Show(() => callback?.Invoke());
                return;
            }

            callback?.Invoke();
        }
    }
}
