using System;
using System.Collections.Generic;

namespace Shared.Boosters
{
    public sealed class BoosterWallet
    {
        private readonly Dictionary<BoosterType, int> boosterCounts = new Dictionary<BoosterType, int>();

        public BoosterWallet(BoosterConfig config)
        {
            Config = config?.Clone() ?? new BoosterConfig().Clone();
            CurrentCoins = Math.Max(0, Config.InitialCoins);
            foreach (BoosterType boosterType in Enum.GetValues(typeof(BoosterType)))
            {
                boosterCounts[boosterType] = Config.GetFreeCount(boosterType);
            }
        }

        public BoosterConfig Config { get; }
        public int CurrentCoins { get; private set; }

        public int GetCount(BoosterType boosterType)
        {
            return boosterCounts.TryGetValue(boosterType, out int count) ? Math.Max(0, count) : 0;
        }

        public void RestoreCount(BoosterType boosterType, int count)
        {
            boosterCounts[boosterType] = Math.Max(0, count);
        }

        public void RefundCharge(BoosterType boosterType)
        {
            boosterCounts[boosterType] = GetCount(boosterType) + 1;
        }

        public bool CanAfford(BoosterType boosterType)
        {
            return CurrentCoins >= Config.GetCost(boosterType);
        }

        public bool TrySpend(BoosterType boosterType)
        {
            return TrySpendBoosterCount(boosterType);
        }

        public bool TryPurchaseCharge(BoosterType boosterType)
        {
            int cost = Config.GetCost(boosterType);
            if (CurrentCoins < cost)
            {
                return false;
            }

            CurrentCoins -= cost;
            boosterCounts[boosterType] = GetCount(boosterType) + Config.GetBuyCount(boosterType);
            return true;
        }

        public int AddCoins(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            CurrentCoins += amount;
            return amount;
        }

        public void SetCoins(int amount)
        {
            CurrentCoins = Math.Max(0, amount);
        }

        public bool TrySpendCoins(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (CurrentCoins < amount)
            {
                return false;
            }

            CurrentCoins -= amount;
            return true;
        }

        private bool TrySpendBoosterCount(BoosterType boosterType)
        {
            int count = GetCount(boosterType);
            if (count <= 0)
            {
                return false;
            }

            boosterCounts[boosterType] = count - 1;
            return true;
        }
    }
}
