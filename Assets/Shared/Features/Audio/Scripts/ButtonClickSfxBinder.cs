using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class ButtonClickSfxBinder : MonoBehaviour
{
    private readonly List<Button> boundButtons = new List<Button>();

    private void OnEnable()
    {
        Rebind();
    }

    private void OnDisable()
    {
        for (int index = 0; index < boundButtons.Count; index++)
        {
            if (boundButtons[index] != null)
            {
                boundButtons[index].onClick.RemoveListener(PlayClickSound);
            }
        }

        boundButtons.Clear();
    }

    public void Rebind()
    {
        OnDisable();
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            Button button = buttons[index];
            if (button == null)
            {
                continue;
            }

            // Skip buttons that already play their own click sound via ButtonPressScaleFeedback
            // (its OnPointerClick calls AudioManager.PlayClickSound()) - binding this too would
            // double the sound (and now the haptic) on every tap of that button.
            if (button.GetComponent<Shared.GameFlow.ButtonPressScaleFeedback>() != null)
            {
                continue;
            }

            button.onClick.RemoveListener(PlayClickSound);
            button.onClick.AddListener(PlayClickSound);
            boundButtons.Add(button);
        }
    }

    private static void PlayClickSound()
    {
        AudioManager.PlayClickSound();
        // Soft is the lowest-intensity ImpactFeedbackStyle - a light generic tap-back for any
        // button routed through this binder, distinct from the stronger gameplay-event haptics
        // (Rigid/Heavy) played directly at their own specific call sites.
        Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
    }
}
