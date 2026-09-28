using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Currency
{
    // Single reusable "coin total" display. Put this on the CoinContainer
    // prefab and reference it wherever a coin count is shown (HUD, level win,
    // out of lives, main menu, collect screen, daily reward, spin wheel, ...).
    // Callers call Refresh()/SetAmount() instead of writing to a TMP_Text
    // directly, so the icon/label wiring and text formatting only ever need
    // to change in this one prefab.
    public sealed class CoinContainerView : MonoBehaviour
    {
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private Image icon;

        // Pulls the player's current total straight from PlayerData - use this
        // whenever a container should just reflect the real total (HUD, main
        // menu, popups) rather than an in-flight/animated value.
        public void Refresh()
        {
            SetAmount(Shared.PlayerData.PlayerData.Instance.TotalCoins);
        }

        public void SetAmount(int amount)
        {
            if (amountText != null)
            {
                amountText.text = amount.ToString();
            }
        }

        public void SetIcon(Sprite sprite)
        {

            if (icon != null && sprite != null)
            {
                icon.sprite = sprite;
            }
        }

    }
}
