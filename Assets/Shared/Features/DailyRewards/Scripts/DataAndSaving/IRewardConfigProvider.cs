using System;
using UnityEngine;
using Shared.GameConfig;

// Two providers:
//
//  - GlobalConfigRewardConfigProvider: pulls DailyReward straight out of
//    Shared.Boot.GlobalConfigRuntimeContext.Config, so every tunable for
//    the daily reward system lives in the same place as the rest of the
//    game's config. This is what DailyRewardManager uses by default.
//
//  - LocalRewardConfigProvider: reads a standalone JSON TextAsset from
//    Resources/ (JsonUtility). Kept around for quick local testing /
//    offline iteration without a full GlobalConfig fetch.
public interface IRewardConfigProvider
{
    void FetchConfig(Action<DailyRewardConfigData> onLoaded);
}

public class GlobalConfigRewardConfigProvider : IRewardConfigProvider
{
    public void FetchConfig(Action<DailyRewardConfigData> onLoaded)
    {
        GlobalConfig config =
            Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config
                : null;

        DailyRewardConfigData dailyRewardConfig =
            config != null && config.DailyReward != null
                ? config.DailyReward
                : new DailyRewardConfigData(); // safe fallback, system never hard-fails

        onLoaded?.Invoke(dailyRewardConfig);
    }
}

public class LocalRewardConfigProvider : IRewardConfigProvider
{
    private readonly string resourcePath;

    public LocalRewardConfigProvider(string resourcePath = "daily_reward_config")
    {
        this.resourcePath = resourcePath;
    }

    public void FetchConfig(Action<DailyRewardConfigData> onLoaded)
    {
        TextAsset json = Resources.Load<TextAsset>(resourcePath);
        DailyRewardConfigData config = json != null
            ? JsonUtility.FromJson<DailyRewardConfigData>(json.text)
            : new DailyRewardConfigData(); // safe fallback, system never hard-fails
        onLoaded?.Invoke(config);
    }
}

// What the Firebase version would look like, if you ever want a third
// option that fetches independently of GlobalConfig:
//
// public class FirebaseRewardConfigProvider : IRewardConfigProvider
// {
//     public void FetchConfig(Action<DailyRewardConfigData> onLoaded)
//     {
//         FirebaseRemoteConfig.DefaultInstance.FetchAndActivateAsync().ContinueWith(t =>
//         {
//             string json = FirebaseRemoteConfig.DefaultInstance
//                 .GetValue("daily_reward_config").StringValue;
//             onLoaded?.Invoke(JsonUtility.FromJson<DailyRewardConfigData>(json));
//         });
//     }
// }