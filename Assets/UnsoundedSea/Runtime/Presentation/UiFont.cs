using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace UnsoundedSea.Runtime
{
    // Keep dynamic glyphs out of the imported asset. Restoring/reimporting that asset
    // must not invalidate atlas textures already referenced by a running UI panel.
    internal static class UiFont
    {
        static readonly Dictionary<FontAsset, FontAsset> fonts = new Dictionary<FontAsset, FontAsset>();

        public static FontAsset Get(FontAsset source)
        {
            if (source == null || source.atlasPopulationMode != AtlasPopulationMode.Dynamic || source.sourceFontFile == null)
                return source;
            if (fonts.TryGetValue(source, out var font) && font != null) return font;
            font = FontAsset.CreateFontAsset(source.sourceFontFile, Mathf.RoundToInt(source.faceInfo.pointSize),
                source.atlasPadding, source.atlasRenderMode, source.atlasWidth, source.atlasHeight,
                AtlasPopulationMode.Dynamic, source.isMultiAtlasTexturesEnabled);
            font.name = source.name + " (Runtime UI)";
            font.hideFlags = HideFlags.DontSave;
            font.material.hideFlags = HideFlags.DontSave;
            fonts[source] = font;
            return font;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            // Also handles consecutive Play sessions with domain reload disabled.
            foreach (var font in fonts.Values)
            {
                if (font == null) continue;
                foreach (var atlas in font.atlasTextures)
                    if (atlas != null) Object.Destroy(atlas);
                if (font.material != null) Object.Destroy(font.material);
                Object.Destroy(font);
            }
            fonts.Clear();
        }
    }
}
