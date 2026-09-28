using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TLC.SpriteAnimation.EditorTools
{
    /// <summary>
    /// Slices a sprite sheet into an even grid, rounding each cell EDGE rather than truncating a
    /// cell size.
    ///
    /// Unity's built-in "Grid By Cell Count" computes one integer cell size and steps by it, so a
    /// 1024px sheet across 5 columns uses 204px and has drifted ~3.6px off the artwork by the last
    /// column — which reads as the character sliding sideways through the animation. Rounding the
    /// edges instead gives 205/205/204/205/205, summing to exactly 1024, with every frame aligned
    /// to the grid the art was actually drawn on.
    ///
    /// Sheets whose size divides evenly by the frame count do not need this; it costs nothing when
    /// they do, so it is safe to use for every sheet.
    /// </summary>
    public class SpriteSheetSlicer : EditorWindow
    {
        private int columns = 5;
        private int rows = 5;

        [MenuItem("Assets/Sprite Animation/Slice Sheet (even grid)...", false, 20)]
        private static void Open()
        {
            var w = GetWindow<SpriteSheetSlicer>(true, "Slice Sheet", true);
            w.minSize = new Vector2(320f, 150f);
        }

        private void OnGUI()
        {
            var textures = GetSelectedTextures();

            EditorGUILayout.LabelField("Selected sheets", textures.Count.ToString());
            columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", columns));
            rows = Mathf.Max(1, EditorGUILayout.IntField("Rows", rows));
            EditorGUILayout.HelpBox(
                columns * rows + " frames per sheet. Frame 0 is the top-left cell and doubles as " +
                "the rest pose, so it should be the character's neutral drawing.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(textures.Count == 0))
            {
                if (GUILayout.Button("Slice"))
                {
                    foreach (string path in textures) Slice(path, columns, rows);
                    AssetDatabase.Refresh();
                    Debug.Log(string.Format(
                        "[SpriteAnimation] Sliced {0} sheet(s) into {1} frames each.",
                        textures.Count, columns * rows));
                }
            }
        }

        public static void Slice(string texturePath, int columns, int rows)
        {
            var imp = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (imp == null || tex == null) return;

            int[] xEdge = new int[columns + 1];
            for (int i = 0; i <= columns; i++)
                xEdge[i] = Mathf.RoundToInt(i * (tex.width / (float)columns));

            int[] yEdge = new int[rows + 1];
            for (int i = 0; i <= rows; i++)
                yEdge[i] = Mathf.RoundToInt(i * (tex.height / (float)rows));

            string baseName = System.IO.Path.GetFileNameWithoutExtension(texturePath);
            var metas = new List<SpriteMetaData>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    // Unity's rect origin is bottom-left, but frame 0 is the TOP-left cell.
                    int yBottom = yEdge[rows - 1 - r];
                    int yTop = yEdge[rows - r];

                    metas.Add(new SpriteMetaData
                    {
                        name = string.Format("{0}_{1}", baseName, r * columns + c),
                        rect = new Rect(xEdge[c], yBottom, xEdge[c + 1] - xEdge[c], yTop - yBottom),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    });
                }
            }

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Multiple;
            imp.spritesheet = metas.ToArray();
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            EditorUtility.SetDirty(imp);
            imp.SaveAndReimport();
        }

        private static List<string> GetSelectedTextures()
        {
            var paths = new List<string>();
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                // Folders expand, so a whole batch of sheets can be sliced in one pass.
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!paths.Contains(p)) paths.Add(p);
                    }
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is TextureImporter && !paths.Contains(path))
                    paths.Add(path);
            }
            return paths;
        }
    }
}
