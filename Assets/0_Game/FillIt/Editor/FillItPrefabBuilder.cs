using System.IO;
using FillIt.UnityAdapters;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FillIt.EditorTools
{
    /// <summary>
    /// Builds the Fill It board cell prefab from the project's existing tile art, so the prefab can
    /// be regenerated after tweaking this script instead of hand-editing it.
    /// </summary>
    public static class FillItPrefabBuilder
    {
        public const string PrefabFolder = "Assets/0_Game/FillIt/Prefabs";
        public const string ArtFolder = "Assets/0_Game/FillIt/Art";
        public const string CellPrefabPath = PrefabFolder + "/FillItCell.prefab";
        public const string SquareSpritePath = ArtFolder + "/fillit_square.png";
        public const string DotSpritePath = ArtFolder + "/fillit_dot.png";

        private const string TileFilledPath = "Assets/0_Game/CatFlap/Art/Sprites/Tile_Base.png";
        private const string TileEmptyPath = "Assets/0_Game/CatFlap/Art/Sprites/Tile_normal.png";
        private const string TileOutlinePath = "Assets/0_Game/CatFlap/Art/Sprites/Tile_hint.png";
        private const string PlayerFontPath = "Assets/Shared/Features/GameFlow/Art/Fonts/Nunito-Bold SDF.asset";
        private const string GivenFontPath = "Assets/Shared/Features/GameFlow/Art/Fonts/Nunito-Black SDF.asset";

        // The UI canvas is Screen Space - Camera at sorting order 2 (popups above it), so every
        // board renderer must stay below 2 or it would draw over the HUD and popups.
        private const int BridgeOrder = -6;
        private const int TileOrder = -5;

        [MenuItem("MangraGames/Fill It/Build Cell Prefab")]
        public static void BuildCellPrefabFromMenu()
        {
            BuildCellPrefab();
        }

        public static FillItCellView BuildCellPrefab()
        {
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(ArtFolder);
            Sprite square = EnsureGeneratedSprite(SquareSpritePath, circle: false);
            Sprite dot = EnsureGeneratedSprite(DotSpritePath, circle: true);
            Sprite tileFilled = LoadSprite(TileFilledPath);
            Sprite tileEmpty = LoadSprite(TileEmptyPath);
            Sprite tileOutline = LoadSprite(TileOutlinePath);
            TMP_FontAsset playerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PlayerFontPath);
            TMP_FontAsset givenFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GivenFontPath);
            float tileSize = tileFilled != null ? tileFilled.bounds.size.x : 1f;

            var root = new GameObject("FillItCell");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            SpriteRenderer bridgeRight = CreateSprite("BridgeRight", visual, square, BridgeOrder);
            SpriteRenderer bridgeDown = CreateSprite("BridgeDown", visual, square, BridgeOrder);
            bridgeRight.enabled = false;
            bridgeDown.enabled = false;
            SpriteRenderer background = CreateSprite("Background", visual, tileEmpty, TileOrder);
            SpriteRenderer selection = CreateSprite("Selection", visual, tileOutline, TileOrder + 1);
            selection.transform.localScale = Vector3.one * 1.06f;
            selection.enabled = false;
            SpriteRenderer hint = CreateSprite("HintOutline", visual, tileOutline, TileOrder + 2);
            hint.transform.localScale = Vector3.one * 1.1f;
            hint.enabled = false;
            SpriteRenderer hintedDot = CreateSprite("HintedDot", visual, dot, TileOrder + 3);
            float dotScale = tileSize * 0.14f / Mathf.Max(0.0001f, dot.bounds.size.x);
            hintedDot.transform.localScale = Vector3.one * dotScale;
            hintedDot.transform.localPosition = new Vector3(tileSize * 0.3f, tileSize * 0.3f, -0.02f);
            hintedDot.color = new Color32(0xFF, 0x9E, 0x3D, 0xFF);
            hintedDot.enabled = false;

            TextMeshPro value = CreateLabel("Value", visual, playerFont, tileSize, TileOrder + 4);
            TextMeshPro hintPreview = CreateLabel("HintPreview", visual, playerFont, tileSize, TileOrder + 4);
            hintPreview.gameObject.SetActive(false);


            FillItCellView view = root.AddComponent<FillItCellView>();
            var so = new SerializedObject(view);
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            so.FindProperty("background").objectReferenceValue = background;
            so.FindProperty("bridgeRight").objectReferenceValue = bridgeRight;
            so.FindProperty("bridgeDown").objectReferenceValue = bridgeDown;
            so.FindProperty("selectionOutline").objectReferenceValue = selection;
            so.FindProperty("hintOutline").objectReferenceValue = hint;
            so.FindProperty("hintedDot").objectReferenceValue = hintedDot;
            so.FindProperty("valueLabel").objectReferenceValue = value;
            so.FindProperty("hintPreviewLabel").objectReferenceValue = hintPreview;
            so.FindProperty("emptySprite").objectReferenceValue = tileEmpty;
            so.FindProperty("filledSprite").objectReferenceValue = tileFilled;
            so.FindProperty("playerFont").objectReferenceValue = playerFont;
            so.FindProperty("givenFont").objectReferenceValue = givenFont;
            so.FindProperty("chainColor").colorValue = new Color32(0x2F, 0x86, 0xE8, 0xFF);
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CellPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return prefab.GetComponent<FillItCellView>();
        }

        private static SpriteRenderer CreateSprite(string name, Transform parent, Sprite sprite, int sortingOrder)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static TextMeshPro CreateLabel(string name, Transform parent, TMP_FontAsset font, float tileSize, int sortingOrder)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var label = child.AddComponent<TextMeshPro>();
            if (font != null)
            {
                label.font = font;
            }

            label.rectTransform.sizeDelta = new Vector2(tileSize * 0.8f, tileSize * 0.8f);
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.1f;
            label.fontSizeMax = 100f;
            label.enableWordWrapping = false;
            label.text = "8";
            label.sortingOrder = sortingOrder;
            label.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;
            return label;
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

            Debug.LogWarning($"[FillIt] Sprite not found at {path}");
            return null;
        }

        // Plain white square / disc used for region bridges and the "hint placed this" dot.
        private static Sprite EnsureGeneratedSprite(string path, bool circle)
        {
            if (!File.Exists(path))
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float radius = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float alpha = 1f;
                        if (circle)
                        {
                            float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                            alpha = Mathf.Clamp01(radius - distance);
                        }

                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }

                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
