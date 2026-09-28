using UnityEngine;

namespace Shared.GameConfig
{
    [CreateAssetMenu(fileName = "GlobalConfig", menuName = "MangraGames/Global Config")]
    public sealed class GlobalConfigAsset : ScriptableObject
    {
        [SerializeField] private GlobalConfig config = new GlobalConfig();

        public GlobalConfig ToConfig()
        {
            return (config ?? new GlobalConfig()).CloneNormalized();
        }
    }
}
