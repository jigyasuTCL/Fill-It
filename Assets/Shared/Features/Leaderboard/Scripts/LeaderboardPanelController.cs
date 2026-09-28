using System.Collections;
using System.Collections.Generic;
using Shared.Boot;
using Shared.GameConfig;
using Shared.GameFlow;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Renders LeaderboardManager's board into the panel: the top 3 go into their own authored
    /// podium slots, everybody else into a scrolling list below.
    ///
    /// The two use SEPARATE row prefabs (podium art is its own thing), so an entry is represented
    /// by a podium row or a list row, never both. Within one container a row is kept alive between
    /// renders and matched by identity rather than rebuilt - that is what lets a rank change
    /// animate: a row that was 7th and is now 5th is the SAME object sliding up two slots, not a
    /// new object appearing where the old one used to be. See RenderBoard.
    /// </summary>
    /// <remarks>
    /// partial: the Inspector context-menu test harness lives in
    /// LeaderboardPanelController.Testing.cs, editor-only, so it can drive RenderBoard with fake
    /// boards without shipping in a build - same file-split convention as
    /// CatFlapGameplayManager.Cheat.cs.
    /// </remarks>
    public sealed partial class LeaderboardPanelController : MonoBehaviour
    {
        [Header("Panel")]
        // Assign in the Inspector - there is no singleton to fall back on. Usually the component
        // on this same prefab; any LeaderboardManager in the scene works.
        [SerializeField] private LeaderboardManager leaderboard;
        // The object actually toggled on/off - keep this SEPARATE from the object this component
        // sits on, or Close() would deactivate this component along with it and no later Open()
        // could ever run.
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private PopupScaleAnimator popupAnimator;
        [SerializeField] private Button closeButton;

        [Header("Backgrounds - exactly one visible")]
        // The board's own full backdrop, for opening from the menu where there is nothing behind
        // it worth seeing.
        [SerializeField] private GameObject normalBackgroundRoot;
        // The see-through one, for opening ON TOP of something - the level-win flow, where the
        // board slides over the game the player just finished rather than replacing it.
        [SerializeField] private GameObject dimBackgroundRoot;

        [Header("Win Flow Continue")]
        // Shown by the WIN FLOW open only, in place of Play, and only when there is no gift: the
        // player either did not place or the event is still running, so there is nothing to
        // collect and the only thing left to do is carry on.
        [SerializeField] private GameObject winFlowContinueRoot;
        [SerializeField] private Button winFlowContinueButton;
        // Raised when it is pressed. The board does not know what comes next in a level win, so it
        // announces rather than acts - same split as onPlayRequested.
        [SerializeField] private UnityEngine.Events.UnityEvent onWinFlowContinue;
        [SerializeField] private bool closePanelOnWinFlowContinue = true;
        // A beat after the row settles, so the button does not appear under a finger still moving.
        [SerializeField] private float winFlowContinueExtraDelay = 0.15f;

        [Header("Score Gain Open")]
        // Raised the moment before the points land, while the board is still showing the standing
        // the player had when they started the level - see PrepareScoreGain.
        [SerializeField] private UnityEngine.Events.UnityEvent onBeforeScoreGain;
        // The pause before the points land. The board has to be readable at the OLD standing first
        // or the climb has no starting position the player ever saw.
        [SerializeField] private float scoreGainDelaySeconds = 0.8f;

        [Header("Info Panel")]
        // Opens the "how this works" panel over the board.
        [SerializeField] private Button infoButton;
        // The panel itself. Kept separate from the button so it can be hidden without taking the
        // button down with it.
        [SerializeField] private GameObject infoPanelRoot;
        // Optional. Without one the panel just appears, which is still correct, just less pretty.
        [SerializeField] private PopupScaleAnimator infoPanelAnimator;
        [SerializeField] private Button infoCloseButton;

        [Header("Top 3")]
        // The PREFERRED way to do the podium: author one row per place directly in the prefab,
        // position and dress each one by hand, and assign them here (index 0 = 1st). Those rows are
        // never spawned, moved, reparented or pooled - the board only binds whoever currently holds
        // each place into the row already sitting there, so the podium is entirely yours to lay out.
        //
        // Leave an index empty to fall back to the old behaviour for that place: a row spawned from
        // podiumRowPrefab into the matching topRankSlots entry. Leave the whole array empty and
        // nothing changes from before.
        [SerializeField] private LeaderboardRowView[] podiumRows = new LeaderboardRowView[3];
        // Show ranks 1-3 in the scroll list as well as on the podium, so the list is a complete
        // board rather than one that mysteriously starts at 4th. The two are independent rows and
        // animate independently: the list row slides like any other, the podium card does its own
        // dip-and-swap.
        //
        // Off restores the old split, where a place on the podium is absent from the list.
        [SerializeField] private bool showTopRanksInListToo = true;
        // Its own prefab - podium art (bigger frame, crown, pedestal) has little in common with an
        // ordinary list row, so they're authored separately rather than one prefab re-skinning
        // itself. Both carry a LeaderboardRowView; any label that place doesn't show is simply
        // left unassigned on that prefab.
        [SerializeField] private LeaderboardRowView podiumRowPrefab;
        // One authored slot per podium place, index 0 = rank 1. Position each slot however the art
        // wants - nothing here assumes they're uniform or in any particular arrangement.
        // Fewer than 3 slots is fine: any place without a slot falls through into the list.
        [SerializeField] private RectTransform[] topRankSlots = new RectTransform[3];
        // true: the row stretches to fill its slot, so each slot's own size decides how big that
        // place reads (1st bigger than 2nd and 3rd). false: the row keeps the podium prefab's own
        // authored size and the slots only mark positions.
        [SerializeField] private bool podiumRowFillsSlot = true;

        [Header("Rest (scroll list)")]
        [SerializeField] private LeaderboardRowView listRowPrefab;
        [SerializeField] private ScrollRect scrollRect;
        // The ScrollRect's Content. Must be anchored to the top (anchor min/max y = 1, pivot y = 1)
        // - RenderBoard positions rows downward from its top edge and sizes its height itself.
        [SerializeField] private RectTransform scrollContent;
        [SerializeField] private float rowHeight = 120f;
        [SerializeField] private float rowSpacing = 8f;
        [SerializeField] private float listTopPadding = 8f;
        [SerializeField] private float listBottomPadding = 8f;
        // Scrolls the player's own row into view when the board opens (skipped when they're on the
        // podium, which is always visible anyway). Without this, a player sitting 17th opens the
        // board to a list of strangers.
        [SerializeField] private bool scrollToPlayerOnOpen = true;

        [Header("Cycle Timer")]
        // Counts down to the end of the current leaderboard cycle, at which point the board resets
        // and the final placings pay out (see LeaderboardManager.RollCycle).
        [SerializeField] private TMPro.TMP_Text timerLabel;
        // d\:hh\:mm\:ss once a cycle runs past a day; the default 24h cycle never does.
        [SerializeField] private string timerFormat = @"hh\:mm\:ss";
        // Shown instead while the board is between events, counting down to the next one.
        [SerializeField] private string cooldownTimerFormat = @"hh\:mm\:ss";
        // Optional caption swap, so the countdown can say what it is counting down to.
        [SerializeField] private GameObject timerRunningRoot;
        [SerializeField] private GameObject timerCooldownRoot;

        [Header("Gifts")]
        [SerializeField] private Button collectGiftsButton;
        // Hidden whenever there is nothing to collect, so the button is never a dead tap.
        [SerializeField] private GameObject collectGiftsRoot;
        // When assigned, the Collect button OPENS THE RESULTS POPUP rather than silently paying
        // out - the player should see the podium and the prizes they won, not just watch a button
        // disappear. Without one it falls back to claiming directly.
        [SerializeField] private LeaderboardRewardPopupController rewardPopup;

        // The same corner of the panel, showing whichever action is actually available. A gift
        // waiting always wins - it is the one thing the player would lose by walking away.
        [SerializeField] private GameObject playButtonRoot;
        [SerializeField] private Button playButton;
        // What "play" means is the game's business, not the leaderboard's - this feature is Shared
        // and must not know what screen it is sending anybody to. Hook the main menu's play flow
        // up to this.
        [SerializeField] private UnityEngine.Events.UnityEvent onPlayRequested;
        // Closes the board on the way out. Off if the play flow does its own transition.
        [SerializeField] private bool closePanelOnPlay = true;
        // Resolves LeaderboardRewardConfigData.IconKey into the gift sprite each rewarded row
        // shows - the same shared database the store cards use.
        [SerializeField] private IconDatabaseConfig iconDatabase;

        [Header("Rank Change Message")]
        [SerializeField] private LeaderboardRankMessageView rankMessage;
        // A climb is the rank NUMBER going down (5th -> 3rd). Separate toggles because the two
        // aren't equally welcome: telling a player they've been overtaken is a different design
        // decision from congratulating them, and either can be switched off on its own.
        [SerializeField] private bool showMessageOnRankClimb = true;
        [SerializeField] private bool showMessageOnRankFall = true;

        [Header("Player Pin")]
        // Docks the player's own row to the edge of the viewport whenever their real row has been
        // scrolled out of sight - top edge when they are above the visible range, bottom when they
        // are below. Scrolling a long board should never lose track of where you are.
        [SerializeField] private bool pinPlayerRowWhenScrolledAway = true;
        // Where the pinned row lives: an empty object outside the viewport's mask and after the
        // Scroll View in the hierarchy, so it draws over the list instead of being clipped by it.
        //
        // Assign THIS and leave pinnedPlayerRow empty, and the row is spawned from listRowPrefab
        // into it on first use - which also guarantees the pin is identical to a list row, because
        // it is literally the same prefab.
        [SerializeField] private RectTransform pinnedRowContainer;
        // Only needed when the row is authored by hand instead. Left empty, it is filled in from
        // pinnedRowContainer above.
        [SerializeField] private LeaderboardRowView pinnedPlayerRow;
        // Where the pinned row docks, as empty RectTransforms used purely as markers. Each marks
        // the EDGE the row lines up with - its top edge against pinnedTopAnchor, its bottom edge
        // against pinnedBottomAnchor - matched in world space.
        //
        // Edges rather than pivots, because the two are rarely the same: a marker sitting flush on
        // the viewport's top edge has a top pivot, while a row prefab has a centre one, and lining
        // those up directly leaves half the row hanging outside the list.
        //
        // Leave them empty to keep the row wherever it was authored and only toggle its visibility.
        [SerializeField] private RectTransform pinnedTopAnchor;
        [SerializeField] private RectTransform pinnedBottomAnchor;

        [Header("Scroll Follow")]
        // While rows are sliding, keep the player's own row centred so they can watch themselves
        // move instead of it travelling off-screen. Scroll input is blocked for the same window -
        // a drag mid-slide fights the follow and neither wins.
        [SerializeField] private bool scrollFollowsPlayerDuringAnimation = true;
        // Held past the end of the slide so the list settles before the player can grab it again.
        [SerializeField] private float scrollFollowSettleSeconds = 0.15f;

        [Header("Animation")]
        // Only the PLAYER's row animates a rank change; every rival snaps straight to its new
        // place with no slide and no punch.
        //
        // The point is focus: when the player moves, twenty rivals sliding at the same time is
        // just noise around the one row that matters. Rivals drift constantly in the background,
        // and the player finds out where they ended up the next time the board is opened - which
        // is also when their new positions get their own fade-in.
        //
        // Off restores the original behaviour, where the whole board reshuffles as a staggered
        // wave.
        [SerializeField] private bool animateRankChangeForPlayerOnly = true;
        [SerializeField] private float rowMoveDurationSeconds = 0.35f;
        // Per-row delay so a reshuffle travels as a wave down the list instead of every row
        // starting in the same frame. 0 moves everything at once.
        [SerializeField] private float rowMoveStaggerSeconds = 0.03f;

        // Parent for pooled, currently-unused rows of both kinds. Left unassigned, spare rows are
        // parked inactive wherever they last lived.
        [SerializeField] private RectTransform poolRoot;

        // Live rows by identity key (see ResolveKey) - the previous render's rows, which the next
        // render matches against to tell "moved" from "newly appeared".
        private readonly Dictionary<string, LeaderboardRowView> activeRows = new Dictionary<string, LeaderboardRowView>();
        // Kept apart: a podium row and a list row are different prefabs and can never substitute
        // for each other.
        private readonly List<LeaderboardRowView> podiumPool = new List<LeaderboardRowView>();
        private readonly List<LeaderboardRowView> listPool = new List<LeaderboardRowView>();
        // Reused across renders so a refresh allocates nothing per row.
        private readonly List<string> renderedKeys = new List<string>();
        private readonly List<string> removedKeys = new List<string>();

        // The player's row key. A leading NUL so it can never collide with a rival name - see
        // ResolveKey for why the player is keyed by role rather than by name.
        private const string PlayerRowKey = "\0player";

        // What RenderBoard last drew. ScrollToPlayer reads this rather than calling GetBoard()
        // again - that call isn't a plain getter: it advances the simulation, can roll the epoch,
        // and writes the save.
        private IReadOnlyList<LeaderboardEntry> lastRenderedBoard;

        // The player's rank as of the previous render, which is what a change is measured against.
        // 0 until the first render: nothing to compare with yet, and a brand-new board must not
        // announce that the player "climbed" to wherever they already were.
        private int lastPlayerRank;

        // Set only across Open()'s own first render - see there. The rank is still tracked while
        // this is true, just not announced.
        private bool suppressRankMessage;

        // One-shot latch for ClearAuthoredRows - see there for why it must never run twice.
        private bool authoredRowsCleared;

        // Where the player landed in the scroll list this render, or -1 when they are on the
        // podium (and so have no list row to follow).
        private int playerListIndex = -1;
        // End-to-end length of the player row's animation this render, straight from the row
        // itself - delay, lift, travel and settle. The follow holds the scroll for exactly this
        // long, so retuning the climb cannot leave the two out of step.
        private float playerAnimationSeconds;
        private Coroutine followRoutine;

        // The player's entry as of the last render, so the pinned row can be kept in step without
        // re-reading the board (GetBoard is not a plain getter - it advances the simulation).
        private LeaderboardEntry playerEntry;
        private bool hasPlayerEntry;

        // Reused by RefreshPlayerPin every frame - GetWorldCorners fills a caller-supplied array,
        // and allocating two of them per frame would be pure garbage.
        private readonly Vector3[] viewportCorners = new Vector3[4];
        private readonly Vector3[] rowCorners = new Vector3[4];
        // Whether the follow currently owns the ScrollRect, and what it was before. Kept as fields
        // rather than coroutine locals so the state can be restored from OUTSIDE the coroutine -
        // Unity does not reliably run a finally block when a coroutine is stopped, and a follow
        // interrupted by the next render would otherwise leave the list unscrollable for good.
        private bool scrollInputBlocked;
        private bool scrollWasEnabledBeforeFollow;

        // Which authored podium places this render actually filled. Reused between renders so
        // a refresh allocates nothing.
        private readonly List<LeaderboardRowView> boundPodiumRows = new List<LeaderboardRowView>();

        // Set the instant Open is asked for, and read by StartClosed - see StartClosed for why the
        // ordering matters.
        private bool openRequested;

        // Which open this is. The action button differs: a menu open offers Play, a win-flow open
        // offers Continue - and neither belongs on the other.
        private bool openedForWinFlow;
        private Coroutine scoreGainRoutine;
        // What the running routine still owes, so Close can pay it instead of losing it.
        private int pendingScoreGain;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (collectGiftsButton != null)
            {
                collectGiftsButton.onClick.RemoveListener(CollectGifts);
                collectGiftsButton.onClick.AddListener(CollectGifts);
            }

            if (playButton != null)
            {
                playButton.onClick.RemoveListener(Play);
                playButton.onClick.AddListener(Play);
            }

            if (winFlowContinueButton != null)
            {
                winFlowContinueButton.onClick.RemoveListener(WinFlowContinue);
                winFlowContinueButton.onClick.AddListener(WinFlowContinue);
            }

            if (infoButton != null)
            {
                infoButton.onClick.RemoveListener(OpenInfo);
                infoButton.onClick.AddListener(OpenInfo);
            }

            if (infoCloseButton != null)
            {
                infoCloseButton.onClick.RemoveListener(CloseInfo);
                infoCloseButton.onClick.AddListener(CloseInfo);
            }

            // Authored visible so its contents can be edited in the prefab, but never visible on
            // the first open - same rule the panel itself follows.
            if (infoPanelRoot != null && infoPanelRoot.activeSelf)
            {
                infoPanelRoot.SetActive(false);
            }

            StartClosed();
        }

        private void OnEnable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= RefreshGiftButton;
                leaderboard.PendingRewardsChanged += RefreshGiftButton;
            }

            // Awake alone is not enough: this lives under the main menu root, which is switched off
            // for gameplay and back on afterwards, and Awake runs once at scene load. Anything that
            // leaves panelRoot active during that round trip brings the board back on screen
            // looking like it opened itself.
            StartClosed();
            RefreshGiftButton();
        }

        private void OnDisable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= RefreshGiftButton;
            }

            // The close animation is a coroutine, and deactivating the menu kills it before its
            // callback runs - which is exactly what leaves panelRoot switched on.
            if (!openRequested && panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Makes sure the board is down at startup, whatever state it was saved in.
        ///
        /// The panel gets left switched on in the prefab all the time while its art is being
        /// authored - that is the only way to see it in the Editor - and without this it is on
        /// screen the moment the menu appears. Nothing opens the board on its own; only Open does.
        ///
        /// The openRequested guard is what stops this eating a legitimate open: when panelRoot is
        /// this same object, Open's SetActive(true) runs this component's OnEnable synchronously,
        /// before the rest of Open, so without the flag the panel would switch itself straight back
        /// off and Open would silently do nothing.
        /// </summary>
        private void StartClosed()
        {
            if (!openRequested && panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }
        }

        public bool IsInfoOpen => infoPanelRoot != null && infoPanelRoot.activeSelf;

        /// <summary>
        /// Shows the info panel over the board with the same ease-back pop every other popup here
        /// uses. Safe to call again while it is already up - it will not restart the animation
        /// mid-way, which is what a double tap on the button would otherwise do.
        /// </summary>
        public void OpenInfo()
        {
            if (infoPanelRoot == null || IsInfoOpen)
            {
                return;
            }

            infoPanelRoot.SetActive(true);
            infoPanelAnimator?.PlayOpen();
        }

        /// <summary>
        /// Hides it again, waiting for the close animation to finish before deactivating so the
        /// panel is not snapped out of existence mid-tween.
        /// </summary>
        public void CloseInfo()
        {
            if (infoPanelRoot == null || !IsInfoOpen)
            {
                return;
            }

            if (infoPanelAnimator != null)
            {
                infoPanelAnimator.PlayClose(() => infoPanelRoot.SetActive(false));
                return;
            }

            infoPanelRoot.SetActive(false);
        }

        private void Update()
        {
            // Only while the panel is up - this is the one thing here that has to tick, and there
            // is no point running it behind a closed popup.
            if (IsOpen)
            {
                RefreshTimer();
                RefreshPlayerPin();
            }
        }

        /// <summary>
        /// Draws the countdown to the end of the cycle. Reads the manager every tick rather than
        /// counting down a local float, so backgrounding the app or changing the clock cannot make
        /// the display drift away from the real deadline.
        /// </summary>
        private void RefreshTimer()
        {
            if (timerLabel == null || leaderboard == null)
            {
                return;
            }

            bool cooling = leaderboard.IsInCooldown;

            timerLabel.text = cooling
                ? leaderboard.TimeUntilNextEvent.ToString(cooldownTimerFormat)
                : leaderboard.TimeUntilCycleEnd.ToString(timerFormat);

            SetActive(timerRunningRoot, !cooling);
            SetActive(timerCooldownRoot, cooling);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        /// <summary>
        /// Pays out whatever is waiting. The manager clears the pending record before it raises
        /// its event, so this cannot double-pay however fast the button is tapped.
        /// </summary>
        public void CollectGifts()
        {
            if (leaderboard == null || !leaderboard.PendingRewards.HasRewards)
            {
                return;
            }

            // The popup claims for itself once the player has been through it, so this must NOT
            // claim first - doing both would leave the popup with nothing to show.
            if (rewardPopup != null)
            {
                rewardPopup.Open();
                return;
            }

            if (!leaderboard.ClaimPendingRewards())
            {
                return;
            }

            RefreshGiftButton();
            // The cycle that just paid out also reset every score, so the board underneath is now
            // stale - redraw it rather than leaving yesterday's standings on screen.
            Refresh(animate: false);
        }

        /// <summary>
        /// Decides which action the panel offers: COLLECT when there is an uncollected gift,
        /// PLAY otherwise.
        ///
        /// One or the other, never both. A gift takes priority whatever phase the event is in -
        /// during a cooldown it is the only thing left to do, and while the next event is already
        /// running it is still owed. Play is what the panel falls back to once there is nothing to
        /// claim, which is the ordinary case.
        ///
        /// Called on open, after every render and whenever a reward is claimed, so the swap cannot
        /// be left showing a Collect button for a gift that is already gone.
        /// </summary>
        private void RefreshGiftButton()
        {
            bool hasGifts = leaderboard != null && leaderboard.PendingRewards.HasRewards;

            if (collectGiftsRoot != null)
            {
                collectGiftsRoot.SetActive(hasGifts);
            }

            if (collectGiftsButton != null)
            {
                collectGiftsButton.interactable = hasGifts;
            }

            // Play belongs to the menu open only. In the win flow the player is mid-level-win;
            // offering them "Play" there would be offering what they are already doing.
            bool showPlay = !hasGifts && !openedForWinFlow;
            bool showContinue = !hasGifts && openedForWinFlow;

            if (playButtonRoot != null)
            {
                playButtonRoot.SetActive(showPlay);
            }

            // Also driven when the buttons share a root, or a hidden one would stay clickable
            // underneath whatever replaced it.
            if (playButton != null)
            {
                playButton.interactable = showPlay;
            }

            if (winFlowContinueRoot != null)
            {
                winFlowContinueRoot.SetActive(showContinue);
            }

            if (winFlowContinueButton != null)
            {
                // Shown but NOT usable while a score animation is still running. ScoreGainRoutine
                // clears scoreGainRoutine and calls back here once the row has finished moving,
                // which is what turns it on. Outside the win flow there is nothing to wait for.
                winFlowContinueButton.interactable = showContinue && scoreGainRoutine == null;
            }
        }

        /// <summary>
        /// Continues the level-win flow. Announces rather than acts - the board has no idea what
        /// comes after a win.
        /// </summary>
        public void WinFlowContinue()
        {
            if (!closePanelOnWinFlowContinue)
            {
                RaiseWinFlowContinued();
                return;
            }

            Close();

            // Announced AFTER the board has actually gone, not when the button is pressed. What
            // waits on this puts its own popup up, and firing during the close animation lands it
            // on top of a board still scaling out.
            if (isActiveAndEnabled)
            {
                StartCoroutine(RaiseWinFlowContinuedWhenClosed());
                return;
            }

            RaiseWinFlowContinued();
        }

        private IEnumerator RaiseWinFlowContinuedWhenClosed()
        {
            // Bounded: a close that somehow never completes must not strand the level-win flow
            // with no popup at all.
            for (float elapsed = 0f; elapsed < 1f && IsOpen; elapsed += Time.unscaledDeltaTime)
            {
                yield return null;
            }

            RaiseWinFlowContinued();
        }

        private void RaiseWinFlowContinued()
        {
            onWinFlowContinue?.Invoke();
            WinFlowContinued?.Invoke();
        }

        /// <summary>
        /// Raised when the player presses Continue on a win-flow open. The code-side twin of
        /// onWinFlowContinue, for a caller that has to WAIT for it - the level-win flow, which
        /// holds its own popup back until the board is done.
        /// </summary>
        public event System.Action WinFlowContinued;

        /// <summary>
        /// Leaves the board for the game. Raises onPlayRequested rather than loading anything
        /// itself - the leaderboard is Shared and has no business knowing what a Cat Flap level is.
        /// </summary>
        public void Play()
        {
            if (closePanelOnPlay)
            {
                Close();
            }

            onPlayRequested?.Invoke();
        }

        /// <summary>
        /// Which gift icon a placing shows, straight off the reward table. Null - the usual answer -
        /// hides the row's gift slot entirely.
        /// </summary>
        private Sprite ResolveGiftSprite(int rank)
        {
            if (iconDatabase == null)
            {
                return null;
            }

            LeaderboardConfigData config = GlobalConfigRuntimeContext.Config?.Leaderboard;
            if (config == null || config.Rewards == null)
            {
                return null;
            }

            for (int index = 0; index < config.Rewards.Count; index++)
            {
                LeaderboardRewardConfigData row = config.Rewards[index];
                if (row == null || !row.Matches(rank) || row.Rewards == null)
                {
                    continue;
                }

                // The first prize with an icon stands in for the whole placement: one slot on a
                // board row cannot show three things, and the results popup lists them all anyway.
                for (int rewardIndex = 0; rewardIndex < row.Rewards.Count; rewardIndex++)
                {
                    LeaderboardRewardItemData reward = row.Rewards[rewardIndex];
                    if (reward == null || string.IsNullOrWhiteSpace(reward.IconKey))
                    {
                        continue;
                    }

                    if (iconDatabase.TryGetIcon(reward.IconKey, out Sprite icon))
                    {
                        return icon;
                    }
                }

                return null;
            }

            return null;
        }

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>Opens the board from the menu, with its own solid background.</summary>
        public void Open()
        {
            Open(dimBackground: false);
        }

        /// <summary>
        /// Opens the board over whatever is already on screen - the level-win flow - waits, then
        /// adds the points the player just earned and lets the row climb to its new place.
        ///
        /// The delay is the whole point. Adding the score before the board is readable would show
        /// the player only the result; they need to see where they WERE for the climb to mean
        /// anything.
        /// </summary>
        public void OpenWithScoreGain(int points)
        {
            Open(dimBackground: true);

            if (scoreGainRoutine != null)
            {
                StopCoroutine(scoreGainRoutine);
                scoreGainRoutine = null;
            }

            pendingScoreGain = points;

            if (points > 0 && leaderboard != null && isActiveAndEnabled)
            {
                scoreGainRoutine = StartCoroutine(ScoreGainRoutine(points));
            }
            else if (points > 0 && leaderboard != null)
            {
                // Nowhere to run a coroutine - grant straight away rather than silently dropping
                // points the player has already earned.
                leaderboard.AddWinPoints(points);
            }

            // AFTER the routine exists, not before. Open already refreshed the buttons, but it ran
            // while scoreGainRoutine was still null - so Continue came up live, in the one case it
            // is supposed to wait for the row to finish moving.
            RefreshGiftButton();
        }

        private IEnumerator ScoreGainRoutine(int points)
        {
            // Realtime: the board is usually shown over a paused game.
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, scoreGainDelaySeconds));

            // Cleared before the grant, so a Close landing mid-animation cannot pay it twice.
            pendingScoreGain = 0;

            PrepareScoreGain(points);

            leaderboard.AddWinPoints(points);
            // animate: true - this is the one open where the movement IS the content.
            Refresh(animate: true);

            // The row is travelling now. playerAnimationSeconds is exactly how long that takes,
            // set by the render that just ran - including the climb lift, which is why this is
            // read from the render rather than assumed to be rowMoveDurationSeconds.
            yield return new WaitForSecondsRealtime(
                Mathf.Max(0f, playerAnimationSeconds) + Mathf.Max(0f, winFlowContinueExtraDelay));

            // Cleared LAST: RefreshGiftButton keeps Continue disabled while this is non-null, so
            // the button cannot be pressed out from under a row still moving.
            scoreGainRoutine = null;
            RefreshGiftButton();
        }

        /// <summary>
        /// The last moment before the score changes.
        ///
        /// Everything on screen still shows the standing the player STARTED the level on: their
        /// old rank, their old points, the rivals above them. Anything that needs to read, capture
        /// or dress up the "before" goes here - a badge on the current position, a count-up label
        /// seeded with the old value, a sound cue for the climb about to happen.
        ///
        /// Add code directly, or wire onBeforeScoreGain in the Inspector; both run at exactly this
        /// point, before AddWinPoints and before the row animates.
        /// </summary>
        private void PrepareScoreGain(int points)
        {
            onBeforeScoreGain?.Invoke();
        }

        private void Open(bool dimBackground)
        {
            if (panelRoot == null)
            {
                return;
            }

            openedForWinFlow = dimBackground;

            // Set before panelRoot goes live, so the correct backdrop is already in place for the
            // first frame rather than swapping a frame later.
            SetActive(normalBackgroundRoot, !dimBackground);
            SetActive(dimBackgroundRoot, dimBackground);

            // Set before SetActive, never after: activating panelRoot can run this component's own
            // OnEnable, and StartClosed there has to be able to tell a real open from a leftover.
            openRequested = true;

            // Activated BEFORE rendering: rows animate with coroutines, which can't start on an
            // inactive hierarchy (they'd silently snap into place instead).
            panelRoot.SetActive(true);

            // The banner usually lives inside the panel, which is about to scale up from zero - a
            // slide played during that gets squashed by the parent's scale and reads as broken. So
            // the opening render only RECORDS the new rank; the message itself waits for the popup
            // to finish opening.
            int rankBeforeOpen = lastPlayerRank;
            suppressRankMessage = true;
            Refresh(animate: false);
            suppressRankMessage = false;
            int rankAfterOpen = lastPlayerRank;

            RefreshTimer();
            RefreshGiftButton();
            // Starts hidden; the first Update decides whether the player is actually off-screen.
            SetPinnedRowVisible(false);

            if (scrollToPlayerOnOpen)
            {
                // Deferred a frame: on the FIRST open the panel was inactive, so its viewport
                // hasn't been through a layout pass yet and measures as zero height - scrolling
                // against that silently does nothing. By the next frame the rects are real.
                StartCoroutine(ScrollToPlayerNextFrame());
            }

            if (popupAnimator != null)
            {
                popupAnimator.PlayOpen(() => ShowRankChangeMessage(rankBeforeOpen, rankAfterOpen));
                return;
            }

            ShowRankChangeMessage(rankBeforeOpen, rankAfterOpen);
        }

        public void Close()
        {
            // Cleared here and nowhere else. Left latched it would tell StartClosed that every
            // later re-enable was a deliberate open, which is how the board comes back by itself.
            openRequested = false;

            // Closing before the points landed must not drop them - they were earned by winning a
            // level, and the board is only where they are SHOWN.
            if (scoreGainRoutine != null)
            {
                StopCoroutine(scoreGainRoutine);
                scoreGainRoutine = null;
                leaderboard?.AddWinPoints(pendingScoreGain);
            }

            if (panelRoot == null)
            {
                return;
            }

            // A banner still mid-pass would otherwise be frozen part-way across and be sitting
            // there, stale, the next time the panel opens.
            rankMessage?.HideImmediate();

            // Same reasoning for a follow caught mid-slide: closing while it owns the ScrollRect
            // would strand the component disabled, and the next Open would show a list that cannot
            // be scrolled at all.
            if (followRoutine != null)
            {
                StopCoroutine(followRoutine);
                followRoutine = null;
            }

            RestoreScrollInput();
            SetPinnedRowVisible(false);

            // Straight off, not animated: it is about to be hidden behind the closing board
            // anyway, and leaving it open would have it waiting there on the next open.
            if (infoPanelRoot != null)
            {
                infoPanelRoot.SetActive(false);
            }

            // isActiveAndEnabled, because PlayClose cannot run its coroutine on a disabled
            // object - the board would simply stay open forever.
            if (popupAnimator != null && isActiveAndEnabled)
            {
                popupAnimator.PlayClose(() => panelRoot.SetActive(false));
                return;
            }

            panelRoot.SetActive(false);
        }

        /// <summary>
        /// Pulls the current board and renders it. animate: false snaps (what opening wants - the
        /// board should already be in its final shape as the popup scales in); true slides every
        /// row that changed place, for a refresh while the panel is already open.
        /// </summary>
        public void Refresh(bool animate = true)
        {
            if (leaderboard == null)
            {
                return;
            }

            RenderBoard(leaderboard.GetBoard(), animate);
        }

        /// <summary>
        /// Places every entry: ranks 1-3 into their own podium slot, the rest down the scroll list
        /// in order. This is the single place that decides where a row belongs, so the two
        /// containers can never disagree about who is where.
        ///
        /// Cases per entry:
        /// - still in the list, new rank: slides to its new slot, staggered, with a punch.
        /// - still on the podium, new place: cuts to the new slot and punches - see the podium
        ///   branch below for why those don't travel.
        /// - not on the board last render: a fresh row, placed directly and faded in.
        /// - crossed BETWEEN podium and list: its old row goes back to that container's pool and a
        ///   new row of the other kind takes its place, since the two are different prefabs. That
        ///   swap can't be a slide either, so it lands with a punch.
        /// </summary>
        public void RenderBoard(IReadOnlyList<LeaderboardEntry> board, bool animate = true)
        {
            if (board == null)
            {
                return;
            }

            ClearAuthoredRows();
            boundPodiumRows.Clear();
            playerListIndex = -1;
            playerAnimationSeconds = 0f;
            hasPlayerEntry = false;

            lastRenderedBoard = board;
            renderedKeys.Clear();
            int listIndex = 0;

            for (int index = 0; index < board.Count; index++)
            {
                LeaderboardEntry entry = board[index];

                // An authored podium row short-circuits everything below: it is a fixed object that
                // already lives where the art wants it, so there is nothing to spawn, match by
                // identity, reparent or animate into place. Its key is deliberately NOT added to
                // renderedKeys either - that list drives the pool, and this row was never in it.
                // A side effect worth knowing: an entry climbing from the list onto the podium
                // leaves its list row unclaimed, so ReleaseRowsNotIn returns it to the pool on its
                // own, which is exactly what should happen.
                LeaderboardRowView authoredRow = ResolvePodiumRow(entry.Rank);
                if (authoredRow != null)
                {
                    BindAuthoredPodiumRow(authoredRow, entry, animate && (!animateRankChangeForPlayerOnly || entry.IsPlayer));

                    if (!showTopRanksInListToo)
                    {
                        continue;
                    }
                }

                // Forced past the spawn-slot path once an authored card has the place: the entry is
                // carrying on into the LIST, and treating it as a podium entry again would put a
                // second copy inside the slot on top of the authored one.
                RectTransform podiumSlot = authoredRow != null ? null : ResolvePodiumSlot(entry.Rank);
                bool wantsPodium = podiumSlot != null;

                LeaderboardRowView prefab = wantsPodium ? podiumRowPrefab : listRowPrefab;
                if (prefab == null)
                {
                    continue;
                }

                string key = ResolveKey(entry, renderedKeys);
                renderedKeys.Add(key);

                bool hasRow = activeRows.TryGetValue(key, out LeaderboardRowView row) && row != null;
                // Same entry, other container - the live row is the wrong prefab for where it now
                // belongs, so retire it and take one of the right kind below.
                bool swappedContainer = hasRow && row.IsPodiumRow != wantsPodium;
                if (swappedContainer)
                {
                    ReleaseRow(row);
                    activeRows.Remove(key);
                    hasRow = false;
                }

                if (!hasRow)
                {
                    row = TakeFromPool(wantsPodium);
                    activeRows[key] = row;
                }

                int previousRank = hasRow ? row.Rank : 0;
                row.Bind(entry, ResolveGiftSprite(entry.Rank));

                bool moved = hasRow && previousRank != entry.Rank;

                if (wantsPodium)
                {
                    row.SetParent(podiumSlot);
                    if (podiumRowFillsSlot)
                    {
                        row.ApplyFillParentLayout();
                    }
                    else
                    {
                        row.ApplyCenteredInParentLayout();
                    }

                    // Podium places never slide, they cut. Each place is its own slot under its
                    // own parent, so a 1st/2nd swap isn't two rows travelling within a shared
                    // space the way the list is - and filling a slot pins the row to it outright,
                    // leaving no offset left to animate. The punch below is what sells the change.
                    row.MoveInstant(Vector2.zero);
                    row.Rect.SetSiblingIndex(0);

                    if (!hasRow && !swappedContainer)
                    {
                        row.PlayAppear();
                    }
                    else if (animate && (moved || swappedContainer))
                    {
                        row.PlayRankChangePunch();
                    }

                    continue;
                }

                row.SetParent(scrollContent);
                row.ApplyListLayout(rowHeight);
                Vector2 targetPosition = new Vector2(0f, -(listTopPadding + listIndex * (rowHeight + rowSpacing)));
                // Keeps the content's hierarchy in visual order, which matters for both Inspector
                // readability and overlap while rows are mid-slide. Through the view rather than
                // the transform, so a climbing row knows where to drop back to afterwards.
                row.SetListSiblingIndex(listIndex);
                // Staggering exists to spread a whole-board wave out. With only one row moving
                // there is no wave to spread, and a delay would just be dead time before the
                // player's row sets off.
                float stagger = animateRankChangeForPlayerOnly ? 0f : listIndex * rowMoveStaggerSeconds;
                bool animateThisRow = animate && (!animateRankChangeForPlayerOnly || entry.IsPlayer);

                // A climb is the rank NUMBER going down. Only a climb gets the lift; a fall keeps
                // the plain slide, since swelling on the way down would celebrate losing a place.
                bool climbing = moved && previousRank > 0 && entry.Rank < previousRank;

                if (entry.IsPlayer)
                {
                    playerEntry = entry;
                    hasPlayerEntry = true;
                    playerListIndex = listIndex;
                    playerAnimationSeconds = animateThisRow
                        ? row.GetMoveTotalSeconds(rowMoveDurationSeconds, moved ? stagger : 0f, climbing)
                        : 0f;
                }

                listIndex++;

                if (!hasRow || !animateThisRow)
                {
                    // A row that wasn't on the board last render has no old position to travel
                    // from, so it lands in place. Dropping off the podium punches on arrival so
                    // the change still registers; a plain first render fades in instead, since
                    // twenty punches at once reads as noise.
                    row.MoveInstant(targetPosition);
                    if (!hasRow)
                    {
                        if (swappedContainer && animateThisRow)
                        {
                            row.PlayRankChangePunch();
                        }
                        else
                        {
                            // Still fades in - an entrance is not a rank change, and a row popping
                            // into existence with no transition at all reads as a glitch.
                            row.PlayAppear();
                        }
                    }

                    continue;
                }

                row.MoveTo(targetPosition, rowMoveDurationSeconds, moved ? stagger : 0f, climbing);
                // No punch on a climb - the lift already carries the emphasis, and stacking a punch
                // on top fights it for the same scale property.
                if (moved && !climbing)
                {
                    row.PlayRankChangePunch(stagger);
                }
            }

            HideUnboundPodiumRows();
            ReleaseRowsNotIn(renderedKeys);
            ResizeContent(listIndex);

            int rankBeforeRender = lastPlayerRank;
            TrackPlayerRank(board);

            // Only when the player's own placing actually moved. Following on every refresh would
            // yank the list back to them even when they are reading somebody else's row.
            if (animate && rankBeforeRender > 0 && lastPlayerRank != rankBeforeRender)
            {
                FollowPlayerThroughAnimation();
            }
        }

        /// <summary>
        /// Destroys whatever was sitting in the row containers before this ever rendered - the
        /// placeholder rows left in the scroll content and the podium slots so the prefab can be
        /// laid out in the Editor. Without this they stay on screen underneath the real board,
        /// which reads as duplicate or ghost entries.
        ///
        /// Runs exactly ONCE, and only before the first row is spawned. That ordering is the whole
        /// safety argument: poolRoot and scrollContent are usually the SAME object (they are in
        /// LeaderBoard.prefab), so pooled rows live in the scroll content between renders - clearing
        /// it on every render would destroy the pool and every live row with it.
        /// </summary>
        private void ClearAuthoredRows()
        {
            if (authoredRowsCleared)
            {
                return;
            }

            authoredRowsCleared = true;

            DisableContentAutoLayout();
            ClearChildren(scrollContent);
            if (topRankSlots == null)
            {
                return;
            }

            for (int index = 0; index < topRankSlots.Length; index++)
            {
                // Skipped wherever a place is authored: an authored podium row is usually parented
                // INTO its slot, so clearing that slot would destroy the very row the Inspector
                // points at - and the board would render a podium of nothing, permanently. Only
                // slots that are going to be spawned into get emptied.
                if (podiumRows != null && index < podiumRows.Length && podiumRows[index] != null)
                {
                    continue;
                }

                ClearChildren(topRankSlots[index]);
            }
        }

        /// <summary>
        /// Switches off any LayoutGroup or ContentSizeFitter sitting on the scroll content.
        ///
        /// This controller positions every row itself, by anchoredPosition, because that is the only
        /// way a row can SLIDE from its old place to its new one. A layout group rewrites all of its
        /// children every layout pass, so the two cannot coexist: the group wins, and because
        /// ApplyListLayout stretches a row horizontally while leaving pivot.x at 0.5, what the group
        /// computes lands the row half its own width to the left. Toggling the component off and on
        /// by hand appears to fix it only because a disabled group stops re-running.
        ///
        /// Disabled rather than removed, so the group can stay in the prefab as an authoring aid for
        /// laying out placeholder rows - it just never runs in play.
        ///
        /// A ContentSizeFitter goes the same way: ResizeContent sets the content height from the row
        /// count, and a fitter would immediately overwrite it.
        /// </summary>
        private void DisableContentAutoLayout()
        {
            if (scrollContent == null)
            {
                return;
            }

            LayoutGroup layoutGroup = scrollContent.GetComponent<LayoutGroup>();
            if (layoutGroup != null && layoutGroup.enabled)
            {
                layoutGroup.enabled = false;
                Debug.Log($"[Leaderboard] Disabled {layoutGroup.GetType().Name} on the scroll content - rows are positioned and animated by code, and a layout group fights that.", scrollContent);
            }

            ContentSizeFitter fitter = scrollContent.GetComponent<ContentSizeFitter>();
            if (fitter != null && fitter.enabled)
            {
                fitter.enabled = false;
            }
        }

        private static void ClearChildren(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            // Backwards: Destroy is deferred to end of frame, but DetachChildren-style index shifts
            // are not worth risking - walking down means each removal cannot skip a sibling.
            for (int index = container.childCount - 1; index >= 0; index--)
            {
                Destroy(container.GetChild(index).gameObject);
            }
        }

        /// <summary>
        /// Fires the slide-in message when the player's own rank has moved since the last render -
        /// whether that happened while they watched (a refresh) or between openings.
        ///
        /// Silent on the very first render: with no previous rank there's no change to report,
        /// only a starting position.
        /// </summary>
        private void TrackPlayerRank(IReadOnlyList<LeaderboardEntry> board)
        {
            int playerRank = 0;
            for (int index = 0; index < board.Count; index++)
            {
                if (board[index].IsPlayer)
                {
                    playerRank = board[index].Rank;
                    break;
                }
            }

            if (playerRank <= 0)
            {
                return;
            }

            int previousRank = lastPlayerRank;
            lastPlayerRank = playerRank;

            if (!suppressRankMessage && previousRank > 0 && playerRank != previousRank)
            {
                ShowRankChangeMessage(previousRank, playerRank);
            }
        }

        /// <summary>
        /// Shows the rank-change banner, honouring the per-direction toggles. Public so a caller
        /// that already knows a rank moved (e.g. a level-win flow reporting it outside the panel)
        /// can play it without a re-render.
        /// </summary>
        public void ShowRankChangeMessage(int previousRank, int newRank)
        {
            if (rankMessage == null || previousRank <= 0 || newRank <= 0 || newRank == previousRank)
            {
                return;
            }

            // Lower rank number = further up the board.
            bool climbed = newRank < previousRank;
            if (climbed ? !showMessageOnRankClimb : !showMessageOnRankFall)
            {
                return;
            }

            rankMessage.Show(newRank, Mathf.Abs(previousRank - newRank), climbed);
        }

        /// <summary>
        /// Keeps the player's row centred for as long as the reshuffle is running, and blocks
        /// scroll input for the same window.
        ///
        /// Tracks the row's LIVE position rather than lerping to where it will end up - the row is
        /// already being animated by its own coroutine, so reading it each frame means the scroll
        /// inherits whatever easing curve the row is using and the two can never disagree.
        /// </summary>
        /// <summary>
        /// Shows the player's row docked to whichever edge they have been scrolled past, and hides
        /// it again the moment their real row is back in view.
        ///
        /// Driven from Update rather than from a scroll callback because the list moves for reasons
        /// other than dragging - inertia, the follow animation, ResizeContent - and a callback would
        /// miss every one of them.
        /// </summary>
        /// <summary>
        /// The object actually moved and shown for the pin. The container when there is one - the
        /// row inside it is stretched to fill, so moving the row instead would fight that fill.
        /// </summary>
        private RectTransform PinTarget
        {
            get
            {
                if (pinnedRowContainer != null)
                {
                    return pinnedRowContainer;
                }

                return pinnedPlayerRow != null ? pinnedPlayerRow.Rect : null;
            }
        }

        /// <summary>
        /// Spawns the pinned row into its container the first time it is needed. No-op when the row
        /// was authored by hand, or when there is nothing to spawn from.
        /// </summary>
        private void EnsurePinnedRow()
        {
            if (pinnedPlayerRow != null || pinnedRowContainer == null || listRowPrefab == null)
            {
                return;
            }

            pinnedPlayerRow = Instantiate(listRowPrefab, pinnedRowContainer);
            // Fills the container, so the container's own size is what decides how the pin reads -
            // one place to tune it rather than two that can disagree.
            pinnedPlayerRow.ApplyFillParentLayout();
            pinnedPlayerRow.MoveInstant(Vector2.zero);
        }

        private void RefreshPlayerPin()
        {
            if (!pinPlayerRowWhenScrolledAway)
            {
                return;
            }

            EnsurePinnedRow();
            if (pinnedPlayerRow == null || PinTarget == null)
            {
                return;
            }

            if (!hasPlayerEntry || playerListIndex < 0 || scrollRect == null || scrollContent == null)
            {
                SetPinnedRowVisible(false);
                return;
            }

            if (!activeRows.TryGetValue(PlayerRowKey, out LeaderboardRowView playerRow) || playerRow == null)
            {
                SetPinnedRowVisible(false);
                return;
            }

            RectTransform viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.transform as RectTransform;

            if (viewport == null)
            {
                SetPinnedRowVisible(false);
                return;
            }

            // Compared as world geometry rather than anchored maths. The anchored version needs the
            // content to be top-anchored with pivot y = 1 and silently picks the wrong edge - or
            // never pins at all - the moment a prefab is authored any other way. Corners are what is
            // actually on screen, whatever the anchors say.
            viewport.GetWorldCorners(viewportCorners);
            playerRow.Rect.GetWorldCorners(rowCorners);

            // GetWorldCorners: 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
            float viewportBottom = viewportCorners[0].y;
            float viewportTop = viewportCorners[1].y;
            float rowBottom = rowCorners[0].y;
            float rowTop = rowCorners[1].y;

            // Entirely past an edge, not merely clipped by it - a row half in view still reads as
            // "you are here" and does not need a stand-in.
            bool scrolledPastAbove = rowBottom >= viewportTop;
            bool scrolledPastBelow = rowTop <= viewportBottom;

            if (!scrolledPastAbove && !scrolledPastBelow)
            {
                SetPinnedRowVisible(false);
                return;
            }

            SetPinnedRowVisible(true);

            // Re-bound only when it has actually gone stale, so this is nearly free per frame.
            if (pinnedPlayerRow.Rank != playerEntry.Rank
                || pinnedPlayerRow.DisplayedScore != playerEntry.Score
                || !string.Equals(pinnedPlayerRow.DisplayedName, playerEntry.Name, System.StringComparison.Ordinal))
            {
                pinnedPlayerRow.Bind(playerEntry, ResolveGiftSprite(playerEntry.Rank));
            }

            RectTransform anchor = scrolledPastAbove ? pinnedTopAnchor : pinnedBottomAnchor;
            if (anchor != null)
            {
                DockPinnedRow(anchor, alignTopEdge: scrolledPastAbove);
            }
        }

        /// <summary>
        /// Lines the pinned row's top or bottom edge up with a marker, in world space.
        ///
        /// Done as a correction after a straight position match rather than by computing an offset
        /// from height and pivot, so it stays right under a scaled canvas and whatever anchoring the
        /// two happen to have - the corners are the rendered geometry, and nothing else has to
        /// agree about anything.
        /// </summary>
        private void DockPinnedRow(RectTransform anchor, bool alignTopEdge)
        {
            RectTransform row = PinTarget;
            if (row == null)
            {
                return;
            }

            row.position = anchor.position;

            row.GetWorldCorners(rowCorners);
            float rowEdge = alignTopEdge ? rowCorners[1].y : rowCorners[0].y;
            float correction = anchor.position.y - rowEdge;
            if (!Mathf.Approximately(correction, 0f))
            {
                row.position += new Vector3(0f, correction, 0f);
            }
        }

        private void SetPinnedRowVisible(bool visible)
        {
            RectTransform target = PinTarget;
            if (target != null && target.gameObject.activeSelf != visible)
            {
                target.gameObject.SetActive(visible);
            }
        }

        private void FollowPlayerThroughAnimation()
        {
            if (!scrollFollowsPlayerDuringAnimation || scrollRect == null || scrollContent == null)
            {
                return;
            }

            // On the podium: nothing in the list to follow, and the podium is always on screen.
            if (playerListIndex < 0)
            {
                return;
            }

            if (!activeRows.TryGetValue(PlayerRowKey, out LeaderboardRowView playerRow) || playerRow == null)
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                return;
            }

            if (followRoutine != null)
            {
                StopCoroutine(followRoutine);
                // Explicitly, here - not left to the stopped coroutine to unwind.
                RestoreScrollInput();
            }

            followRoutine = StartCoroutine(FollowPlayerRoutine(playerRow, playerAnimationSeconds));
        }

        private IEnumerator FollowPlayerRoutine(LeaderboardRowView playerRow, float animationSeconds)
        {
            // The row's own end-to-end length plus a moment to settle. Asked of the row rather than
            // recomputed here, so the climb timings can be retuned without this drifting out of
            // step and handing the scroll back mid-slide.
            float duration = Mathf.Max(0f, animationSeconds) + Mathf.Max(0f, scrollFollowSettleSeconds);

            BlockScrollInput();

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (playerRow == null)
                {
                    break;
                }

                // anchoredPosition.y runs negative down the list, so negating it gives the distance
                // from the content's top edge - the same measure ScrollToPlayer uses.
                CenterScrollOnRowTop(-playerRow.Rect.anchoredPosition.y);
                yield return null;
            }

            RestoreScrollInput();
            followRoutine = null;
        }

        /// <summary>
        /// Takes the ScrollRect away from the player for the duration of a follow.
        ///
        /// Disabled outright, not just velocity-zeroed: a disabled ScrollRect receives no drag
        /// callbacks at all, so a drag cannot fight the follow and leave the two arguing frame by
        /// frame. Setting verticalNormalizedPosition still works while it is off, which is what
        /// lets the follow keep driving it.
        /// </summary>
        private void BlockScrollInput()
        {
            if (scrollRect == null || scrollInputBlocked)
            {
                return;
            }

            scrollInputBlocked = true;
            scrollWasEnabledBeforeFollow = scrollRect.enabled;
            scrollRect.velocity = Vector2.zero;
            scrollRect.enabled = false;
        }

        private void RestoreScrollInput()
        {
            if (scrollRect == null || !scrollInputBlocked)
            {
                return;
            }

            scrollInputBlocked = false;
            scrollRect.enabled = scrollWasEnabledBeforeFollow;
            // Zeroed on the way out too, or the momentum from whatever the follow did to the
            // content carries on once control goes back to the player.
            scrollRect.velocity = Vector2.zero;
        }

        /// <summary>
        /// Puts the given distance-from-top at the middle of the viewport. Shared by the follow and
        /// by ScrollToPlayer so the two cannot drift into framing the same row differently.
        /// </summary>
        private void CenterScrollOnRowTop(float rowTop)
        {
            float viewportHeight = scrollRect.viewport != null ? scrollRect.viewport.rect.height : 0f;
            float scrollableHeight = scrollContent.rect.height - viewportHeight;
            if (scrollableHeight <= 0f)
            {
                return;
            }

            // Centred rather than flush to the top, so the rivals immediately above and below - the
            // ones actually worth chasing - stay in view too.
            float desiredOffset = Mathf.Clamp(rowTop - (viewportHeight - rowHeight) * 0.5f, 0f, scrollableHeight);
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - desiredOffset / scrollableHeight);
        }

        private IEnumerator ScrollToPlayerNextFrame()
        {
            yield return null;
            ScrollToPlayer();
        }

        /// <summary>
        /// Scrolls so the player's own row is visible. No-op while they're on the podium (already
        /// on screen, and not in the list at all) or while the list is short enough to fit whole.
        /// </summary>
        public void ScrollToPlayer()
        {
            if (scrollRect == null || scrollContent == null || lastRenderedBoard == null)
            {
                return;
            }

            int listIndex = -1;
            int scanned = 0;
            for (int index = 0; index < lastRenderedBoard.Count; index++)
            {
                if (!IsInScrollList(lastRenderedBoard[index].Rank))
                {
                    continue;
                }

                if (lastRenderedBoard[index].IsPlayer)
                {
                    listIndex = scanned;
                    break;
                }

                scanned++;
            }

            if (listIndex < 0)
            {
                return;
            }

            CenterScrollOnRowTop(listTopPadding + listIndex * (rowHeight + rowSpacing));
        }

        /// <summary>The hand-authored row for a place, or null when that place is spawned into a slot instead.</summary>
        private LeaderboardRowView ResolvePodiumRow(int rank)
        {
            int rowIndex = rank - 1;
            if (podiumRows == null || rowIndex < 0 || rowIndex >= podiumRows.Length)
            {
                return null;
            }

            return podiumRows[rowIndex];
        }

        /// <summary>
        /// Whether a place gets a row in the SCROLL LIST. Anything that does not has to be skipped
        /// when counting list positions, or every index below it is pushed out.
        ///
        /// An authored podium place is in the list too when showTopRanksInListToo is on - it has a
        /// card AND a row. A place spawned into a topRankSlot never is: that row physically lives
        /// inside the slot.
        /// </summary>
        private bool IsInScrollList(int rank)
        {
            if (ResolvePodiumRow(rank) != null)
            {
                return showTopRanksInListToo;
            }

            return ResolvePodiumSlot(rank) == null;
        }

        /// <summary>
        /// Shows an authored row and binds whoever holds that place now.
        ///
        /// Nothing here touches the transform. The row keeps the exact position, size and parent it
        /// was authored with - that is the entire point of authoring it - so a place changing hands
        /// is sold by the punch alone, the same way the spawned podium already behaved.
        /// </summary>
        private void BindAuthoredPodiumRow(LeaderboardRowView row, LeaderboardEntry entry, bool animate)
        {
            row.MarkAsPodiumRow(true);
            boundPodiumRows.Add(row);

            // Read BEFORE the row is touched. A podium place keeps the same RANK for its whole life
            // - slot 1 is always 1st - so rank cannot say whether anything changed. Who is standing
            // there can.
            bool wasShowingSomeone = row.Rank > 0;
            bool occupantChanged = wasShowingSomeone
                && (!string.Equals(row.DisplayedName, entry.Name, System.StringComparison.Ordinal)
                    || row.DisplayedScore != entry.Score);

            if (!row.gameObject.activeSelf)
            {
                row.gameObject.SetActive(true);
            }

            Sprite gift = ResolveGiftSprite(entry.Rank);

            if (!wasShowingSomeone)
            {
                // Nothing to hand over from - the podium is filling for the first time.
                row.Bind(entry, gift);
                row.PlayAppear();
                return;
            }

            if (!animate || !occupantChanged)
            {
                row.Bind(entry, gift);
                return;
            }

            // Dips out of sight, takes the new name and score at the bottom, and rises back with
            // them. Bind is deliberately deferred into the callback rather than called here - doing
            // it now would swap the card's contents in plain view before it ever moved.
            row.PlayPodiumDataSwap(() => row.Bind(entry, gift));
        }

        /// <summary>
        /// Hides any authored podium place no entry claimed - a board with fewer entries than
        /// places.
        ///
        /// Runs AFTER the bind pass, never before it. Hiding every row up front and letting the
        /// binds switch them back on looks equivalent but is not: Bind reads whether a row was
        /// already showing somebody in order to choose between fading in and punching, and a row
        /// that was just deactivated always reads as "new" - so the whole podium faded in from
        /// transparent on every single refresh.
        /// </summary>
        private void HideUnboundPodiumRows()
        {
            if (podiumRows == null)
            {
                return;
            }

            for (int index = 0; index < podiumRows.Length; index++)
            {
                LeaderboardRowView row = podiumRows[index];
                if (row == null || boundPodiumRows.Contains(row) || !row.gameObject.activeSelf)
                {
                    continue;
                }

                // Rank back to 0 so the next entry to take this place fades in rather than
                // punching - it genuinely is arriving, not moving.
                row.ResetForPool();
                row.gameObject.SetActive(false);
            }
        }

        private RectTransform ResolvePodiumSlot(int rank)
        {
            int slotIndex = rank - 1;
            if (topRankSlots == null || slotIndex < 0 || slotIndex >= topRankSlots.Length)
            {
                return null;
            }

            return topRankSlots[slotIndex];
        }

        private void ResizeContent(int listRowCount)
        {
            if (scrollContent == null)
            {
                return;
            }

            float height = listRowCount <= 0
                ? 0f
                : listTopPadding + listRowCount * rowHeight + (listRowCount - 1) * rowSpacing + listBottomPadding;
            scrollContent.sizeDelta = new Vector2(scrollContent.sizeDelta.x, height);
        }

        /// <summary>
        /// Identity for matching a row across renders. The player is keyed by role rather than by
        /// name, since renaming themselves mid-session must not read as "the player left and a
        /// stranger arrived". Rivals key by name, with an occurrence suffix: a rival pool smaller
        /// than RivalCount hands the same name to several rivals (see
        /// LeaderboardManager.BuildNameIndices), and duplicate keys would collapse them into one
        /// shared row.
        /// </summary>
        private static string ResolveKey(LeaderboardEntry entry, List<string> alreadyRendered)
        {
            if (entry.IsPlayer)
            {
                return PlayerRowKey;
            }

            string key = entry.Name ?? string.Empty;
            int occurrence = 0;
            for (int index = 0; index < alreadyRendered.Count; index++)
            {
                if (alreadyRendered[index] == key || alreadyRendered[index].StartsWith(key + "\0", System.StringComparison.Ordinal))
                {
                    occurrence++;
                }
            }

            return occurrence == 0 ? key : key + "\0" + occurrence;
        }

        private LeaderboardRowView TakeFromPool(bool podium)
        {
            List<LeaderboardRowView> pool = podium ? podiumPool : listPool;
            while (pool.Count > 0)
            {
                LeaderboardRowView pooled = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                if (pooled == null)
                {
                    continue;
                }

                pooled.gameObject.SetActive(true);
                return pooled;
            }

            LeaderboardRowView prefab = podium ? podiumRowPrefab : listRowPrefab;
            RectTransform parent = podium ? poolRoot : scrollContent;
            LeaderboardRowView created = Instantiate(prefab, parent != null ? parent : transform as RectTransform);
            // Stamped at spawn and never changed - it's which prefab this object IS, and decides
            // which pool it returns to.
            created.MarkAsPodiumRow(podium);
            return created;
        }

        private void ReleaseRowsNotIn(List<string> keptKeys)
        {
            removedKeys.Clear();
            foreach (KeyValuePair<string, LeaderboardRowView> pair in activeRows)
            {
                if (!keptKeys.Contains(pair.Key))
                {
                    removedKeys.Add(pair.Key);
                }
            }

            for (int index = 0; index < removedKeys.Count; index++)
            {
                ReleaseRow(activeRows[removedKeys[index]]);
                activeRows.Remove(removedKeys[index]);
            }
        }

        private void ReleaseRow(LeaderboardRowView row)
        {
            if (row == null)
            {
                return;
            }

            row.ResetForPool();
            row.gameObject.SetActive(false);
            if (poolRoot != null)
            {
                row.SetParent(poolRoot);
            }

            (row.IsPodiumRow ? podiumPool : listPool).Add(row);
        }
    }
}
