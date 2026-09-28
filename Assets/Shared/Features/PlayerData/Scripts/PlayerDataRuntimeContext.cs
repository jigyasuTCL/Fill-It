namespace Shared.PlayerData
{
    public static class PlayerDataRuntimeContext
    {
        public static PlayerData Data { get; private set; }
        public static string FileName { get; private set; }

        public static bool HasData => Data != null;

        public static void Set(PlayerData data, string fileName)
        {
            Data = data;
            FileName = fileName;
        }
    }
}
