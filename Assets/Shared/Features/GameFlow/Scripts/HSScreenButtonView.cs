using System.Collections;
using Shared.GameFlow;
using UnityEngine;
using UnityEngine.UI;

public class HSScreenButtonView : MonoBehaviour
{
    public Toggle baseToggle;

    public GameObject baseObj;
    public GameObject activeObj;
    [Tooltip("Shown instead of baseObj/activeObj whenever SetLocked(true) is called - e.g. a " +
             "padlock badge for a bottom-nav icon whose feature isn't unlocked yet at the " +
             "player's current level.")]
    public GameObject lockedObj;

    // public HSScreenType screenType;

    // public HSScreenController controller;

    public LayoutElement layoutElement;

    [Tooltip("Popped up from this button itself (see ShowLockedToast) instead of opening a " +
             "popup, when this icon is tapped while its feature isn't unlocked yet.")]
    [SerializeField] private ToastMessageController lockedToast;

    [SerializeField] private float activeEnterDistance = 12f;
    [SerializeField] private float activeEnterDuration = 0.18f;

    private RectTransform activeRect;
    private Vector2 activeRestingPosition;
    private bool activePositionCaptured;
    private Coroutine activeEnterRoutine;
bool isLocked = false;

    private void OnEnable()
    {
        // Guards against this button being disabled mid the active-enter slide (e.g. bottom nav
        // toggled off/on) - snap back to resting instead of leaving activeRect stuck mid-slide
        // the next time this becomes active.
        if (activeEnterRoutine != null)
        {
            StopCoroutine(activeEnterRoutine);
            activeEnterRoutine = null;
        }

        if (activePositionCaptured && activeRect != null)
        {
            activeRect.anchoredPosition = activeRestingPosition;
        }

        // Re-apply the current locked state too - otherwise a stale isLocked from before this
        // GameObject was disabled could leave the wrong baseObj/activeObj/lockedObj combination
        // showing once it's enabled again.
        SetLocked(isLocked);
    }

    public void OnToggle(bool isActive)
    {
        if(isActive)
        AudioManager.PlayClickSound();
        OnBaseToggle(isActive);
    }

    public void OnBaseToggle(bool isActive)
    {

        baseObj.SetActive(!isActive);
        activeObj.SetActive(isActive);


        if (isActive)
        {
            PlayActiveEnter();
        }
        if (isLocked)
        {
            if (baseObj != null) baseObj.SetActive(false);
            if (activeObj != null) activeObj.SetActive(false);
            if (lockedObj != null) lockedObj.SetActive(true);
        }
        // layoutElement.enabled = isActive;

        // controller.OnScreenToggle(screenType, isActive);
    }

    /// <summary>Mutually exclusive with the base/active look above - locked always wins,
    /// overriding whatever OnBaseToggle last set, and un-locking falls back to base (inactive),
    /// not active, since a caller that unlocks never also asserts selection in the same call.</summary>
    public void SetLocked(bool locked)
    {
        isLocked=locked;
        if (locked)
        {
            // Disable every other state root first, so lockedObj is the only one ever active
            // while locked - no state can leak through alongside it.
            if (baseObj != null) baseObj.SetActive(false);
            if (activeObj != null) activeObj.SetActive(false);
            if (lockedObj != null) lockedObj.SetActive(true);
            return;
        }

        if (lockedObj != null) lockedObj.SetActive(false);

        if (baseObj != null && activeObj != null && !activeObj.activeSelf)
        {
            baseObj.SetActive(true);
        }
    }

    /// <summary>Pops this button's own toast (see lockedToast) showing message - callers use
    /// this instead of OnToggle when a tap lands on this icon while it's locked, so the feedback
    /// visibly comes from the icon itself rather than a separate popup.</summary>
    public void ShowLockedToast(string message)
    {
        lockedToast?.Show(message, transform);
    }

    private void PlayActiveEnter()
    {
        if (activeObj == null)
        {
            return;
        }

        if (activeRect == null)
        {
            activeRect = activeObj.GetComponent<RectTransform>();
        }

        if (activeRect == null)
        {
            return;
        }

        if (!activePositionCaptured)
        {
            activeRestingPosition = activeRect.anchoredPosition;
            activePositionCaptured = true;
        }

        if (activeEnterRoutine != null)
        {
            StopCoroutine(activeEnterRoutine);
        }

        activeEnterRoutine = StartCoroutine(EnterMoveUpRoutine());
    }

    private IEnumerator EnterMoveUpRoutine()
    {
        Vector2 from = activeRestingPosition - new Vector2(0f, Mathf.Abs(activeEnterDistance));
        Vector2 to = activeRestingPosition;
        activeRect.anchoredPosition = from;

        float seconds = Mathf.Max(0.01f, activeEnterDuration);
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOutCubic(Mathf.Clamp01(elapsed / seconds));
            activeRect.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
            yield return null;
        }

        activeRect.anchoredPosition = to;
        activeEnterRoutine = null;
    }

    private static float EaseOutCubic(float t)
    {
        float value = 1f - t;
        return 1f - value * value * value;
    }
}
