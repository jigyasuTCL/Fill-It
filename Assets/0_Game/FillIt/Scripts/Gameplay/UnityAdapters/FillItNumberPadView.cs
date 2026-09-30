using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// The 1-9 + erase keypad under the board: writes a number into the selected cell (the
    /// alternative to drawing regions by dragging). Buttons are assigned in the Inspector - index
    /// 0 of numberButtons is "1". Each number's label is tinted with that number's region colour.
    /// </summary>
    public sealed class FillItNumberPadView : MonoBehaviour
    {
        [SerializeField] private Button[] numberButtons = new Button[9];
        [SerializeField] private Button eraseButton;
        [SerializeField] private CanvasGroup canvasGroup;
        [Tooltip("Optional - numbers are coloured to match the board's region colours.")]
        [SerializeField] private FillItCellView colorSource;
        [SerializeField] private Color labelOutlineColor = new Color32(0x2B, 0x33, 0x5E, 0xFF);

        public event Action<int> NumberPressed = delegate { };

        private void Awake()
        {
            for (int index = 0; index < numberButtons.Length; index++)
            {
                Button button = numberButtons[index];
                if (button == null)
                {
                    continue;
                }

                int value = index + 1;
                button.onClick.AddListener(() => NumberPressed?.Invoke(value));
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = value.ToString();
                    if (colorSource != null)
                    {
                        Image image = button.GetComponent<Image>();
                        if (image != null)
                        {
                            image.color = colorSource.GetNumberColor(value);
                        }

                        label.color = labelOutlineColor;
                    }
                }
            }

            if (eraseButton != null)
            {
                eraseButton.onClick.AddListener(() => NumberPressed?.Invoke(0));
            }
        }

        public void SetInteractable(bool interactable)
        {
            if (canvasGroup != null)
            {
                canvasGroup.interactable = interactable;
                canvasGroup.blocksRaycasts = interactable;
            }
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
