using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    // Attached to each page GameObject under InfoPanelController.Pages (e.g. Rule1..Rule5).
    // Gives the panel explicit references to that page's own image/text instead of searching
    // the page's children, which breaks if a page's hierarchy has more than one Image/TMP_Text.
    public sealed class InfoPageView : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] private TMP_Text text;

        public void SetImage(Sprite sprite)
        {
            if (image != null && sprite != null)
                image.sprite = sprite;
        }

        public void SetText(string description)
        {
            if (text != null)
                text.text = description;
        }
    }
}
