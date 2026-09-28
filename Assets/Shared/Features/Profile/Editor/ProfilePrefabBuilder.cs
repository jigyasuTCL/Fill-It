using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Profile.Editor
{
    /// <summary>
    /// Builds the two prefabs ProfilePanelController spawns at runtime - a category section and a
    /// single grid cell - with their layout components and script references already wired, so the
    /// only work left is dressing them with art.
    ///
    /// Generated rather than hand-authored because the wiring is what is easy to get wrong: the
    /// Content Size Fitters that let a grid report its height up through the section into the
    /// scroll content, and the sibling order that makes a frame draw AROUND its inner preview
    /// rather than behind it. Both are silent failures in the Inspector - a section that collapses
    /// to zero height, or a paw that vanishes under its own frame.
    ///
    /// Re-running SKIPS a prefab that already exists. These are meant to be dressed with art and
    /// kept; regenerating over that would throw the art away. Delete one to have it rebuilt.
    /// </summary>
    public static class ProfilePrefabBuilder
    {
        private const string PrefabFolder = "Assets/Shared/Features/Profile/Prefab";
        private const string SectionPrefabPath = PrefabFolder + "/ProfileSection.prefab";
        private const string ItemPrefabPath = PrefabFolder + "/ProfileItemCell.prefab";

        // Matches the Grid Layout Group already authored on the panel's scroll Content, which this
        // moves down into the section - the panel's own Content becomes a vertical stack instead.
        private static readonly Vector2 CellSize = new Vector2(170f, 170f);
        private static readonly Vector2 CellSpacing = new Vector2(25f, 25f);
        private const int GridColumns = 4;

        private const float SectionSpacing = 12f;
        private const float TitleHeight = 48f;

        [MenuItem("MangraGames/Build Profile Prefabs")]
        public static void Build()
        {
            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            BuildSectionPrefab();
            BuildItemPrefab();
            AssetDatabase.SaveAssets();
        }

        // =========================================================
        // SECTION
        // =========================================================

        private static void BuildSectionPrefab()
        {
            if (File.Exists(SectionPrefabPath))
            {
                Debug.Log($"ProfilePrefabBuilder: {SectionPrefabPath} already exists - left alone. Delete it to rebuild.");
                return;
            }

            GameObject root = CreateUIObject("ProfileSection", null);
            try
            {
                RectTransform rootRect = root.GetComponent<RectTransform>();
                // Top-stretch: a section is as wide as the scroll content and as tall as its own
                // contents make it.
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0.5f, 1f);

                VerticalLayoutGroup stack = root.AddComponent<VerticalLayoutGroup>();
                stack.childAlignment = TextAnchor.UpperCenter;
                stack.spacing = SectionSpacing;
                stack.childControlWidth = true;
                stack.childControlHeight = true;
                stack.childForceExpandWidth = true;
                // Force-expanding height would stretch the title and the grid to share the
                // section's height instead of each taking its own.
                stack.childForceExpandHeight = false;

                // Without this the section reports no height to the panel's own vertical stack and
                // every section lands on top of the last.
                ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                GameObject titleRoot = CreateUIObject("TitleRoot", root.transform);
                LayoutElement titleLayout = titleRoot.AddComponent<LayoutElement>();
                titleLayout.preferredHeight = TitleHeight;

                GameObject titleGO = CreateUIObject("Title", titleRoot.transform);
                Stretch(titleGO.GetComponent<RectTransform>());
                TextMeshProUGUI title = titleGO.AddComponent<TextMeshProUGUI>();
                title.text = "Classic";
                title.fontSize = 32f;
                title.alignment = TextAlignmentOptions.Center;
                title.color = new Color32(0x8A, 0x6B, 0x4F, 0xFF);
                title.raycastTarget = false;

                GameObject itemsRoot = CreateUIObject("ItemsRoot", root.transform);
                GridLayoutGroup grid = itemsRoot.AddComponent<GridLayoutGroup>();
                grid.cellSize = CellSize;
                grid.spacing = CellSpacing;
                grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = GridColumns;

                // Same reason as the section's own fitter: the grid has to report the height its
                // rows actually need, or the section collapses around it.
                ContentSizeFitter itemsFitter = itemsRoot.AddComponent<ContentSizeFitter>();
                itemsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                itemsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                ProfileCategorySectionView view = root.AddComponent<ProfileCategorySectionView>();
                SerializedObject serialized = new SerializedObject(view);
                serialized.FindProperty("titleLabel").objectReferenceValue = title;
                serialized.FindProperty("titleRoot").objectReferenceValue = titleRoot;
                serialized.FindProperty("itemsRoot").objectReferenceValue = itemsRoot.GetComponent<RectTransform>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, SectionPrefabPath, "Dress TitleRoot with the header strip art; the grid settings are already the panel's.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // =========================================================
        // ITEM CELL
        // =========================================================

        private static void BuildItemPrefab()
        {
            if (File.Exists(ItemPrefabPath))
            {
                Debug.Log($"ProfilePrefabBuilder: {ItemPrefabPath} already exists - left alone. Delete it to rebuild.");
                return;
            }

            GameObject root = CreateUIObject("ProfileItemCell", null);
            try
            {
                RectTransform rootRect = root.GetComponent<RectTransform>();
                // Size is driven by the section's Grid Layout Group, so this is only the authoring
                // size seen when the prefab is opened on its own.
                rootRect.sizeDelta = CellSize;

                // Invisible, but it is what the Button raycasts against - covering the whole cell
                // so the gaps between the art and the cell edge are still tappable. raycastTarget
                // is independent of alpha, so a fully transparent image still receives the tap.
                Image hitArea = root.AddComponent<Image>();
                hitArea.color = new Color(1f, 1f, 1f, 0f);
                hitArea.raycastTarget = true;

                Button button = root.AddComponent<Button>();
                button.targetGraphic = hitArea;
                // The target graphic is invisible, so the default colour tint would do nothing
                // visible anyway. Swap to SpriteSwap/Animation once the art exists.
                button.transition = Selectable.Transition.None;

                // Art container, NOT the cell root: the CanvasGroup that dims a locked item lives
                // here so the padlock and its label above stay at full opacity over dimmed art.
                GameObject artRoot = CreateUIObject("Art", root.transform);
                Stretch(artRoot.GetComponent<RectTransform>());
                CanvasGroup artCanvasGroup = artRoot.AddComponent<CanvasGroup>();

                // FIRST child = drawn first = behind. A frame has to draw its border AROUND this,
                // so the inner preview must precede the art image; reordering these hides the paw.
                GameObject innerGO = CreateUIObject("InnerPreview", artRoot.transform);
                RectTransform innerRect = innerGO.GetComponent<RectTransform>();
                innerRect.anchorMin = new Vector2(0.5f, 0.5f);
                innerRect.anchorMax = new Vector2(0.5f, 0.5f);
                innerRect.sizeDelta = CellSize * 0.55f;
                Image innerImage = innerGO.AddComponent<Image>();
                innerImage.raycastTarget = false;
                // Starts hidden - the panel enables it only for frame cells, which are the only
                // ones given something to frame.
                innerImage.enabled = false;

                GameObject artGO = CreateUIObject("Border", artRoot.transform);
                Stretch(artGO.GetComponent<RectTransform>());
                Image artImage = artGO.AddComponent<Image>();
                artImage.raycastTarget = false;
                artImage.preserveAspect = true;

                GameObject nameGO = CreateUIObject("Name", root.transform);
                RectTransform nameRect = nameGO.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 0f);
                nameRect.anchorMax = new Vector2(1f, 0f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.sizeDelta = new Vector2(0f, 28f);
                TextMeshProUGUI nameLabel = nameGO.AddComponent<TextMeshProUGUI>();
                nameLabel.fontSize = 20f;
                nameLabel.alignment = TextAlignmentOptions.Center;
                nameLabel.raycastTarget = false;
                // Optional on the cell - delete this object if the design shows art only, and the
                // view simply skips it.
                nameGO.SetActive(false);

                GameObject selectedRoot = CreateUIObject("SelectedRoot", root.transform);
                Stretch(selectedRoot.GetComponent<RectTransform>());
                GameObject tickGO = CreateUIObject("Tick", selectedRoot.transform);
                RectTransform tickRect = tickGO.GetComponent<RectTransform>();
                // Bottom-right corner, like the green check in the design.
                tickRect.anchorMin = new Vector2(1f, 0f);
                tickRect.anchorMax = new Vector2(1f, 0f);
                tickRect.pivot = new Vector2(1f, 0f);
                tickRect.sizeDelta = new Vector2(48f, 48f);
                Image tickImage = tickGO.AddComponent<Image>();
                tickImage.color = new Color32(0x3F, 0xC3, 0x5B, 0xFF);
                tickImage.raycastTarget = false;
                selectedRoot.SetActive(false);

                GameObject lockRoot = CreateUIObject("LockRoot", root.transform);
                Stretch(lockRoot.GetComponent<RectTransform>());
                GameObject lockBgGO = CreateUIObject("Bg", lockRoot.transform);
                Stretch(lockBgGO.GetComponent<RectTransform>());
                Image lockBg = lockBgGO.AddComponent<Image>();
                lockBg.color = new Color(0f, 0f, 0f, 0.25f);
                lockBg.raycastTarget = false;

                GameObject lockLabelGO = CreateUIObject("LockLabel", lockRoot.transform);
                RectTransform lockLabelRect = lockLabelGO.GetComponent<RectTransform>();
                lockLabelRect.anchorMin = new Vector2(0f, 0f);
                lockLabelRect.anchorMax = new Vector2(1f, 0f);
                lockLabelRect.pivot = new Vector2(0.5f, 0f);
                lockLabelRect.sizeDelta = new Vector2(0f, 36f);
                TextMeshProUGUI lockLabel = lockLabelGO.AddComponent<TextMeshProUGUI>();
                lockLabel.text = "Level 12";
                lockLabel.fontSize = 22f;
                lockLabel.alignment = TextAlignmentOptions.Center;
                lockLabel.color = Color.white;
                lockLabel.raycastTarget = false;
                lockRoot.SetActive(false);

                ProfileItemButtonView view = root.AddComponent<ProfileItemButtonView>();
                SerializedObject serialized = new SerializedObject(view);
                serialized.FindProperty("button").objectReferenceValue = button;
                serialized.FindProperty("artImage").objectReferenceValue = artImage;
                serialized.FindProperty("innerPreviewImage").objectReferenceValue = innerImage;
                serialized.FindProperty("nameLabel").objectReferenceValue = nameLabel;
                serialized.FindProperty("selectedRoot").objectReferenceValue = selectedRoot;
                serialized.FindProperty("lockRoot").objectReferenceValue = lockRoot;
                serialized.FindProperty("lockLabel").objectReferenceValue = lockLabel;
                serialized.FindProperty("artCanvasGroup").objectReferenceValue = artCanvasGroup;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, ItemPrefabPath, "Swap the placeholder tick/lock colours for the real art. Keep InnerPreview above Border in the hierarchy.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject created = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                created.transform.SetParent(parent, false);
            }

            return created;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SavePrefab(GameObject root, string path, string nextStep)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success)
            {
                Debug.LogError($"ProfilePrefabBuilder: failed to save prefab at {path}.");
                return;
            }

            Debug.Log($"ProfilePrefabBuilder: created {path}. {nextStep}");
        }
    }
}
