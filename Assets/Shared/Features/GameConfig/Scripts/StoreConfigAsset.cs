using UnityEngine;

namespace Shared.GameConfig
{
    [CreateAssetMenu(fileName = "StoreConfig", menuName = "MangraGames/Store Config")]
    public sealed class StoreConfigAsset : ScriptableObject
    {
        [SerializeField] private StoreConfig config = new StoreConfig();

        public StoreConfig ToConfig()
        {
            return (config ?? new StoreConfig()).CloneNormalized();
        }
    }
}
