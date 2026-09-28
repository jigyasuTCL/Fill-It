using System;
using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Models;
using Shared.Boosters;

namespace CatFlap.Views
{
    public interface ICatFlapAnimationView
    {
        void PlayCellChanged(CatFlapCellPosition position, CatFlapCellValue value, bool isNote, Action onComplete);
        void PlayCellsLocked(IReadOnlyList<CatFlapCellPosition> positions, Action onComplete);
        void PlayMistake(CatFlapCellPosition position, Action onComplete);
        void PlayBooster(BoosterType boosterType, CatFlapCellRevealResult result, Action onComplete);
        void PlayPageState(CatFlapGameState state, Action onComplete);
        void CancelAll();
    }
}
