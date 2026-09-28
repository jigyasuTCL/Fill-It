using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.SpinWheel.Editor
{
    /// <summary>
    /// Editor tool for authoring Spin_Wheel_Config.json — the file
    /// SpinWheelManager.ApplyConfig() loads via Resources.Load&lt;TextAsset&gt;
    /// ("Spin_Wheel_Config") when you're not wiring live Balancy data.
    ///
    /// IMPORTANT: like SpinWheelManagerEditor, this file must live inside a
    /// folder literally named "Editor" (e.g. Assets/Scripts/SpinWheel/Editor/)
    /// so Unity strips it out of player builds.
    ///
    /// Open via Tools > Spin Wheel > Config Editor.
    /// </summary>
    public class SpinWheelConfigEditorWindow : EditorWindow
    {
        private const string ResourceFileName = "Spin_Wheel_Config";
        private const string DefaultRelativePath = "Assets/Resources/SpinTheWheel/Spin_Wheel_Config.json";

        private SpinWheelRemoteConfig _config;
        private string _currentPath;
        private Vector2 _scroll;
        private bool _stagesFoldout = true;
        private bool _tablesFoldout = true;

        [MenuItem("Tools/Spin Wheel/Config Editor")]
        public static void Open()
        {
            var window = GetWindow<SpinWheelConfigEditorWindow>("Spin Wheel Config");
            window.minSize = new Vector2(520, 480);
            window.LoadFromDisk(FindExistingConfigPath() ?? DefaultRelativePath);
        }

        private void OnEnable()
        {
            if (_config == null)
                LoadFromDisk(FindExistingConfigPath() ?? DefaultRelativePath);
        }

        // -----------------------------------------------------------
        // Load / Save
        // -----------------------------------------------------------

        private static string FindExistingConfigPath()
        {
            // Search the whole project for the file rather than assuming
            // Assets/Resources — in case it lives in a different Resources
            // folder (e.g. inside a package or a per-feature folder).
            string[] guids = AssetDatabase.FindAssets(ResourceFileName + " t:TextAsset");
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(assetPath) == ResourceFileName)
                    return assetPath;
            }
            return null;
        }

        private void LoadFromDisk(string path)
        {
            _currentPath = path;

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    _config = JsonUtility.FromJson<SpinWheelRemoteConfig>(json) ?? CreateDefaultConfig();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SpinWheelConfigEditor] Failed to parse '{path}': {e.Message}");
                    _config = CreateDefaultConfig();
                }
            }
            else
            {
                _config = CreateDefaultConfig();
            }

            NormalizeSpinIndices();
        }

        private void SaveToDisk()
        {
            if (_config.Stages == null || _config.Stages.Count == 0)
            {
                EditorUtility.DisplayDialog("Spin Wheel Config", "Add at least one stage before saving.", "OK");
                return;
            }

            if (_config.RewardTables == null || _config.RewardTables.Count == 0)
            {
                EditorUtility.DisplayDialog("Spin Wheel Config", "Add at least one reward table before saving.", "OK");
                return;
            }

            if (!IsUnderResourcesFolder(_currentPath))
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Spin Wheel Config",
                    $"'{_currentPath}' is not inside a 'Resources' folder.\n\n" +
                    "SpinWheelManager.ApplyConfig() loads this file via Resources.Load, " +
                    "so it must live under a folder named exactly 'Resources' to be found at runtime.\n\n" +
                    "Save here anyway?",
                    "Save Anyway", "Cancel");
                if (!proceed) return;
            }

            NormalizeSpinIndices();

            string directory = Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(_config, true);
            File.WriteAllText(_currentPath, json);
            AssetDatabase.Refresh();

            Debug.Log($"[SpinWheelConfigEditor] Saved config to '{_currentPath}'.");
        }

        private void SaveAs()
        {
            string absoluteDefault = Path.Combine(Application.dataPath, "Resources");
            string path = EditorUtility.SaveFilePanel("Save Spin Wheel Config", absoluteDefault, ResourceFileName, "json");
            if (string.IsNullOrEmpty(path)) return;

            // Convert absolute path back to a project-relative "Assets/..." path.
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            _currentPath = path;
            SaveToDisk();
        }

        private static bool IsUnderResourcesFolder(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Resources/") || normalized.StartsWith("Resources/");
        }

        private SpinWheelRemoteConfig CreateDefaultConfig()
        {
            return new SpinWheelRemoteConfig
            {
                //UnlockAtRegularLevel = 10,
                Stages = new List<SpinStageConfig>
                {
                    new SpinStageConfig { SpinIndex = 1, WinsRequired = 0, IsFree = true, IsCapStage = false }
                },
                RewardTables = new List<RewardTable>
                {
                    new RewardTable { TableId = "Table1", Rewards = new List<RewardConfig>() }
                }
            };
        }

        /// <summary>
        /// SpinIndex is purely a readability label — SpinWheelRemoteConfig.
        /// GetStageForSpinIndex() actually indexes Stages by LIST POSITION,
        /// not by this field. Keeping it auto-synced to (position + 1) here
        /// stops the displayed number ever lying about which stage is really
        /// used for a given spin.
        /// </summary>
        private void NormalizeSpinIndices()
        {
            if (_config?.Stages == null) return;
            for (int i = 0; i < _config.Stages.Count; i++)
                _config.Stages[i].SpinIndex = i + 1;
        }

        // -----------------------------------------------------------
        // GUI
        // -----------------------------------------------------------

        private void OnGUI()
        {
            if (_config == null)
                LoadFromDisk(FindExistingConfigPath() ?? DefaultRelativePath);

            EditorGUILayout.LabelField("Spin Wheel Remote Config", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("File", _currentPath, EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawUnlockSection();
            EditorGUILayout.Space(12);
            DrawStagesSection();
            EditorGUILayout.Space(12);
            DrawRewardTablesSection();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            DrawFooterButtons();
        }

        private void DrawUnlockSection()
        {
            //EditorGUILayout.LabelField("Feature Unlock", EditorStyles.boldLabel);
            //_config.UnlockAtRegularLevel = EditorGUILayout.IntField(
            //    new GUIContent("Unlock At Regular Level", "The regular level that unlocks the wheel and grants the free first spin."),
            //    _config.UnlockAtRegularLevel);
        }

        private void DrawStagesSection()
        {
            _stagesFoldout = EditorGUILayout.Foldout(_stagesFoldout, $"Stages ({_config.Stages?.Count ?? 0})", true, EditorStyles.foldoutHeader);
            if (!_stagesFoldout) return;

            EditorGUILayout.HelpBox(
                "Stage N = the win requirement to unlock the Nth spin. Stage 1 is normally " +
                "WinsRequired 0 / IsFree true. Any spin beyond the last stage reuses that last " +
                "stage's WinsRequired forever — mark it IsCapStage for clarity (position in the " +
                "list is what actually drives this, not the SpinIndex number, which is just a label).",
                MessageType.Info);

            if (_config.Stages == null) _config.Stages = new List<SpinStageConfig>();

            for (int i = 0; i < _config.Stages.Count; i++)
            {
                var stage = _config.Stages[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.IntField("Spin Index", i + 1, GUILayout.Width(160));

                GUILayout.FlexibleSpace();
                DrawMoveRemoveButtons(_config.Stages, i);
                EditorGUILayout.EndHorizontal();

                stage.WinsRequired = EditorGUILayout.IntField("Wins Required", stage.WinsRequired);
                stage.IsFree = EditorGUILayout.Toggle("Is Free", stage.IsFree);
                stage.IsCapStage = EditorGUILayout.Toggle("Is Cap Stage", stage.IsCapStage);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Stage"))
            {
                int nextRequired = _config.Stages.Count > 0 ? _config.Stages[_config.Stages.Count - 1].WinsRequired : 0;
                _config.Stages.Add(new SpinStageConfig
                {
                    SpinIndex = _config.Stages.Count + 1,
                    WinsRequired = nextRequired,
                    IsFree = false,
                    IsCapStage = false
                });
            }
            if (GUILayout.Button("Mark Last As Cap Stage"))
            {
                for (int i = 0; i < _config.Stages.Count; i++)
                    _config.Stages[i].IsCapStage = (i == _config.Stages.Count - 1);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRewardTablesSection()
        {
            _tablesFoldout = EditorGUILayout.Foldout(_tablesFoldout, $"Reward Tables ({_config.RewardTables?.Count ?? 0})", true, EditorStyles.foldoutHeader);
            if (!_tablesFoldout) return;

            EditorGUILayout.HelpBox(
                "Tables play in order — table 0 first, then table 1, and so on — wrapping back " +
                "to table 0 once every table has been played through once. TableId is just a " +
                "readable label; the order in this list is what actually matters.",
                MessageType.Info);

            if (_config.RewardTables == null) _config.RewardTables = new List<RewardTable>();

            for (int t = 0; t < _config.RewardTables.Count; t++)
            {
                var table = _config.RewardTables[t];
                if (table.Rewards == null) table.Rewards = new List<RewardConfig>();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                table.TableId = EditorGUILayout.TextField($"Table {t}", table.TableId);
                DrawMoveRemoveButtons(_config.RewardTables, t);
                EditorGUILayout.EndHorizontal();

                float totalWeight = 0f;
                foreach (var r in table.Rewards) totalWeight += Mathf.Max(0f, r.Weight);

                EditorGUI.indentLevel++;
                for (int r = 0; r < table.Rewards.Count; r++)
                {
                    DrawRewardRow(table.Rewards, r, totalWeight);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                if (GUILayout.Button("+ Add Reward"))
                {
                    table.Rewards.Add(new RewardConfig
                    {
                        RewardId = $"{table.TableId}_reward_{table.Rewards.Count + 1}",
                        Type = RewardType.Coins,
                        Amount = 0,
                        Weight = 1f,
                        RewardIconKey = string.Empty
                    });
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }

            if (GUILayout.Button("+ Add Reward Table"))
            {
                _config.RewardTables.Add(new RewardTable
                {
                    TableId = $"Table{_config.RewardTables.Count + 1}",
                    Rewards = new List<RewardConfig>()
                });
            }
        }

        private void DrawRewardRow(List<RewardConfig> rewards, int index, float totalWeight)
        {
            var reward = rewards[index];

            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            reward.RewardId = EditorGUILayout.TextField("Reward Id", reward.RewardId);
            DrawMoveRemoveButtons(rewards, index);
            EditorGUILayout.EndHorizontal();

            reward.Type = (RewardType)EditorGUILayout.EnumPopup("Type", reward.Type);
            reward.Amount = EditorGUILayout.IntField("Amount", reward.Amount);

            if (reward.Type == RewardType.Booster)
                reward.BoosterId = EditorGUILayout.TextField("Booster Id", reward.BoosterId);

            reward.RewardIconKey = EditorGUILayout.TextField(
                new GUIContent("Reward Icon Key", "Key into IconDatabaseConfig for this reward's image. Leave blank to fall back to Reward Id."),
                reward.RewardIconKey);

            reward.Weight = EditorGUILayout.FloatField(
                new GUIContent("Weight", "Relative weight only — currently unused by RewardSelector's " +
                                          "positional playback, but kept for a future weighted-random mode."),
                reward.Weight);

            if (totalWeight > 0f)
            {
                float pct = Mathf.Max(0f, reward.Weight) / totalWeight * 100f;
                EditorGUILayout.LabelField(" ", $"{pct:F1}% of this table's weight", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Small ^ / v / x button cluster shared by every reorderable row.</summary>
        private void DrawMoveRemoveButtons<T>(List<T> list, int index)
        {
            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("↑", GUILayout.Width(24)))
                    (list[index - 1], list[index]) = (list[index], list[index - 1]);
            }
            using (new EditorGUI.DisabledScope(index == list.Count - 1))
            {
                if (GUILayout.Button("↓", GUILayout.Width(24)))
                    (list[index + 1], list[index]) = (list[index], list[index + 1]);
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
                list.RemoveAt(index);
        }

        private void DrawFooterButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reload From Disk", GUILayout.Height(28)))
                LoadFromDisk(_currentPath);

            if (GUILayout.Button("Save", GUILayout.Height(28)))
                SaveToDisk();

            if (GUILayout.Button("Save As...", GUILayout.Height(28)))
                SaveAs();

            EditorGUILayout.EndHorizontal();
        }
    }
}