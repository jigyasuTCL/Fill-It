using FillIt.Runtime;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Shows the hint card while a hint highlight is active: the number to place plus a sentence
    /// explaining why (chosen from the hint's reason key). Purely a UI reaction to
    /// FillItGameplayManager's hint state - it does not decide when a hint is shown or cleared.
    /// </summary>
    public sealed class FillItHintBannerController : MonoBehaviour
    {
        [SerializeField] private GameObject bannerRoot;
        [SerializeField] private PopupScaleAnimator animator;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI descriptionLabel;
        [Tooltip("Optional - shows the revealed number in large type next to the description.")]
        [SerializeField] private TextMeshProUGUI valueLabel;
        [SerializeField] private string fallbackFormat = "Place a {0} here.";

        public void Show(int value, string reasonKey)
        {
            if (bannerRoot == null)
            {
                return;
            }

            if (iconImage != null)
            {
                iconImage.enabled = valueLabel == null && iconImage.sprite != null;
            }

            if (valueLabel != null)
            {
                valueLabel.text = value.ToString();
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = ResolveDescription(value, reasonKey);
            }

            bannerRoot.SetActive(true);
            animator?.PlayOpen();
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

        private string ResolveDescription(int value, string reasonKey)
        {
            if (FillItHintTextConfig.Load()?.TryBuildMessage(reasonKey, value, out string configuredMessage) == true)
            {
                return configuredMessage;
            }

            return string.Format(fallbackFormat, value);
        }
    }
}
