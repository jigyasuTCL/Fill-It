using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using Shared.GameConfig;

namespace Shared.Store
{
    public sealed class StoreManager : MonoBehaviour
    {
        public StoreItemLifecycleEvent PurchaseRequested = new StoreItemLifecycleEvent();
        public StoreItemLifecycleEvent RewardGranted = new StoreItemLifecycleEvent();

        private StoreConfig config = new StoreConfig();
        private Action<int> grantCoinsAction;
        private Func<int> coinBalanceProvider;

        public StoreConfig Config => config ?? (config = new StoreConfig());

        public void Configure(StoreConfig storeConfig, Action<int> grantCoins, Func<int> coinBalance)
        {
            config = storeConfig ?? new StoreConfig();
            grantCoinsAction = grantCoins;
            coinBalanceProvider = coinBalance;
        }

        public int CurrentCoinBalance()
        {
            return coinBalanceProvider?.Invoke() ?? 0;
        }

        public StoreItemDefinition FindItem(string itemId)
        {
            if (config?.Items == null || string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            for (int index = 0; index < config.Items.Count; index++)
            {
                StoreItemDefinition item = config.Items[index];
                if (item != null && string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }

        // Looks up an item by its raw STORE product id (StoreItemDefinition.ProductId) rather than
        // its own in-game ItemId - used to grant a purchase recovered outside the normal request
        // flow (see InAppManager.UnattendedPurchaseGranted), where only the store's product id is
        // known, not which config entry it belongs to.
        public StoreItemDefinition FindItemByProductId(string productId)
        {
            if (config?.Items == null || string.IsNullOrWhiteSpace(productId))
            {
                return null;
            }

            for (int index = 0; index < config.Items.Count; index++)
            {
                StoreItemDefinition item = config.Items[index];
                if (item != null && string.Equals(item.ProductId, productId, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }

        public StoreItemStatus GetStatus(StoreItemDefinition item)
        {
            var status = new StoreItemStatus();
            if (item == null)
            {
                return status;
            }

            int purchaseCount = GetPurchaseCount(item.ItemId);
            status.PurchaseCount = purchaseCount;

            // Authoritative regardless of how IsRepeatable is authored on this item - Remove Ads
            // is a permanent one-time unlock, so PlayerData.NoAdsPurchased alone decides this,
            // rather than relying on config to also mark it non-repeatable.
            if (item.Type == StoreItemType.RemoveAds && Shared.PlayerData.PlayerData.Instance.NoAdsPurchased)
            {
                status.IsSoldOut = true;
                return status;
            }

            if (purchaseCount > 0 && !item.IsRepeatable)
            {
                status.IsSoldOut = true;
                return status;
            }

            if (item.MaxPurchaseCount > 0 && purchaseCount >= item.MaxPurchaseCount)
            {
                status.IsSoldOut = true;
                return status;
            }

            float cooldownSeconds = ResolveCooldownSeconds(item);
            if (cooldownSeconds > 0f && purchaseCount > 0)
            {
                DateTime lastClaimUtc = GetLastClaimUtc(item.ItemId);
                double elapsedSeconds = (DateTime.UtcNow - lastClaimUtc).TotalSeconds;
                float remainingSeconds = cooldownSeconds - (float)elapsedSeconds;
                if (remainingSeconds > 0f)
                {
                    status.SecondsRemaining = remainingSeconds;
                    return status;
                }
            }

            status.IsAvailable = true;
            return status;
        }

        public bool TryClaimFreeReward(string itemId)
        {
            StoreItemDefinition item = FindItem(itemId);
            if (item == null || item.Type != StoreItemType.FreeGrant)
            {
                return false;
            }

            if (!GetStatus(item).IsAvailable)
            {
                return false;
            }

            GrantCoins(item.CoinAmount);
            RecordClaim(item.ItemId);
            RewardGranted.Invoke(itemId);
            return true;
        }

        public void RequestPurchase(string itemId)
        {
            StoreItemDefinition item = FindItem(itemId);
            if (item == null || !GetStatus(item).IsAvailable)
            {
                return;
            }

            PurchaseRequested.Invoke(itemId);
        }

        public bool CompletePurchase(string itemId)
        {
            StoreItemDefinition item = FindItem(itemId);
            if (item == null)
            {
                return false;
            }

            if (item.Type == StoreItemType.CoinPack)
            {
                GrantCoins(item.CoinAmount);
            }
            else if (item.Type == StoreItemType.RemoveAds)
            {
                GrantNoAds();
            }

            RecordClaim(item.ItemId);
            RewardGranted.Invoke(itemId);
            return true;
        }

        private float ResolveCooldownSeconds(StoreItemDefinition item)
        {
            if (item.Type == StoreItemType.FreeGrant && item.IsTimerBased)
            {
                return Mathf.Max(0f, item.TimerHours) * 3600f;
            }

            if (item.HasCooldown)
            {
                return Mathf.Max(0f, item.CooldownHours) * 3600f;
            }

            return 0f;
        }

        private void GrantCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            grantCoinsAction?.Invoke(amount);
        }

        // One-time unlock - saved immediately (rather than left to whatever save flow the caller
        // eventually runs) since this flag alone gates the purchase button/ads forever after.
        private static void GrantNoAds()
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            if (data.NoAdsPurchased)
            {
                return;
            }

            data.NoAdsPurchased = true;
            data.Touch();

            MangraGames.AdsManager.Instance.HideBanner();

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }

        private static string ClaimKey(string itemId)
        {
            return $"store.{itemId}.lastClaimUtc";
        }

        private static string CountKey(string itemId)
        {
            return $"store.{itemId}.purchaseCount";
        }

        private static void RecordClaim(string itemId)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.SetString(ClaimKey(itemId), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            data.TryGetInt(CountKey(itemId), out int count);
            data.SetInt(CountKey(itemId), count + 1);
            data.Touch();
        }

        private static DateTime GetLastClaimUtc(string itemId)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            if (data.TryGetString(ClaimKey(itemId), out string raw) &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime value))
            {
                return value;
            }

            return DateTime.MinValue;
        }

        private static int GetPurchaseCount(string itemId)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.TryGetInt(CountKey(itemId), out int count);
            return count;
        }
    }

    [Serializable]
    public sealed class StoreItemLifecycleEvent : UnityEvent<string>
    {
    }
}
