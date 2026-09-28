using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    public static class InputManager
    {
        public static bool GamePlayInputEnabled { get; set; }

        /// <summary>
        /// True while any modal popup (Settings, Buy Booster, Store, Unlock Screen, ...) has
        /// disabled every other Button in the scene via BlockAllButtonsExcept - see that method.
        /// </summary>
        public static bool UIInputBlocked => blockCount > 0;

        // Reference-counted so two popups briefly overlapping (one opening while another hasn't
        // finished closing) can't have the first Close() re-enable buttons the second one still
        // needs blocked - only the matching outermost Unblock actually restores anything.
        private static int blockCount;
        private static readonly List<Button> disabledButtons = new List<Button>();

        /// <summary>
        /// Disables every currently-interactable Button in the scene except ones under
        /// exemptRoot (typically the popup's own root RectTransform, so its own buttons - e.g.
        /// the Unlock Screen's Collect button - keep working while everything behind it doesn't).
        /// Must be paired with a matching UnblockAllButtons call once the popup fully closes.
        /// </summary>
        public static void BlockAllButtonsExcept(Transform exemptRoot)
        {
            blockCount++;

            foreach (Selectable selectable in Selectable.allSelectablesArray)
            {
                if (!(selectable is Button button) || !button.interactable)
                {
                    continue;
                }

                if (exemptRoot != null && button.transform.IsChildOf(exemptRoot))
                {
                    continue;
                }

                if (disabledButtons.Contains(button))
                {
                    continue;
                }

                button.interactable = false;
                disabledButtons.Add(button);
            }
        }

        /// <summary>
        /// Matching call for BlockAllButtonsExcept. Only actually re-enables buttons once every
        /// outstanding block has been released (blockCount back to zero).
        /// </summary>
        public static void UnblockAllButtons()
        {
            if (blockCount == 0)
            {
                return;
            }

            blockCount--;

            if (blockCount > 0)
            {
                return;
            }

            foreach (Button button in disabledButtons)
            {
                if (button != null)
                {
                    button.interactable = true;
                }
            }

            disabledButtons.Clear();
        }
    }
}
