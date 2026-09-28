using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public enum RewardRequireType
{
    Free, // no gate, claim directly
    Ads,  // must watch a rewarded ad first
    IAP   // must own/purchase an IAP first
}

// Shared fields for anything claimable. Plain (non-abstract) base class so
// it can still be constructed directly, and so Newtonsoft.Json can
// deserialize it as part of Shared.GameConfig.GlobalConfig.DailyReward.
[Serializable]
public class BaseRewardItem
{
    [JsonProperty("id")]
    public string id;

    [JsonProperty("type")]
    public string type;    // "coins", "coins", "energy", ...

    [JsonProperty("amount")]
    public int amount;

    [JsonProperty("iconKey")]
    public string iconKey; // looked up in your asset/sprite table

    // How THIS item is claimed. Independent of whether the daily reward
    // system as a whole is unlocked - see DailyRewardConfigData.enableRestriction.
    [JsonProperty("requireType")]
    public RewardRequireType requireType = RewardRequireType.Free;
}

[Serializable]
public class RewardItem : BaseRewardItem
{
}

[Serializable]
public class SubRewardItem : BaseRewardItem
{
}

// One "day"'s worth of sub-rewards. Within a set, subRewards[1] requires
// subRewards[0] claimed first, subRewards[2] requires [0] and [1], etc.
// (same sequential-unlock rule as before, just scoped to one set now.)
[Serializable]
public class SubRewardSet
{
    [JsonProperty("subRewards")]
    public List<SubRewardItem> subRewards = new List<SubRewardItem>();
}

// NOTE: DailyRewardConfig used to live here. It has moved to
// Shared.GameConfig.DailyRewardConfigData (Shared/GameConfig/DailyRewardConfigData.cs)
// so it can hang off GlobalConfig.DailyReward instead of being loaded
// standalone from Resources.

// Persisted via the shared PlayerData save file (see RewardStateStore). Main and sub
// rewards are fully independent cycles - nothing here is shared between them, so
// claiming one never resets or affects the other.
[Serializable]
public class RewardSaveState
{
    public long mainReadyAtUnixSeconds; // when the next main reward becomes claimable
    public int mainCycleIndex;          // index into mainRewards; loops via %

    public long subReadyAtUnixSeconds;  // when the next sub-reward set becomes claimable (0 = no cooldown pending)
    public int subCycleIndex;           // index into subRewardSets; loops via %
    public List<string> claimedSubRewardIds = new List<string>();
}