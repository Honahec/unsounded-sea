using System;
using System.Linq;
using UnityEditor;
using UnsoundedSea.Core;
using UnsoundedSea.Editor;
using UnsoundedSea.Runtime;

public static class CardCatalogSetup
{
    public static string Build()
    {
        var result = FrameworkSetup.SyncCardCatalog();
        ConfigureCardArt();
        Validate();
        return result;
    }

    private static void ConfigureCardArt()
    {
        foreach (var card in CardCatalog.All)
        {
            var path = "Assets/UnsoundedSea/Resources/CardArt/" + card.Id + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 75;
            importer.SaveAndReimport();
        }
    }

    public static string Validate()
    {
        if (CardCatalog.All.Count != 96) throw new InvalidOperationException($"Expected 96 catalog cards, found {CardCatalog.All.Count}.");
        if (CardCatalog.All.Select(card => card.Id).Distinct().Count() != CardCatalog.All.Count) throw new InvalidOperationException("Card IDs must be unique.");
        foreach (var card in CardCatalog.All)
        {
            var path = "Assets/UnsoundedSea/Content/Cards/" + card.Id + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            if (asset == null) throw new InvalidOperationException("Missing card asset: " + path);
            var built = asset.Build();
            if (built.Id != card.Id || built.Name != card.Name || built.Description != card.Description || built.Cost != card.Cost ||
                built.SanityCost != card.SanityCost || built.Exhaust != card.Exhaust ||
                !built.Effects.Select(effect => (effect.Id, effect.Amount)).SequenceEqual(card.Effects.Select(effect => (effect.Id, effect.Amount))))
                throw new InvalidOperationException("Card asset is out of sync: " + path);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/UnsoundedSea/Resources/CardArt/" + card.Id + ".png") == null)
                throw new InvalidOperationException("Missing card art: " + card.Id);
        }
        var starter = new[] { 1, 2, 12, 19, 20, 21, 22, 37, 39, 41, 55, 71 };
        if (starter.Select(number => CardCatalog.All.Single(card => CardCatalog.Number(card.Id) == number).Id).Distinct().Count() != 12)
            throw new InvalidOperationException("Starter deck must contain 12 unique cards.");
        return "Validated 96 card assets, art files, and the 12-card starter deck.";
    }
}
