using System.Collections.Generic;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Board-wide wave scheduler for CatFlapSpriteCellViewAdapter's Idle mood: rather than every
    /// cell independently rolling its own random delay and playing whenever it feels like it, the
    /// whole board plays idle animation in WAVES - TargetWaveSize() cells (20-25% of
    /// currently-eligible cells, floored by a board-length-scaled minimum of 2-5) are picked at
    /// random and play together; once every member of that wave has finished, the board goes
    /// completely quiet for a shared random delay before the next wave is picked. A cell not
    /// picked for the current wave just waits on its static rest sprite until a future wave picks
    /// it.
    ///
    /// Scoped to Idle only - Happy/Sad reactions (row/column completed, rule broken) always play
    /// when triggered regardless of this budget, since they're deliberate short reactive bursts,
    /// not ambient idle noise.
    ///
    /// Static by design: there is exactly one board on screen at a time, and every cell instance
    /// (pooled, reused across levels) shares the same scheduler for as long as the app runs.
    /// </summary>
    internal static class CatFlapCharacterIdleThrottle
    {
        // Fraction of currently-eligible cells picked into each wave - a fixed tuning constant
        // within the requested 20%-30% range, not re-rolled on every check, so wave size doesn't
        // visibly jitter from one poll to the next.
        private const float ConcurrentFraction = 0.25f;

        // Board-length-scaled floor beneath ConcurrentFraction above, taken from
        // CatFlapSpriteBoardViewAdapter's own sizeBounds range (Mathf.Max(rows, columns) in
        // {2,4,6,8,10}) - smallest boards get a floor of 2 characters per wave, largest get 5,
        // linearly interpolated in between. SetBoardLength should be called once per Build().
        private const int MinBoardLength = 2;
        private const int MaxBoardLength = 10;
        private const int MinConcurrentAtMinLength = 2;
        private const int MaxConcurrentAtMaxLength = 5;

        private static readonly HashSet<CatFlapSpriteCellViewAdapter> eligibleCells = new HashSet<CatFlapSpriteCellViewAdapter>();

        // Cells currently playing as part of THIS wave. Empty means no wave is in progress right
        // now - either waiting out the inter-wave delay, or free to pick a new wave immediately.
        private static readonly HashSet<CatFlapSpriteCellViewAdapter> activeWave = new HashSet<CatFlapSpriteCellViewAdapter>();
        private static readonly List<CatFlapSpriteCellViewAdapter> waveSelectionScratch = new List<CatFlapSpriteCellViewAdapter>();

        private static float nextWaveAllowedTime = float.NegativeInfinity;
        private static int boardLength = MinBoardLength;

        /// <summary>
        /// Call once per board Build() with Mathf.Max(rows, columns) - sets the floor a wave's
        /// size never drops below, scaled to this level's board size.
        /// </summary>
        public static void SetBoardLength(int length)
        {
            boardLength = Mathf.Max(1, length);
        }

        public static void RegisterEligible(CatFlapSpriteCellViewAdapter cell)
        {
            eligibleCells.Add(cell);
        }

        public static void UnregisterEligible(CatFlapSpriteCellViewAdapter cell)
        {
            eligibleCells.Remove(cell);
            activeWave.Remove(cell);
        }

        private static int MinConcurrentByBoardLength()
        {
            float t = Mathf.InverseLerp(MinBoardLength, MaxBoardLength, boardLength);
            return Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(MinConcurrentAtMinLength, MaxConcurrentAtMaxLength, t)),
                MinConcurrentAtMinLength,
                MaxConcurrentAtMaxLength);
        }

        private static int TargetWaveSize()
        {
            int byFraction = Mathf.RoundToInt(eligibleCells.Count * ConcurrentFraction);
            return Mathf.Max(MinConcurrentByBoardLength(), byFraction);
        }

        /// <summary>
        /// Returns true if this cell is (or has just become) part of the currently-playing wave.
        /// While a wave is in progress, only its own members ever return true - everyone else
        /// waits for it to fully finish. Once the board is quiet (no wave active) and the
        /// inter-wave delay set by the last ReleaseFromWave has elapsed, the NEXT wave is picked
        /// here, at random, from every currently-eligible cell - so it's normal for this to return
        /// true for a cell that wasn't the one that happened to trigger the pick.
        /// </summary>
        public static bool TryJoinWave(CatFlapSpriteCellViewAdapter cell)
        {
            if (activeWave.Contains(cell))
            {
                return true;
            }

            if (activeWave.Count > 0)
            {
                return false;
            }

            if (Time.time < nextWaveAllowedTime)
            {
                return false;
            }

            PickNewWave();
            return activeWave.Contains(cell);
        }

        private static void PickNewWave()
        {
            int target = Mathf.Min(TargetWaveSize(), eligibleCells.Count);
            if (target <= 0)
            {
                return;
            }

            waveSelectionScratch.Clear();
            waveSelectionScratch.AddRange(eligibleCells);

            // Partial Fisher-Yates shuffle - only shuffles as many entries as we actually need to
            // pick, rather than the whole list.
            for (int i = 0; i < target; i++)
            {
                int swapIndex = UnityEngine.Random.Range(i, waveSelectionScratch.Count);
                (waveSelectionScratch[i], waveSelectionScratch[swapIndex]) = (waveSelectionScratch[swapIndex], waveSelectionScratch[i]);
                activeWave.Add(waveSelectionScratch[i]);
            }

            waveSelectionScratch.Clear();
        }

        /// <summary>
        /// Called once this cell's own play in the current wave has finished. Once every member of
        /// the wave has done the same (activeWave becomes empty), arms the board-wide quiet period
        /// - a fresh random delay between minDelaySeconds/maxDelaySeconds - before the next wave
        /// can be picked. A cell releasing outside of any wave (e.g. a tap-driven immediate play
        /// that bypassed wave gating - see CatFlapSpriteCellViewAdapter.PlayOneIdleClip) is a
        /// harmless no-op here and never arms/re-arms the delay.
        /// </summary>
        public static void ReleaseFromWave(CatFlapSpriteCellViewAdapter cell, float minDelaySeconds, float maxDelaySeconds)
        {
            bool wasInWave = activeWave.Remove(cell);
            if (wasInWave && activeWave.Count == 0)
            {
                float minDelay = Mathf.Max(0f, minDelaySeconds);
                float maxDelay = Mathf.Max(minDelay, maxDelaySeconds);
                nextWaveAllowedTime = Time.time + UnityEngine.Random.Range(minDelay, maxDelay);
            }
        }
    }
}
