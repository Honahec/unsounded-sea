using System;
using System.Linq;
using UnityEditor;
using UnsoundedSea.Editor;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;
public static class SupportContentSetup
{
    public static string Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop play mode before syncing.");
        if(UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>().Any(x=>EditorUtility.IsDirty(x)&&AssetDatabase.GetAssetPath(x).StartsWith("Assets/UnsoundedSea/Content/",StringComparison.Ordinal)))throw new InvalidOperationException("Save content edits first.");
        FrameworkSetup.SyncCardCatalog();
        foreach(var card in CardCatalog.All.Skip(90))
        {
            var path="Assets/UnsoundedSea/Resources/CardArt/"+card.Id+".png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.wrapMode=UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.Compressed;importer.compressionQuality=75;importer.SaveAndReimport();
            if(AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path)==null)throw new InvalidOperationException("Missing new art: "+path);
            if(AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/UnsoundedSea/Content/Cards/"+card.Id+".asset").Build().Description!=card.Description)throw new InvalidOperationException("Card mismatch.");
        }
        AssetDatabase.SaveAssets();return "96 card specifications synchronized; six generated backgrounds imported and verified.";
    }
}
