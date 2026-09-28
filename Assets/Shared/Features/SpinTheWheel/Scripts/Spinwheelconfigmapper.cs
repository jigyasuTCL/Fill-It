using System.Collections.Generic;
using Shared.GameConfig;

namespace Game.SpinWheel
{
    /// <summary>
    /// Converts the live GlobalConfig's SpinTheWheelConfigData into the
    /// plain SpinWheelRemoteConfig that RewardSelector and
    /// SpinWheelUIController actually consume at runtime.
    ///
    /// This is the ONE adapter function called out in SpinWheelManager's
    /// original comments ("write ONE small adapter function that turns
    /// whatever [remote config] hands you into a SpinWheelRemoteConfig").
    /// Keeping the mapping isolated here means SpinWheelModels.cs stays free
    /// of any Newtonsoft/Shared.GameConfig dependency � it's still just
    /// plain C#, safe to unit test on its own � and if GlobalConfig's shape
    /// changes later, only this file needs updating.
    /// </summary>
    public static class SpinWheelConfigMapper
    {
        public static SpinWheelRemoteConfig ToRuntimeConfig(SpinTheWheelConfigData data)
        {
            var runtimeConfig = new SpinWheelRemoteConfig
            {
                Stages = new List<SpinStageConfig>(),
                RewardTables = new List<RewardTable>()
            };

            if (data?.Stages != null)
            {
                foreach (var stage in data.Stages)
                {
                    runtimeConfig.Stages.Add(new SpinStageConfig
                    {
                        SpinIndex = stage.SpinIndex,
                        WinsRequired = stage.WinsRequired,
                        IsFree = stage.IsFree,
                        IsCapStage = stage.IsCapStage
                    });
                }
            }

            if (data?.RewardTables != null)
            {
                foreach (var tableData in data.RewardTables)
                {
                    var table = new RewardTable
                    {
                        TableId = tableData.TableId,
                        Rewards = new List<RewardConfig>()
                    };

                    if (tableData.Rewards != null)
                    {
                        foreach (var rewardData in tableData.Rewards)
                        {
                            table.Rewards.Add(new RewardConfig
                            {
                                RewardId = rewardData.RewardId,
                                Type = rewardData.Type,
                                Amount = rewardData.Amount,
                                BoosterId = rewardData.BoosterId,
                                Weight = rewardData.Weight,
                                RewardIconKey = rewardData.RewardIconKey
                            });
                        }
                    }

                    runtimeConfig.RewardTables.Add(table);
                }
            }

            return runtimeConfig;
        }
    }
}