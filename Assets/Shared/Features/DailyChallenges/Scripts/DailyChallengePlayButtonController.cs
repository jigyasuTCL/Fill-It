using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    /// <summary>
    /// The popup's single CTA button for the selected day - shows "Play (date)" or "Watch Ad to
    /// Play" depending on what DailyChallengeController.GetPlayRequirement/HasInProgressAttempt
    /// say. Resuming an in-progress attempt shows the normal "Play (date)" label too, not a
    /// separate "Continue" one.
    ///
    /// Purely a view - it owns no wiring of its own. DailyChallengeManager subscribes
    /// controller.SelectedDayChanged directly to Refresh, and wires Button's onClick straight to
    /// DailyChallengeController.PlayButtonClick, both once in Manager's own Awake/OnDestroy. There
    /// is deliberately no OnEnable/OnDisable here: nothing in this component's behavior depends on
    /// this GameObject's own active state, so there's nothing for a local OnEnable/OnDisable to do
    /// that Manager can't already do for it.
    /// </summary>
    public sealed class DailyChallengePlayButtonController : MonoBehaviour
    {
        [SerializeField] private DailyChallengeController controller;
        [SerializeField] private Button button;
        [SerializeField] private GameObject adsObject;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text Adslabel;
        [SerializeField] private string playText = "Play";
        [SerializeField] private string watchAdText = "Watch Ad to Play";

        /// <summary>Exposed so DailyChallengeManager can wire the click listener itself instead
        /// of this component subscribing to its own button.</summary>
        public Button Button => button;

        public void Refresh(DailyChallengeData data)
        {
            if (button == null || controller == null)
            {
                return;
            }

            if (data == null)
            {
                button.gameObject.SetActive(false);
                return;
            }

            if (controller.HasInProgressAttempt(data))
            {
                SetLabel(BuildPlayLabelWithDate(data));
                button.gameObject.SetActive(true);
                adsObject.gameObject.SetActive(false);

                return;
            }

            switch (controller.GetPlayRequirement(data))
            {
                case DailyChallengePlayRequirement.Free:
                    SetLabel(BuildPlayLabelWithDate(data));
                adsObject.gameObject.SetActive(false);
                    button.gameObject.SetActive(true);
                    break;
                case DailyChallengePlayRequirement.RewardedAd:
                    SetLabel("");
            var date = new DateTime(data.Year, data.Month, data.Day);

                    Adslabel.text=$"{playText}({date:MMM dd})".ToUpper();
                    button.gameObject.SetActive(true);
                    adsObject.gameObject.SetActive(true);
                    break;
                default:
                adsObject.gameObject.SetActive(false);
                    button.gameObject.SetActive(false);
                    break;
            }
        }

        // e.g. "Play (02 : Aug)" - so the CTA is unambiguous about which day it plays even when
        // the selected day isn't today (a missed day retried through the calendar).
        private string BuildPlayLabelWithDate(DailyChallengeData data)
        {
            var date = new DateTime(data.Year, data.Month, data.Day);
            return $"{playText}({date:MMM dd})".ToUpper();
        }

        private void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}