using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.AppreciationText
{
    /// <summary>
    /// Data asset for the Appreciation Text feature - one list of text-image entries per tier,
    /// plus the shared entrance/hold/exit timings and the fallback sfx. Swap/author this asset per
    /// project; AppreciationTextManager itself has no game-specific knowledge.
    /// </summary>
    [CreateAssetMenu(fileName = "AppreciationTextConfig", menuName = "Shared/Appreciation Text Config")]
    public sealed class AppreciationTextConfig : ScriptableObject
    {
        [Serializable]
        public sealed class TierEntries
        {
            [SerializeField] private List<AppreciationTextEntry> entries = new List<AppreciationTextEntry>();
            public List<AppreciationTextEntry> Entries => entries;
        }

        [Header("Entries per tier")]
        [SerializeField] private TierEntries easy = new TierEntries();
        [SerializeField] private TierEntries medium = new TierEntries();
        [SerializeField] private TierEntries hard = new TierEntries();
        [SerializeField] private TierEntries veryHard = new TierEntries();

        [Header("Warning entries - played instead of a tier entry when a wrong solution appears")]
        [SerializeField] private TierEntries warning = new TierEntries();

        [Header("Fallback SFX - plays when the picked entry has none of its own")]
        [SerializeField] private AudioClip defaultSfx;

        [Header("Timing - entrance (scale up + rotate) -> hold -> exit (move up + fade)")]
        [Tooltip("Total of entrance+hold+exit should land around 1-1.5s.")]
        [SerializeField] private float entranceDurationSeconds = 0.35f;
        [SerializeField] private float holdDurationSeconds = 0.5f;
        [SerializeField] private float exitDurationSeconds = 0.35f;
        [SerializeField] private float entranceStartScale = 0f;
        [Tooltip("Scale the entrance pops up to before easing back down to 1x - the \"overshoot\" of the scale-up-then-ease-back-out effect.")]
        [SerializeField] private float entrancePeakScale = 1.5f;
        [SerializeField] private float entranceStartRotationDegrees = -25f;
        [SerializeField] private float exitMoveUpDistance = 80f;

        [Header("Queueing - stagger between multiple Play() calls arriving close together")]
        [SerializeField] private float queueStaggerSeconds = 0.15f;

        public IReadOnlyList<AppreciationTextEntry> GetEntries(AppreciationTextTier tier)
        {
            switch (tier)
            {
                case AppreciationTextTier.Easy: return easy.Entries;
                case AppreciationTextTier.Hard: return hard.Entries;
                case AppreciationTextTier.VeryHard: return veryHard.Entries;
                default: return medium.Entries;
            }
        }

        public IReadOnlyList<AppreciationTextEntry> WarningEntries => warning.Entries;

        public AudioClip DefaultSfx => defaultSfx;
        public float EntranceDurationSeconds => Mathf.Max(0.01f, entranceDurationSeconds);
        public float HoldDurationSeconds => Mathf.Max(0f, holdDurationSeconds);
        public float ExitDurationSeconds => Mathf.Max(0.01f, exitDurationSeconds);
        public float EntranceStartScale => Mathf.Max(0f, entranceStartScale);
        public float EntrancePeakScale => Mathf.Max(1f, entrancePeakScale);
        public float EntranceStartRotationDegrees => entranceStartRotationDegrees;
        public float ExitMoveUpDistance => exitMoveUpDistance;
        public float QueueStaggerSeconds => Mathf.Max(0f, queueStaggerSeconds);
    }
}
