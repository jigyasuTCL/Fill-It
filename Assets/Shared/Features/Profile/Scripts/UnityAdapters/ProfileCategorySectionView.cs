using TMPro;
using UnityEngine;

namespace Shared.Profile
{
    /// <summary>
    /// One titled band inside a tab - the "Classic" header plus the grid of cells under it.
    ///
    /// Exists so the panel never has to lay anything out itself: put the GridLayoutGroup on
    /// itemsRoot and a ContentSizeFitter on this object in the prefab, and a tab of any number of
    /// categories stacks correctly under the tab's own VerticalLayoutGroup.
    /// </summary>
    public sealed class ProfileCategorySectionView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleLabel;
        // The decorative header strip around the title. Hidden along with the title on an untitled
        // category, so an ungrouped run of items reads as one continuous grid.
        [SerializeField] private GameObject titleRoot;
        [SerializeField] private RectTransform itemsRoot;

        /// <summary>Parent for this section's cells. Falls back to this object so a prefab with no explicit items root still works.</summary>
        public RectTransform ItemsRoot => itemsRoot != null ? itemsRoot : transform as RectTransform;

        public void SetTitle(string title)
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(title);

            if (titleLabel != null)
            {
                titleLabel.text = title ?? string.Empty;
            }

            if (titleRoot != null)
            {
                titleRoot.SetActive(hasTitle);
            }
            else if (titleLabel != null)
            {
                titleLabel.gameObject.SetActive(hasTitle);
            }
        }
    }
}
