using SharedGlobalConfig = Shared.GameConfig.GlobalConfig;

namespace Shared.Boot
{
    public static class GlobalConfigRuntimeContext
    {
        public static SharedGlobalConfig Config { get; private set; }

        public static bool HasConfig => Config != null;

        public static void Set(SharedGlobalConfig config)
        {
            Config = config;
        }
    }
}
