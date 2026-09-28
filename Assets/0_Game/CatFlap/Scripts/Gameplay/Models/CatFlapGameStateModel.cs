using System;

namespace CatFlap.Models
{
    public sealed class CatFlapGameStateModel
    {
        public CatFlapGameState Current { get; private set; } = CatFlapGameState.Uninitialized;

        public bool TryTransitionTo(CatFlapGameState next)
        {
            if (!CanTransition(Current, next))
            {
                return false;
            }

            Current = next;
            return true;
        }

        public void TransitionTo(CatFlapGameState next)
        {
            if (!TryTransitionTo(next))
            {
                throw new InvalidOperationException($"Invalid Cat Flap state transition from {Current} to {next}.");
            }
        }

        public void Reset()
        {
            Current = CatFlapGameState.Uninitialized;
        }

        public static bool CanTransition(CatFlapGameState current, CatFlapGameState next)
        {
            switch (current)
            {
                case CatFlapGameState.Uninitialized:
                    return next == CatFlapGameState.MainMenu || next == CatFlapGameState.Initializing;
                case CatFlapGameState.MainMenu:
                    return next == CatFlapGameState.Initializing;
                case CatFlapGameState.Initializing:
                    return next == CatFlapGameState.Playing || next == CatFlapGameState.Failed || next == CatFlapGameState.MainMenu;
                case CatFlapGameState.Playing:
                    return next == CatFlapGameState.Resolving || next == CatFlapGameState.Won || next == CatFlapGameState.Failed || next == CatFlapGameState.Initializing || next == CatFlapGameState.MainMenu;
                case CatFlapGameState.Resolving:
                    return next == CatFlapGameState.Playing || next == CatFlapGameState.Won || next == CatFlapGameState.Failed || next == CatFlapGameState.Initializing || next == CatFlapGameState.MainMenu;
                case CatFlapGameState.Won:
                case CatFlapGameState.Failed:
                    return next == CatFlapGameState.Initializing || next == CatFlapGameState.MainMenu;
                default:
                    return false;
            }
        }
    }
}
