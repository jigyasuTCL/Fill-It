using PlayerDataModel = Shared.PlayerData.PlayerData;

namespace Shared.Vibration
{
    public sealed class VibrationManager
    {
        private static VibrationManager instance;
        private bool initialized;

        public static VibrationManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new VibrationManager();
                }

                instance.Init();
                return instance;
            }
        }

        public void Init()
        {
            if (initialized)
            {
                return;
            }

            global::Vibration.Init();
            initialized = true;
        }

        public void PlayVibration(global::ImpactFeedbackStyle impactStyle)
        {
            Init();
            if (!PlayerDataModel.Instance.IsHapticOn)
            {
                return;
            }

            global::Vibration.VibrateIOS(impactStyle);
        }

        public void PlayVibration(global::NotificationFeedbackStyle notificationStyle)
        {
            Init();
            if (!PlayerDataModel.Instance.IsHapticOn)
            {
                return;
            }

            global::Vibration.VibrateIOS(notificationStyle);
        }
    }
}
