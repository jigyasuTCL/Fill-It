using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.AppreciationText
{
    /// <summary>
    /// Plug-and-play "row/column completed" appreciation popup: call
    /// <c>AppreciationTextManager.Instance.Play(tier, worldPosition)</c> from anywhere and a
    /// random text-image for that tier scales/rotates in, holds, then moves up and fades out, with
    /// its sfx (or the config's default sfx) played alongside. Disable the whole feature via
    /// <see cref="Enabled"/> - while disabled, Play()/PlayWarning() are complete no-ops.
    ///
    /// <c>PlayWarning(worldPosition)</c> shares every bit of that machinery (queueing, screen
    /// clamp, sfx, no-immediate-repeat) but draws from the config's own separate Warning entries
    /// list instead of a tier - use it wherever a wrong solution/mistake should show the same kind
    /// of popup instead of a positive-tier one.
    ///
    /// Multiple Play()/PlayWarning() calls arriving close together (e.g. a row and a column
    /// completing on the same move) are queued and staggered by
    /// AppreciationTextConfig.QueueStaggerSeconds rather than dropped or stacked instantly on top
    /// of each other. The same entry never plays twice in a row within its own tier/warning list.
    ///
    /// The spawn position is always clamped to stay within the screen (minus a margin), so a
    /// popup anchored near a board edge never renders partly or fully off-screen.
    /// </summary>
    public sealed class AppreciationTextManager : MonoBehaviour
    {
        [SerializeField] private bool enabledFeature = true;
        [SerializeField] private AppreciationTextConfig config;
        [SerializeField] private Transform spawnParent;
        [SerializeField] private AppreciationTextItemView itemPrefab;

        [Header("Screen clamp - keeps the popup fully on-screen")]
        [SerializeField] private Camera screenClampCamera;
        [Range(0f, 0.45f)]
        [SerializeField] private float screenEdgeMargin = 0.12f;

        private static AppreciationTextManager instance;
        public static AppreciationTextManager Instance => instance;

        /// <summary>Master on/off switch. While false, every Play()/PlayWarning() call is a no-op.</summary>
        public bool Enabled
        {
            get => enabledFeature;
            set => enabledFeature = value;
        }

        private readonly struct PendingPlay
        {
            public readonly bool IsWarning;
            public readonly AppreciationTextTier Tier;
            public readonly Vector3 Position;

            public PendingPlay(AppreciationTextTier tier, Vector3 position)
            {
                IsWarning = false;
                Tier = tier;
                Position = position;
            }

            public PendingPlay(Vector3 position, bool isWarning)
            {
                IsWarning = isWarning;
                Tier = default;
                Position = position;
            }
        }

        private readonly Queue<PendingPlay> pendingPlays = new Queue<PendingPlay>();
        private readonly Dictionary<AppreciationTextTier, int> lastShownIndex = new Dictionary<AppreciationTextTier, int>();
        private int lastWarningIndex = -1;
        private Coroutine drainRoutine;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>Queues one appreciation popup from the given tier at spawnParent's position. No-op while <see cref="Enabled"/> is false.</summary>
        public void Play(AppreciationTextTier tier)
        {
            if (spawnParent == null)
            {
                return;
            }

            Play(tier, spawnParent.position);
        }

        /// <summary>Queues one appreciation popup from the given tier, anchored at worldPosition (clamped on-screen). No-op while <see cref="Enabled"/> is false.</summary>
        public void Play(AppreciationTextTier tier, Vector3 worldPosition)
        {
            if (!TryLogWhyDisabled(tier.ToString()))
            {
                return;
            }

            pendingPlays.Enqueue(new PendingPlay(tier, worldPosition));
            EnsureDraining();
        }

        /// <summary>Queues one warning popup (wrong solution/mistake) at spawnParent's position. No-op while <see cref="Enabled"/> is false.</summary>
        public void PlayWarning()
        {
            if (spawnParent == null)
            {
                return;
            }

            PlayWarning(spawnParent.position);
        }

        /// <summary>
        /// Queues one warning popup (wrong solution/mistake), anchored at worldPosition (clamped
        /// on-screen), drawn from the config's Warning entries rather than a tier. Shares queueing,
        /// screen clamp, sfx and no-immediate-repeat with Play(). No-op while <see cref="Enabled"/>
        /// is false.
        /// </summary>
        public void PlayWarning(Vector3 worldPosition)
        {
            if (!TryLogWhyDisabled("Warning"))
            {
                return;
            }

            pendingPlays.Enqueue(new PendingPlay(worldPosition, isWarning: true));
            EnsureDraining();
        }

        // Shared by Play() and PlayWarning() - logs exactly which setup requirement is missing
        // (rather than silently no-opping) so "why isn't the text appearing" can be diagnosed from
        // the Console alone. Returns false when the call should be dropped.
        private bool TryLogWhyDisabled(string requestLabel)
        {
            if (!enabledFeature)
            {
                return false;
            }

            if (config == null)
            {
                Debug.LogWarning($"{nameof(AppreciationTextManager)}: {requestLabel} not shown - no {nameof(AppreciationTextConfig)} assigned in the Inspector.");
                return false;
            }

            if (itemPrefab == null)
            {
                Debug.LogWarning($"{nameof(AppreciationTextManager)}: {requestLabel} not shown - no {nameof(itemPrefab)} assigned in the Inspector.");
                return false;
            }

            return true;
        }

        private void EnsureDraining()
        {
            if (drainRoutine == null)
            {
                drainRoutine = StartCoroutine(DrainQueue());
            }
        }

        private IEnumerator DrainQueue()
        {
            while (pendingPlays.Count > 0)
            {
                PendingPlay pending = pendingPlays.Dequeue();
                if (pending.IsWarning)
                {
                    SpawnEntry(config.WarningEntries, PickWarningIndex(config.WarningEntries.Count), pending.Position);
                }
                else
                {
                    IReadOnlyList<AppreciationTextEntry> entries = config.GetEntries(pending.Tier);
                    SpawnEntry(entries, PickIndex(pending.Tier, entries.Count), pending.Position);
                }

                if (pendingPlays.Count > 0 && config.QueueStaggerSeconds > 0f)
                {
                    yield return new WaitForSeconds(config.QueueStaggerSeconds);
                }
            }

            drainRoutine = null;
        }

        private void SpawnEntry(IReadOnlyList<AppreciationTextEntry> entries, int index, Vector3 worldPosition)
        {
            if (entries == null || entries.Count == 0 || index < 0)
            {
                Debug.LogWarning($"{nameof(AppreciationTextManager)}: not shown - no entries configured on {nameof(AppreciationTextConfig)} for this list.");
                return;
            }

            AppreciationTextEntry entry = entries[index];

            Vector3 clampedPosition = ClampToScreen(worldPosition);
            AppreciationTextItemView instance = Instantiate(
                itemPrefab,
                clampedPosition,
                itemPrefab.transform.rotation,
                spawnParent);
            instance.Play(entry.TextImage, config, entry.ParticleColor);

            AudioClip sfx = entry.Sfx != null ? entry.Sfx : config.DefaultSfx;
            PlaySfx(sfx);
        }

        // Keeps the spawn position within the screen (minus screenEdgeMargin on every side), so a
        // popup anchored on a cell near the board's edge never renders partly off-screen.
        private Vector3 ClampToScreen(Vector3 worldPosition)
        {
            Camera cam = screenClampCamera != null ? screenClampCamera : Camera.main;
            if (cam == null)
            {
                return worldPosition;
            }

            Vector3 viewportPoint = cam.WorldToViewportPoint(worldPosition);
            viewportPoint.x = Mathf.Clamp(viewportPoint.x, screenEdgeMargin, 1f - screenEdgeMargin);
            viewportPoint.y = Mathf.Clamp(viewportPoint.y, screenEdgeMargin, 1f - screenEdgeMargin);
            return cam.ViewportToWorldPoint(viewportPoint);
        }

        // Never repeats the same entry twice in a row within one tier (when there's a choice).
        private int PickIndex(AppreciationTextTier tier, int count)
        {
            if (count == 1)
            {
                lastShownIndex[tier] = 0;
                return 0;
            }

            int previous = lastShownIndex.TryGetValue(tier, out int stored) ? stored : -1;
            int index = Random.Range(0, count);
            if (index == previous)
            {
                index = (index + 1) % count;
            }

            lastShownIndex[tier] = index;
            return index;
        }

        // Same no-immediate-repeat logic as PickIndex, tracked separately for the Warning list.
        private int PickWarningIndex(int count)
        {
            if (count == 1)
            {
                lastWarningIndex = 0;
                return 0;
            }

            int index = Random.Range(0, count);
            if (index == lastWarningIndex)
            {
                index = (index + 1) % count;
            }

            lastWarningIndex = index;
            return index;
        }

        private static void PlaySfx(AudioClip clip)
        {
            if (clip == null || AudioManager.Instance == null || AudioManager.Instance.soundSource == null)
            {
                return;
            }

            AudioManager.Instance.soundSource.PlayOneShot(clip);
        }
    }
}
