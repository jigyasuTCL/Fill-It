using UnityEngine;

namespace MangraGames
{
    /// <summary>
    /// Continuously rotates the loading icon while it's active. Attach to the center image of
    /// the ad-loading screen root that AdsManager shows/hides - rotation runs for free whenever
    /// that root is active and simply stops (OnEnable/OnDisable) when it isn't.
    /// </summary>
    public sealed class AdLoadingSpinner : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 180f;

        private void Update()
        {
            transform.Rotate(0f, 0f, -degreesPerSecond * Time.deltaTime);
        }
    }
}
