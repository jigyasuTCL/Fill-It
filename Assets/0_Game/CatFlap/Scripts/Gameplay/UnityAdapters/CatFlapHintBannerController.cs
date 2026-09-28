using CatFlap.Models;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Shows the hint icon + a runtime-built description while a hint highlight is active. The
    /// description text is chosen from the hint's reason key (why this cell/value was suggested),
    /// not a fixed string. Purely a UI reaction to CatFlapGameplayManager's hint state - it does
    /// not itself decide when a hint is shown or cleared.
    /// </summary>
    public sealed class CatFlapHintBannerController : MonoBehaviour
    {
        [SerializeField] private GameObject bannerRoot;
        [SerializeField] private PopupScaleAnimator animator;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI descriptionLabel;
        [SerializeField] private Sprite fishIconSprite;
        [SerializeField] private Sprite catIconSprite;
        [SerializeField] private string fishName = "Fish";
        [SerializeField] private string catName = "Cat";

        [Header("Description templates for reasons CatFlapHintTextConfig (Resources/Configs) has no entry for - {0} = suggested value, {1} = the other value")]
        [SerializeField] private string unlocksForcedCellsFormat = "Placing {0} here unlocks more forced cells nearby.";
        [SerializeField] private string oneMissingLineFormat = "This line just needs one more {0} to complete it.";
        [SerializeField] private string maxTwoAdjacentFormat = "Two adjacent cells already have {1} - place {0} here to fix it.";
        [SerializeField] private string leastEmptyLineFormat = "This row/column has the fewest empty cells left - try {0} here.";
        [SerializeField] private string fallbackFormat = "Place {0} here to help complete a line.";

        public void Show(CatFlapCellValue value, string reasonKey)
        {
            if (bannerRoot == null)
            {
                return;
            }

            if (iconImage != null)
            {
                Sprite icon = ResolveIcon(value);
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = ResolveDescription(value, reasonKey);
            }

            bannerRoot.SetActive(true);
            animator?.PlayOpen();
        }

        private string ResolveDescription(CatFlapCellValue value, string reasonKey)
        {
            if (CatFlapHintTextConfig.Load()?.TryBuildMessage(reasonKey, value, out string configuredMessage) == true)
            {
                return configuredMessage;
            }

            string valueName = value == CatFlapCellValue.Cat ? catName : fishName;
            string otherValueName = value == CatFlapCellValue.Cat ? fishName : catName;
            return string.Format(ResolveTemplate(reasonKey), valueName, otherValueName);
        }

        public void Hide()
        {
            if (bannerRoot == null || !bannerRoot.activeSelf)
            {
                return;
            }

            if (animator == null)
            {
                bannerRoot.SetActive(false);
                return;
            }

            animator.PlayClose(() => bannerRoot.SetActive(false));
        }

        private string ResolveTemplate(string reasonKey)
        {
            switch (reasonKey)
            {
                case "UnlocksForcedCells":
                    return unlocksForcedCellsFormat;
                case "OneMissingLine":
                    return oneMissingLineFormat;
                case "MaxTwoAdjacent":
                    return maxTwoAdjacentFormat;
                case "LeastEmptyLine":
                    return leastEmptyLineFormat;
                default:
                    return fallbackFormat;
            }
        }

        private Sprite ResolveIcon(CatFlapCellValue value)
        {
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return fishIconSprite;
                case CatFlapCellValue.Cat:
                    return catIconSprite;
                default:
                    return null;
            }
        }
    }
}
