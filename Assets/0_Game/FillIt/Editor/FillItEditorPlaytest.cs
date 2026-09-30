using System.Reflection;
using System.Text;
using FillIt.UnityAdapters;
using UnityEngine;

namespace FillIt.EditorTools
{
    /// <summary>
    /// Play-mode automation for checking the core loop from the editor: feeds the gameplay
    /// manager the same press / drag / release events the board raises for a real finger.
    /// </summary>
    public static class FillItEditorPlaytest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static FillItGameplayManager Manager => Object.FindFirstObjectByType<FillItGameplayManager>();

        /// <summary>Press on path[0], drag through the rest, release. Returns a one-line report.</summary>
        public static string Drag(int[] path)
        {
            FillItGameplayManager manager = Manager;
            if (manager == null || path == null || path.Length == 0)
            {
                return "no manager/path";
            }

            System.Type type = typeof(FillItGameplayManager);
            bool accepted = (bool)type.GetMethod("HandleCellPressed", Private).Invoke(manager, new object[] { path[0] });
            for (int index = 1; index < path.Length; index++)
            {
                type.GetMethod("HandleCellDragEntered", Private).Invoke(manager, new object[] { path[index] });
            }

            type.GetMethod("HandleCellReleased", Private).Invoke(manager, null);
            return $"accepted={accepted} " + Describe();
        }

        public static string Tap(int index)
        {
            return Drag(new[] { index });
        }

        public static string Pad(int value)
        {
            FillItGameplayManager manager = Manager;
            typeof(FillItGameplayManager).GetMethod("HandleNumberPadPressed", Private).Invoke(manager, new object[] { value });
            return Describe();
        }

        public static string Describe()
        {
            FillItGameplayManager manager = Manager;
            if (manager == null || manager.Controller == null || !manager.Controller.Session.HasLevel)
            {
                return "no level";
            }

            var session = manager.Controller.Session;
            var builder = new StringBuilder();
            builder.Append($"L{manager.CurrentLevelNumber} {session.Board.Size}x{session.Board.Size} state={session.GameState.Current} lives={session.LevelMistakeLimit.RemainingMistakes}/{session.LevelMistakeLimit.MaxMistakes} input={Shared.GameFlow.InputManager.GamePlayInputEnabled} board=");
            for (int index = 0; index < session.Board.CellCount; index++)
            {
                if (index > 0 && index % session.Board.Size == 0)
                {
                    builder.Append('/');
                }

                builder.Append(session.Board.GetValue(index));
            }

            builder.Append($" violations={session.Analysis.GetViolations().Count}");
            return builder.ToString();
        }
    }
}
