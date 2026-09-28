using System;
using System.Collections.Generic;
using UnityEngine;

// Data-only ScriptableObject: maps a reward's iconKey (see BaseRewardItem.iconKey
// in RewardModels.cs) to the Sprite that should be shown for it.
//
// Setup:
//   1. Assets > Create > Rewards > Icon Database
//   2. Fill in one entry per iconKey used in your DailyReward config
//      (main rewards and sub rewards both use the same iconKey field,
//      so one database covers both).
//   3. Drag the asset onto RewardSlotView.iconDatabase (and anywhere
//      else that needs to resolve an icon from a key).
[CreateAssetMenu(
    fileName = "IconDatabaseConfig",
    menuName = "IconData/IconConfig")]
public class IconDatabaseConfig : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        // Must match BaseRewardItem.iconKey exactly (case-sensitive).
        public string iconKey;
        public Sprite icon;
    }

    [SerializeField]
    private List<Entry> entries = new List<Entry>();

    // Optional: shown when a reward's iconKey has no matching entry, so
    // slots don't render blank/broken icons if config and database ever
    // drift out of sync.
    [SerializeField]
    private Sprite fallbackIcon;

    private Dictionary<string, Sprite> lookup;

    private void BuildLookupIfNeeded()
    {
        if (lookup != null)
            return;

        lookup = new Dictionary<string, Sprite>(entries.Count);

        foreach (Entry entry in entries)
        {
            if (string.IsNullOrEmpty(entry.iconKey))
                continue;

            if (lookup.ContainsKey(entry.iconKey))
            {
                Debug.LogWarning(
                    $"{nameof(IconDatabaseConfig)} '{name}' has a duplicate " +
                    $"iconKey '{entry.iconKey}'. Only the first entry will be used.");
                continue;
            }

            lookup.Add(entry.iconKey, entry.icon);
        }
    }

    // True only if iconKey matched a real, non-null sprite entry.
    // fallbackIcon is NOT applied here on purpose - use GetIconOrFallback()
    // if you want the fallback applied automatically.
    public bool TryGetIcon(string iconKey, out Sprite icon)
    {
        BuildLookupIfNeeded();

        if (!string.IsNullOrEmpty(iconKey) &&
            lookup.TryGetValue(iconKey, out Sprite found) &&
            found != null)
        {
            icon = found;
            return true;
        }

        icon = null;
        return false;
    }

    // Convenience wrapper: matched icon if found, otherwise fallbackIcon
    // (which may itself be null if none was assigned in the inspector).
    public Sprite GetIconOrFallback(string iconKey)
    {
        return TryGetIcon(iconKey, out Sprite icon)
            ? icon
            : fallbackIcon;
    }

    // Call if entries are ever mutated at runtime (not expected in normal use).
    public void InvalidateCache()
    {
        lookup = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        InvalidateCache();
    }
#endif
}