#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Inspector context-menu harness for RenderBoard - right-click the component header (or use
    /// its ⋮ menu) in Play mode and pick an entry under "Test".
    ///
    /// Exists because the real board is close to untestable by hand: scores come from levels won
    /// at or above GlobalConfig.LevelAttemptUnlockLevel, and rival movement is driven by real
    /// elapsed hours, so watching a rank actually change would mean winning levels or waiting out
    /// the simulation. These drive RenderBoard directly with a fake board instead, which is the
    /// same input the real one produces.
    ///
    /// Editor-only by compilation, not just by convention - none of this reaches a build.
    /// </summary>
    public sealed partial class LeaderboardPanelController
    {
        private const int FakeRivalCount = 19;

        private sealed class FakeRow
        {
            public string Name;
            public int Score;
            public bool IsPlayer;
        }

        // Kept between context-menu invocations so the "shuffle"/"move player" entries mutate the
        // SAME board rather than generating an unrelated one - rows can only be seen moving if
        // they were already somewhere to begin with.
        private List<FakeRow> fakeBoard;

        [ContextMenu("Test/1. Render Fake Board")]
        private void TestRenderFakeBoard()
        {
            if (!EnsureTestable())
            {
                return;
            }

            fakeBoard = new List<FakeRow>(FakeRivalCount + 1);
            for (int index = 0; index < FakeRivalCount; index++)
            {
                fakeBoard.Add(new FakeRow
                {
                    Name = $"Rival {index + 1:00}",
                    // Descending spread with jitter, so the starting board looks like a real one
                    // rather than a straight line.
                    Score = 120 - index * 5 + Random.Range(-2, 3)
                });
            }

            fakeBoard.Add(new FakeRow { Name = "You (test)", Score = 60, IsPlayer = true });

            // animate: false - nothing to animate from on a first render, same as Open() does.
            RenderBoard(BuildFakeEntries(), animate: false);
            Debug.Log("[Leaderboard] Rendered fake board. Run 2/3/4 to watch rows move.", this);
        }

        [ContextMenu("Test/2. Shuffle Scores (animated)")]
        private void TestShuffleScores()
        {
            if (!EnsureTestable() || !EnsureFakeBoard())
            {
                return;
            }

            // Small per-rival nudges rather than fresh random scores: a few rows swap places and
            // the rest hold, which is what a real refresh looks like and what the stagger/slide
            // was tuned against. Re-randomising everything just reads as noise.
            for (int index = 0; index < fakeBoard.Count; index++)
            {
                if (fakeBoard[index].IsPlayer)
                {
                    continue;
                }

                fakeBoard[index].Score = Mathf.Max(0, fakeBoard[index].Score + Random.Range(-6, 9));
            }

            RenderBoard(BuildFakeEntries(), animate: true);
        }

        [ContextMenu("Test/3. Player into top 3 (animated)")]
        private void TestPlayerIntoTopThree()
        {
            if (!EnsureTestable() || !EnsureFakeBoard())
            {
                return;
            }

            // Exercises the list -> podium swap: the player's list row is released to its pool and
            // a podium row takes over, which is the one transition that can't be a slide.
            int secondHighest = ResolveRivalScoreAt(1);
            SetFakePlayerScore(secondHighest + 1);
            RenderBoard(BuildFakeEntries(), animate: true);
        }

        [ContextMenu("Test/4. Player out of top 3 (animated)")]
        private void TestPlayerOutOfTopThree()
        {
            if (!EnsureTestable() || !EnsureFakeBoard())
            {
                return;
            }

            // The reverse swap - podium row released, list row takes over - landing the player
            // mid-list so the scroll list has rows both above and below them.
            int midRivalScore = ResolveRivalScoreAt(Mathf.Min(8, FakeRivalCount - 1));
            SetFakePlayerScore(Mathf.Max(0, midRivalScore - 1));
            RenderBoard(BuildFakeEntries(), animate: true);
        }

        [ContextMenu("Test/5. Rank climb message")]
        private void TestClimbMessage()
        {
            if (!EnsureTestable())
            {
                return;
            }

            // Goes through the controller rather than straight to the view, so the
            // showMessageOnRankClimb toggle is part of what's being tested - if nothing appears,
            // that flag is off.
            ShowRankChangeMessage(9, 4);
        }

        [ContextMenu("Test/6. Rank fall message")]
        private void TestFallMessage()
        {
            if (!EnsureTestable())
            {
                return;
            }

            ShowRankChangeMessage(4, 9);
        }

        [ContextMenu("Test/7. Scroll to player")]
        private void TestScrollToPlayer()
        {
            if (!EnsureTestable())
            {
                return;
            }

            ScrollToPlayer();
        }

        [ContextMenu("Test/Render REAL board")]
        private void TestRenderRealBoard()
        {
            if (!EnsureTestable())
            {
                return;
            }

            fakeBoard = null;
            Refresh(animate: false);
        }

        [ContextMenu("Test/Open panel")]
        private void TestOpenPanel()
        {
            if (!EnsureTestable())
            {
                return;
            }

            Open();
        }

        [ContextMenu("Test/Open with +5 score (win flow)")]
        private void TestOpenWithFiveScore()
        {
            TestOpenWithScoreGain(5);
        }

        [ContextMenu("Test/Open with +25 score (win flow)")]
        private void TestOpenWithTwentyFiveScore()
        {
            TestOpenWithScoreGain(25);
        }

        /// <summary>
        /// The whole level-win flow in one click: the board opens over the dim background at the
        /// player's CURRENT standing, holds, then the points land and the row climbs.
        ///
        /// Worth having as a cheat because the real path needs a level won with hearts to spare -
        /// and the interesting part, the climb, only reads correctly if the board was on screen
        /// showing the old position first. That ordering is exactly what breaks silently.
        /// </summary>
        private void TestOpenWithScoreGain(int points)
        {
            if (!EnsureTestable())
            {
                return;
            }

            if (leaderboard == null)
            {
                Debug.LogWarning("[Leaderboard] Assign the LeaderboardManager to test the win flow.", this);
                return;
            }

            Debug.Log(
                $"[Leaderboard] Win flow: opening at {leaderboard.PlayerScore} points, +{points} in "
                + $"{scoreGainDelaySeconds:0.##}s.",
                this);

            OpenWithScoreGain(points);
        }

        [ContextMenu("Test/Close panel")]
        private void TestClosePanel()
        {
            if (!EnsureTestable())
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Sorts and ranks the fake rows exactly the way LeaderboardManager.GetBoard does - score
        /// descending, ties to the player - so what RenderBoard receives here has the same shape as
        /// the real thing, including the player's tie-break advantage.
        /// </summary>
        private IReadOnlyList<LeaderboardEntry> BuildFakeEntries()
        {
            var sorted = new List<FakeRow>(fakeBoard);
            sorted.Sort((left, right) =>
            {
                int byScore = right.Score.CompareTo(left.Score);
                return byScore != 0 ? byScore : right.IsPlayer.CompareTo(left.IsPlayer);
            });

            var entries = new List<LeaderboardEntry>(sorted.Count);
            for (int index = 0; index < sorted.Count; index++)
            {
                entries.Add(new LeaderboardEntry(index + 1, sorted[index].Name, sorted[index].Score, sorted[index].IsPlayer));
            }

            return entries;
        }

        private void SetFakePlayerScore(int score)
        {
            for (int index = 0; index < fakeBoard.Count; index++)
            {
                if (fakeBoard[index].IsPlayer)
                {
                    fakeBoard[index].Score = Mathf.Max(0, score);
                    return;
                }
            }
        }

        /// <summary>Nth-highest rival score (0-based), ignoring the player - used to aim the player at a specific rank.</summary>
        private int ResolveRivalScoreAt(int index)
        {
            var scores = new List<int>(fakeBoard.Count);
            for (int rowIndex = 0; rowIndex < fakeBoard.Count; rowIndex++)
            {
                if (!fakeBoard[rowIndex].IsPlayer)
                {
                    scores.Add(fakeBoard[rowIndex].Score);
                }
            }

            if (scores.Count == 0)
            {
                return 0;
            }

            scores.Sort((left, right) => right.CompareTo(left));
            return scores[Mathf.Clamp(index, 0, scores.Count - 1)];
        }

        /// <summary>
        /// Play mode only, with the panel active. Rows animate via coroutines, which can't run in
        /// edit mode or on an inactive hierarchy - in either case the board would silently render
        /// with everything snapped, which looks exactly like a broken animation.
        /// </summary>
        private bool EnsureTestable()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Leaderboard] Test menu needs Play mode - row movement runs on coroutines.", this);
                return false;
            }

            if (panelRoot == null)
            {
                Debug.LogWarning("[Leaderboard] panelRoot is unassigned.", this);
                return false;
            }

            if (podiumRowPrefab == null || listRowPrefab == null)
            {
                Debug.LogWarning("[Leaderboard] Assign both podiumRowPrefab and listRowPrefab before testing.", this);
                return false;
            }

            // Activated directly rather than through Open(), which would re-render the REAL board
            // over whatever fake one is being tested.
            if (!panelRoot.activeSelf)
            {
                panelRoot.SetActive(true);
            }

            return true;
        }

        private bool EnsureFakeBoard()
        {
            if (fakeBoard != null && fakeBoard.Count > 0)
            {
                return true;
            }

            Debug.LogWarning("[Leaderboard] Run \"Test/1. Render Fake Board\" first - there's no board to move rows around on.", this);
            return false;
        }
    }
}
#endif
