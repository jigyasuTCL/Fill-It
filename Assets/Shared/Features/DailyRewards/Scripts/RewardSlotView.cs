using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum SubRewardSlotState { Locked, Claimable, Claimed }

// Only job: display one sub-reward slot for whatever state it's told to
// show. Knows nothing about the manager, the config source, or timing.
public class RewardSlotView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private GameObject claimedOverlay;
    [SerializeField] private GameObject adBadge;  // optional � shown when this slot needs a rewarded ad
    [SerializeField] private GameObject iapBadge; // optional � shown when this slot needs a purchase
    [SerializeField] private Button claimButton;

    // Source point for the Collect Screen's jump-in animation when this slot's reward is claimed.
    public RectTransform IconTransform => iconImage != null ? iconImage.rectTransform : null;

    // Lets the panel lock every slot's claim button while any reward's collect screen is
    // playing, independent of this slot's own Locked/Claimable/Claimed state.
    public void SetInteractable(bool interactable)
    {
        if (claimButton != null)
            claimButton.interactable = interactable;
    }

    [Header("Icon lookup")]
    [Tooltip("Maps SubRewardItem.iconKey -> Sprite. Assign the shared IconDatabase asset here (same one on every slot prefab).")]
    [SerializeField] private IconDatabaseConfig iconDatabase;

    [SerializeField]
    private GameObject AvailableGameobjectCat;
    [SerializeField]
    private GameObject LockedGameobjectCat;
    [SerializeField]
    private GameObject CollectedGameobjectCat;

    [Header("Unlock Transition - played only on Locked -> Claimable (see Setup)")]
    [Tooltip("Optional. If set, lockedOverlay fades out through this instead of snapping off.")]
    [SerializeField] private CanvasGroup lockedOverlayGroup;
    [SerializeField] private float lockedFadeOutDuration = 0.2f;
    [Tooltip("Optional. If set, claimButton fades in through this and is only interactable once fully visible.")]
    [SerializeField] private CanvasGroup claimButtonGroup;
    [SerializeField] private float claimFadeInDuration = 0.2f;
    [SerializeField] private Vector3 availableCatPunchStrength = new Vector3(0.25f, 0.25f, 0f);
    [SerializeField] private float availableCatPunchDuration = 0.4f;
    [SerializeField] private int availableCatPunchVibrato = 8;
    [SerializeField] private float availableCatPunchElasticity = 0.6f;

    // null until the first Setup call, so a slot that opens already Claimable (never seen as
    // Locked this session) shows instantly instead of playing an unlock it didn't witness.
    private SubRewardSlotState? currentState;

    public void Setup(SubRewardItem data, SubRewardSlotState state, Action onClaimClicked)
    {
        amountText.text = "x"+data.amount.ToString();

        ApplyIcon(data.iconKey);

        bool justUnlocked = currentState == SubRewardSlotState.Locked && state == SubRewardSlotState.Claimable;
        currentState = state;

        claimedOverlay.SetActive(state == SubRewardSlotState.Claimed);
        CollectedGameobjectCat.SetActive(state == SubRewardSlotState.Claimed);

        if (adBadge != null) adBadge.SetActive(data.requireType == RewardRequireType.Ads);
        if (iapBadge != null) iapBadge.SetActive(data.requireType == RewardRequireType.IAP);

        claimButton.onClick.RemoveAllListeners();
        if (state == SubRewardSlotState.Claimable)
            claimButton.onClick.AddListener(() => onClaimClicked?.Invoke());

        if (justUnlocked)
        {
            PlayUnlockTransition();
            return;
        }

        lockedOverlay.SetActive(state == SubRewardSlotState.Locked);
        LockedGameobjectCat.SetActive(state == SubRewardSlotState.Locked);
        claimButton.gameObject.SetActive(state == SubRewardSlotState.Claimable);
        AvailableGameobjectCat.SetActive(state == SubRewardSlotState.Claimable);
        SetGroupAlpha(lockedOverlayGroup, state == SubRewardSlotState.Locked ? 1f : 0f);
        SetGroupInteractable(claimButtonGroup, state == SubRewardSlotState.Claimable);
        SetGroupAlpha(claimButtonGroup, state == SubRewardSlotState.Claimable ? 1f : 0f);
    }

    // Locked -> Claimable only: the lock fades out (then deactivates once invisible), the claim
    // button fades in and only becomes interactable once fully visible, and the cat that marks
    // "available" punches to draw the eye to the change instead of just popping into place.
    private void PlayUnlockTransition()
    {
        // Debug.LogError($"[RewardSlotView:{name}] Unlock animation played at t={Time.time:F3} frame={Time.frameCount}");

        LockedGameobjectCat.SetActive(false);

        AvailableGameobjectCat.SetActive(true);
        AvailableGameobjectCat.transform.DOKill();
        AvailableGameobjectCat.transform.DOPunchScale(
            availableCatPunchStrength, availableCatPunchDuration, availableCatPunchVibrato, availableCatPunchElasticity);

        claimButton.gameObject.SetActive(true);
        SetGroupInteractable(claimButtonGroup, false);
        if (claimButtonGroup != null)
        {
            claimButtonGroup.DOKill();
            claimButtonGroup.alpha = 0f;
            claimButtonGroup.DOFade(1f, claimFadeInDuration)
                .OnComplete(() => SetGroupInteractable(claimButtonGroup, true));
        }
        else
        {
            SetGroupInteractable(claimButtonGroup, true);
        }

        if (lockedOverlayGroup != null)
        {
            lockedOverlayGroup.DOKill();
            lockedOverlayGroup.DOFade(0f, lockedFadeOutDuration)
                .OnComplete(() => lockedOverlay.SetActive(false));
        }
        else
        {
            lockedOverlay.SetActive(false);
        }
    }

    private static void SetGroupAlpha(CanvasGroup group, float alpha)
    {
        if (group != null)
        {
            group.alpha = alpha;
        }
    }

    private static void SetGroupInteractable(CanvasGroup group, bool interactable)
    {
        if (group != null)
        {
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }

    private void ApplyIcon(string iconKey)
    {
        if (iconImage == null)
            return;

        if (iconDatabase == null)
        {
            Debug.LogWarning(
                $"{nameof(RewardSlotView)} on {name} has no {nameof(IconDatabaseConfig)} " +
                "assigned - icons won't be shown.");
            return;
        }

        Sprite sprite = iconDatabase.GetIconOrFallback(iconKey);

        if (sprite != null)
        {
            iconImage.sprite = sprite;
        }

        // Hide the image entirely rather than showing Unity's broken-sprite
        // square if neither the key nor the fallback resolved.
        iconImage.enabled = sprite != null;
    }
}