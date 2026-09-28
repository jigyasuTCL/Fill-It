using TMPro;
using UnityEngine;

namespace Shared.GameFlow.UnityAdapters
{
    public sealed class MainMenuPlayLevelLabelAdapter : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (levelLabel == null)
            {
                return;
            }

            int levelNumber = Shared.PlayerData.PlayerData.Instance.CurrentLevelIndex + 1;
            levelLabel.text = $"Level {levelNumber}";
        }
    }
}
