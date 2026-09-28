using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.Leaderboard
{
    /// <summary>
    /// The whole persisted leaderboard state, stored as JSON under a single PlayerData key (see
    /// LeaderboardSaveManager). A few hundred bytes - the roster is derived, never stored.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardStateData
    {
        // Bumped only when the shape below changes. An unknown/newer version is regenerated from
        // scratch rather than partially migrated.
        //
        // 6: SettledEventId below, for the cooldown phase. An event now pays out when it ENDS
        // but does not roll until the cooldown after it has also elapsed, so the two moments need
        // telling apart - and a save written before that distinction existed cannot say which of
        // them it was in.
        //
        // 5: PlacementWins below. A bump rather than a default, because a count that starts at
        // zero on an existing save is simply the truth - nothing was ever recorded before it - and
        // the alternative is a state file whose shape depends on when it was written.
        //
        // 4: the constant-rate rivals became planned BOTS. Rival rows are gone from the save
        // entirely - a bot's whole event is derived from EventSeed, so nothing per-bot is stored.
        //
        // 3: the board became cyclic - CycleStartUtcTicks below. Regenerating on a version jump
        // is right here anyway: a state written before cycles existed has no cycle start, and
        // inventing one would either end the cycle instantly or run it for an arbitrary stretch.
        //
        // 2: rivals gained AvatarIndex/FrameIndex. A bump rather than letting them default, because
        // version-1 state would deserialise with both at 0 and every rival would wear the same
        // face. What regenerating costs is the rival roster and their base scores - the player's
        // OWN score lives under a separate PlayerData key (LeaderboardSaveManager.ScoreKey) and
        // survives untouched, so the board reshuffles once on update and the player keeps
        // everything they earned.
        public const int CurrentVersion = 6;

        [JsonProperty("v")]
        public int Version = CurrentVersion;

        [JsonProperty("seed")]
        public int InstallSeed;

        // DateTime.UtcNow.Ticks at the last AdvanceTime. Stored as ticks so the value round-trips
        // through JSON without timezone or format ambiguity.
        [JsonProperty("t")]
        public long LastSimUtcTicks;

        // When the CURRENT cycle opened, as DateTime.UtcNow.Ticks. The cycle ends
        // LeaderboardConfigData.CycleHours later, at which point RollCycle pays out the final
        // placings and starts everything over. Stored as a start rather than an end so changing
        // CycleHours in config retimes the running cycle instead of stranding it.
        [JsonProperty("cs")]
        public long CycleStartUtcTicks;

        // Goes up by one per event. Feeds the event seed, so each event gets a completely fresh
        // roster while staying reproducible from the install seed alone.
        [JsonProperty("ev")]
        public int EventId;

        // How many times the player has finished in each place, index 0 = 1st. Only placements
        // that actually paid out are counted, so this stays three or four entries long rather than
        // one per rank on the board.
        //
        // Lives in state rather than alongside the pending reward because it is a lifetime tally:
        // collecting clears what is owed, never what was achieved.
        [JsonProperty("pw")]
        public List<int> PlacementWins = new List<int>();

        // The last EventId that has already paid out. Defaults to -1 rather than 0 because 0 is a
        // real event id: a fresh save claiming event 0 was settled would never pay its first event.
        [JsonProperty("se")]
        public int SettledEventId = -1;

        /// <summary>
        /// Records one finish at <paramref name="rank"/> and returns the new total for that place -
        /// the number the results screen puts in "you've won 1st place N times".
        /// </summary>
        public int RegisterPlacement(int rank)
        {
            if (rank <= 0)
            {
                return 0;
            }

            if (PlacementWins == null)
            {
                PlacementWins = new List<int>();
            }

            // Grown on demand: a board whose reward table later pays 4th should start counting
            // 4th places without a schema change.
            while (PlacementWins.Count < rank)
            {
                PlacementWins.Add(0);
            }

            PlacementWins[rank - 1] += 1;
            return PlacementWins[rank - 1];
        }
    }
}
