using UnityEngine;

namespace Shared.GameConfig
{
    [CreateAssetMenu(fileName = "GameRulesConfig", menuName = "MangraGames/Game Rules Config")]
    public sealed class GameRulesConfigAsset : ScriptableObject
    {
        [SerializeField] private GameRulesConfig config = new GameRulesConfig();

        public GameRulesConfig ToConfig()
        {
            return (config ?? new GameRulesConfig()).CloneNormalized();
        }
    }
}
