using Shared.GameFlow;
using UnityEngine;

namespace Shared.GameConfig
{
    public enum UnlockFeature
    {
        None,
        SpinWheel,
        DailyChallenge,
        DailyReward
    }

    [System.Serializable]   // <-- required so this shows up in the Inspector inside the List
    public class UnlockData
    {
        [Header("Unlock Info")]
        public string unlockId;
        [Tooltip("Only used when Feature is None. When a feature is assigned, the level " +
                 "comes from that feature's own UnlockLevel config instead - set it there.")]
        public int levelNumber;
        [Tooltip("Order to show this popup in when multiple unlocks are ready at the same " +
                 "time. Lower shows first. Popup priority: Spin Wheel -> Daily Reward -> " +
                 "Daily Challenge.")]
        public int priority;
        public string header;
        public MainObjectAnimationInfo mainObjectPrefab;
        [TextArea] public string description;

        [Header("On Closed")]
        [Tooltip("Feature to auto-open once this unlock's popup is dismissed.")]
        public UnlockFeature feature = UnlockFeature.None;

        [System.NonSerialized] private bool? cachedShown;

        private static readonly Shared.PlayerData.PlayerDataSaveSystem saveSystem =
            new Shared.PlayerData.PlayerDataSaveSystem();

        private string SaveKey => "Unlock_Shown_" + unlockId;

        public bool IsShown
        {
            get
            {
                if (cachedShown == null)
                {
                    Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
                    cachedShown = data.TryGetInt(SaveKey, out int value) && value == 1;
                }
                return cachedShown.Value;
            }
        }

        public void MarkShown()
        {
            cachedShown = true;
            SetAndSave(1);
        }

        public void ResetShown()
        {
            cachedShown = false;
            SetAndSave(0);
        }

        private void SetAndSave(int value)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.SetInt(SaveKey, value);

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;

            saveSystem.Save(fileName, data);
        }
    }
}
