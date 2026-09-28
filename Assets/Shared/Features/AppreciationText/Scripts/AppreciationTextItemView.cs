using System.Collections;
using UnityEngine;

namespace Shared.AppreciationText
{
    /// <summary>
    /// Lives on the appreciation text prefab. Plays scale-up + rotate in, holds, then moves up
    /// while fading out, and destroys itself. Knows nothing about tiers/queueing - purely a
    /// self-contained "play this sprite" animation, driven by AppreciationTextManager. World-space
    /// SpriteRenderer, matching the rest of the board's sprite-based gameplay.
    /// </summary>
    public sealed class AppreciationTextItemView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Particle VFX - one per AppreciationParticleColor, matched to the entry's own text image color")]
        [SerializeField] private ParticleSystem yellowParticle;
        [SerializeField] private ParticleSystem purpleParticle;
        [SerializeField] private ParticleSystem blueParticle;
        [SerializeField] private ParticleSystem peachParticle;

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        public void Play(Sprite sprite, AppreciationTextConfig config, AppreciationParticleColor particleColor)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = sprite;
            }

            PlayParticle(particleColor);
            StartCoroutine(PlaySequence(config));
        }

        // Only the color matching this entry's own text image plays - every other one is
        // deactivated outright (not just left un-played), since the prefab used to ship with one
        // of them (peach) always active regardless of which entry was showing.
        private void PlayParticle(AppreciationParticleColor particleColor)
        {
            SetParticleActive(yellowParticle, particleColor == AppreciationParticleColor.Yellow);
            SetParticleActive(purpleParticle, particleColor == AppreciationParticleColor.Purple);
            SetParticleActive(blueParticle, particleColor == AppreciationParticleColor.Blue);
            SetParticleActive(peachParticle, particleColor == AppreciationParticleColor.Peach);
        }

        private static void SetParticleActive(ParticleSystem particle, bool active)
        {
            if (particle == null)
            {
                return;
            }

            particle.gameObject.SetActive(active);
            if (active)
            {
                particle.Play(true);
            }
        }

        private IEnumerator PlaySequence(AppreciationTextConfig config)
        {
            Transform self = transform;
            Vector3 homePosition = self.position;
            Vector3 startScale = Vector3.one * config.EntranceStartScale;
            Quaternion startRotation = Quaternion.Euler(0f, 0f, config.EntranceStartRotationDegrees);

            self.localScale = startScale;
            self.rotation = startRotation;
            self.position = homePosition;
            SetAlpha(1f);

            // Entrance, in two beats over the same total duration + rotate to neutral throughout:
            // 1. Grow past normal size up to EntrancePeakScale (ease-out).
            // 2. Ease back out from that peak down to normal size (ease-out-back, so it settles
            //    with a small extra bounce rather than stopping dead at 1x).
            float entranceDuration = config.EntranceDurationSeconds;
            Vector3 peakScale = Vector3.one * config.EntrancePeakScale;
            const float growFraction = 0.6f;
            float growDuration = entranceDuration * growFraction;
            float settleDuration = entranceDuration - growDuration;

            for (float elapsed = 0f; elapsed < growDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / growDuration);
                self.localScale = Vector3.Lerp(startScale, peakScale, EaseOutCubic(t));
                self.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, elapsed / entranceDuration);
                yield return null;
            }

            self.localScale = peakScale;

            for (float elapsed = 0f; elapsed < settleDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / settleDuration);
                self.localScale = Vector3.LerpUnclamped(peakScale, Vector3.one, EaseOutBack(t));
                self.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, (growDuration + elapsed) / entranceDuration);
                yield return null;
            }

            self.localScale = Vector3.one;
            self.rotation = Quaternion.identity;

            // Hold.
            if (config.HoldDurationSeconds > 0f)
            {
                yield return new WaitForSeconds(config.HoldDurationSeconds);
            }

            // Exit: move up + fade out.
            float exitDuration = config.ExitDurationSeconds;
            Vector3 exitPosition = homePosition + new Vector3(0f, config.ExitMoveUpDistance, 0f);
            for (float elapsed = 0f; elapsed < exitDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / exitDuration);
                self.position = Vector3.Lerp(homePosition, exitPosition, t);
                SetAlpha(1f - t);
                yield return null;
            }

            Destroy(gameObject);
        }

        private void SetAlpha(float alpha)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            Color color = spriteRenderer.color;
            color.a = alpha;
            spriteRenderer.color = color;
        }

        private static float EaseOutCubic(float t)
        {
            float x = 1f - t;
            return 1f - x * x * x;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
