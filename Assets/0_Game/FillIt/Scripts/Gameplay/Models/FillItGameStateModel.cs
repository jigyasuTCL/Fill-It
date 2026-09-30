using System;

namespace FillIt.Models
{
    public sealed class FillItGameStateModel
    {
        public FillItGameState Current { get; private set; } = FillItGameState.Uninitialized;

        public bool TryTransitionTo(FillItGameState next)
        {
            if (!CanTransition(Current, next))
            {
                return false;
            }

            Current = next;
            return true;
        }

        public void TransitionTo(FillItGameState next)
        {
            if (!TryTransitionTo(next))
            {
                throw new InvalidOperationException($"Invalid Fill It state transition from {Current} to {next}.");
            }
        }

        public void Reset()
        {
            Current = FillItGameState.Uninitialized;
        }

        public static bool CanTransition(FillItGameState current, FillItGameState next)
        {
            switch (current)
            {
                case FillItGameState.Uninitialized:
                    return next == FillItGameState.MainMenu || next == FillItGameState.Initializing;
                case FillItGameState.MainMenu:
                    return next == FillItGameState.Initializing;
                case FillItGameState.Initializing:
                    return next == FillItGameState.Playing || next == FillItGameState.Failed || next == FillItGameState.MainMenu;
                case FillItGameState.Playing:
                    return next == FillItGameState.Resolving || next == FillItGameState.Won || next == FillItGameState.Failed || next == FillItGameState.Initializing || next == FillItGameState.MainMenu;
                case FillItGameState.Resolving:
                    return next == FillItGameState.Playing || next == FillItGameState.Won || next == FillItGameState.Failed || next == FillItGameState.Initializing || next == FillItGameState.MainMenu;
                case FillItGameState.Won:
                case FillItGameState.Failed:
                    return next == FillItGameState.Initializing || next == FillItGameState.MainMenu;
                default:
                    return false;
            }
        }
    }
}
