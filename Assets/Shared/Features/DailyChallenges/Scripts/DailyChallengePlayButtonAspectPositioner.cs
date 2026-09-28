using UnityEngine;

namespace DailyChallenges
{
    /// <summary>
    /// Repositions the Daily Challenge popup's playButtonContainer's Y position differently
    /// depending on the device's screen aspect ratio (height/width) - X is left untouched.
    ///
    /// Buckets are data-driven (see AspectRatioYPosition/positions) rather than hardcoded to a
    /// fixed count, sorted ascending by maxAspectRatio: the device's own aspect ratio picks the
    /// first bucket whose maxAspectRatio it fits under, falling back to the last (tallest) bucket
    /// for anything taller than every configured entry. Defaults match the three buckets already
    /// tested:
    ///
    ///   - Up to ~16:9 / 4:3 (2560x1440, 1920x1080, iPad Pro 12.9 2018)       -> y = -725
    ///   - Up to ~18:9 (2160x1080, 2960x1440)                                 -> y = -795
    ///   - Taller (custom 9:21)                                               -> y = -890
    ///
    /// Runs once on Awake using Screen.width/Screen.height at that point - a device's screen
    /// aspect ratio doesn't change mid-session on mobile, so no further updates are needed.
    /// </summary>
    public sealed class DailyChallengePlayButtonAspectPositioner : MonoBehaviour
    {
        [System.Serializable]
        private struct AspectRatioYPosition
        {
            [Tooltip("Inspector-only label - not used at runtime.")]
            public string label;

            [Tooltip("Devices with an aspect ratio (Screen.height / Screen.width) at or below " +
                     "this use yPosition below. The LAST entry (sorted ascending) also catches " +
                     "any device taller than every configured entry.")]
            public float maxAspectRatio;

            public float yPosition;
        }

        [Tooltip("The container to reposition. Falls back to this GameObject's own " +
                 "RectTransform if left unassigned.")]
        [SerializeField] private RectTransform playButtonContainer;

        [Tooltip("Sorted ascending by maxAspectRatio - see the class summary for the default buckets.")]
        [SerializeField]
        private AspectRatioYPosition[] positions =
        {
            new AspectRatioYPosition
            {
                label = "16:9 / 4:3 (2560x1440, 1920x1080, iPad Pro 12.9 2018)",
                maxAspectRatio = 1.889f,
                yPosition = -725f
            },
            new AspectRatioYPosition
            {
                label = "18:9 (2160x1080, 2960x1440)",
                maxAspectRatio = 2.194f,
                yPosition = -795f
            },
            new AspectRatioYPosition
            {
                label = "9:21 (custom)",
                maxAspectRatio = float.MaxValue,
                yPosition = -890f
            }
        };

        private void Awake()
        {
            ApplyPosition();
        }

        [ContextMenu("Reapply Position")]
        private void ApplyPosition()
        {
            RectTransform target = playButtonContainer != null ? playButtonContainer : transform as RectTransform;
            if (target == null || positions == null || positions.Length == 0)
            {
                return;
            }

            float aspectRatio = Screen.width > 0 ? (float)Screen.height / Screen.width : 0f;

            // Last entry doubles as the fallback for anything taller than every configured
            // bucket, so a device more extreme than the tallest tested aspect still gets a
            // sensible (rather than default/zero) position.
            float y = positions[positions.Length - 1].yPosition;

            for (int i = 0; i < positions.Length; i++)
            {
                if (aspectRatio <= positions[i].maxAspectRatio)
                {
                    y = positions[i].yPosition;
                    break;
                }
            }

            Vector2 anchoredPosition = target.anchoredPosition;
            anchoredPosition.y = y;
            target.anchoredPosition = anchoredPosition;
        }
    }
}
