using UnityEditor;
using UnityEngine;

namespace Shared.Leaderboard.Editor
{
    /// <summary>
    /// Draws the normal LeaderboardManager inspector plus the cheats: nudging the player's score,
    /// and replaying a whole event compressed into a few seconds.
    ///
    /// Everything here is Play-mode only. Outside Play mode the manager has not initialised, its
    /// score lives in a PlayerData file rather than on the component, and there are no coroutines
    /// to drive a simulation with.
    /// </summary>
    [CustomEditor(typeof(LeaderboardManager))]
    public sealed class LeaderboardManagerEditor : UnityEditor.Editor
    {
        private const int ScoreStep = 10;
        private const float DefaultSimulationSeconds = 60f;

        private float simulationSeconds = DefaultSimulationSeconds;
        private int placementRank = 1;
        private int finishRank = 1;
        private float skipHours = 1f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (LeaderboardManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cheat", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to use these. The board lives in the PlayerData save, not on this component.",
                    MessageType.Info);
                return;
            }

            DrawScoreCheats(manager);
            EditorGUILayout.Space();
            DrawTimeSkipCheats(manager);
            EditorGUILayout.Space();
            DrawSimulationCheats(manager);
            EditorGUILayout.Space();
            DrawResultsCheats(manager);
        }

        /// <summary>
        /// Jumps the event clock rather than waiting it out. Unlike the simulation these are
        /// instant and leave the real system clock alone - they move the cycle's anchor, so every
        /// phase lands exactly where a genuine wait would have put it.
        /// </summary>
        private void DrawTimeSkipCheats(LeaderboardManager manager)
        {
            EditorGUILayout.LabelField("Skip time", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "Phase",
                manager.IsInCooldown
                    ? "cooldown - next event in " + Format(manager.TimeUntilNextEvent)
                    : "running - ends in " + Format(manager.TimeUntilCycleEnd));

            using (new EditorGUILayout.HorizontalScope())
            {
                finishRank = EditorGUILayout.IntSlider("Finish at rank", finishRank, 1, 20);

                if (GUILayout.Button("End event", GUILayout.Width(80f)))
                {
                    manager.CheatFinishEventAt(finishRank);
                    RepaintBoard(false);
                }
            }

            if (GUILayout.Button("Skip cooldown (start next event)"))
            {
                manager.CheatSkipCooldown();
                RepaintBoard(false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                skipHours = EditorGUILayout.FloatField("Skip hours", skipHours);

                if (GUILayout.Button("Skip", GUILayout.Width(80f)))
                {
                    manager.CheatSkipHours(skipHours);
                    RepaintBoard(false);
                }
            }

            EditorGUILayout.HelpBox(
                "End event sets the score that produces that placing, then jumps to the event's end so "
                + "the real settlement runs - payout, podium snapshot and cooldown all through the "
                + "normal path. You are left mid-cooldown: standings frozen, Collect button up, results "
                + "popup deliberately still closed. Skip cooldown is what opens it.\n\n"
                + "A rank below the last scoring bot is not reachable - the console reports the placing "
                + "that actually came out.",
                MessageType.None);

            // The phase readout above is a countdown; without this it only moves when something
            // else repaints the inspector.
            Repaint();
        }

        private static string Format(System.TimeSpan span)
        {
            return span.ToString(@"hh\:mm\:ss");
        }

        /// <summary>
        /// Triggers the results screen without waiting out an event - the usual reason it "does not
        /// open" is simply that the player did not finish in a paying place.
        /// </summary>
        private void DrawResultsCheats(LeaderboardManager manager)
        {
            EditorGUILayout.LabelField("Results screen", EditorStyles.boldLabel);

            bool pending = manager.PendingRewards.HasRewards;
            EditorGUILayout.LabelField(
                "Pending",
                pending ? "rank " + manager.PendingRewards.Rank + ", " + manager.PendingRewards.Rewards.Count + " reward(s)" : "nothing");

            using (new EditorGUILayout.HorizontalScope())
            {
                placementRank = EditorGUILayout.IntSlider("Finish at rank", placementRank, 1, 10);

                if (GUILayout.Button("Award", GUILayout.Width(70f)))
                {
                    manager.CheatGrantPlacement(placementRank);
                }
            }

            using (new EditorGUI.DisabledScope(!pending))
            {
                if (GUILayout.Button("Clear pending"))
                {
                    manager.CheatClearPendingRewards();
                }
            }

            EditorGUILayout.HelpBox(
                "Award builds the payout from the config table for that place and announces it, so the "
                + "results panel opens exactly as it would after a real event. A rank the table does not "
                + "cover pays nothing and opens nothing - that is the intended behaviour, not a bug.",
                MessageType.None);
        }

        private void DrawScoreCheats(LeaderboardManager manager)
        {
            EditorGUILayout.LabelField("Score", manager.PlayerScore.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("-" + ScoreStep))
                {
                    manager.CheatAddScore(-ScoreStep);
                    RepaintBoard(true);
                }

                if (GUILayout.Button("+" + ScoreStep))
                {
                    manager.CheatAddScore(ScoreStep);
                    RepaintBoard(true);
                }
            }
        }

        private void DrawSimulationCheats(LeaderboardManager manager)
        {
            EditorGUILayout.LabelField("Simulate event", EditorStyles.boldLabel);

            if (manager.IsSimulating)
            {
                if (GUILayout.Button("Stop simulation"))
                {
                    manager.CheatStopSimulation();
                }

                // Repainted every frame while running, so the score readout above actually ticks
                // instead of sitting on whatever it was when the button was pressed.
                Repaint();
                return;
            }

            simulationSeconds = EditorGUILayout.Slider("Over (seconds)", simulationSeconds, 5f, 300f);

            if (GUILayout.Button("Run the whole event in " + simulationSeconds.ToString("0") + "s"))
            {
                manager.CheatSimulateEvent(simulationSeconds);
                RepaintBoard(false);
            }

            // The winning run is a separate button rather than the default, because a simulation
            // that quietly rigged the result would be useless for judging whether the bots feel
            // fair to play against.
            if (GUILayout.Button("Run it and finish 1st"))
            {
                manager.CheatSimulateEventAsWinner(simulationSeconds);
                RepaintBoard(false);
            }

            EditorGUILayout.HelpBox(
                "Swaps the manager's clock for a fast one: bots join, play their bursts and go quiet, "
                + "the timer runs out and the event settles - all through the real code path, nothing "
                + "special-cased. Open the leaderboard panel first to watch it happen."
                + "A plain run almost never pays out: the player starts on nothing while the bots play "
                + "a full day, so they finish near last and the reward table covers only the top few. "
                + "Use the second button to see the results screen.",
                MessageType.None);
        }

        /// <summary>
        /// Redraws an open leaderboard so a change is visible immediately rather than on whatever
        /// happens to refresh it next.
        /// </summary>
        private static void RepaintBoard(bool animate)
        {
            LeaderboardPanelController panel = Object.FindFirstObjectByType<LeaderboardPanelController>();
            if (panel != null && panel.IsOpen)
            {
                panel.Refresh(animate);
            }
        }
    }
}
