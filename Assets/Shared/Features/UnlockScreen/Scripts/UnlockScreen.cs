using System;
using System.Collections.Generic;
using Shared.GameConfig;
using TMPro;
using UnityEngine;

namespace Shared.GameFlow
{
    public class UnlockScreen : MonoBehaviour
    {
        [Header("UI References")]
        public TMP_Text UnlockHeader;
        public TMP_Text UnlockDescription;

        [SerializeField]
        UnlockManager UnlockManager;

        [SerializeField]
        PopupAnimator popupAnimator; // drag the PopupAnimator on this popup

        [Tooltip("Plays the main-object/header/unlock-text/description/button staged reveal " +
                 "once the popup panel itself has finished opening.")]
        [SerializeField]
        UnlockRevealSequencer revealSequencer;

        private List<UnlockData> unlockDatas = new List<UnlockData>();
        private UnlockFeature currentFeature = UnlockFeature.None;

        public void ApplyConfig(List<UnlockData> unlockDatas)
        {
            this.unlockDatas = unlockDatas;
        }

        public void Show(int index)
        {
            if (index < 0 || index >= unlockDatas.Count)
            {
                Debug.LogWarning("UnlockScreen: index out of range: " + index);
                return;
            }

            UnlockData data = unlockDatas[index];
            currentFeature = data.feature;
            UnlockHeader.text = data.header;
            UnlockDescription.text = data.description;

            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.RewardScreen);

            revealSequencer?.SetMainObjectPrefab(data.mainObjectPrefab);
            revealSequencer?.ResetVisualState();

            // Blocks every other Button in the scene for as long as this popup is open - its own
            // buttons (e.g. Collect/Continue, under this transform) stay exempt. Matching
            // UnblockAllButtons call is in OnClose, once the popup has fully closed.
            InputManager.BlockAllButtonsExcept(transform);

            // PopupAnimator handles activating the GameObject itself (via rootToToggle),
            // so we don't SetActive here anymore. The staged inner reveal (main object ->
            // header -> unlock text -> description -> button) only starts once the popup panel
            // itself has finished scaling/fading in.
            popupAnimator.Open(() => revealSequencer?.Play());

            // MarkShown still deferred until OnClose, for restart-safety
        }

        public void OnClose()
        {
            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);

            // Fade out everything except the main object first (see
            // UnlockRevealSequencer.PlayExitFadeOut), THEN fly the main object to its feature's
            // Main Menu target (PlayMoveToTarget - no-ops if that feature has no target
            // configured), THEN play the popup's own close animation while notifying the manager
            // (which opens the unlocked feature) in parallel. Every other Button in the scene
            // stays blocked (see Show's BlockAllButtonsExcept) until BOTH of those finish - i.e.
            // until the unlocked feature's own panel has completely finished opening, not just
            // until this popup is gone - see WaitForBothThenUnblock.
            if (revealSequencer != null)
            {
                RectTransform mainMenuTarget = UnlockManager != null ? UnlockManager.GetMainMenuTarget(currentFeature) : null;

                revealSequencer.PlayExitFadeOut(() =>
                {
                    // Debug.LogError("1");
                    revealSequencer.PlayMoveToTarget(mainMenuTarget, () =>
                    {
                        // Debug.LogError("2");
                        Action onBothDone = WaitForBothThenUnblock();

                        UnlockManager.OnUnlockClosed(onBothDone);
                        popupAnimator.Close(onBothDone);
                    });
                });
                return;
            }

            popupAnimator.Close(() =>
            {
                UnlockManager.OnUnlockClosed(InputManager.UnblockAllButtons);
            });
        }

        /// <summary>
        /// Returns a callback that only calls InputManager.UnblockAllButtons once it has been
        /// invoked twice - lets the popup's own close animation and the unlocked feature's
        /// panel-open animation run in parallel (as before) while still keeping every other
        /// Button blocked until the SLOWER of the two actually finishes.
        /// </summary>
        private static Action WaitForBothThenUnblock()
        {
            int pending = 2;
            return () =>
            {
                pending--;
                if (pending == 0)
                {
                    InputManager.UnblockAllButtons();
                }
            };
        }
    }
}
