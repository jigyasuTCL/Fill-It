using System.Collections.Generic;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Display names for the leaderboard's simulated rivals. Content lives here - in an asset a
    /// designer can edit and localise - rather than in GlobalConfig's JSON or a hardcoded array.
    ///
    /// Names are picked once per install (seeded, without repeats) and then frozen in the saved
    /// state, so a rival never changes identity between sessions even if this list is later edited.
    /// Needs at least as many entries as LeaderboardConfigData.RivalCount; shorter lists still
    /// work but will repeat names.
    /// </summary>
    [CreateAssetMenu(fileName = "LeaderboardRivalPool", menuName = "MangraGames/Leaderboard Rival Pool")]
    public sealed class LeaderboardRivalPoolAsset : ScriptableObject
    {
        [SerializeField] private List<string> names = new List<string>();

        public int Count => names != null ? names.Count : 0;

        /// <summary>
        /// Wraps out-of-range indices instead of throwing, so a saved state that outlived a
        /// shortened name list still renders something sensible.
        /// </summary>
        public string GetName(int index)
        {
            if (names == null || names.Count == 0)
            {
                return "Player";
            }

            int wrapped = ((index % names.Count) + names.Count) % names.Count;
            return string.IsNullOrWhiteSpace(names[wrapped]) ? "Player" : names[wrapped];
        }
    }
}
