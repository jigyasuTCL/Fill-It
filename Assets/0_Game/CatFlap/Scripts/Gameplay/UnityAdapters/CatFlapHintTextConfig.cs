using CatFlap.Models;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Player-facing hint sentences, one template per CatFlapHintReason that has an existing
    /// detection path in CatFlapForcedCellAnalyzer/CatFlapSolutionHintProvider. Loaded once from
    /// Resources/Configs/CatFlapHintTextConfig (see Load) alongside every other config
    /// ScriptableObject asset in that same single folder.
    ///
    /// {0}/{1}/{2} in a template are filled in from the hint's correct value - Cat/Fish is
    /// binary, so "the other value" is always just whichever one {0} isn't:
    /// {0} = correct value, singular ("cat"/"fish")
    /// {1} = other value, singular ("fish"/"cat")
    /// {2} = other value, plural ("fish"/"cats") - "fish" is already its own plural
    ///
    /// Reasons with no template here (CorrectNote, RuleBased, UnlocksForcedCells, and the three
    /// conditions with no detection logic yet - "every legal finish puts X here", "only one finish
    /// avoids copying another line", "placing X here dead-ends the board later") fall back to
    /// CatFlapSolutionHintProvider's own generic message.
    /// </summary>
    [CreateAssetMenu(fileName = "CatFlapHintTextConfig", menuName = "MangraGames/CatFlap/Hint Text Config")]
    public sealed class CatFlapHintTextConfig : ScriptableObject
    {
        private const string ResourcePath = "Configs/CatFlapHintTextConfig";

        [Header("Empty square sits next to 2 [same] that already touch")]
        [TextArea] [SerializeField] private string forcedAdjacentPairTemplate = "Two {2} are already touching.\nSo this one is a {0}!";

        [Header("Empty square sits in the gap between 2 [same]")]
        [TextArea] [SerializeField] private string forcedSandwichTemplate = "A {1} here makes 3.\nSo this one is a {0}!";

        [Header("The row or column already holds all its [other]")]
        [TextArea] [SerializeField] private string forcedLineMaximumTemplate = "This line already has all its {2}.\nSo this one is a {0}!";

        [Header("A placed [wrong value] cannot be correct in any valid finish")]
        [TextArea] [SerializeField] private string wrongTileTemplate = "This can't be a {1}.\nIt has to be a {0}!";

        [Header("No pattern applies, but only one answer fits the board")]
        [TextArea] [SerializeField] private string closestToCompletionTemplate = "Only one choice works here.\nIt's a {0}!";

        private static CatFlapHintTextConfig cachedInstance;
        private static bool triedLoad;

        /// <summary>Resources.Load'd once and cached; null (not an exception) if the asset was never created.</summary>
        public static CatFlapHintTextConfig Load()
        {
            if (!triedLoad)
            {
                cachedInstance = Resources.Load<CatFlapHintTextConfig>(ResourcePath);
                triedLoad = true;
            }

            return cachedInstance;
        }

        /// <summary>
        /// True and fills in message only for a reasonKey (CatFlapHintReason.ToString(), see
        /// CatFlapCellRevealResult.ReasonKey) this config has a template for.
        /// </summary>
        public bool TryBuildMessage(string reasonKey, CatFlapCellValue correctValue, out string message)
        {
            string template = ResolveTemplate(reasonKey);
            if (string.IsNullOrEmpty(template))
            {
                message = string.Empty;
                return false;
            }

            bool correctIsCat = correctValue == CatFlapCellValue.Cat;
            string target = correctIsCat ? "cat" : "fish";
            string otherSingular = correctIsCat ? "fish" : "cat";
            // "fish" is already its own plural - only "cat" pluralizes to "cats".
            string otherPlural = correctIsCat ? "fish" : "cats";
            message = string.Format(template, target, otherSingular, otherPlural);
            return true;
        }

        private string ResolveTemplate(string reasonKey)
        {
            switch (reasonKey)
            {
                case "ForcedAdjacentPair":
                    return forcedAdjacentPairTemplate;
                case "ForcedSandwich":
                    return forcedSandwichTemplate;
                case "ForcedLineMaximum":
                    return forcedLineMaximumTemplate;
                case "WrongTile":
                case "WrongTileInViolation":
                    return wrongTileTemplate;
                case "ClosestToCompletion":
                    return closestToCompletionTemplate;
                default:
                    return null;
            }
        }
    }
}
