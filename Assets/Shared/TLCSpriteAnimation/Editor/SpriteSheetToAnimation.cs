using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TLC.SpriteAnimation.EditorTools
{
    /// <summary>
    /// Turns a sliced sprite sheet into a <see cref="SpriteAnimationSO"/>.
    ///
    /// Exists so a new character is a menu click rather than twenty-five hand-dragged sprite
    /// references, and so a re-exported sheet can be re-imported without redoing the work.
    ///
    /// The clip asset is written BESIDE the sheet, with the same name. That keeps the module
    /// free of any project-specific folder layout: wherever the art lives, the data lands next to
    /// it, and moving the pair later is a normal asset move that Unity fixes up.
    /// </summary>
    public static class SpriteSheetToAnimation
    {
        private static readonly Dictionary<string, float> staticFillCache =
            new Dictionary<string, float>();

        [MenuItem("Assets/Sprite Animation/Create Sprite Animation from Sheet", true)]
        private static bool ValidateSelection() => GetSelectedSheetPaths().Count > 0;

        [MenuItem("Assets/Sprite Animation/Create Sprite Animation from Sheet")]
        private static void CreateFromSelection()
        {
            var made = new List<string>();

            // Cleared per run: a static sprite may have been re-exported since the last import,
            // and a stale fill fraction would bake a wrong correction into every new clip.
            staticFillCache.Clear();

            foreach (string path in GetSelectedSheetPaths())
            {
                var clip = Build(path);
                if (clip != null) made.Add(AssetDatabase.GetAssetPath(clip));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(made.Count == 0
                ? "[SpriteAnimation] Nothing to import. Select one or more sliced sheets, or a " +
                  "folder containing them."
                : "[SpriteAnimation] Created/updated:\n  " + string.Join("\n  ", made));
        }

        /// <summary>
        /// Builds (or refreshes) the clip asset for one sheet. Returns null if the texture has no
        /// sliced sprites — a Single-mode texture is a sheet someone forgot to slice, not a clip.
        /// </summary>
        public static SpriteAnimationSO Build(string texturePath)
        {
            Sprite[] frames = LoadFramesInOrder(texturePath);
            if (frames.Length == 0)
            {
                Debug.LogWarning(string.Format(
                    "[SpriteAnimation] '{0}' has no sliced frames. Select it and run one of the " +
                    "Assets > Sprite Animation > Slice Sheet commands first.",
                    Path.GetFileName(texturePath)));
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(texturePath);
            string folder = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            string clipPath = folder + "/" + name + ".asset";

            // Update in place when it already exists, so re-running never orphans references from
            // a prefab or set asset that already points at this clip.
            var clip = AssetDatabase.LoadAssetAtPath<SpriteAnimationSO>(clipPath);
            bool isNew = clip == null;
            if (isNew) clip = ScriptableObject.CreateInstance<SpriteAnimationSO>();

            clip.frames = frames;
            clip.artFitMultiplier = MeasureArtFit(texturePath, frames[0], name);

            if (isNew) AssetDatabase.CreateAsset(clip, clipPath);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>
        /// The sheet's sprites in playback order.
        ///
        /// Sorted by the trailing index in the name, numerically. Unity returns sub-assets in an
        /// unspecified order, and a plain string sort would run 0, 1, 10, 11, 2 — which looks like
        /// a broken animation rather than a sorting bug, so it is worth being explicit.
        /// </summary>
        private static Sprite[] LoadFramesInOrder(string texturePath)
        {
            var sprites = new List<Sprite>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(texturePath))
            {
                if (obj is Sprite s) sprites.Add(s);
            }

            sprites.Sort((a, b) => TrailingIndex(a.name).CompareTo(TrailingIndex(b.name)));
            return sprites.ToArray();
        }

        private static int TrailingIndex(string name)
        {
            int i = name.Length;
            while (i > 0 && char.IsDigit(name[i - 1])) i--;
            return i < name.Length && int.TryParse(name.Substring(i), out int n) ? n : 0;
        }

        /// <summary>
        /// How much larger the clip must be drawn so its artwork matches the character's static
        /// sprite on screen.
        ///
        /// Sheets pad every frame; a standalone sprite does not. Matching the two sprite RECTS
        /// therefore leaves the animated character noticeably smaller than the static one — on a
        /// real case, 43% of its frame against 77% of the static sprite, so it drew at 56% size.
        /// This measures the real opaque bounds of both and returns the ratio.
        ///
        /// CONVENTION: the static counterpart is the sprite asset named after the sheet's prefix,
        /// the part before the first underscore — "hero_idle.png" looks for "hero.png". Returns 1
        /// when there is no such sprite, so a sheet with no static counterpart simply gets no
        /// correction rather than a wrong one. That is a perfectly normal setup, so it reports at
        /// Log level rather than as a warning.
        /// </summary>
        private static float MeasureArtFit(string sheetPath, Sprite frame, string sheetName)
        {
            int underscore = sheetName.IndexOf('_');
            string prefix = underscore > 0 ? sheetName.Substring(0, underscore) : sheetName;

            Sprite staticSprite = FindStaticSprite(prefix, sheetPath);
            if (staticSprite == null)
            {
                Debug.Log(string.Format(
                    "[SpriteAnimation] No single sprite named '{0}' found, so '{1}' gets no " +
                    "art-fit correction. Set artFitMultiplier by hand if it looks small.",
                    prefix, sheetName));
                return 1f;
            }

            float frameFill = OpaqueWidthFraction(sheetPath, frame.rect);

            // Cached: six look-at sheets for one character would otherwise re-measure the same
            // static sprite six times.
            if (!staticFillCache.TryGetValue(prefix, out float staticFill))
            {
                staticFill = OpaqueWidthFraction(
                    AssetDatabase.GetAssetPath(staticSprite), staticSprite.rect);
                staticFillCache[prefix] = staticFill;
            }

            if (frameFill <= 0.001f || staticFill <= 0.001f) return 1f;
            return staticFill / frameFill;
        }

        /// <summary>
        /// The project's single (unsliced) sprite asset with this exact filename.
        ///
        /// Multi-sprite textures are skipped: another SHEET whose name happens to match is not a
        /// static counterpart, and measuring against one of its padded frames would produce a
        /// correction as wrong as having none. The sheet being imported is excluded outright.
        /// </summary>
        private static Sprite FindStaticSprite(string assetName, string excludePath)
        {
            foreach (string guid in AssetDatabase.FindAssets(assetName + " t:Sprite"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == excludePath) continue;
                if (Path.GetFileNameWithoutExtension(path) != assetName) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.spriteImportMode != SpriteImportMode.Single)
                    continue;

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) return sprite;
            }
            return null;
        }

        /// <summary>
        /// Fraction of the region's width covered by non-transparent pixels.
        ///
        /// Reads through a temporary RenderTexture rather than flipping the importer's Read/Write
        /// flag. Toggling that flag costs a full reimport of a 1024x1024 texture each way, which
        /// made importing six sheets slow enough to time out — and it mutates import settings, so
        /// an interrupted run could leave Read/Write enabled and silently bloat the build. A blit
        /// touches no asset state at all.
        /// </summary>
        private static float OpaqueWidthFraction(string texturePath, Rect region)
        {
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (source == null) return 0f;

            var rt = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                readable = new Texture2D((int)region.width, (int)region.height,
                                         TextureFormat.RGBA32, false);
                readable.ReadPixels(region, 0, 0);
                readable.Apply();

                var px = readable.GetPixels();
                int w = readable.width;
                int min = int.MaxValue, max = int.MinValue;

                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a <= 0.02f) continue;
                    int x = i % w;
                    if (x < min) min = x;
                    if (x > max) max = x;
                }

                return max < min ? 0f : (max - min + 1) / region.width;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) Object.DestroyImmediate(readable);
            }
        }

        /// <summary>
        /// Every sliced sheet in the selection. A selected folder expands, so a whole character's
        /// worth of sheets can be imported in one command.
        /// </summary>
        private static List<string> GetSelectedSheetPaths()
        {
            var paths = new List<string>();

            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (IsSlicedSheet(p) && !paths.Contains(p)) paths.Add(p);
                    }
                    continue;
                }

                if (IsSlicedSheet(path) && !paths.Contains(path)) paths.Add(path);
            }

            return paths;
        }

        private static bool IsSlicedSheet(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null && importer.spriteImportMode == SpriteImportMode.Multiple;
        }
    }
}
