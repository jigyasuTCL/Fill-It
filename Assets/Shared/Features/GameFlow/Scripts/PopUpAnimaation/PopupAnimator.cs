using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Reusable popup open/close animation using DOTween (scale-punch,
/// OutBack open / InBack close by default � matches the Spin Wheel feel).
///
/// Plug-and-play: drop this on any popup, assign the transform (and
/// optionally a CanvasGroup for interactable/raycast blocking, and the
/// GameObject to toggle active/inactive), then call Open()/Close() from
/// whatever controller owns that popup.
///
/// Knows nothing about game logic - only plays the animation and reports
/// completion via callback.
/// </summary>
public class PopupAnimator : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform to scale. Required.")]
    [SerializeField] private Transform targetTransform;

    public Transform TargetTransform => targetTransform;

    [Tooltip("Optional. If set, blocks input while animating, and fades alpha with open/close.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Optional. If set, SetActive(true/false) is handled automatically.")]
    [SerializeField] private GameObject rootToToggle;

    [Header("Open")]
    [SerializeField] private float openDuration = 0.3f;
    [SerializeField] private Ease openEase = Ease.OutBack;

    [Header("Close")]
    [SerializeField] private float closeDuration = 0.3f;
    [SerializeField] private Ease closeEase = Ease.InBack;

    [Header("Scale")]
    [SerializeField] private Vector3 hiddenScale = Vector3.zero;
    [SerializeField] private Vector3 shownScale = Vector3.one;

    [Header("Fade")]
    [SerializeField] private float hiddenAlpha = 0f;
    [SerializeField] private float shownAlpha = 1f;

    private Tween activeTween;

    public bool IsAnimating =>
        activeTween != null && activeTween.IsActive() && activeTween.IsPlaying();

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Plays the open animation. Activates rootToToggle first (if assigned).
    /// </summary>
    public void Open(Action onComplete = null)
    {
        if (IsAnimating && IsOpen)
            return;

        KillActiveTween();

        if (rootToToggle != null)
            rootToToggle.SetActive(true);

        SetInteractable(false);

        if (targetTransform != null)
            targetTransform.localScale = hiddenScale;

        if (canvasGroup != null)
            canvasGroup.alpha = hiddenAlpha;

        Sequence seq = DOTween.Sequence();

        seq.Join(targetTransform
            .DOScale(shownScale, openDuration)
            .SetEase(openEase));

        if (canvasGroup != null)
        {
            seq.Join(canvasGroup
                .DOFade(shownAlpha, openDuration)
                .SetEase(Ease.Linear));
        }

        seq.OnComplete(() =>
        {
            activeTween = null;
            IsOpen = true;
            SetInteractable(true);
            onComplete?.Invoke();
        });

        activeTween = seq;
    }

    /// <summary>
    /// Plays the close animation. Deactivates rootToToggle on completion (if assigned).
    /// </summary>
    public void Close(Action onComplete = null)
    {
        if (IsAnimating && !IsOpen)
            return;

        KillActiveTween();

        SetInteractable(false);

        Sequence seq = DOTween.Sequence();

        seq.Join(targetTransform
            .DOScale(hiddenScale, closeDuration)
            .SetEase(closeEase));

        if (canvasGroup != null)
        {
            seq.Join(canvasGroup
                .DOFade(hiddenAlpha, closeDuration)
                .SetEase(Ease.Linear));
        }

        seq.OnComplete(() =>
        {
            activeTween = null;
            IsOpen = false;

            if (rootToToggle != null)
                rootToToggle.SetActive(false);

            onComplete?.Invoke();
        });

        activeTween = seq;
    }

    /// <summary>
    /// Snap instantly to open/closed with no animation. Useful on init.
    /// </summary>
    public void SnapTo(bool open)
    {
        KillActiveTween();

        if (targetTransform != null)
            targetTransform.localScale = open ? shownScale : hiddenScale;

        if (canvasGroup != null)
            canvasGroup.alpha = open ? shownAlpha : hiddenAlpha;

        if (rootToToggle != null)
            rootToToggle.SetActive(open);

        IsOpen = open;
        SetInteractable(open);
    }

    private void KillActiveTween()
    {
        if (activeTween != null && activeTween.IsActive())
            activeTween.Kill();

        activeTween = null;
    }

    private void SetInteractable(bool value)
    {
        if (canvasGroup == null)
            return;

        // canvasGroup.interactable = value;
        // canvasGroup.blocksRaycasts = value;
    }

    private void OnDestroy()
    {
        KillActiveTween();
    }
}