using UnityEngine;

public class SubRewardBarController : MonoBehaviour
{
    [SerializeField] private RectTransform subRewardsContainer;
    [SerializeField] private RectTransform bar;

    [Header("Bar Settings")]
    [SerializeField] private float topPadding = 0f;
    [SerializeField] private float bottomPadding = 0f;

    private void LateUpdate()
    {
        UpdateBar();
    }

    public void UpdateBar()
    {
        if (subRewardsContainer == null || bar == null)
            return;

        float height = subRewardsContainer.rect.height;

        height -= topPadding;
        height -= bottomPadding;

        height = Mathf.Max(0f, height);

        bar.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            height
        );

        // Keep bar centered vertically inside SubRewards.
        bar.anchorMin = new Vector2(bar.anchorMin.x, 0.5f);
        bar.anchorMax = new Vector2(bar.anchorMax.x, 0.5f);
        bar.pivot = new Vector2(bar.pivot.x, 0.5f);

        Vector2 position = bar.anchoredPosition;
        position.y = (topPadding - bottomPadding) * 0.5f;
        bar.anchoredPosition = position;
    }
}