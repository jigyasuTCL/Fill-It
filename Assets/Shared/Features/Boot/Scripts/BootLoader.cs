using System.Collections;
using MangraGames;
using Shared.Analytics;
using Shared.GameConfig;
using Shared.Notifications;
using Shared.PlayerData;
using Shared.RemoteConfig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using SharedGlobalConfig = Shared.GameConfig.GlobalConfig;
using SharedPlayerData = Shared.PlayerData.PlayerData;

namespace Shared.Boot
{
    public sealed class BootLoader : MonoBehaviour
    {
        [FormerlySerializedAs("gameConfig")]
        [SerializeField] private GlobalConfigAsset globalConfig;
        [SerializeField] private string playerDataFileName = PlayerDataDefaults.FileName;
        [SerializeField] private GameObject studioLoadingImageRoot;
        [SerializeField] private GameObject gameSplashImageRoot;
        [SerializeField] private float studioLoadingSeconds = 2f;
        [SerializeField] private float gameSplashSeconds = 2f;
        [SerializeField] private float fadeSeconds = 0.4f;
        [SerializeField] private int targetFrameRate = 60;
        [SerializeField] private string nextSceneName = "GamePlay";

        private IEnumerator Start()
        {
            Application.targetFrameRate = Mathf.Max(1, targetFrameRate);
            // Gameplay taps can be sparse enough between moves that the OS would otherwise dim/lock
            // the screen mid-level - never let it sleep for the whole app lifetime.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            SharedGlobalConfig config = LoadGlobalConfig();
            GlobalConfigRuntimeContext.Set(config);

            PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();
            SharedPlayerData data = saveSystem.LoadOrCreate(playerDataFileName, config.InitialCoins);
            SharedPlayerData.Init(data, playerDataFileName);
            saveSystem.Save(playerDataFileName, SharedPlayerData.Instance);

            // No Core/service dependency (unlike AdsManager/AnalyticsManager below), so this is
            // safe to spin up this early, well before the scene load - it just needs to exist
            // before the player can background and resume the app. Moved to AFTER PlayerData.Init
            // above (not before it, like it used to run) - its Awake synchronously logs
            // notification_permission/notification_tap (see LocalNotificationController/
            // LocalNotificationAppStateWatcher), both of which read/write PlayerData.Instance.
            // Running before Init meant that touched a throwaway default PlayerData instance
            // instead of the real loaded save, silently discarding those counters the moment the
            // real Init call above replaced the runtime context.
            LocalNotificationAppStateWatcher.EnsureExists();

            CanvasGroup studioGroup = GetOrAddCanvasGroup(studioLoadingImageRoot);
            CanvasGroup splashGroup = GetOrAddCanvasGroup(gameSplashImageRoot);

            // Studio loading screen is retired - never shown - but the game splash screen
            // stays up for the same total duration the two screens used to occupy between them
            // (studioLoadingSeconds + fadeSeconds crossfade + gameSplashSeconds), so overall
            // boot timing before the scene load kicks off is unchanged.
            SetActive(studioLoadingImageRoot, false);
            SetAlpha(studioGroup, 0f);
            SetActive(gameSplashImageRoot, true);
            SetAlpha(splashGroup, 1f);
            yield return new WaitForSeconds(Mathf.Max(0f, studioLoadingSeconds + fadeSeconds + gameSplashSeconds));

            Transform splashRootParent = gameSplashImageRoot != null ? gameSplashImageRoot.transform.root : null;
            if (splashRootParent != null)
            {
                DontDestroyOnLoad(splashRootParent.gameObject);
            }

            DontDestroyOnLoad(gameObject);

            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(nextSceneName);
            while (loadOperation != null && !loadOperation.isDone)
            {
                yield return null;
            }

            // Deliberately after the scene load, not in AdsManager.Awake - that runs the instant
            // BootScene's objects are created, before any of this coroutine's own setup work.
            // Coredian's Core.GetService<IAdvertisementsService>() call (which triggers the whole
            // ad SDK's own Core.Initialize()) is heavy enough that it's better done once BootScene
            // is fully finished with its own job, not competing with it.
            AdsManager.Instance.InitializeAdvertising();

            // Same reasoning as AdsManager.InitializeAdvertising above - Core.GetService<IFirebaseService>()
            // is subject to the same Core.Initialize() timing concern.
            AnalyticsManager.Instance.InitializeAnalytics();

            // Same reasoning again - Core.GetService<IInAppPurchasesService>() is subject to the
            // same Core.Initialize() timing concern. No-op in the Editor (see InAppManager).
            InAppManager.Instance.InitializeInApp();

            // Same reasoning again - Core.GetService<IFirebaseRemoteConfigService>() is subject to
            // the same Core.Initialize() timing concern. Applies safe local defaults synchronously
            // before attempting any Firebase call, so AdsConfigController/EconomyConfigController
            // are always usable immediately even if Remote Config never answers.
            FirebaseRemoteConfigManager.Instance.Initialize();

            // No LeaderboardManager.Initialize() call here any more. It lives on the leaderboard
            // prefab in the gameplay scene, which BootScene cannot hold a serialized reference to,
            // and it no longer has a singleton to reach through. It initializes itself lazily on
            // first use instead - by which point PlayerData.Init and the GlobalConfig load above
            // have both long since run, which is the ordering that call was here to guarantee.

            // Deliberately here too, not from LocalNotificationAppStateWatcher.Awake (which runs
            // far earlier, before PlayerData.Init above) - notification_permission/
            // notification_tap must log after both PlayerData is ready and gameplay has actually
            // been entered, same as every other analytics event.
            LocalNotificationController.MarkReadyAndFlushPendingPermission();
            LocalNotificationAppStateWatcher.ReportNotificationTapIfLaunchedFromOne();

            yield return Fade(splashGroup, 1f, 0f);
            SetActive(gameSplashImageRoot, false);

            if (splashRootParent != null)
            {
                Destroy(splashRootParent.gameObject);
            }

            Destroy(gameObject);
        }

        private CanvasGroup GetOrAddCanvasGroup(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private IEnumerator Fade(CanvasGroup group, float from, float to)
        {
            float duration = Mathf.Max(0f, fadeSeconds);
            if (duration <= 0f)
            {
                SetAlpha(group, to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(group, Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetAlpha(group, to);
        }

        private static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        private SharedGlobalConfig LoadGlobalConfig()
        {
            if (globalConfig == null)
            {
                return new SharedGlobalConfig().CloneNormalized();
            }

            return globalConfig.ToConfig();
        }

        private static void SetActive(GameObject root, bool active)
        {
            if (root != null)
            {
                root.SetActive(active);
            }
        }
    }
}
