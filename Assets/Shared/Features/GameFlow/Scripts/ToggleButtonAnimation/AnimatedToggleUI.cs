using DG.Tweening;
using Shared.Vibration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnimatedToggleUI : MonoBehaviour
{
    [Header("Toggle")]
    [SerializeField] private Toggle toggle;

    [Header("References")]
    [SerializeField] private Image background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private TMP_Text onLabel;
    [SerializeField] private TMP_Text offLabel;

    [Header("Colors")]
    [SerializeField] private Color onColor = new Color(0.45f, 0.78f, 0.32f);
    [SerializeField] private Color offColor = new Color(0.55f, 0.51f, 0.49f);

    [Header("Handle Position")]
    [SerializeField] private float offX = -35f;
    [SerializeField] private float onX = 35f;

    [Header("Animation")]
    [SerializeField] private float animationDuration = 0.2f;

    private Sequence animationSequence;

    public Toggle Toggle => toggle;

    private void Awake()
    {
        toggle.onValueChanged.AddListener(OnToggleChanged);

        // Set initial state without animation.
        ApplyState(toggle.isOn);
    }

    private void OnDestroy()
    {
        toggle.onValueChanged.RemoveListener(OnToggleChanged);

        animationSequence?.Kill();
    }

    private void OnToggleChanged(bool value)
    {
        AnimateState(value);

        VibrationManager.Instance.PlayVibration(ImpactFeedbackStyle.Soft);
        AudioManager.Instance.PlaySoundAction?.Invoke(value ? SoundName.ToggleOn : SoundName.ToggleOff);
    }

    /// <summary>
    /// Syncs the toggle to an external value (e.g. loaded save data) without firing
    /// onValueChanged, so callers can refresh UI state safely.
    /// </summary>
    /// <param name="value">Target on/off state.</param>
    /// <param name="animate">True to animate the transition, false to snap instantly.</param>
    public void SetState(bool value, bool animate)
    {
        toggle.SetIsOnWithoutNotify(value);

        if (animate)
        {
            AnimateState(value);
        }
        else
        {
            ApplyState(value);
        }
    }

    private void AnimateState(bool value)
    {
        animationSequence?.Kill();

        float targetX = value ? onX : offX;
        Color targetColor = value ? onColor : offColor;

        animationSequence = DOTween.Sequence();

        // Background color
        animationSequence.Join(
            background
                .DOColor(targetColor, animationDuration)
                .SetEase(Ease.OutQuad)
        );

        // Move handle
        animationSequence.Join(
            handle
                .DOAnchorPosX(targetX, animationDuration)
                .SetEase(Ease.OutBack)
        );

        // Small bounce
        animationSequence.Join(
            handle
                .DOScale(1.08f, animationDuration * 0.5f)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo)
        );
        DOVirtual.DelayedCall(animationDuration * 0.5f, () =>
        {
            SetLabelState(value);
        });
        // Swap text
    }

    private void ApplyState(bool value)
    {
        background.color = value ? onColor : offColor;

        Vector2 position = handle.anchoredPosition;
        position.x = value ? onX : offX;
        handle.anchoredPosition = position;

        handle.localScale = Vector3.one;

        SetLabelState(value);
    }

    private void SetLabelState(bool value)
    {
        onLabel.gameObject.SetActive(value);
        offLabel.gameObject.SetActive(!value);
    }
}