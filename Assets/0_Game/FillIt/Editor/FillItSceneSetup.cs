using System.Collections.Generic;
using FillIt.UnityAdapters;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FillIt.EditorTools
{
    /// <summary>
    /// One-shot (re-runnable) wiring of the GamePlay scene for Fill It: board view settings, the
    /// number pad, booster bar (Notes off, Fix on), Fillomino rule chips, the hint card's number,
    /// and the FTUE guided moves. Safe to run again - it reuses what it created last time.
    /// </summary>
    public static class FillItSceneSetup
    {
        private const string ScenePath = "Assets/0_Game/CatFlap/Scenes/GamePlay.unity";
        private const string TileSpritePath = "Assets/0_Game/CatFlap/Art/Sprites/Tile_Base.png";
        private const string BlackFontPath = "Assets/Shared/Features/GameFlow/Art/Fonts/Nunito-Black SDF.asset";
        private const string CanvasName = "CatFlapCanvas";

        // FTUE guided moves (row-major cell indices; one cell = a tap) - see FillItFtueDriver.
        private static readonly Dictionary<int, int[][]> FtueMoves = new Dictionary<int, int[][]>
        {
            { 1, new[] { new[] { 0 }, new[] { 8 } } },
            { 2, new[] { new[] { 1, 2, 5 } } },
            { 3, new[] { new[] { 0, 1, 2, 5 } } },
            { 4, new[] { new[] { 0, 1 }, new[] { 3, 7, 11, 15 }, new[] { 6, 10 }, new[] { 4, 8 }, new[] { 9, 13 }, new[] { 14 } } },
        };

        [MenuItem("MangraGames/Fill It/Setup GamePlay Scene")]
        public static void SetupFromMenu()
        {
            Debug.Log(Setup());
        }

        public static string Setup()
        {
            var log = new System.Text.StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            FillItCellView cellPrefab = AssetDatabase.LoadAssetAtPath<FillItCellView>(FillItPrefabBuilder.CellPrefabPath) ?? FillItPrefabBuilder.BuildCellPrefab();
            Sprite tile = LoadSprite(TileSpritePath);
            TMP_FontAsset blackFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BlackFontPath);

            GameObject canvas = GameObject.Find(CanvasName);
            Transform gameplayRoot = canvas.transform.Find("GamePlayUIRoot");
            var manager = Object.FindFirstObjectByType<FillItGameplayManager>(FindObjectsInactive.Include);
            var board = Object.FindFirstObjectByType<FillItBoardView>(FindObjectsInactive.Include);

            SetupBoard(board, cellPrefab, log);
            FillItNumberPadView pad = SetupNumberPad(gameplayRoot, tile, blackFont, cellPrefab, log);
            SetupBoosters(gameplayRoot, log);
            SetupRules(gameplayRoot, tile, blackFont, cellPrefab, log);
            SetupHintBanner(gameplayRoot, blackFont, log);
            SetupFtueUi(gameplayRoot, log);
            SetupTimerHud(gameplayRoot, blackFont, log);
            SetupManager(manager, pad, board, log);

            // GamePlayUIRoot/BoardRoot are prefab instances - plain property writes above are only
            // kept once recorded as instance overrides.
            RecordPrefabOverrides(gameplayRoot);
            RecordPrefabOverrides(board.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("Scene saved.");
            return log.ToString();
        }

        private static void SetupBoard(FillItBoardView board, FillItCellView cellPrefab, System.Text.StringBuilder log)
        {
            var so = new SerializedObject(board);
            so.FindProperty("cellPrefab").objectReferenceValue = cellPrefab;
            so.FindProperty("inputCamera").objectReferenceValue = Camera.main;
            so.FindProperty("baseTransform").objectReferenceValue = board.transform.Find("Base");
            so.FindProperty("cellContainer").objectReferenceValue = board.transform.Find("CellContainer");
            so.FindProperty("boardWorldWidth").floatValue = 5.5f;
            so.FindProperty("boardCenterYOffset").floatValue = 0.7f;
            so.FindProperty("basePadding").floatValue = 0.35f;
            so.FindProperty("cellFillPercent").floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Board backdrop below every cell (cells use orders -6..-1; the UI canvas is at 2).
            SpriteRenderer backdrop = board.transform.Find("Base").GetComponent<SpriteRenderer>();
            backdrop.sortingOrder = -10;

            Transform container = board.transform.Find("CellContainer");
            for (int index = container.childCount - 1; index >= 0; index--)
            {
                Object.DestroyImmediate(container.GetChild(index).gameObject);
            }

            log.AppendLine("Board wired (cell prefab, 5.5 world width).");
        }

        private static FillItNumberPadView SetupNumberPad(Transform gameplayRoot, Sprite tile, TMP_FontAsset font, FillItCellView cellPrefab, System.Text.StringBuilder log)
        {
            Transform existing = gameplayRoot.Find("NumberPadRoot");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("NumberPadRoot", typeof(RectTransform), typeof(CanvasGroup), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)root.transform;
            rect.SetParent(gameplayRoot, false);
            Transform boosters = gameplayRoot.Find("BoosterPanelRoot");
            if (boosters != null)
            {
                rect.SetSiblingIndex(boosters.GetSiblingIndex());
            }

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.16f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 150f);
            rect.sizeDelta = new Vector2(1010f, 100f);
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var buttons = new Button[9];
            for (int value = 1; value <= 9; value++)
            {
                buttons[value - 1] = CreatePadButton(rect, $"Key{value}", value.ToString(), 58f, tile, font, cellPrefab.GetNumberColor(value));
            }

            Button erase = CreatePadButton(rect, "KeyErase", "DEL", 34f, tile, font, new Color32(0xE9, 0xE4, 0xE0, 0xFF));

            var pad = root.AddComponent<FillItNumberPadView>();
            var so = new SerializedObject(pad);
            SerializedProperty array = so.FindProperty("numberButtons");
            array.arraySize = 9;
            for (int index = 0; index < 9; index++)
            {
                array.GetArrayElementAtIndex(index).objectReferenceValue = buttons[index];
            }

            so.FindProperty("eraseButton").objectReferenceValue = erase;
            so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("colorSource").objectReferenceValue = cellPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("Number pad created (1-9 + DEL).");
            return pad;
        }

        private static Button CreatePadButton(RectTransform parent, string name, string text, float fontSize, Sprite tile, TMP_FontAsset font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = tile;
            image.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            System.Type feedback = System.Type.GetType("Shared.GameFlow.ButtonPressScaleFeedback, Assembly-CSharp") ?? System.Type.GetType("ButtonPressScaleFeedback, Assembly-CSharp");
            if (feedback != null)
            {
                go.AddComponent(feedback);
            }

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = text;
            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(0x2B, 0x33, 0x5E, 0xFF);
            label.raycastTarget = false;
            return button;
        }

        private static void SetupBoosters(Transform gameplayRoot, System.Text.StringBuilder log)
        {
            Transform boosters = gameplayRoot.Find("BoosterPanelRoot");
            SetActive(boosters, "NotesButton", false);
            SetActive(boosters, "ApplyAllNotesButton", false);
            Transform fix = boosters.Find("FixButton");
            Transform notes = boosters.Find("NotesButton");
            if (fix != null && notes != null)
            {
                var fixRect = (RectTransform)fix;
                var notesRect = (RectTransform)notes;
                fixRect.anchorMin = notesRect.anchorMin;
                fixRect.anchorMax = notesRect.anchorMax;
                fixRect.pivot = notesRect.pivot;
                fixRect.anchoredPosition = notesRect.anchoredPosition;
                fixRect.sizeDelta = notesRect.sizeDelta;
                fix.gameObject.SetActive(true);
            }

            log.AppendLine("Boosters: Notes hidden, Fix shown in its slot.");
        }

        private static void SetupRules(Transform gameplayRoot, Sprite tile, TMP_FontAsset font, FillItCellView cellPrefab, System.Text.StringBuilder log)
        {
            Transform rules = gameplayRoot.Find("RulesPanelRoot");
            Transform tooBig = rules.Find("EqualCountsRule");
            Transform tooSmall = rules.Find("MaxTwoAdjacentRule");
            // Main rule first.
            tooBig.SetSiblingIndex(0);
            DecorateRule(tooBig, "Region size = its number", new[] { 3, 3, 3 }, tile, font, cellPrefab);
            DecorateRule(tooSmall, "Leave room to grow", new[] { 1, 2, 2 }, tile, font, cellPrefab);
            SetActive(rules, "UniqueLinesRule", false);
            log.AppendLine("Rule chips relabelled for Fillomino.");
        }

        private static void DecorateRule(Transform rule, string title, int[] example, Sprite tile, TMP_FontAsset font, FillItCellView cellPrefab)
        {
            Transform icon = rule.Find("RuleIcon");
            var iconImage = icon.GetComponent<Image>();
            iconImage.enabled = false;
            Transform old = icon.Find("Example");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var exampleRoot = new GameObject("Example", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)exampleRoot.transform;
            rect.SetParent(icon, false);
            rect.anchoredPosition = new Vector2(0f, 26f);
            rect.sizeDelta = new Vector2(210f, 66f);
            var layout = exampleRoot.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            foreach (int value in example)
            {
                var cell = new GameObject("Cell" + value, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                cell.transform.SetParent(rect, false);
                var element = cell.GetComponent<LayoutElement>();
                element.preferredWidth = element.preferredHeight = 64f;
                var image = cell.GetComponent<Image>();
                image.sprite = tile;
                image.color = cellPrefab.GetNumberColor(value);
                image.raycastTarget = false;
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.SetParent(cell.transform, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                var label = labelGo.GetComponent<TextMeshProUGUI>();
                label.text = value.ToString();
                if (font != null)
                {
                    label.font = font;
                }

                label.fontSize = 40f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color32(0x2B, 0x33, 0x5E, 0xFF);
                label.raycastTarget = false;
            }

            // The "rule broken" highlight: a plain red tile instead of the old Cat/Fish artwork.
            Transform outline = rule.Find("Outline");
            if (outline != null)
            {
                var outlineImage = outline.GetComponent<Image>();
                outlineImage.sprite = tile;
                outlineImage.type = Image.Type.Simple;
                outlineImage.color = new Color32(0xFF, 0x6B, 0x6B, 0xE6);
                ((RectTransform)outline).sizeDelta = new Vector2(300f, 180f);
            }

            var titleLabel = rule.Find("TitleLabel").GetComponent<TextMeshProUGUI>();
            titleLabel.text = title;
            titleLabel.enableAutoSizing = true;
            titleLabel.fontSizeMin = 18f;
            titleLabel.fontSizeMax = 34f;
        }

        private static void SetupHintBanner(Transform gameplayRoot, TMP_FontAsset font, System.Text.StringBuilder log)
        {
            Transform bannerRoot = gameplayRoot.Find("HintBannerRoot");
            var banner = bannerRoot.GetComponent<FillItHintBannerController>();
            Transform icon = bannerRoot.Find("Content/IconImage");
            Transform existing = icon.Find("Value");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var labelGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(icon, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = 130f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(0xFF, 0x8A, 0x2A, 0xFF);
            label.raycastTarget = false;
            label.text = "3";
            icon.GetComponent<Image>().enabled = false;

            var so = new SerializedObject(banner);
            so.FindProperty("valueLabel").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("Hint card shows the revealed number.");
        }

        // Timer gameplay mode countdown: a pill in the hearts' spot (same background art), hidden
        // until FillItStatusViewAdapter.SetTimer shows it.
        private static void SetupTimerHud(Transform gameplayRoot, TMP_FontAsset font, System.Text.StringBuilder log)
        {
            Transform center = gameplayRoot.Find("TopHUDRoot/LevelCenterRoot");
            var hearts = center.Find("LevelAttemptCount") as RectTransform;
            Transform existing = center.Find("TimerHud");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("TimerHud", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.SetParent(center, false);
            rect.anchorMin = hearts.anchorMin;
            rect.anchorMax = hearts.anchorMax;
            rect.pivot = hearts.pivot;
            rect.anchoredPosition = hearts.anchoredPosition;
            rect.sizeDelta = new Vector2(Mathf.Max(220f, hearts.sizeDelta.x * 0.85f), hearts.sizeDelta.y * 0.6f);
            var background = root.GetComponent<Image>();
            var heartsImage = hearts.GetComponent<Image>();
            background.sprite = heartsImage.sprite;
            background.type = heartsImage.type;
            background.color = heartsImage.color;
            background.raycastTarget = false;

            var labelGo = new GameObject("Countdown", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = "01:00";
            label.enableAutoSizing = true;
            label.fontSizeMin = 30f;
            label.fontSizeMax = 64f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(0x2B, 0x33, 0x5E, 0xFF);
            label.raycastTarget = false;
            root.SetActive(false);

            var status = gameplayRoot.GetComponentInChildren<FillItStatusViewAdapter>(true);
            var so = new SerializedObject(status);
            so.FindProperty("timerHudRoot").objectReferenceValue = root;
            so.FindProperty("timerCountdownLabel").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("Timer HUD created in the hearts' spot.");
        }

        // Skip Tutorial sits above the board's top-right corner; the tutorial text auto-fits its card.
        private static void SetupFtueUi(Transform gameplayRoot, System.Text.StringBuilder log)
        {
            var skip = gameplayRoot.Find("SkipTutorialBtn") as RectTransform;
            if (skip != null)
            {
                skip.anchorMin = skip.anchorMax = new Vector2(1f, 0.71f);
                skip.anchoredPosition = new Vector2(-193.2f, 190f);
            }

            Transform body = gameplayRoot.Find("FtueBannerRoot/FtueBodyLabel");
            if (body != null)
            {
                var label = body.GetComponent<TextMeshProUGUI>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 28f;
                label.fontSizeMax = Mathf.Max(label.fontSize, 52f);
            }

            log.AppendLine("FTUE: skip button moved above the board, banner text auto-sizes.");
        }

        private static void SetupManager(FillItGameplayManager manager, FillItNumberPadView pad, FillItBoardView board, System.Text.StringBuilder log)
        {
            var so = new SerializedObject(manager);
            so.FindProperty("numberPad").objectReferenceValue = pad;
            so.FindProperty("boardView").objectReferenceValue = board;
            SerializedProperty entries = so.FindProperty("ftueLevelSteps");
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                int level = entry.FindPropertyRelative("LevelNumber").intValue;
                SerializedProperty moves = entry.FindPropertyRelative("Moves");
                if (!FtueMoves.TryGetValue(level, out int[][] paths))
                {
                    moves.arraySize = 0;
                    continue;
                }

                moves.arraySize = paths.Length;
                for (int move = 0; move < paths.Length; move++)
                {
                    SerializedProperty path = moves.GetArrayElementAtIndex(move).FindPropertyRelative("Path");
                    path.arraySize = paths[move].Length;
                    for (int cell = 0; cell < paths[move].Length; cell++)
                    {
                        path.GetArrayElementAtIndex(cell).intValue = paths[move][cell];
                    }
                }

                log.AppendLine($"FTUE level {level}: {paths.Length} guided moves.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RecordPrefabOverrides(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                {
                    continue;
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                foreach (Component component in child.GetComponents<Component>())
                {
                    if (component != null)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }
                }
            }
        }

        private static void SetActive(Transform parent, string childName, bool active)
        {
            Transform child = parent != null ? parent.Find(childName) : null;
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }

        private static Sprite LoadSprite(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite)
                {
                    return sprite;
                }
            }

            return null;
        }
    }
}
