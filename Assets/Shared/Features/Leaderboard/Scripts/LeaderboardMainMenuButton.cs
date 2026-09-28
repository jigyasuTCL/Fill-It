using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// The leaderboard chip on the main menu: how long the event has left, where the player is
    /// standing, and whether there is a prize waiting. Opens the board when tapped.
    ///
    /// Three states, the same three ProfileMainMenuButton uses:
    /// - feature off in config -> hidden outright, as though it never shipped.
    /// - coming soon (-1) or below the unlock level -> locked, not tappable, with a reason.
    /// - otherwise -> available, showing the live details.
    ///
    /// Owns no rules. Which state applies, what the clock says and what is owed are all
    /// LeaderboardManager's answers; this draws them and gates the tap.
    /// </summary>
    public sealed class LeaderboardMainMenuButton : MonoBehaviour
    {
        [Header("References")]
        // Assign in the Inspector - LeaderboardManager is not a singleton.
        [SerializeField] private LeaderboardManager leaderboard;
        [SerializeField] private LeaderboardPanelController panel;
        [SerializeField] private Button button;

        [Header("Feature Root")]
        // Everything this component owns on screen. Hidden outright when the feature is disabled in
        // config - no placeholder, no locked badge, nothing.
        //
        // Assign it to a CHILD, not to this component's own object. Left unassigned the fallback
        // deactivates this GameObject, which takes this component down with it and makes the hide
        // one-way for the session - survivable only because Enabled comes from config loaded at
        // boot and never flips mid-run.
        [SerializeField] private GameObject featureRoot;

        [Header("States - exactly one visible")]
        [SerializeField] private GameObject availableRoot;
        [SerializeField] private GameObject lockedRoot;
        [SerializeField] private TMP_Text lockedLabel;

        [Header("Locked Copy")]
        [SerializeField] private string comingSoonText = "Coming Soon";
        // {0} is the level number, matching DailyChallengeButtonUI's own wording.
        [SerializeField] private string unlockLevelFormat = "Unlocks at level {0}";

        [Header("Timer")]
        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private string timerFormat = @"hh\:mm\:ss";
        // Shown instead while the board is between events, counting down to the next one.
        [SerializeField] private string cooldownTimerFormat = @"hh\:mm\:ss";
        // Optional caption swap, so the countdown can say what it is counting down to.
        [SerializeField] private GameObject timerRunningRoot;
        [SerializeField] private GameObject timerCooldownRoot;

        [Header("Standing")]
        [SerializeField] private TMP_Text rankLabel;
        [SerializeField] private TMP_Text scoreLabel;
        // {0} is the rank, already ordinal-suffixed.
        [SerializeField] private string rankFormat = "{0}";
        [SerializeField] private string scoreFormat = "{0}";
        // The player's own avatar and frame, if the chip shows them. Author frameImage ABOVE
        // avatarImage in the hierarchy - the frame is a border drawn around the avatar.
        [SerializeField] private Image avatarImage;
        [SerializeField] private Image frameImage;

        [Header("Gift")]
        // Shown while a reward is unclaimed. LeaderboardRewardBadge can drive its own pulse on the
        // same object; this only decides whether it is there at all.
        [SerializeField] private GameObject giftRoot;

        [Header("Refresh")]
        // The clock is cheap - it is two DateTime subtractions.
        [SerializeField] private float timerRefreshSeconds = 1f;
        // The standing is NOT cheap: reading it evaluates every bot's whole plan, so it runs on its
        // own slower beat. Rank moves over minutes on a menu nobody is playing from.
        [SerializeField] private float standingRefreshSeconds = 10f;

        private float timerCountdown;
        private float standingCountdown;
        private bool available;

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
            if (leaderboard != null)
            {
                // A reward settling while the menu is up has to light the gift here immediately,
                // not on the next slow tick.
                leaderboard.PendingRewardsChanged -= RefreshGift;
                leaderboard.PendingRewardsChanged += RefreshGift;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= RefreshGift;
            }
        }

        private void Update()
        {
            if (!available)
            {
                return;
            }

            timerCountdown -= Time.unscaledDeltaTime;
            if (timerCountdown <= 0f)
            {
                timerCountdown = Mathf.Max(0.1f, timerRefreshSeconds);
                RefreshTimer();
            }

            standingCountdown -= Time.unscaledDeltaTime;
            if (standingCountdown <= 0f)
            {
                standingCountdown = Mathf.Max(0.5f, standingRefreshSeconds);
                RefreshStanding();
            }
        }

        /// <summary>
        /// Redraws everything. Public so the menu can call it after anything that might have moved
        /// the player's score - finishing a level, for instance.
        /// </summary>
        public void Refresh()
        {
            if (leaderboard == null)
            {
                return;
            }

            bool enabled = leaderboard.Enabled;
            SetActive(featureRoot != null ? featureRoot : gameObject, enabled);

            if (!enabled)
            {
                available = false;
                return;
            }

            available = leaderboard.IsFeatureUnlocked;

            SetActive(availableRoot, available);
            SetActive(lockedRoot, !available);

            if (button != null)
            {
                button.interactable = available;
            }

            if (!available)
            {
                if (lockedLabel != null)
                {
                    lockedLabel.text = leaderboard.IsComingSoon
                        ? comingSoonText
                        : string.Format(unlockLevelFormat, leaderboard.UnlockLevel);
                }

                // A locked board still has nothing to collect, so make sure a stale gift from a
                // previous state is not left showing.
                SetActive(giftRoot, false);
                return;
            }

            RefreshTimer();
            RefreshStanding();
            RefreshGift();

            timerCountdown = Mathf.Max(0.1f, timerRefreshSeconds);
            standingCountdown = Mathf.Max(0.5f, standingRefreshSeconds);
        }

        private void RefreshTimer()
        {
            bool cooling = leaderboard.IsInCooldown;

            if (timerLabel != null)
            {
                timerLabel.text = cooling
                    ? leaderboard.TimeUntilNextEvent.ToString(cooldownTimerFormat)
                    : leaderboard.TimeUntilCycleEnd.ToString(timerFormat);
            }

            SetActive(timerRunningRoot, !cooling);
            SetActive(timerCooldownRoot, cooling);
        }

        private void RefreshStanding()
        {
            // One read, used for both labels: PlayerRank builds the whole board to answer, so
            // asking twice would do all of that work twice.
            if (rankLabel != null)
            {
                rankLabel.text = string.Format(rankFormat, ResolveOrdinal(leaderboard.PlayerRank));
            }

            if (scoreLabel != null)
            {
                scoreLabel.text = string.Format(scoreFormat, leaderboard.PlayerScore);
            }

            ApplySprite(avatarImage, leaderboard.PlayerAvatar);
            ApplySprite(frameImage, leaderboard.PlayerFrame);
        }

        private void RefreshGift()
        {
            SetActive(giftRoot, leaderboard != null && leaderboard.PendingRewards.HasRewards);
        }

        /// <summary>
        /// Opens the board. Safe to wire straight to a Button: a locked or disabled feature is
        /// refused here, not just visually - the button's interactable flag is presentation, this
        /// is the gate.
        /// </summary>
        public void Open()
        {
            if (leaderboard == null || panel == null || !leaderboard.IsFeatureUnlocked)
            {
                return;
            }

            panel.Open();
        }

        /// <summary>
        /// Hands over the live level number, the same handshake ProfileMainMenuButton.SetLevel and
        /// SpinWheelMenuButtonUIController use. Call it from the menu:
        ///
        ///     leaderboardMainMenuButton?.SetLevel(GetCurrentLevelNumber);
        ///
        /// Optional - without it the gate reads the saved PlayerData level index instead, which is
        /// correct on a cold main menu but lags the live number during a session.
        /// </summary>
        public void SetLevel(Func<int> currentLevelNumberProvider)
        {
            if (leaderboard != null && currentLevelNumberProvider != null)
            {
                leaderboard.SetLevelProvider(currentLevelNumberProvider);
            }

            Refresh();
        }

        private static void ApplySprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            // Disabled rather than left blank: an enabled Image with no sprite draws a white box.
            image.enabled = sprite != null;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static string ResolveOrdinal(int rank)
        {
            // 11th/12th/13th are the exception every naive suffix rule gets wrong.
            int lastTwo = rank % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return rank + "th";
            }

            switch (rank % 10)
            {
                case 1: return rank + "st";
                case 2: return rank + "nd";
                case 3: return rank + "rd";
                default: return rank + "th";
            }
        }
    }
}
