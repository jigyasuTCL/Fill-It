using Newtonsoft.Json;
using Shared.GameConfig;
using System;
using System.Collections.Generic;

namespace Shared.Leaderboard
{
    /// <summary>
    /// One reward the player has earned but not yet collected. A flattened copy of the config row
    /// that produced it, NOT a reference back to config: what somebody won yesterday must not
    /// change because the reward table was re-tuned overnight.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardRewardData
    {
        [JsonProperty("k")]
        public LeaderboardRewardKind Kind;

        [JsonProperty("a")]
        public int Amount;

        [JsonProperty("i")]
        public string ItemId = string.Empty;

        [JsonProperty("ic")]
        public string IconKey = string.Empty;
    }

    /// <summary>
    /// What is sitting in the "Collect Gifts" button - the placement the player finished a cycle
    /// at, and what it pays.
    ///
    /// Persisted rather than granted on the spot because a cycle almost always ends while the game
    /// is closed. The rollover happens on the next launch, and the player has to actually see the
    /// screen and press the button, or a day's reward would be paid into a session they never knew
    /// about.
    ///
    /// Stored as JSON under its own PlayerData key, same convention as LeaderboardStateData.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardPodiumPlayerData
    {
        [JsonProperty("n")]
        public string Name = string.Empty;

        [JsonProperty("s")]
        public int Score;

        [JsonProperty("p")]
        public bool IsPlayer;

        [JsonProperty("a")]
        public int AvatarIndex = -1;

        [JsonProperty("f")]
        public int FrameIndex = -1;

        /// <summary>
        /// What the PLAYER was wearing when the event ended, as catalog item ids.
        ///
        /// Rivals are fine as catalog indices - theirs are derived from the event seed and cannot
        /// change. The player's can: the result sits unclaimed through a whole cooldown, and they
        /// are free to go and change their avatar in the meantime. Recording the ids pins the
        /// podium to the moment it was won.
        /// </summary>
        [JsonProperty("ai")]
        public string AvatarId = string.Empty;

        [JsonProperty("fi")]
        public string FrameId = string.Empty;
    }

    [Serializable]
    public sealed class LeaderboardPendingRewardsData
    {
        // 3: the podium entries record the player's worn avatar/frame ids, so the results screen
        //    can draw the winner it actually was rather than whatever is equipped when it opens.
        // 2: RankWinCount below, for the results screen's headline.
        public const int CurrentVersion = 3;

        [JsonProperty("v")]
        public int Version = CurrentVersion;

        /// <summary>Final rank of the cycle that just ended. 0 means nothing is pending.</summary>
        [JsonProperty("r")]
        public int Rank;

        /// <summary>The score the player finished on, for the claim popup to show.</summary>
        [JsonProperty("s")]
        public int Score;

        /// <summary>
        /// How many times this place has been won in total, INCLUDING this finish - a snapshot
        /// taken when the event settled rather than a live read, so a receipt that sat unclaimed
        /// through another event still reads the way it did when it was earned.
        /// </summary>
        [JsonProperty("rw")]
        public int RankWinCount;

        [JsonProperty("w")]
        public List<LeaderboardRewardData> Rewards = new List<LeaderboardRewardData>();

        /// <summary>
        /// Snapshot of the finished cycle's top three. Stored because the live board is reset
        /// immediately after settlement. Art is represented by catalog indices for rivals and
        /// resolved from the player's equipped profile for the local player.
        /// </summary>
        [JsonProperty("pod")]
        public List<LeaderboardPodiumPlayerData> Podium = new List<LeaderboardPodiumPlayerData>();

        [JsonIgnore]
        public bool HasRewards => Rank > 0 && Rewards != null && Rewards.Count > 0;
    }
}
