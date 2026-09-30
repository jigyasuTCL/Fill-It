namespace Shared.GameConfig
{
    /// <summary>
    /// How a level can be lost. Hearts: wrong moves spend a limited number of lives. Timer: the
    /// level has a countdown and wrong moves cost nothing. Both end in the same Out-of-Lives
    /// ("Time's Up") revive flow.
    /// </summary>
    public enum GameplayMode
    {
        Hearts,
        Timer
    }
}
