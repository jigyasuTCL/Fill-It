using System.Collections.Generic;
using Shared.GameConfig;
using UnityEngine;

namespace Shared.GameFlow
{
    /// <summary>
    /// Content for every Unlock popup entry (id, header, description, which feature it unlocks),
    /// kept as its own ScriptableObject asset separate from GlobalConfig - GlobalConfig stays
    /// JSON-clean (its own Unlocks list was JsonIgnore'd specifically because it carried Unity
    /// object references JSON can't serialize), while this asset is free to carry whatever
    /// Unity references an unlock entry needs.
    ///
    /// Each entry's Feature dropdown (see UnlockData.feature) is what ties the two configs
    /// together at runtime: UnlockManager.ResolveUnlockLevel reads the actual unlock LEVEL from
    /// GlobalConfig's matching feature block (SpinTheWheel/DailyChallenge/DailyReward), so the
    /// level only ever needs to be authored in one place - this asset just says which feature.
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockFeaturesConfig", menuName = "MangraGames/Unlock Features Config")]
    public sealed class UnlockFeaturesConfig : ScriptableObject
    {
        public List<UnlockData> Unlocks = new List<UnlockData>();
    }
}
