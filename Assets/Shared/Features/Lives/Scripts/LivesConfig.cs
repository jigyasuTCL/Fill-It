using System;

namespace Shared.Lives
{
    [Serializable]
    public sealed class LivesConfig
    {
        public int MaxLives = 5;
        public int InitialLives = 5;
        public float RegenSeconds = 300f;
        public int CoinRestoreCost = 50;
        public int AdRewardLives = 1;
    }
}
