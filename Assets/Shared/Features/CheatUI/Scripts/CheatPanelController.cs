using System.Collections;
using TMPro;
using Shared.Boosters;
using Shared.PlayerData;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Shared.CheatUI
{
    public sealed class CheatPanelController : MonoBehaviour
    {
        [SerializeField] private Button revealButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        // Gems -> Coins rename - FormerlySerializedAs preserves existing Inspector wiring.
        [FormerlySerializedAs("addGemsButton")]
        [SerializeField] private Button addCoinsButton;
        [SerializeField] private Button winLevelButton;
        [SerializeField] private Button failLevelButton;
        [SerializeField] private Button testPurchaseSuccessButton;
        [SerializeField] private Button testPurchaseFailedButton;
        [SerializeField] private Button addHintButton;
        [SerializeField] private Button addFixButton;
        [SerializeField] private Button addUndoButton;
        [SerializeField] private Button jumpLevelButton;
        [SerializeField] private Button applyWrongValidationDelayButton;
        [SerializeField] private Button applyCorrectValidationDelayButton;
        [SerializeField] private Button openTesterGuiButton;
        [FormerlySerializedAs("gemsInput")]
        [SerializeField] private TMP_InputField coinsInput;
        [SerializeField] private TMP_InputField boosterInput;
        [SerializeField] private TMP_InputField levelInput;
        [SerializeField] private TMP_InputField wrongValidationDelayInput;
        [SerializeField] private TMP_InputField correctValidationDelayInput;
        [SerializeField] private TMP_Text loopLevelReadoutText;
        [SerializeField] private string playerDataFileName = PlayerDataDefaults.FileName;
        [SerializeField] private string currentLevelIndexKey = "FillIt.CurrentLevelIndex";
        [SerializeField] private string boosterCountKeyPrefix = PlayerDataDefaults.BoosterCountKeyPrefix;

        private bool gameplayInputWasEnabled;
        private bool gameplayInputCaptured;
        private Coroutine releaseGameplayInputRoutine;
        private readonly PlayerDataSaveSystem playerDataSaveSystem = new PlayerDataSaveSystem();

        public CheatEvent PlayerDataChanged = new CheatEvent();
        public CheatEvent WinLevelRequested = new CheatEvent();
        public CheatEvent FailLevelRequested = new CheatEvent();
        public CheatEvent TestPurchaseSuccessRequested = new CheatEvent();
        public CheatEvent TestPurchaseFailedRequested = new CheatEvent();
        public CheatFloatEvent WrongValidationDelayChanged = new CheatFloatEvent();
        public CheatFloatEvent CorrectValidationDelayChanged = new CheatFloatEvent();

        private void Awake()
        {
            EnsureValidationDelayControls();
            EnsureOpenTesterGuiButton();
            EnsureLoopLevelReadoutText();
            SetPanelVisible(false);
        }

        private void OnEnable()
        {
            AddListeners();
        }

        private void OnDisable()
        {
            RemoveListeners();
            ReleaseGameplayInput();
        }

        private void AddListeners()
        {
            EnsureValidationDelayControls();

            revealButton?.onClick.AddListener(TrackRevealTap);
            closeButton?.onClick.AddListener(ClosePanel);
            addCoinsButton?.onClick.AddListener(RequestAddCoins);
            winLevelButton?.onClick.AddListener(RequestWinLevel);
            failLevelButton?.onClick.AddListener(RequestFailLevel);
            testPurchaseSuccessButton?.onClick.AddListener(RequestTestPurchaseSuccess);
            testPurchaseFailedButton?.onClick.AddListener(RequestTestPurchaseFailed);
            addHintButton?.onClick.AddListener(RequestAddHint);
            addFixButton?.onClick.AddListener(RequestAddFix);
            addUndoButton?.onClick.AddListener(RequestAddUndo);
            jumpLevelButton?.onClick.AddListener(RequestJumpLevel);
            applyWrongValidationDelayButton?.onClick.AddListener(RequestWrongValidationDelay);
            applyCorrectValidationDelayButton?.onClick.AddListener(RequestCorrectValidationDelay);
            openTesterGuiButton?.onClick.AddListener(RequestOpenTesterGui);
        }

        private void RemoveListeners()
        {
            revealButton?.onClick.RemoveListener(TrackRevealTap);
            closeButton?.onClick.RemoveListener(ClosePanel);
            addCoinsButton?.onClick.RemoveListener(RequestAddCoins);
            winLevelButton?.onClick.RemoveListener(RequestWinLevel);
            failLevelButton?.onClick.RemoveListener(RequestFailLevel);
            testPurchaseSuccessButton?.onClick.RemoveListener(RequestTestPurchaseSuccess);
            testPurchaseFailedButton?.onClick.RemoveListener(RequestTestPurchaseFailed);
            addHintButton?.onClick.RemoveListener(RequestAddHint);
            addFixButton?.onClick.RemoveListener(RequestAddFix);
            addUndoButton?.onClick.RemoveListener(RequestAddUndo);
            jumpLevelButton?.onClick.RemoveListener(RequestJumpLevel);
            applyWrongValidationDelayButton?.onClick.RemoveListener(RequestWrongValidationDelay);
            applyCorrectValidationDelayButton?.onClick.RemoveListener(RequestCorrectValidationDelay);
            openTesterGuiButton?.onClick.RemoveListener(RequestOpenTesterGui);
        }

        private void TrackRevealTap()
        {
            SetPanelVisible(true);
        }

        private void ClosePanel()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ButtonBack);
            SetPanelVisible(false);
        }

        private void SetPanelVisible(bool visible)
        {
            if (visible)
            {
                StopReleaseGameplayInputRoutine();
                if (!gameplayInputCaptured)
                {
                    gameplayInputWasEnabled = Shared.GameFlow.InputManager.GamePlayInputEnabled;
                    gameplayInputCaptured = true;
                }

                Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            }
            else if (gameplayInputCaptured)
            {
                ReleaseGameplayInputAfterFrame();
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(visible);
            }
        }

        private void ReleaseGameplayInput()
        {
            StopReleaseGameplayInputRoutine();
            if (!gameplayInputCaptured)
            {
                return;
            }

            Shared.GameFlow.InputManager.GamePlayInputEnabled = gameplayInputWasEnabled;
            gameplayInputCaptured = false;
        }

        private void ReleaseGameplayInputAfterFrame()
        {
            StopReleaseGameplayInputRoutine();
            releaseGameplayInputRoutine = StartCoroutine(ReleaseGameplayInputNextFrame());
        }

        private IEnumerator ReleaseGameplayInputNextFrame()
        {
            yield return null;
            releaseGameplayInputRoutine = null;
            ReleaseGameplayInput();
        }

        private void StopReleaseGameplayInputRoutine()
        {
            if (releaseGameplayInputRoutine == null)
            {
                return;
            }

            StopCoroutine(releaseGameplayInputRoutine);
            releaseGameplayInputRoutine = null;
        }

        private void RequestAddCoins()
        {
            Shared.PlayerData.PlayerData data = LoadPlayerData();
            data.TotalCoins = Mathf.Max(0, data.TotalCoins + ReadInt(coinsInput, 100));
            SavePlayerData(data);
            PlayerDataChanged.Invoke();
            ClosePanel();
        }

        private void RequestWinLevel()
        {
            WinLevelRequested.Invoke();
            ClosePanel();
        }

        private void RequestFailLevel()
        {
            FailLevelRequested.Invoke();
            ClosePanel();
        }

        private void RequestTestPurchaseSuccess()
        {
            TestPurchaseSuccessRequested.Invoke();
            ClosePanel();
        }

        private void RequestTestPurchaseFailed()
        {
            TestPurchaseFailedRequested.Invoke();
            ClosePanel();
        }

        private void RequestAddHint()
        {
            AddBoosterCount(BoosterType.Hint, ReadPositiveInt(boosterInput, 1));
            ClosePanel();
        }

        private void RequestAddFix()
        {
            AddBoosterCount(BoosterType.Fix, ReadPositiveInt(boosterInput, 1));
            ClosePanel();
        }

        private void RequestAddUndo()
        {
            AddBoosterCount(BoosterType.Undo, ReadPositiveInt(boosterInput, 1));
            ClosePanel();
        }

        private void RequestJumpLevel()
        {
            Shared.PlayerData.PlayerData data = LoadPlayerData();
            int targetLevelIndex = ReadPositiveInt(levelInput, 1) - 1;
            data.CurrentLevelIndex = Mathf.Max(0, targetLevelIndex);
            data.SetInt(currentLevelIndexKey, data.CurrentLevelIndex);
            SavePlayerData(data);
            PlayerDataChanged.Invoke();
            ClosePanel();
        }

        /// <summary>
        /// The game pushes the save key it actually reads the level index from (GlobalConfig
        /// CurrentLevelIndexSaveKey), so Jump Level can't drift from it via a stale serialized value.
        /// </summary>
        public void SetCurrentLevelIndexKey(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                currentLevelIndexKey = key;
            }
        }

        public void SetValidationDelayValues(float wrongDelaySeconds, float correctDelaySeconds)
        {
            EnsureValidationDelayControls();
            if (wrongValidationDelayInput != null)
            {
                wrongValidationDelayInput.text = Mathf.Max(0f, wrongDelaySeconds).ToString("0.###");
            }

            if (correctValidationDelayInput != null)
            {
                correctValidationDelayInput.text = Mathf.Max(0f, correctDelaySeconds).ToString("0.###");
            }
        }

        // Shows which underlying level is actually loaded once loop-level content past
        // GlobalConfig.TotalLevels replays the curated level pool in a shuffled order (see
        // CatFlapGameplayManager.ResolveLoopLevelNumber) - e.g. displayedLevelNumber 101 might
        // actually have loaded level 47. Shows just the plain level number while the two match
        // (every level before the loop starts).
        public void SetLoopLevelReadout(int displayedLevelNumber, int actualLevelNumber)
        {
            EnsureLoopLevelReadoutText();
            if (loopLevelReadoutText == null)
            {
                return;
            }

            loopLevelReadoutText.text = displayedLevelNumber == actualLevelNumber
                ? $""//Level: {displayedLevelNumber}
                : $"loop Level {actualLevelNumber}";
        }

        private void RequestWrongValidationDelay()
        {
            WrongValidationDelayChanged.Invoke(ReadPositiveFloat(wrongValidationDelayInput, 0.75f));
        }

        private void RequestCorrectValidationDelay()
        {
            CorrectValidationDelayChanged.Invoke(ReadPositiveFloat(correctValidationDelayInput, 0.75f));
        }

        // Opens the BoomBit SDK's own Tester GUI (ConsentFoldout/FirebaseSDKFoldout/UsercentricsFoldout
        // are already enabled in Tester GUI Config Node.asset) - lets QA/dev inspect Firebase consent
        // state on device without needing the 4-corner tap gesture TesterGUILauncherService normally requires.
        // Under DEV_MODE this never touches Coredian.TesterGUI at all (not just skips calling into
        // it) - same reasoning as AdsManager/InAppManager/FirebaseAnalyticsHandler: some dev
        // machines have a broken/missing plugin setup that fails to even compile, not just throw at
        // runtime, so the plugin type can't appear anywhere in this method under DEV_MODE.
        private void RequestOpenTesterGui()
        {
#if !DEV_MODE
            Coredian.TesterGUI.ITesterGUIService service = Core.GetService<Coredian.TesterGUI.ITesterGUIService>();
            service?.Show();
#else
#endif
            ClosePanel();
        }

        private static int ReadInt(TMP_InputField input, int fallback)
        {
            if (input == null || !int.TryParse(input.text, out int value))
            {
                return fallback;
            }

            return value;
        }

        private static int ReadPositiveInt(TMP_InputField input, int fallback)
        {
            if (input == null || !int.TryParse(input.text, out int value))
            {
                return fallback;
            }

            return Mathf.Max(1, value);
        }

        private static float ReadPositiveFloat(TMP_InputField input, float fallback)
        {
            if (input == null || !float.TryParse(input.text, out float value))
            {
                return fallback;
            }

            return Mathf.Max(0f, value);
        }

        private void EnsureValidationDelayControls()
        {
            ResolveValidationDelayControls();

            if (panelRoot == null ||
                (wrongValidationDelayInput != null &&
                 correctValidationDelayInput != null &&
                 applyWrongValidationDelayButton != null &&
                 applyCorrectValidationDelayButton != null &&
                 applyWrongValidationDelayButton != applyCorrectValidationDelayButton))
            {
                return;
            }

            Transform parent = closeButton != null && closeButton.transform.parent != null
                ? closeButton.transform.parent
                : panelRoot.transform;
            if (wrongValidationDelayInput == null || applyWrongValidationDelayButton == null)
            {
                wrongValidationDelayInput = CreateDelayInputRow(parent, "WrongAnswerCheckDelay", "Wrong Delay", "0.75", out applyWrongValidationDelayButton);
            }

            if (correctValidationDelayInput == null || applyCorrectValidationDelayButton == null)
            {
                correctValidationDelayInput = CreateDelayInputRow(parent, "CorrectAnswerCheckDelay", "Correct Delay", "0.75", out applyCorrectValidationDelayButton);
            }
        }

        // Mirrors EnsureValidationDelayControls's find-or-create pattern - looks for a button
        // named "OpenTesterGuiButton" already placed in the prefab first, and only creates one at
        // runtime if none is found, so an Editor-authored button (once someone adds it to the
        // prefab) always takes precedence over this fallback.
        private void EnsureOpenTesterGuiButton()
        {
            if (openTesterGuiButton != null)
            {
                return;
            }

            Transform existing = FindChildByName("OpenTesterGuiButton");
            if (existing != null)
            {
                openTesterGuiButton = existing.GetComponent<Button>();
                if (openTesterGuiButton != null)
                {
                    return;
                }
            }

            if (panelRoot == null)
            {
                return;
            }

            Transform parent = closeButton != null && closeButton.transform.parent != null
                ? closeButton.transform.parent
                : panelRoot.transform;

            openTesterGuiButton = CreateButton(parent, "Open Tester GUI");
            openTesterGuiButton.name = "OpenTesterGuiButton";
            SetPreferredSize(openTesterGuiButton.gameObject, 160f, 36f);
        }

        // Mirrors EnsureOpenTesterGuiButton's find-or-create pattern - a "LoopLevelReadoutText"
        // added to the prefab by hand later always takes precedence over this runtime fallback.
        private void EnsureLoopLevelReadoutText()
        {
            if (loopLevelReadoutText != null)
            {
                return;
            }

            Transform existing = FindChildByName("LoopLevelReadoutText");
            if (existing != null)
            {
                loopLevelReadoutText = existing.GetComponent<TMP_Text>();
                if (loopLevelReadoutText != null)
                {
                    return;
                }
            }

            if (panelRoot == null)
            {
                return;
            }

            Transform parent = closeButton != null && closeButton.transform.parent != null
                ? closeButton.transform.parent
                : panelRoot.transform;

            loopLevelReadoutText = CreateText(parent, "Level: -");
            loopLevelReadoutText.name = "LoopLevelReadoutText";
            loopLevelReadoutText.color = Color.black;
            SetPreferredSize(loopLevelReadoutText.gameObject, 260f, 36f);
        }

        private Transform FindChildByName(string childName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                if (children[index].name == childName)
                {
                    return children[index];
                }
            }

            return null;
        }

        private void ResolveValidationDelayControls()
        {
            Transform wrongRow = FindDelayRow(
                "WrongAnswerCheckDelay",
                "Set Delay - Wrong answer check",
                "Wrong Delay");
            Transform correctRow = FindDelayRow(
                "CorrectAnswerCheckDelay",
                "Set Delay - Correct answer check",
                "Correct Delay");

            if (wrongRow != null)
            {
                wrongValidationDelayInput = wrongRow.GetComponentInChildren<TMP_InputField>(true);
                applyWrongValidationDelayButton = wrongRow.GetComponentInChildren<Button>(true);
            }

            if (correctRow != null)
            {
                correctValidationDelayInput = correctRow.GetComponentInChildren<TMP_InputField>(true);
                applyCorrectValidationDelayButton = correctRow.GetComponentInChildren<Button>(true);
            }
        }

        private Transform FindDelayRow(string rowName, params string[] expectedTexts)
        {
            Transform fallback = null;
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                Transform child = children[index];
                if (child.name != rowName)
                {
                    continue;
                }

                TMP_InputField input = child.GetComponentInChildren<TMP_InputField>(true);
                Button button = child.GetComponentInChildren<Button>(true);
                if (input == null || button == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = child;
                }
                if (ContainsExpectedText(child, expectedTexts))
                {
                    return child;
                }
            }

            return fallback;
        }

        private static bool ContainsExpectedText(Transform root, params string[] expectedTexts)
        {
            if (root == null || expectedTexts == null || expectedTexts.Length == 0)
            {
                return false;
            }

            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
            {
                string labelText = labels[labelIndex].text;
                if (string.IsNullOrWhiteSpace(labelText))
                {
                    continue;
                }

                for (int expectedIndex = 0; expectedIndex < expectedTexts.Length; expectedIndex++)
                {
                    if (labelText == expectedTexts[expectedIndex])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static TMP_InputField CreateDelayInputRow(Transform parent, string rowName, string labelText, string placeholderText, out Button applyButton)
        {
            GameObject row = new GameObject(rowName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 8f;

            TMP_Text label = CreateText(row.transform, labelText);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            SetPreferredSize(label.gameObject, 120f, 36f);

            TMP_InputField input = CreateInput(row.transform, placeholderText);
            SetPreferredSize(input.gameObject, 110f, 36f);

            applyButton = CreateButton(row.transform, "Apply");
            SetPreferredSize(applyButton.gameObject, 90f, 36f);
            return input;
        }

        private static TMP_Text CreateText(Transform parent, string value)
        {
            GameObject textObject = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = 22f;
            text.color = Color.black;
            return text;
        }

        private static TMP_InputField CreateInput(Transform parent, string placeholderText)
        {
            GameObject inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputObject.transform.SetParent(parent, false);
            inputObject.GetComponent<Image>().color = Color.white;

            TMP_Text text = CreateText(inputObject.transform, string.Empty);
            StretchToParent(text.rectTransform);
            text.margin = new Vector4(8f, 4f, 8f, 4f);
            TMP_Text placeholder = CreateText(inputObject.transform, placeholderText);
            StretchToParent(placeholder.rectTransform);
            placeholder.margin = new Vector4(8f, 4f, 8f, 4f);
            placeholder.color = new Color(0f, 0f, 0f, 0.45f);

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            return input;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.21f, 0.78f, 0.54f, 1f);
            TMP_Text text = CreateText(buttonObject.transform, label);
            StretchToParent(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            return buttonObject.GetComponent<Button>();
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void SetPreferredSize(GameObject target, float width, float height)
        {
            LayoutElement layoutElement = target.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = target.AddComponent<LayoutElement>();
            }

            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = height;
        }

        private void AddBoosterCount(BoosterType boosterType, int amount)
        {
            Shared.PlayerData.PlayerData data = LoadPlayerData();
            string key = CreateBoosterCountKey(boosterType);
            data.TryGetInt(key, out int currentCount);
            data.SetInt(key, Mathf.Max(0, currentCount + amount));
            SavePlayerData(data);
            PlayerDataChanged.Invoke();
        }

        private Shared.PlayerData.PlayerData LoadPlayerData()
        {
            if (!PlayerDataRuntimeContext.HasData)
            {
                Shared.PlayerData.PlayerData data = playerDataSaveSystem.LoadOrCreate(playerDataFileName, 0);
                Shared.PlayerData.PlayerData.Init(data, playerDataFileName);
            }

            return Shared.PlayerData.PlayerData.Instance;
        }

        private void SavePlayerData(Shared.PlayerData.PlayerData data)
        {
            string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
                ? playerDataFileName
                : PlayerDataRuntimeContext.FileName;
            playerDataSaveSystem.Save(fileName, data);
        }

        private string CreateBoosterCountKey(BoosterType boosterType)
        {
            return boosterCountKeyPrefix + boosterType;
        }
    }

    [System.Serializable]
    public sealed class CheatEvent : UnityEvent
    {
    }

    [System.Serializable]
    public sealed class CheatFloatEvent : UnityEvent<float>
    {
    }
}
