using FillIt.Runtime;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Player-facing hint sentences, one template per <see cref="FillItHintReason"/>. Loaded once
    /// from Resources/Configs/FillItHintTextConfig alongside the other config assets.
    /// {0} in a template is the number the hint reveals.
    /// </summary>
    [CreateAssetMenu(fileName = "FillItHintTextConfig", menuName = "MangraGames/FillIt/Hint Text Config")]
    public sealed class FillItHintTextConfig : ScriptableObject
    {
        private const string ResourcePath = "Configs/FillItHintTextConfig";

        [Header("A placed number that can't be right")]
        [TextArea] [SerializeField] private string fixWrongNumberTemplate = "This number doesn't fit.\nIt has to be a {0}!";

        [Header("Empty cell that finishes a region")]
        [TextArea] [SerializeField] private string completeRegionTemplate = "One cell left for this {0}.\nIt goes here!";

        [Header("Empty cell that grows a region")]
        [TextArea] [SerializeField] private string growRegionTemplate = "This {0} needs more room.\nIt grows into here!";

        [Header("Empty cell that stands alone")]
        [TextArea] [SerializeField] private string singleCellTemplate = "This cell stands on its own.\nIt's a {0}!";

        [Header("Empty cell that starts a new region")]
        [TextArea] [SerializeField] private string startRegionTemplate = "A new region starts here.\nIt's a {0}!";

        private static FillItHintTextConfig cachedInstance;
        private static bool triedLoad;

        /// <summary>Resources.Load'd once and cached; null (not an exception) if the asset was never created.</summary>
        public static FillItHintTextConfig Load()
        {
            if (!triedLoad)
            {
                cachedInstance = Resources.Load<FillItHintTextConfig>(ResourcePath);
                triedLoad = true;
            }

            return cachedInstance;
        }

        public bool TryBuildMessage(string reasonKey, int value, out string message)
        {
            string template = ResolveTemplate(reasonKey);
            if (string.IsNullOrEmpty(template))
            {
                message = string.Empty;
                return false;
            }

            message = string.Format(template, value);
            return true;
        }

        private string ResolveTemplate(string reasonKey)
        {
            switch (reasonKey)
            {
                case FillItHintReason.FixWrongNumber:
                    return fixWrongNumberTemplate;
                case FillItHintReason.CompleteRegion:
                    return completeRegionTemplate;
                case FillItHintReason.GrowRegion:
                    return growRegionTemplate;
                case FillItHintReason.SingleCell:
                    return singleCellTemplate;
                case FillItHintReason.StartRegion:
                    return startRegionTemplate;
                default:
                    return null;
            }
        }
    }
}
