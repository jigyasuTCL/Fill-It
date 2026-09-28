using Shared.GameFlow;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Profile
{
    /// <summary>
    /// The profile picker: your avatar and name at the top, an Avatar/Frame tab pair below, and a
    /// Confirm button that commits.
    ///
    /// Everything is STAGED, not applied as you go - the name included. A Confirm button only
    /// means something if there is something to confirm, so a tap moves the tick and updates the
    /// preview, and typing a name only fills the field, while nothing at all is written; Confirm
    /// saves the lot, and closing any other way discards it. Purchases are the one exception - real
    /// money is spent immediately and the item is granted permanently, because a grant that could
    /// be undone by closing a panel would be indefensible. Only the CHOICE to wear it stages.
    ///
    /// Owns no rules. Every "can they have this / what does it cost" question goes to
    /// ProfileManager; this reads the answers back and draws them. That is what keeps a mis-wired
    /// button from handing out a paid frame - the gate is in TryEquip, not here.
    /// </summary>
    public sealed class ProfilePanelController : MonoBehaviour
    {
        /// <summary>
        /// One tab's worth of Inspector wiring. A serializable class rather than parallel arrays so
        /// the two tabs cannot drift out of alignment in the Inspector.
        /// </summary>
        [Serializable]
        private sealed class TabBinding
        {
            public Button Button;

            // The tab's own background, swapped between the two sprites below - the raised brown
            // tab versus the flat pale one. This is the usual way to skin a tab; the two state
            // roots further down are the alternative for art that needs whole objects toggled.
            public Image TabImage;
            public Sprite SelectedSprite;
            public Sprite UnselectedSprite;

            // The other way to skin the same image: one sprite, two tints. Opt-in rather than
            // always-on because a Color that was never authored serialises as transparent BLACK,
            // not white - applied unconditionally, a freshly added pair would make both tabs
            // vanish. Composes with the sprite swap above; use either or both.
            public bool UseColorSwap;
            public Color SelectedColor = Color.white;
            public Color UnselectedColor = Color.white;

            // The tab's label. Its colour swaps with the state too - a dark-on-pale label stays
            // dark and unreadable once the tab turns brown underneath it.
            public TMP_Text Label;
            public Color SelectedLabelColor = Color.white;
            public Color UnselectedLabelColor = Color.white;

            // The tab's own object under TabSelector. Brought to the FRONT of its siblings while
            // selected: the two tabs overlap like folder tabs, and in Unity UI the later sibling
            // draws on top - so without this the tab authored second always laps the other one,
            // whichever is actually selected. Falls back to the Button's transform when left
            // unassigned.
            public RectTransform TabRoot;

            // Whole-object alternative to the sprite swap above, for art that is two different
            // objects rather than one image with two sprites. Leave both unassigned when using
            // TabImage. Must never be the same object as each other or as ContentRoot.
            public GameObject SelectedRoot;
            public GameObject UnselectedRoot;
            // This tab's scroll view. Toggled whole, so each tab keeps its own scroll position
            // while the player switches back and forth.
            public GameObject ContentRoot;
            // Parent the category sections are spawned under - the scroll view's Content. Give it
            // a VerticalLayoutGroup and a ContentSizeFitter in the prefab.
            public RectTransform SectionsRoot;
        }

        [Header("Panel")]
        // The object actually toggled on/off - keep this SEPARATE from the object this component
        // sits on, or Close() would deactivate this component along with it and no later Open()
        // could ever run.
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private PopupScaleAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button confirmButton;
        // Assign in the Inspector - ProfileManager is not a singleton.
        [SerializeField] private ProfileManager profile;

        [Header("Header - avatar and name")]
        // The live "this is you" preview. Shows the PENDING choice, so the player sees what they
        // are about to confirm.
        [SerializeField] private Image previewAvatarImage;
        [SerializeField] private Image previewFrameImage;
        // Always live - the field IS the name display, sized to cover the whole name area, so
        // tapping anywhere on the name starts editing. There is no separate label to keep in sync.
        [SerializeField] private TMP_InputField nameInput;
        // The pencil, if the art has one. Optional and purely a shortcut: it focuses the field,
        // which is exactly what tapping the field already does. Leave it unassigned (or delete the
        // button) and nothing is lost.
        [SerializeField] private Button nameEditButton;

        [Header("Message Line")]
        // One shared line for everything the panel needs to say back: why a rename was refused, or
        // why a locked item cannot be had yet. Both are short, transient, and never wanted at the
        // same time. Optional - leave it unassigned and the panel stays silent.
        [SerializeField] private TMP_Text messageLabel;

        [Header("Tabs")]
        [SerializeField] private TabBinding avatarTab;
        [SerializeField] private TabBinding frameTab;

        [Header("Cells")]
        [SerializeField] private ProfileCategorySectionView sectionPrefab;
        [SerializeField] private ProfileItemButtonView itemPrefab;
        // A frame is a border around empty space, so a frame cell needs something to frame. These
        // two decide what:
        //
        // On - every frame cell shows the player's own currently-picked avatar inside it, so the
        // grid previews the real pairing and changing avatar updates all of them at once.
        // Off - framePreviewPlaceholder is used instead, and frame cells render as empty borders
        // when that is unassigned too.
        [SerializeField] private bool showPlayerAvatarInFrameCells = true;

        // The neutral stand-in shown inside frame cells when showPlayerAvatarInFrameCells is off -
        // the paw silhouette in the mock. Never point this at a FRAME sprite: it draws inside
        // every frame, so a frame here puts a second border in each cell.
        [SerializeField] private Sprite framePreviewPlaceholder;

        [Header("Locked Item Copy")]
        // {0} is the level number. Kept as format strings so the wording stays with the UI.
        [SerializeField] private string levelLockFormat = "Level {0}";
        [SerializeField] private string levelLockMessageFormat = "Reach level {0} to unlock this.";
        [SerializeField] private string rewardLockFallback = "Reward";
        [SerializeField] private string rewardLockMessageFormat = "Earn this from {0}.";

        // Spawned cells per slot, in the same order the catalog's enabled items come out in, so a
        // refresh can rebind them by walking both together.
        private readonly List<ProfileItemButtonView> avatarCells = new List<ProfileItemButtonView>();
        private readonly List<ProfileItemButtonView> frameCells = new List<ProfileItemButtonView>();

        // The staged choice - what the tick and the preview show, and what Confirm writes. Seeded
        // from what is equipped every time the panel opens, so discarding is just "don't save".
        private string pendingAvatarId;
        private string pendingFrameId;

        // Guards against a second tap while the store is still working on the first. InAppManager
        // puts its own loading overlay up, but the grid underneath stays live.
        private bool purchaseInFlight;

        private bool tabsBuilt;

        // Set the instant Open is asked for, and read by Awake - see StartClosed for why the
        // ordering between those two matters.
        private bool openRequested;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(Confirm);
                confirmButton.onClick.AddListener(Confirm);
            }

            if (nameEditButton != null)
            {
                nameEditButton.onClick.RemoveListener(FocusNameInput);
                nameEditButton.onClick.AddListener(FocusNameInput);
            }

            if (nameInput != null)
            {
                // onEndEdit rather than onSubmit: it covers the keyboard's done/return AND simply
                // tapping away from the field, so a player who types a name and then goes straight
                // to picking a frame still has it saved. On mobile the Confirm button is usually
                // behind the soft keyboard at the moment they finish typing, so waiting for
                // Confirm alone would lose it.
                nameInput.onEndEdit.RemoveListener(HandleNameEditEnded);
                nameInput.onEndEdit.AddListener(HandleNameEditEnded);
            }

            BindTab(avatarTab, ProfileSlot.Avatar);
            BindTab(frameTab, ProfileSlot.Frame);

            StartClosed();
        }

        private void OnEnable()
        {
            // Awake alone is not enough. This lives under the main menu root, which is switched off
            // for gameplay and back on afterwards - and Awake runs exactly once, at scene load. Any
            // way the panel ends up left active during that round trip, it would come back on
            // screen looking like it opened itself. Re-asserting here closes that door.
            StartClosed();
        }

        private void OnDisable()
        {
            // The close animation is a coroutine, and deactivating the menu kills it before its
            // callback runs - which is exactly what leaves panelRoot switched on. Force it down so
            // an interrupted close cannot survive into the next visit.
            if (!openRequested && panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Makes sure the panel is down at startup, whatever state it was saved in.
        ///
        /// The panel gets left switched on in the scene all the time while its art is being
        /// authored - that is the only way to see it in the Editor - and without this, pressing
        /// Play shows the profile over the menu every single launch. Nothing else opens it: the
        /// prefab ships inactive and only ProfileMainMenuButton ever calls Open.
        ///
        /// The openRequested guard is what stops this from eating a legitimate open. When panelRoot
        /// is this same object, Open's SetActive(true) is what first runs this Awake - synchronously,
        /// before the rest of Open - so without the flag the panel would switch itself straight back
        /// off and Open would silently do nothing.
        /// </summary>
        private void StartClosed()
        {
            if (!openRequested && panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }
        }

        private void BindTab(TabBinding tab, ProfileSlot slot)
        {
            if (tab?.Button == null)
            {
                return;
            }

            tab.Button.onClick.RemoveAllListeners();
            tab.Button.onClick.AddListener(() => ShowTab(slot));
        }

        // =========================================================
        // OPEN / CLOSE / CONFIRM
        // =========================================================

        public void Open()
        {
            if (panelRoot == null || profile == null)
            {
                return;
            }

            // Before SetActive, never after: activating panelRoot can run this component's own
            // Awake, and StartClosed there has to be able to tell a real open from a leftover
            // authoring state.
            openRequested = true;
            panelRoot.SetActive(true);

            // Staged state is re-seeded from what is actually equipped on every open, so a previous
            // visit that was closed without confirming leaves nothing behind.
            pendingAvatarId = profile.GetEquipped(ProfileSlot.Avatar)?.Id;
            pendingFrameId = profile.GetEquipped(ProfileSlot.Frame)?.Id;

            EnsureTabs();

            if (nameInput != null)
            {
                nameInput.SetTextWithoutNotify(profile.PlayerName);
                nameInput.characterLimit = profile.MaxNameLength;
            }

            SetMessage(string.Empty);
            ShowTab(ProfileSlot.Avatar);
            Refresh();

            popupAnimator?.PlayOpen();
        }

        /// <summary>
        /// Writes the staged choices and the name, then closes. A rejected name (too short) blocks
        /// the close and leaves the panel up with the reason showing, rather than discarding what
        /// the player typed.
        /// </summary>
        public void Confirm()
        {
            if (profile == null)
            {
                return;
            }

            // The name is written HERE and nowhere else, same as the avatar and frame - leaving
            // the field only validated it. Runs first so a refused name blocks the close with the
            // reason showing, instead of saving the two slots and dropping the name silently.
            if (!CommitName())
            {
                return;
            }

            CommitSlot(ProfileSlot.Avatar, pendingAvatarId);
            CommitSlot(ProfileSlot.Frame, pendingFrameId);
            ClosePanel();
        }

        /// <summary>
        /// Discards the staged choices. Granted items stay granted - a purchase is not part of what
        /// is being staged - so nothing the player paid for is lost by closing.
        /// </summary>
        public void Close()
        {
            ClosePanel();
        }

        private void CommitSlot(ProfileSlot slot, string pendingId)
        {
            ProfileItemDefinition item = profile.Catalog != null ? profile.Catalog.FindById(slot, pendingId) : null;
            if (item != null)
            {
                // TryEquip is a no-op when this is already what is worn, and refuses anything
                // locked - so confirming an unchanged panel writes nothing at all.
                profile.TryEquip(slot, item);
            }
        }

        private void ClosePanel()
        {
            // Cleared here and nowhere else. The flag exists only to protect the brief window
            // inside Open; left latched it would tell StartClosed that every later re-enable was a
            // deliberate open, which is precisely how the panel came back by itself.
            openRequested = false;

            if (panelRoot == null)
            {
                return;
            }

            // Drop focus before the panel goes, so the soft keyboard doesn't linger over whatever
            // is behind it.
            if (nameInput != null && nameInput.isFocused)
            {
                nameInput.DeactivateInputField();
            }

            // isActiveAndEnabled, because PlayClose cannot run its coroutine on a disabled
            // object - the panel would simply stay open forever.
            if (popupAnimator != null && isActiveAndEnabled)
            {
                popupAnimator.PlayClose(() => panelRoot.SetActive(false));
                return;
            }

            panelRoot.SetActive(false);
        }

        // =========================================================
        // TABS
        // =========================================================

        public void ShowTab(ProfileSlot slot)
        {
            ApplyTabState(avatarTab, slot == ProfileSlot.Avatar);
            ApplyTabState(frameTab, slot == ProfileSlot.Frame);
            // A message about an item in the tab being left behind would be stale the moment the
            // player looks somewhere else.
            SetMessage(string.Empty);
        }

        private static void ApplyTabState(TabBinding tab, bool isSelected)
        {
            if (tab == null)
            {
                return;
            }

            if (tab.ContentRoot != null)
            {
                tab.ContentRoot.SetActive(isSelected);
            }

            // Sprite swap is the usual skin: one image, two sprites. Each is applied only when
            // BOTH are authored - a half-filled pair would otherwise leave the tab stuck on
            // whichever one exists, which looks like the tab never responds.
            if (tab.TabImage != null && tab.SelectedSprite != null && tab.UnselectedSprite != null)
            {
                tab.TabImage.sprite = isSelected ? tab.SelectedSprite : tab.UnselectedSprite;
            }

            if (tab.TabImage != null && tab.UseColorSwap)
            {
                tab.TabImage.color = isSelected ? tab.SelectedColor : tab.UnselectedColor;
            }

            if (tab.Label != null)
            {
                tab.Label.color = isSelected ? tab.SelectedLabelColor : tab.UnselectedLabelColor;
            }

            // Only the selected tab is raised; the other is left where it is and so ends up behind
            // it. Raising both would just restore the authored order and settle nothing.
            if (isSelected)
            {
                // Explicit null checks rather than ?. - these are UnityEngine.Objects, where ?.
                // bypasses Unity's destroyed-object null and would call into a dead transform.
                if (tab.TabRoot != null)
                {
                    tab.TabRoot.SetAsLastSibling();
                }
                else if (tab.Button != null)
                {
                    tab.Button.transform.SetAsLastSibling();
                }
            }

            // SelectedRoot/UnselectedRoot are the tab's OWN two-state art and must be three
            // distinct objects from ContentRoot. Pointing any two of them at the same object is
            // an easy Inspector slip with no error to show for it - the later SetActive simply
            // wins, and an UnselectedRoot aliased onto the content switches the selected tab off
            // the instant it is switched on, hiding the whole tab. Skip rather than obey, so a
            // mis-drag costs the tab's highlight art instead of its entire contents.
            bool rootsDistinct = tab.SelectedRoot != tab.UnselectedRoot;

            if (tab.SelectedRoot != null && tab.SelectedRoot != tab.ContentRoot && rootsDistinct)
            {
                tab.SelectedRoot.SetActive(isSelected);
            }

            if (tab.UnselectedRoot != null && tab.UnselectedRoot != tab.ContentRoot && rootsDistinct)
            {
                tab.UnselectedRoot.SetActive(!isSelected);
            }

            // The tab you are already on should not look pressable.
            if (tab.Button != null)
            {
                tab.Button.interactable = !isSelected;
            }
        }

        // =========================================================
        // NAME
        // =========================================================

        /// <summary>
        /// The pencil's only job - put the caret in the field and raise the keyboard, which is the
        /// same thing tapping the field itself does. Kept purely because the art has a pencil.
        /// </summary>
        private void FocusNameInput()
        {
            if (nameInput == null)
            {
                return;
            }

            nameInput.Select();
            nameInput.ActivateInputField();
        }

        /// <summary>
        /// Leaving the field only CHECKS the name - it never saves one. Confirm is the single
        /// moment anything is written, the same as the avatar and frame.
        ///
        /// Validating here anyway is what stops a player typing one character, picking a frame,
        /// pressing Confirm and only then being told the name was too short - by which point they
        /// have lost track of which field the complaint is about.
        /// </summary>
        private void HandleNameEditEnded(string _)
        {
            if (profile == null || nameInput == null)
            {
                return;
            }

            SetMessage(profile.ValidateName(nameInput.text, out _, out string rejection) ? string.Empty : rejection);
        }

        /// <summary>
        /// Saves the typed name. Returns false when it was refused, leaving the field open with the
        /// reason showing so the player can fix it.
        ///
        /// Called only from Confirm. The name stages exactly like the avatar and frame do - typing
        /// one and closing the panel any other way discards it, and nothing reaches
        /// PlayerData.Username until the player actually commits.
        /// </summary>
        private bool CommitName()
        {
            if (profile == null || nameInput == null)
            {
                return true;
            }

            if (!profile.TrySetPlayerName(nameInput.text, out string rejection))
            {
                SetMessage(rejection);
                return false;
            }

            SetMessage(string.Empty);
            // Read back rather than leaving what was typed: TrySetPlayerName trims, so the field
            // should show what was actually saved. SetTextWithoutNotify, or this would re-enter
            // through onEndEdit.
            nameInput.SetTextWithoutNotify(profile.PlayerName);
            return true;
        }

        private void SetMessage(string message)
        {
            if (messageLabel == null)
            {
                return;
            }

            messageLabel.text = message ?? string.Empty;
            messageLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        // =========================================================
        // BUILD / REFRESH
        // =========================================================

        private void EnsureTabs()
        {
            if (tabsBuilt || profile == null || profile.Catalog == null || itemPrefab == null || sectionPrefab == null)
            {
                return;
            }

            tabsBuilt = true;
            BuildTab(ProfileSlot.Avatar, avatarTab, avatarCells);
            BuildTab(ProfileSlot.Frame, frameTab, frameCells);
        }

        /// <summary>
        /// Spawns one section per enabled category and one cell per enabled item within it. Built
        /// once: the catalog cannot change at runtime, and only the STATE of a cell does - which
        /// Refresh rebinds without touching the hierarchy.
        /// </summary>
        private void BuildTab(ProfileSlot slot, TabBinding tab, List<ProfileItemButtonView> cells)
        {
            if (tab?.SectionsRoot == null)
            {
                return;
            }

            IReadOnlyList<ProfileCategory> categories = profile.Catalog.GetCategories(slot);
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ProfileCategory category = categories[categoryIndex];
                if (category == null || !category.Enabled || category.Items == null)
                {
                    continue;
                }

                ProfileCategorySectionView section = null;
                for (int itemIndex = 0; itemIndex < category.Items.Count; itemIndex++)
                {
                    ProfileItemDefinition item = category.Items[itemIndex];
                    // Skip both unfinished authoring rows and items deliberately held back.
                    if (item == null || !item.IsValid || !item.Enabled)
                    {
                        continue;
                    }

                    // Spawned lazily so a category whose items are all disabled leaves no empty
                    // header behind.
                    if (section == null)
                    {
                        section = Instantiate(sectionPrefab, tab.SectionsRoot);
                        section.SetTitle(category.Title);
                    }

                    cells.Add(Instantiate(itemPrefab, section.ItemsRoot));
                }
            }
        }

        /// <summary>
        /// Redraws the preview and both tabs from pending state. Public so whichever system grants
        /// a profile item (a reward flow, a restore) can refresh an open panel.
        /// </summary>
        public void Refresh()
        {
            if (profile == null)
            {
                return;
            }

            Sprite pendingAvatarArt = ResolveArt(ProfileSlot.Avatar, pendingAvatarId);
            Sprite pendingFrameArt = ResolveArt(ProfileSlot.Frame, pendingFrameId);

            ApplySprite(previewAvatarImage, pendingAvatarArt);
            ApplySprite(previewFrameImage, pendingFrameArt);

            // The name field is deliberately NOT touched here. Refresh runs on every cell tap, and
            // rewriting the field mid-edit would fight the player's typing and drop their caret.
            // Open seeds it; CommitName reads the trimmed value back.

            // Avatar cells pass nothing: an avatar IS the art, there is no inside to fill.
            BindCells(ProfileSlot.Avatar, avatarCells, null);

            // Frame cells get whatever goes inside the border - the player's own avatar, or the
            // neutral stand-in. Read every refresh rather than cached, so picking a new avatar
            // re-previews it across the whole frame grid immediately.
            Sprite frameInner = showPlayerAvatarInFrameCells ? pendingAvatarArt : framePreviewPlaceholder;
            BindCells(ProfileSlot.Frame, frameCells, frameInner);
        }

        private void BindCells(ProfileSlot slot, List<ProfileItemButtonView> cells, Sprite innerPreview)
        {
            if (profile.Catalog == null)
            {
                return;
            }

            string pendingId = slot == ProfileSlot.Frame ? pendingFrameId : pendingAvatarId;
            IReadOnlyList<ProfileCategory> categories = profile.Catalog.GetCategories(slot);
            int cellIndex = 0;

            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ProfileCategory category = categories[categoryIndex];
                if (category == null || !category.Enabled || category.Items == null)
                {
                    continue;
                }

                for (int itemIndex = 0; itemIndex < category.Items.Count && cellIndex < cells.Count; itemIndex++)
                {
                    ProfileItemDefinition item = category.Items[itemIndex];
                    if (item == null || !item.IsValid || !item.Enabled)
                    {
                        continue;
                    }

                    bool unlocked = profile.IsUnlocked(slot, item);
                    cells[cellIndex].Bind(
                        slot,
                        item,
                        unlocked,
                        string.Equals(item.Id, pendingId, StringComparison.Ordinal),
                        unlocked ? string.Empty : ResolveLockText(item),
                        innerPreview,
                        HandleItemClicked);
                    cellIndex++;
                }
            }
        }

        private Sprite ResolveArt(ProfileSlot slot, string id)
        {
            if (profile.Catalog == null)
            {
                return null;
            }

            ProfileItemDefinition item = profile.Catalog.FindById(slot, id) ?? profile.Catalog.ResolveDefault(slot);
            return item?.Art;
        }

        private static void ApplySprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        // =========================================================
        // CLICKS
        // =========================================================

        /// <summary>
        /// A tap means different things depending on the item's state: choose it, buy it, or
        /// explain why it cannot be had yet. Deciding that here - rather than in the cell - is what
        /// lets the cell stay purely presentational.
        /// </summary>
        private void HandleItemClicked(ProfileSlot slot, ProfileItemDefinition item)
        {
            if (profile == null || item == null || !item.IsValid || purchaseInFlight)
            {
                return;
            }

            if (profile.IsUnlocked(slot, item))
            {
                SetPending(slot, item.Id);
                SetMessage(string.Empty);
                Refresh();
                return;
            }

            if (item.Unlock == ProfileUnlockKind.Purchase)
            {
                purchaseInFlight = true;
                profile.Purchase(slot, item, success =>
                {
                    purchaseInFlight = false;
                    if (success)
                    {
                        // Select it straight away - a player who just paid for a frame did not do
                        // it to then have to tap it a second time. Still only STAGED, so Confirm
                        // remains the thing that dresses them; the item itself is theirs either
                        // way.
                        SetPending(slot, item.Id);
                    }

                    Refresh();
                });

                return;
            }

            // Level and Reward items cannot be bought or bypassed; all that is left is to say why.
            SetMessage(ResolveLockMessage(item));
        }

        private void SetPending(ProfileSlot slot, string id)
        {
            if (slot == ProfileSlot.Frame)
            {
                pendingFrameId = id;
                return;
            }

            pendingAvatarId = id;
        }

        /// <summary>
        /// The short line on the padlock itself - a level number, a price, or the reward's name.
        /// The longer explanation shown when a locked item is tapped lives in ResolveLockMessage.
        /// </summary>
        private string ResolveLockText(ProfileItemDefinition item)
        {
            switch (item.Unlock)
            {
                case ProfileUnlockKind.Level:
                    return string.Format(levelLockFormat, item.UnlockLevel);

                case ProfileUnlockKind.Purchase:
                    return profile.ResolvePriceLabel(item);

                case ProfileUnlockKind.Reward:
                    return string.IsNullOrWhiteSpace(item.RewardHint) ? rewardLockFallback : item.RewardHint;

                default:
                    return string.Empty;
            }
        }

        private string ResolveLockMessage(ProfileItemDefinition item)
        {
            switch (item.Unlock)
            {
                case ProfileUnlockKind.Level:
                    return string.Format(levelLockMessageFormat, item.UnlockLevel);

                case ProfileUnlockKind.Reward:
                    return string.Format(
                        rewardLockMessageFormat,
                        string.IsNullOrWhiteSpace(item.RewardHint) ? rewardLockFallback : item.RewardHint);

                default:
                    return string.Empty;
            }
        }
    }
}
