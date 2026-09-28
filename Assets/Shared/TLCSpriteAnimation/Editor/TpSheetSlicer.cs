using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TLC.SpriteAnimation.EditorTools
{
    /// <summary>
    /// Slices a TexturePacker atlas using the ".tpsheet" data file exported alongside it.
    ///
    /// The counterpart to <see cref="SpriteSheetSlicer"/>, which handles an even grid. A packed
    /// atlas is not a grid: frames sit at arbitrary positions, are trimmed to their opaque bounds,
    /// and each carries its own pivot so the trimming does not make the character slide around.
    /// All of that is expressible with <see cref="SpriteMetaData"/> — rect plus pivot — so no
    /// third-party importer is needed, and none is added to the project.
    ///
    /// ROTATION IS NOT SUPPORTED, and does not need to be: Unity's sprite rects are axis-aligned
    /// with nowhere to record a rotation, so TexturePacker's Unity exporter never rotates. A
    /// generic XML/plist export from the same tool DOES rotate and cannot be used here — that is
    /// the whole reason this reads .tpsheet specifically.
    ///
    /// Frames are named "{sheet}_{n}" to match the grid slicer exactly, so everything downstream —
    /// <see cref="SpriteSheetToAnimation"/>'s ordering and its art-fit measurement — works on a
    /// packed sheet with no changes.
    /// </summary>
    public static class TpSheetSlicer
    {
        public const string DataExtension = ".tpsheet";

        [MenuItem("Assets/Sprite Animation/Slice Sheet (TexturePacker)", true, 21)]
        private static bool Validate() => CollectSheets().Count > 0;

        [MenuItem("Assets/Sprite Animation/Slice Sheet (TexturePacker)", false, 21)]
        private static void SliceSelection()
        {
            var report = new List<string>();

            foreach (string dataPath in CollectSheets())
            {
                if (Slice(dataPath, out string line)) report.Add(line);
                else Debug.LogWarning("[SpriteAnimation] " + line);
            }

            AssetDatabase.Refresh();

            Debug.Log(report.Count == 0
                ? "[SpriteAnimation] No .tpsheet files found. Select the .tpsheet (or its folder) " +
                  "and make sure the .png sits beside it with the same name."
                : "[SpriteAnimation] Sliced:\n  " + string.Join("\n  ", report) +
                  "\n\nNext: select the sliced .png and run " +
                  "Assets > Sprite Animation > Create Sprite Animation from Sheet.");
        }

        /// <summary>
        /// Applies one .tpsheet to its texture. Returns false with a reason rather than throwing,
        /// so a folder containing one bad file still imports the rest.
        /// </summary>
        public static bool Slice(string dataPath, out string message)
        {
            message = null;

            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, dataPath);
            if (!File.Exists(absolute))
            {
                message = dataPath + ": file not found on disk.";
                return false;
            }

            string baseName = Path.GetFileNameWithoutExtension(dataPath);
            string folder = Path.GetDirectoryName(dataPath)?.Replace('\\', '/');

            // The SIBLING png wins over the ":texture=" line. Renaming the pair to fit a naming
            // convention would otherwise mean hand-editing the .tpsheet as well, and a stale name
            // in there would send the slice at whatever file the artist happened to export first.
            string texturePath = folder + "/" + baseName + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            if (texture == null)
            {
                message = string.Format(
                    "{0}: no '{1}.png' beside it. The image and the .tpsheet must share a name.",
                    Path.GetFileName(dataPath), baseName);
                return false;
            }

            if (!TryParse(File.ReadAllLines(absolute), out var frames, out Vector2Int declared,
                          out string parseError))
            {
                message = Path.GetFileName(dataPath) + ": " + parseError;
                return false;
            }

            if (frames.Count == 0)
            {
                message = Path.GetFileName(dataPath) + ": no frame rows.";
                return false;
            }

            // The rects are in the coordinates of the image TexturePacker exported. If the png was
            // replaced or re-exported at another size the numbers are meaningless, and the result
            // is a sheet sliced into visibly wrong pieces rather than an error.
            if (declared.x > 0 && (declared.x != texture.width || declared.y != texture.height))
            {
                message = string.Format(
                    "{0}: .tpsheet says the atlas is {1}x{2} but '{3}.png' is {4}x{5}. Re-export " +
                    "the pair together.",
                    Path.GetFileName(dataPath), declared.x, declared.y, baseName,
                    texture.width, texture.height);
                return false;
            }

            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                message = texturePath + ": not a texture.";
                return false;
            }

            var metas = new List<SpriteMetaData>(frames.Count);
            foreach (var f in frames)
            {
                metas.Add(new SpriteMetaData
                {
                    // Same naming as the grid slicer, which is what lets
                    // "Create Sprite Animation from Sheet" order and measure these unchanged.
                    name = string.Format("{0}_{1}", baseName, f.index),

                    // Copied verbatim. This is the ".tpsheet" format, TexturePacker's UNITY
                    // exporter, so y is already measured up from the bottom — unlike their generic
                    // XML/plist output, which is top-down and does need flipping.
                    //
                    // Getting this wrong is close to invisible: every rect still lands inside the
                    // texture and the frame count still comes out right, so only looking at the
                    // sliced art shows it. Measured on a real 25-frame sheet, flipping dropped the
                    // average opaque fill of a frame from 62% to 48%, with the frames nearest the
                    // atlas's empty band falling to ~20%.
                    rect = new Rect(f.x, f.y, f.width, f.height),

                    alignment = (int)SpriteAlignment.Custom,
                    pivot = f.pivot,
                });
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritesheet = metas.ToArray();
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            message = string.Format("{0}.png -> {1} frames ({2}x{3})",
                baseName, metas.Count, texture.width, texture.height);
            return true;
        }

        private struct FrameEntry
        {
            public int index;
            public int x, y, width, height;
            public Vector2 pivot;
        }

        /// <summary>
        /// Reads the ".tpsheet" text format.
        ///
        /// Lines starting with '#' are comments and ':' are directives (":size=512x1024"). A frame
        /// row is semicolon separated, with spaces after some separators:
        ///
        ///     name;x;y;w;h; pivotX;pivotY; borderL;borderB;borderR;borderT; [polygon data...]
        ///
        /// Anything past the borders is TexturePacker's tight-mesh polygon, which is ignored: a
        /// rectangular sprite costs a little overdraw at sprite sizes and saves carrying a mesh
        /// format we would then have to keep in step with their exporter.
        /// </summary>
        private static bool TryParse(string[] lines, out List<FrameEntry> frames,
                                     out Vector2Int declaredSize, out string error)
        {
            frames = new List<FrameEntry>();
            declaredSize = Vector2Int.zero;
            error = null;

            var invariant = CultureInfo.InvariantCulture;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == ':')
                {
                    if (line.StartsWith(":size=", System.StringComparison.Ordinal))
                    {
                        string[] wh = line.Substring(6).Split('x');
                        if (wh.Length == 2
                            && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
                        {
                            declaredSize = new Vector2Int(w, h);
                        }
                    }
                    continue;
                }

                string[] parts = line.Split(';');
                if (parts.Length < 7) continue;

                var entry = new FrameEntry();

                if (!int.TryParse(parts[1].Trim(), out entry.x)
                    || !int.TryParse(parts[2].Trim(), out entry.y)
                    || !int.TryParse(parts[3].Trim(), out entry.width)
                    || !int.TryParse(parts[4].Trim(), out entry.height))
                {
                    continue;
                }

                float.TryParse(parts[5].Trim(), NumberStyles.Float, invariant, out float px);
                float.TryParse(parts[6].Trim(), NumberStyles.Float, invariant, out float py);
                entry.pivot = new Vector2(px, py);

                // Playback order comes from the trailing number in the frame name
                // ("frame_007" -> 7), not from the row order: TexturePacker writes rows in
                // packing order, which has nothing to do with the animation.
                entry.index = TrailingIndex(parts[0].Trim(), frames.Count);

                frames.Add(entry);
            }

            frames.Sort((a, b) => a.index.CompareTo(b.index));
            return true;
        }

        private static int TrailingIndex(string name, int fallback)
        {
            string bare = Path.GetFileNameWithoutExtension(name);
            int i = bare.Length;
            while (i > 0 && char.IsDigit(bare[i - 1])) i--;

            return i < bare.Length && int.TryParse(bare.Substring(i), out int n) ? n : fallback;
        }

        /// <summary>
        /// Every .tpsheet in the selection. A selected folder expands, and selecting the .png
        /// works too — the pair share a name, so either half finds the other.
        /// </summary>
        private static List<string> CollectSheets()
        {
            var found = new List<string>();

            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    string abs = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath) ?? string.Empty, path);
                    if (!Directory.Exists(abs)) continue;

                    foreach (string file in Directory.GetFiles(abs, "*" + DataExtension,
                                                               SearchOption.AllDirectories))
                    {
                        Add(found, "Assets" + file.Substring(Application.dataPath.Length)
                                                  .Replace('\\', '/'));
                    }
                    continue;
                }

                if (path.EndsWith(DataExtension, System.StringComparison.OrdinalIgnoreCase))
                {
                    Add(found, path);
                }
                else if (path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                {
                    string sibling = Path.ChangeExtension(path, DataExtension).Replace('\\', '/');
                    string abs = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath) ?? string.Empty, sibling);
                    if (File.Exists(abs)) Add(found, sibling);
                }
            }

            return found;
        }

        private static void Add(List<string> list, string path)
        {
            if (!list.Contains(path)) list.Add(path);
        }
    }
}
