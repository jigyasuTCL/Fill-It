using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Profile
{
    /// <summary>
    /// The profile chip on the main menu: shows the player's current avatar, frame and name, and
    /// opens the picker when tapped.
    ///
    /// Three states, mirroring SpinWheelMenuButtonUIController:
    /// - feature off in config  -> the whole thing is hidden, as though it never shipped.
    /// - coming soon (-1) or below the unlock level -> locked, not tappable, with a reason.
    /// - otherwise -> available, showing the real avatar/frame/name.
    ///
    /// Owns no rules. Which state applies is ProfileManager's answer (IsFeatureEnabled /
    /// IsComingSoon / IsFeatureUnlocked); this draws it and gates the tap.
    /// </summary>
    public sealed class ProfileMainMenuButton : MonoBehaviour
    {
        [Header("References")]
        // Assign in the Inspector - ProfileManager is not a singleton.
        [SerializeField] private ProfileManager profile;
        [SerializeField] private ProfilePanelController panel;
        [SerializeField] private Button button;

        [Header("Feature Root")]
        // Everything this component owns on screen. Hidden outright when the feature is disabled
        // in config - no placeholder, no locked badge, nothing.
        //
        // Assign it to a CHILD, not to this component's own object. Left unassigned the fallback
        // deactivates this GameObject, which takes this component down with it and makes the hide
        // one-way for the session - survivable only because Enabled comes from config loaded at
        // boot and never flips mid-run.
        [SerializeField] private GameObject featureRoot;

        [Header("Showcase")]
        // Author frameImage ABOVE avatarImage in the hierarchy, same as the leaderboard row - the
        // frame is a border drawn around the avatar.
        [SerializeField] private Image avatarImage;
        [SerializeField] private Image frameImage;
        [SerializeField] private TMP_Text nameLabel;

        [Header("States - exactly one visible")]
        [SerializeField] private GameObject availableRoot;
        [SerializeField] private GameObject lockedRoot;
        [SerializeField] private TMP_Text lockedLabel;

        [Header("Locked Copy")]
        [SerializeField] private string comingSoonText = "Coming Soon";
        // {0} is the level number, matching DailyChallengeButtonUI's own wording.
        [SerializeField] private string unlockLevelFormat = "Unlocks at level {0}";

        private void Awake()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(Open);
                button.onClick.AddListener(Open);
            }
        }

        private void OnEnable()
        {
            if (profile != null)
            {
                // Confirming in the picker changes the avatar behind this chip - without this the
                // menu keeps showing the old one until something else happens to redraw it.
                profile.ProfileChanged -= Refresh;
                profile.ProfileChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (profile != null)
            {
                profile.ProfileChanged -= Refresh;
            }
        }

        /// <summary>
        /// Hands over the live level number, same handshake MainMenuHandler already uses for
        /// SpinWheelMenuButtonUIController.SetLevel and DailyRewardMainMenuButton.LoadData. Call
        /// it from there:
        ///
        ///     profileMainMenuButton?.SetLevel(GetCurrentLevelNumber);
        ///
        /// Optional - without it the gate reads the saved PlayerData level index instead, which is
        /// correct on a cold main menu but lags the live number during a session.
        /// </summary>
        public void SetLevel(Func<int> currentLevelNumberProvider)
        {
            if (profile != null && currentLevelNumberProvider != null)
            {
                profile.SetLevelProvider(currentLevelNumberProvider);
            }

            Refresh();
        }

        /// <summary>
        /// Opens the picker. Safe to wire straight to a Button: a locked or disabled feature is
        /// refused here, not just visually - the button's own interactable flag is presentation,
        /// this is the gate.
        /// </summary>
        public void Open()
        {
            if (profile == null || panel == null || !profile.IsFeatureUnlocked)
            {
                return;
            }

            panel.Open();
        }

        /// <summary>
        /// Redraws state and showcase. Public so the menu can re-run it after a level win pushes
        /// the player past the unlock level.
        /// </summary>
        public void Refresh()
        {
            if (profile == null)
            {
                return;
            }

            if (!profile.IsFeatureEnabled)
            {
                SetActiveSafe(featureRoot != null ? featureRoot : gameObject, false);
                return;
            }

            SetActiveSafe(featureRoot != null ? featureRoot : gameObject, true);

            bool unlocked = profile.IsFeatureUnlocked;
            SetActiveSafe(availableRoot, unlocked);
            SetActiveSafe(lockedRoot, !unlocked);

            if (button != null)
            {
                button.interactable = unlocked;
            }

            if (!unlocked && lockedLabel != null)
            {
                // Coming soon has no level to name - saying "Unlocks at level -1" is the failure
                // this whole branch exists to avoid.
                lockedLabel.text = profile.IsComingSoon
                    ? comingSoonText
                    : string.Format(unlockLevelFormat, profile.UnlockLevel);
            }

            RefreshShowcase();
        }

        /// <summary>
        /// The avatar/frame/name are bound even while locked. They are the player's identity, not
        /// a reward for reaching the level - an empty chip behind a padlock reads as broken art,
        /// where the real avatar behind one reads as "this is yours, you just cannot change it
        /// yet".
        /// </summary>
        private void RefreshShowcase()
        {
            ApplySprite(avatarImage, profile.GetEquippedArt(ProfileSlot.Avatar));
            ApplySprite(frameImage, profile.GetEquippedArt(ProfileSlot.Frame));

            if (nameLabel != null)
            {
                nameLabel.text = profile.PlayerName;
            }
        }

        private static void ApplySprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            // Disabled, not transparent: a null sprite on an enabled Image draws a white box.
            image.enabled = sprite != null;
        }

        private static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
