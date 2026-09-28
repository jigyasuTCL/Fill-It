using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

// Builds the two single-icon TMP Sprite Assets used to show inline Cat/Fish icons inside FTUE
// popup text (e.g. "Tap the empty tile once to place a <sprite=\"CatFlapFtueFishSpriteAsset\"
// name=\"Fish\">") instead of the plain words "Cat"/"Fish" - see FtueStepData.Description text in
// Assets/Resources/Configs/FtueSteps_Level*.asset. Also wires the Fish one (with Cat chained as
// its fallback) onto FtueBannerController's descriptionLabel inside GamePlayUIRoot.prefab, as a
// secondary fallback for any text that only uses the shorter <sprite name="X"> form.
//
// Each sprite asset's ENTIRE spriteSheet texture is used as a single glyph (glyphRect covering
// the full 0,0-width,height rect) - reusing the existing board icon art (Cat_icon.png/
// fish_icon.png) directly rather than packing a combined atlas, since TMP_SpriteAsset.
// fallbackSpriteAssets already lets one sprite asset (Fish) fall back to another (Cat) for a name
// it doesn't itself define - see TMP_SpriteAssetImporter.cs (Unity's own built-in "Sprite
// Importer" window) for the reference recipe this mirrors (GlyphMetrics/GlyphRect construction,
// default material setup).
public static class CatFlapFtueSpriteAssetBuilder
{
    private const string FishIconPath = "Assets/0_Game/CatFlap/Art/Sprites/fish_icon.png";
    private const string CatIconPath = "Assets/0_Game/CatFlap/Art/Sprites/Cat_icon.png";

    // Must live under a Resources folder at the path TMP_Settings.defaultSpriteAssetPath
    // ("Sprite Assets/") expects - <sprite="AssetName" ...> resolves via
    // Resources.Load(defaultSpriteAssetPath + AssetName) when it isn't already cached, exactly
    // how Unity's own shipped EmojiOne.asset is set up (Assets/TextMesh Pro/Resources/Sprite
    // Assets/EmojiOne.asset). Switched to this from a plain per-label default spriteAsset
    // assignment - that approach kept intermittently failing to resolve at runtime for reasons
    // that didn't show up in any inspectable asset/prefab data (persisted across a full Editor
    // restart too), so this uses TMP's own independent, more commonly-used resolution path
    // instead of relying solely on FtueBannerController.descriptionLabel's own default.
    private const string OutputFolder = "Assets/Resources/Sprite Assets";
    private const string FishSpriteAssetName = "CatFlapFtueFishSpriteAsset";
    private const string CatSpriteAssetName = "CatFlapFtueCatSpriteAsset";
    private const string FishSpriteAssetPath = OutputFolder + "/" + FishSpriteAssetName + ".asset";
    private const string CatSpriteAssetPath = OutputFolder + "/" + CatSpriteAssetName + ".asset";

    private const string GamePlayUIRootPrefabPath = "Assets/Shared/Features/GameFlow/Prefabs/UI/GamePlayUIRoot.prefab";

    [MenuItem("MangraGames/Build Ftue Icon Sprite Assets")]
    public static void Build()
    {
        EnsureFolderExists(OutputFolder);

        TMP_SpriteAsset fishSpriteAsset = CreateOrReplaceSingleIconSpriteAsset(FishSpriteAssetPath, FishIconPath, "Fish");
        TMP_SpriteAsset catSpriteAsset = CreateOrReplaceSingleIconSpriteAsset(CatSpriteAssetPath, CatIconPath, "Cat");

        if (fishSpriteAsset == null || catSpriteAsset == null)
        {
            Debug.LogError("CatFlapFtueSpriteAssetBuilder: failed to build one or both sprite assets - see errors above.");
            return;
        }

        // "Fish" is the one assigned directly to the label - "Cat" resolves through it as a
        // fallback (same mechanism TMP uses for font asset fallback chains).
        fishSpriteAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset> { catSpriteAsset };
        EditorUtility.SetDirty(fishSpriteAsset);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WireDescriptionLabel(fishSpriteAsset);

        Debug.Log("CatFlapFtueSpriteAssetBuilder: built Fish/Cat FTUE sprite assets under Resources/Sprite Assets and wired FtueBannerController.descriptionLabel in GamePlayUIRoot.prefab as a fallback. " +
            $"Use <sprite=\"{FishSpriteAssetName}\" name=\"Fish\"> / <sprite=\"{CatSpriteAssetName}\" name=\"Cat\"> in FtueStepData.Description text.");
    }

    private static TMP_SpriteAsset CreateOrReplaceSingleIconSpriteAsset(string assetPath, string iconTexturePath, string spriteName)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(iconTexturePath);
        if (texture == null)
        {
            Debug.LogError($"CatFlapFtueSpriteAssetBuilder: couldn't load icon texture at '{iconTexturePath}'.");
            return null;
        }

        // Rebuild fresh each run rather than editing in place - keeps this idempotent/safe to
        // re-run after swapping icon art later.
        AssetDatabase.DeleteAsset(assetPath);

        var spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        spriteAsset.spriteSheet = texture;

        // TMP's <sprite> tag has no working "size=" attribute in this TMP version (it's
        // unimplemented in TMP_Text's tag parser - including it invalidates the whole tag), so
        // final on-screen size must come from the glyph/sprite data itself. IMPORTANT: with this
        // sprite asset's faceInfo.pointSize left at 0 (never set), TMP_Text's actual sprite-sizing
        // formula (see TMP_Text.SaveGlyphVertexInfo, the "spriteFace.pointSize > 0" false branch)
        // is `elementScale = fontFace.ascentLine / glyph.metrics.height * spriteCharacter.scale *
        // glyph.scale * spriteScale`, and the rendered size is `glyph.metrics.<dimension> *
        // elementScale` - metrics.height CANCELS OUT of that product entirely, so its absolute
        // value (TargetGlyphSize below) has NO effect on final on-screen size, only on preserving
        // the correct width:height ASPECT RATIO between the two metrics. The actual size knob is
        // spriteCharacter.scale (IconScaleMultiplier) - a straight multiplier with nothing to
        // cancel it.
        const float TargetGlyphSize = 90f;
        // Straight multiplier on top of the "one text line tall" baseline (fontFace.ascentLine) -
        // see the comment above for why THIS, not TargetGlyphSize, is what actually controls size.
        const float IconScaleMultiplier = 1.5f;
        // Fraction of the glyph's own height kept ABOVE the baseline (via GlyphMetrics'
        // horizontalBearingY) - 1.0 would sit it fully on the baseline like a normal capital
        // letter (no descender), which read as sitting too HIGH/floating relative to the
        // surrounding text's midline. Dropping this shifts the whole icon down a bit, closer to
        // how an emoji actually sits relative to a text line. Expressed as a fraction of height
        // (not an absolute value) so it scales consistently with IconScaleMultiplier above - the
        // same height-cancellation reasoning applies to this product too. 0.8 read as floating
        // too high; 0.62 overcorrected and dropped it below the text's vertical center instead -
        // splitting the difference lands it centered on the line.
        const float BaselineBearingFraction = 0.72f;
        // Fraction of the glyph's own width actually used as the TMP cursor advance (the last
        // GlyphMetrics param) - less than 1 tucks the next character in closer than the icon's
        // full width, since the icon's source PNG has its own transparent margin around the
        // visible art, so a 1:1 advance (the old behavior) left a visible gap before the
        // following word even with "no extra kerning" on top of it. 0.72 overcorrected the other
        // way. Left back at 0.85 - the right-side gap itself was already fine, only the left side
        // (see LeftBearingWidthFraction below) needed adjusting.
        const float AdvanceWidthFraction = 0.85f;
        // Fraction of the glyph's own width used as a NEGATIVE horizontalBearingX (GlyphMetrics'
        // 3rd param) - pulls the glyph itself back toward the preceding text/pen position,
        // trimming the gap on the LEFT side specifically (the source PNG's own transparent margin
        // put empty space there even with the text's own single literal space character already
        // accounted for).
        const float LeftBearingWidthFraction = 0.18f;
        float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        float glyphHeight = TargetGlyphSize;
        float glyphWidth = TargetGlyphSize * aspect;

        var glyph = new TMP_SpriteGlyph
        {
            index = 0,
            metrics = new GlyphMetrics(glyphWidth, glyphHeight, -glyphWidth * LeftBearingWidthFraction, glyphHeight * BaselineBearingFraction, glyphWidth * AdvanceWidthFraction),
            glyphRect = new GlyphRect(0, 0, texture.width, texture.height),
            scale = 1f
        };

        var spriteCharacter = new TMP_SpriteCharacter(0xFFFE, glyph)
        {
            name = spriteName,
            scale = IconScaleMultiplier
        };

        // spriteCharacterTable/spriteGlyphTable (TMP_SpriteAsset) and version (TMP_Asset) are all
        // public GET, INTERNAL SET properties - fine for Unity's own shipped editor code (same
        // assembly/friend access), not for a user Editor script, so their private backing fields
        // are set directly via reflection instead - exactly equivalent to what those internal
        // setters would have done, since Unity's serializer reads these fields by [SerializeField]
        // regardless of C# accessor visibility.
        SetPrivateField(spriteAsset, typeof(TMP_SpriteAsset), "m_SpriteCharacterTable", new List<TMP_SpriteCharacter> { spriteCharacter });
        SetPrivateField(spriteAsset, typeof(TMP_SpriteAsset), "m_GlyphTable", new List<TMP_SpriteGlyph> { glyph });
        // The name/glyph-index lookup dictionaries used by <sprite name=X> at render time are
        // otherwise only rebuilt lazily (the first time something reads spriteCharacterTable/
        // spriteCharacterLookupTable) - setting the tables via reflection above bypasses whatever
        // triggers that, so force it explicitly now rather than risk it staying built from an
        // earlier (empty) state for this asset instance.
        spriteAsset.UpdateLookupTables();

        AssetDatabase.CreateAsset(spriteAsset, assetPath);
        SetPrivateField(spriteAsset, typeof(TMP_Asset), "m_Version", "1.1.0");
        spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);

        Shader shader = Shader.Find("TextMeshPro/Sprite");
        var material = new Material(shader);
        material.SetTexture(ShaderUtilities.ID_MainTex, spriteAsset.spriteSheet);
        material.name = spriteAsset.name + " Material";
        spriteAsset.material = material;
        AssetDatabase.AddObjectToAsset(material, spriteAsset);

        EditorUtility.SetDirty(spriteAsset);
        return spriteAsset;
    }

    private static void WireDescriptionLabel(TMP_SpriteAsset fishSpriteAsset)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GamePlayUIRootPrefabPath);
        try
        {
            var banner = root.GetComponentInChildren<Shared.Ftue.FtueBannerController>(true);
            if (banner == null)
            {
                Debug.LogError($"CatFlapFtueSpriteAssetBuilder: no FtueBannerController found in '{GamePlayUIRootPrefabPath}' - assign the sprite asset to its descriptionLabel manually.");
                return;
            }

            var serializedBanner = new SerializedObject(banner);
            SerializedProperty descriptionLabelProperty = serializedBanner.FindProperty("descriptionLabel");
            var descriptionLabel = descriptionLabelProperty?.objectReferenceValue as TMP_Text;
            if (descriptionLabel == null)
            {
                Debug.LogError("CatFlapFtueSpriteAssetBuilder: FtueBannerController.descriptionLabel is unassigned - assign the sprite asset to it manually once that's wired.");
                return;
            }

            descriptionLabel.spriteAsset = fishSpriteAsset;
            EditorUtility.SetDirty(descriptionLabel);

            PrefabUtility.SaveAsPrefabAsset(root, GamePlayUIRootPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetPrivateField(object target, System.Type declaringType, string fieldName, object value)
    {
        FieldInfo field = declaringType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"CatFlapFtueSpriteAssetBuilder: reflection field '{fieldName}' not found on {declaringType} - TMP's internal layout may have changed.");
            return;
        }

        field.SetValue(target, value);
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folderPath).Replace('\\', '/');
        string newFolderName = System.IO.Path.GetFileName(folderPath);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolderExists(parent);
        }

        AssetDatabase.CreateFolder(parent, newFolderName);
    }
}
