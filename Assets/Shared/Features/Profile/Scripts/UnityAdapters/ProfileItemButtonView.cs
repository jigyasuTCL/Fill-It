using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Profile
{
    /// <summary>
    /// One cell in the avatar or frame grid. Purely presentational plus a click - it never equips,
    /// grants or buys anything itself; ProfilePanelController owns every decision and hands this a
    /// finished state to draw.
    ///
    /// Shows exactly ONE of selectedRoot / lockRoot at a time (an unlocked, unselected item shows
    /// neither), the same "one state root visible" convention BoosterButtonUI uses for its
    /// lock/count/cost roots.
    /// </summary>
    public sealed class ProfileItemButtonView : MonoBehaviour
    {
        [SerializeField] private Button button;
        // The item's own art: the avatar portrait, or the frame's border.
        [SerializeField] private Image artImage;
        // What sits INSIDE the art. A frame cell is a border around nothing, so it needs something
        // to frame - the panel passes either an authored placeholder (the paw) or the player's
        // current avatar. Left unassigned, or handed no sprite, it simply hides.
        [SerializeField] private Image innerPreviewImage;
        [SerializeField] private TMP_Text nameLabel;

        [Header("State Roots - at most one is ever visible")]
        // The green tick on the item the player has chosen.
        [SerializeField] private GameObject selectedRoot;
        // Shown on anything still locked, whatever the reason; lockLabel says which reason.
        [SerializeField] private GameObject lockRoot;
        [SerializeField] private TMP_Text lockLabel;

        [Header("Locked Look")]
        // Dims the art behind the padlock. Left unassigned, a locked item renders at full colour
        // and only the lock root marks it.
        [SerializeField] private CanvasGroup artCanvasGroup;
        [SerializeField] private float lockedAlpha = 0.45f;

        private ProfileItemDefinition item;
        private ProfileSlot slot;
        private Action<ProfileSlot, ProfileItemDefinition> clicked;

        public ProfileItemDefinition Item => item;

        private void Awake()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }

            EnsureInnerPreviewBehindArt();
        }

        /// <summary>
        /// Forces the inner preview to draw BEHIND the item art.
        ///
        /// A frame is a border around empty space, so what sits inside it has to be underneath it;
        /// in Unity UI that means an earlier sibling. Getting this backwards puts the paw (or the
        /// player's avatar) on top of the border it is supposed to sit inside, which in a frame
        /// grid reads as every cell having a blank blob stamped over it - and nothing in the
        /// Inspector hints that sibling order is load-bearing.
        ///
        /// Enforced here rather than left to authoring, since it is a property of what this
        /// component DOES, not a decision the prefab gets to make. Only reorders when the two
        /// share a parent; a design that nests them separately is left alone.
        /// </summary>
        private void EnsureInnerPreviewBehindArt()
        {
            if (innerPreviewImage == null || artImage == null)
            {
                return;
            }

            RectTransform inner = innerPreviewImage.rectTransform;
            RectTransform art = artImage.rectTransform;
            if (inner.parent != art.parent || inner.GetSiblingIndex() < art.GetSiblingIndex())
            {
                return;
            }

            inner.SetSiblingIndex(art.GetSiblingIndex());
        }

        /// <summary>
        /// Draws this cell. Called on every panel refresh, not only on spawn - choosing one item
        /// clears another's tick, and a purchase turns a third from locked to owned, so the whole
        /// tab redraws rather than trying to track which cells moved.
        /// </summary>
        /// <param name="isSelected">
        /// Reflects the panel's PENDING choice, not what is saved - the tick has to follow the tap
        /// immediately even though nothing is committed until Confirm.
        /// </param>
        /// <param name="lockText">
        /// What the padlock says: a level number, a price, or a reward hint. Empty for an unlocked
        /// item - the panel resolves this, since only it knows about prices and progression.
        /// </param>
        public void Bind(
            ProfileSlot itemSlot,
            ProfileItemDefinition definition,
            bool isUnlocked,
            bool isSelected,
            string lockText,
            Sprite innerPreview,
            Action<ProfileSlot, ProfileItemDefinition> onClicked)
        {
            slot = itemSlot;
            item = definition;
            clicked = onClicked;

            ApplySprite(artImage, definition?.Art);
            ApplySprite(innerPreviewImage, innerPreview);

            if (nameLabel != null)
            {
                nameLabel.text = definition != null ? definition.ResolveDisplayName() : string.Empty;
            }

            if (selectedRoot != null)
            {
                selectedRoot.SetActive(isUnlocked && isSelected);
            }

            if (lockRoot != null)
            {
                lockRoot.SetActive(!isUnlocked);
            }

            if (lockLabel != null)
            {
                lockLabel.text = lockText ?? string.Empty;
            }

            if (artCanvasGroup != null)
            {
                artCanvasGroup.alpha = isUnlocked ? 1f : Mathf.Clamp01(lockedAlpha);
            }

            // Still interactable while locked: tapping a locked item is how a purchase starts, and
            // how a level/reward item explains itself. Only a blank authoring row is truly dead.
            if (button != null)
            {
                button.interactable = definition != null && definition.IsValid;
            }
        }

        private static void ApplySprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            // Disabled, not transparent: a null sprite on an enabled Image draws a white box the
            // size of the rect, which reads as a bug rather than as missing content.
            image.enabled = sprite != null;
        }

        private void HandleClick()
        {
            clicked?.Invoke(slot, item);
        }
    }
}
