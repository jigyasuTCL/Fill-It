using Shared.GameFlow;
using Shared.GameFlow.UnityAdapters;
using Shared.Store.UnityAdapters;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapUIFlowManager : MonoBehaviour, IGameFlowView
    {
        [SerializeField] private GameFlowViewAdapter gameFlowView;
        [SerializeField] private GameObject nextLevelButtonRoot;

        public OutOfLivesPopupController OutOfLivesPopup => gameFlowView != null ? gameFlowView.OutOfLivesPopup : null;
        public BuyBoosterPopupController BuyBoosterPopup => gameFlowView != null ? gameFlowView.BuyBoosterPopup : null;
        public StorePopupController StorePopup => gameFlowView != null ? gameFlowView.StorePopup : null;

        /// <summary>
        /// While true, Win/Fail state changes are swallowed here instead of reaching
        /// GameFlowViewAdapter - CatFlapRuntimeController's own RenderStatus unconditionally calls
        /// ShowState(Win/Fail) whenever session.GameState.Current is Won/Failed, independent of
        /// CatFlapGameplayManager's event handlers, so it can't be stopped by guarding those
        /// handlers alone. Daily Challenge has its own result popup (see
        /// CatFlapDailyChallengeBridge) and sets this true/false around its sessions so the
        /// regular LevelWinUIRoot/LevelFailUIRoot pages never show underneath/alongside it.
        /// </summary>
        public bool SuppressWinFailPages { get; set; }

        public void ShowMainMenu()
        {
            ShowState(GameFlowState.MainMenu);
        }

        public void ShowGameplay()
        {
            ShowState(GameFlowState.Gameplay);
        }

        public void ShowWin()
        {
            ShowState(GameFlowState.Win);
        }

        public void ShowFail()
        {
            ShowState(GameFlowState.Fail);
        }

        public void ShowState(GameFlowState state)
        {
            if (SuppressWinFailPages && (state == GameFlowState.Win || state == GameFlowState.Fail))
            {
                return;
            }

            gameFlowView?.ShowState(state);
        }

        public void SetHasNextLevel(bool hasNextLevel)
        {
            if (nextLevelButtonRoot != null)
            {
                nextLevelButtonRoot.SetActive(hasNextLevel);
            }
        }

        public LevelWinPopupController GetOrCreateLevelWinPopup()
        {
            return gameFlowView != null ? gameFlowView.GetOrCreateLevelWinPopup() : null;
        }

        public LevelFailPopupController GetOrCreateLevelFailPopup()
        {
            return gameFlowView != null ? gameFlowView.GetOrCreateLevelFailPopup() : null;
        }
        public void SetCurrentCoinOutOfLife(int refillCoinCost)
        {
            OutOfLivesPopupController popup = OutOfLivesPopup;
            if (popup == null)
            {
                return;
            }

            popup.SetCurretCoin(refillCoinCost);
        }
        public void ShowOutOfLivesPopup(int refillCoinCost)
        {
            OutOfLivesPopupController popup = OutOfLivesPopup;
            if (popup == null)
            {
                return;
            }

            popup.SetRefillCoinCost(refillCoinCost);
            popup.Show();
        }

        public void CloseOutOfLivesPopup(UnityEngine.Events.UnityAction onClosed = null)
        {
            OutOfLivesPopupController popup = OutOfLivesPopup;
            if (popup != null)
            {
                popup.Close(onClosed);
            }
            else
            {
                onClosed?.Invoke();
            }
        }

        public void ShowBuyBoosterPopup(Shared.Boosters.BoosterType boosterType, int buyCount, int adBuyCount, int coinCost)
        {
            BuyBoosterPopupController popup = BuyBoosterPopup;
            if (popup == null)
            {
                return;
            }

            popup.SetContent(boosterType, buyCount, adBuyCount, coinCost);
            popup.Show();
        }

        public void CloseBuyBoosterPopup(UnityEngine.Events.UnityAction onClosed = null)
        {
            BuyBoosterPopupController popup = BuyBoosterPopup;
            if (popup != null)
            {
                popup.Close(onClosed);
            }
            else
            {
                onClosed?.Invoke();
            }
        }

        public void ShowStorePopup()
        {
            StorePopupController popup = StorePopup;
            popup?.Show();
        }

        public void CloseStorePopup(UnityEngine.Events.UnityAction onClosed = null)
        {
            StorePopupController popup = StorePopup;
            if (popup != null)
            {
                popup.Close(onClosed);
            }
            else
            {
                onClosed?.Invoke();
            }
        }
    }
}
