using MangraGames;
using Shared.GameFlow;
using Shared.NoAdsPopup;
using Shared.Purchase;
using Shared.Store.UnityAdapters;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow.UnityAdapters
{
    public sealed class GameFlowViewAdapter : MonoBehaviour, IGameFlowView
    {
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private GameObject gameplayRoot;
        [SerializeField] private GameObject winRoot;
        [SerializeField] private GameObject failRoot;
        [SerializeField] private OutOfLivesPopupController outOfLivesPopup;
        [SerializeField] private BuyBoosterPopupController buyBoosterPopup;
        [SerializeField] private StorePopupController storePopup;

        // AdsManager lives on a DontDestroyOnLoad object created from BootScene, so it can't hold
        // Inspector references into this scene's UI - this adapter owns the actual instances and
        // registers them with AdsManager at runtime instead, every time this scene (re)loads.
        [SerializeField] private FakeAdViewerController fakeAdViewer;
        [SerializeField] private GameObject adLoadingRoot;
        [SerializeField] private NoAdsPopupController noAdsPopup;
        [SerializeField] private PurchaseSuccessPopupController purchaseSuccessPopup;
        [SerializeField] private PurchaseFailedPopupController purchaseFailedPopup;

        private void Awake()
        {
            AdsManager.Instance.SetFakeAdViewer(fakeAdViewer);
            AdsManager.Instance.SetLoadingScreen(adLoadingRoot);
            AdsManager.Instance.SetNoAdsPopup(noAdsPopup);

            // Purchases share the exact same loading overlay as ads (see InAppManager.SetLoadingScreen) -
            // it temporarily swaps in a store-specific message while a purchase/restore is in flight.
            InAppManager.Instance.SetLoadingScreen(adLoadingRoot);
            InAppManager.Instance.SetPurchaseSuccessPopup(purchaseSuccessPopup);
            InAppManager.Instance.SetPurchaseFailedPopup(purchaseFailedPopup);
        }

        public void ShowMainMenu()
        {
            SetActivePage(mainMenuRoot);
        }

        public void ShowGameplay()
        {
            SetActivePage(gameplayRoot);
        }

        public void ShowWin()
        {
            SetActivePage(winRoot);
            GetOrCreateLevelWinPopup();
        }

        public void ShowFail()
        {
            SetActivePage(failRoot);
            GetOrCreateLevelFailPopup();
        }

        public void ShowState(GameFlowState state)
        {
            switch (state)
            {
                case GameFlowState.MainMenu:
                    ShowMainMenu();
                    break;
                case GameFlowState.Win:
                    ShowWin();
                    break;
                case GameFlowState.Fail:
                    ShowFail();
                    break;
                default:
                    ShowGameplay();
                    break;
            }
        }

        private void SetActivePage(GameObject activeRoot)
        {
            SetActive(mainMenuRoot, mainMenuRoot == activeRoot);
            SetActive(gameplayRoot, gameplayRoot == activeRoot);
            SetActive(winRoot, winRoot == activeRoot);
            SetActive(failRoot, failRoot == activeRoot);
        }

        private static void SetActive(GameObject root, bool active)
        {
            if (root != null)
            {
                root.SetActive(active);
            }
        }

        public LevelWinPopupController GetOrCreateLevelWinPopup()
        {
            if (winRoot == null)
            {
                return null;
            }

            LevelWinPopupController popup = winRoot.GetComponent<LevelWinPopupController>();
            if (popup == null)
            {
                popup = winRoot.GetComponentInChildren<LevelWinPopupController>(true);
            }

            return popup != null ? popup : winRoot.AddComponent<LevelWinPopupController>();
        }

        public LevelFailPopupController GetOrCreateLevelFailPopup()
        {
            if (failRoot == null)
            {
                return null;
            }

            LevelFailPopupController popup = failRoot.GetComponent<LevelFailPopupController>();
            if (popup == null)
            {
                popup = failRoot.GetComponentInChildren<LevelFailPopupController>(true);
            }

            return popup != null ? popup : failRoot.AddComponent<LevelFailPopupController>();
        }

        public OutOfLivesPopupController OutOfLivesPopup => outOfLivesPopup;
        public BuyBoosterPopupController BuyBoosterPopup => buyBoosterPopup;
        public StorePopupController StorePopup => storePopup;
    }
}
