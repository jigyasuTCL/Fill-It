using System.Collections.Generic;
using UnityEngine;

namespace FillIt.Levels
{
    /// <summary>Parses + validates level JSON into <see cref="FillItLevelData"/>.</summary>
    public sealed class FillItLevelRepository
    {
        public FillItLevelLoadResult LoadFromTextAsset(TextAsset asset)
        {
            return asset == null
                ? FillItLevelLoadResult.Failure("Level TextAsset is not assigned.")
                : LoadFromJson(asset.text);
        }

        public FillItLevelLoadResult LoadFromJson(string json)
        {
            if (!FillItLevelJson.TryParse(json, out FillItLevelData level, out string parseError))
            {
                return FillItLevelLoadResult.Failure(parseError);
            }

            List<string> errors = FillItLevelValidator.Validate(level);
            return errors.Count > 0 ? FillItLevelLoadResult.Failure(errors) : FillItLevelLoadResult.Success(level);
        }
    }
}
