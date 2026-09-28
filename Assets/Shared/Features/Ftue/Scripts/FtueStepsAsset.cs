using System.Collections.Generic;
using UnityEngine;

namespace Shared.Ftue
{
    // Designer-authored FTUE step content for a single tutorial sequence - one asset per level
    // (or per tutorial moment) rather than embedding this text in level data/JSON. Generic: just
    // an ordered list of FtueStepData, no knowledge of which level or game it belongs to - a
    // game's own code (e.g. Cat Flap's CatFlapGameplayManager) maps level numbers to assets.
    [CreateAssetMenu(fileName = "FtueSteps", menuName = "MangraGames/Ftue Steps")]
    public sealed class FtueStepsAsset : ScriptableObject
    {
        public List<FtueStepData> Steps = new List<FtueStepData>();
    }
}
