// Run through the Editor Pipeline. Original ArtSource models are never changed.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class RuntimeTextureSetup
{
    const string Root = "Assets/UnsoundedSea/Art/Textures/RuntimeModels";
    const string Manifest = "ArtSource/RuntimeTextures/manifest.json";
    public static object CompactExternalizedBuffers()
    {
        if(EditorApplication.isPlaying || Enumerable.Range(0,EditorSceneManager.sceneCount).Any(i=>EditorSceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Exit Play and save scene edits first.");
        var manifest=JObject.Parse(File.ReadAllText(Manifest));long removed=0;
        Directory.CreateDirectory("Logs/Performance/ModelBackups");
        if(!File.Exists("Logs/Performance/ModelBackups/manifest-before-cleanup.json"))
            File.Copy(Manifest,"Logs/Performance/ModelBackups/manifest-before-cleanup.json");
        foreach(var pair in (JObject)manifest["models"])
        {
            if(pair.Value["runtimeBinSha256"]!=null)continue;
            var resolved=Path.GetFullPath(pair.Key);
            if(!resolved.StartsWith(Path.GetFullPath("Assets/UnsoundedSea/Art/Models")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model path outside runtime model directory.");
            var original=File.ReadAllBytes(pair.Key);
            if(Hash(original)!=(string)pair.Value["runtimeGlbSha256"])throw new InvalidDataException("Unexpected model revision: "+pair.Key);
            var backup="Logs/Performance/ModelBackups/"+Hash(original)+".glb";
            if(!File.Exists(backup))File.Copy(pair.Key,backup);
            if(Hash(File.ReadAllBytes(backup))!=Hash(original))throw new InvalidDataException("Backup verification failed.");
            int jsonLength=BitConverter.ToInt32(original,12),binStart=28+jsonLength;
            var json=JObject.Parse(Encoding.UTF8.GetString(original,20,jsonLength));
            var views=(JArray)json["bufferViews"];
            var imageHashes=new HashSet<string>(pair.Value["textures"].Select(t=>(string)t["sha256"]));
            var referenced=new HashSet<int>(json.Descendants().OfType<JProperty>().Where(p=>p.Name=="bufferView").Select(p=>(int)p.Value));
            var retained=new JArray();var imageViews=new JArray();
            using var packed=new MemoryStream();
            for(int i=0;i<views.Count;i++)
            {
                var view=views[i];int offset=(int?)view["byteOffset"]??0,length=(int)view["byteLength"];
                var payload=new byte[length];Buffer.BlockCopy(original,binStart+offset,payload,0,length);
                bool image=!referenced.Contains(i)&&imageHashes.Contains(Hash(payload));
                if(image){imageViews.Add(i);payload=new byte[4];}
                else retained.Add(new JObject { ["index"]=i,["sha256"]=Hash(payload),["length"]=payload.Length });
                while(packed.Length%4!=0)packed.WriteByte(0);
                view["byteOffset"]=(int)packed.Length;view["byteLength"]=payload.Length;
                packed.Write(payload,0,payload.Length);
            }
            if(imageViews.Count==0)continue;
            while(packed.Length%4!=0)packed.WriteByte(0);
            var bin=packed.ToArray();json["buffers"][0]["byteLength"]=bin.Length;
            foreach(var item in retained)
            {
                var view=views[(int)item["index"]];int offset=(int)view["byteOffset"],length=(int)view["byteLength"];
                if(Hash(bin,offset,length)!=(string)item["sha256"])throw new InvalidDataException("Retained buffer verification failed.");
            }
            var text=Encoding.UTF8.GetBytes(json.ToString(Formatting.None));int padded=(text.Length+3)&~3;
            var output=new byte[28+padded+bin.Length];Buffer.BlockCopy(original,0,output,0,20);
            Buffer.BlockCopy(BitConverter.GetBytes(output.Length),0,output,8,4);Buffer.BlockCopy(BitConverter.GetBytes(padded),0,output,12,4);
            Buffer.BlockCopy(text,0,output,20,text.Length);for(int i=text.Length;i<padded;i++)output[20+i]=32;
            Buffer.BlockCopy(BitConverter.GetBytes(bin.Length),0,output,20+padded,4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x004E4942),0,output,24+padded,4);
            Buffer.BlockCopy(bin,0,output,28+padded,bin.Length);
            // Atomic replacement also avoids truncating a file still mapped by Unity's importer on Windows.
            var pending=Path.GetFullPath("Logs/Performance/ModelBackups/pending-"+Hash(output)+".glb");
            File.WriteAllBytes(pending,output);AssetDatabase.ReleaseCachedFileHandles();
            File.Replace(pending,resolved,null);
            AssetDatabase.ImportAsset(pair.Key,ImportAssetOptions.ForceSynchronousImport);
            if(AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key)==null)throw new InvalidDataException("Reimport failed: "+pair.Key);
            removed+=original.Length-output.Length;
            pair.Value["runtimeGlbSha256"]=Hash(output);pair.Value["runtimeBinSha256"]=Hash(bin);
            pair.Value["retainedBufferViews"]=retained;pair.Value["externalizedImageBufferViews"]=imageViews;
            File.WriteAllText(Manifest,manifest.ToString(Formatting.Indented));
        }
        manifest["policy"]="Lossless PNG extraction; original resolution and mip chain; Standalone BC7 quality 100; RGB glTF normals. Only unreferenced image payloads removed; geometry, rig and animation buffer views verified byte-for-byte. ArtSource originals unchanged.";
        File.WriteAllText(Manifest,manifest.ToString(Formatting.Indented));
        return new { removedSourceBytes=removed };
    }
    public static object Verify()
    {
        var manifest = JObject.Parse(File.ReadAllText(Manifest));
        var unique = new HashSet<string>();long rgbaBytes=0,bc7Bytes=0;
        foreach (var pair in (JObject)manifest["models"])
        {
            var bytes=File.ReadAllBytes(pair.Key);
            if(Hash(bytes)!=(string)pair.Value["runtimeGlbSha256"])throw new InvalidDataException("Model hash mismatch: "+pair.Key);
            var jsonLength=BitConverter.ToInt32(bytes,12);
            var binHash=(string)(pair.Value["runtimeBinSha256"]??pair.Value["sourceBinSha256"]??pair.Value["unchangedBinSha256"]);
            if(Hash(bytes,28+jsonLength)!=binHash)throw new InvalidDataException("Model BIN changed: "+pair.Key);
            if(pair.Value["retainedBufferViews"] is JArray retained)
            {
                var json=JObject.Parse(Encoding.UTF8.GetString(bytes,20,jsonLength));
                foreach(var item in retained)
                {
                    var view=json["bufferViews"][(int)item["index"]];int offset=(int?)view["byteOffset"]??0,length=(int)view["byteLength"];
                    if(length!=(int)item["length"]||Hash(bytes,28+jsonLength+offset,length)!=(string)item["sha256"])
                        throw new InvalidDataException("Geometry/rig buffer view changed: "+pair.Key);
                }
            }
            var dependencies=AssetDatabase.GetDependencies(pair.Key,true);
            if(AssetDatabase.LoadAllAssetsAtPath(pair.Key).OfType<Texture2D>().Any())throw new InvalidDataException("Unexpected embedded texture: "+pair.Key);
            foreach(var item in pair.Value["textures"])
            {
                var path=(string)item["path"];
                if(!dependencies.Contains(path))throw new InvalidDataException("Texture dependency missing: "+path);
                if(!unique.Add(path))continue;
                if(Hash(File.ReadAllBytes(path))!=(string)item["sha256"])throw new InvalidDataException("PNG hash mismatch: "+path);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture==null || texture.width!=(int)item["width"] || texture.height!=(int)item["height"] || texture.format!=TextureFormat.BC7)
                    throw new InvalidDataException("Texture specification mismatch: "+path);
                if(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)!=(bool)item["srgb"] || texture.isReadable ||
                    texture.mipmapCount!=1+(int)Math.Log(Math.Max(texture.width,texture.height),2))
                    throw new InvalidDataException("Texture color space, readback or mip chain mismatch: "+path);
                for(int mip=0;mip<texture.mipmapCount;mip++)
                {
                    int w=Math.Max(1,texture.width>>mip),h=Math.Max(1,texture.height>>mip);
                    rgbaBytes+=(long)w*h*4;bc7Bytes+=(long)((w+3)/4)*((h+3)/4)*16;
                }
            }
        }
        return new { models=((JObject)manifest["models"]).Count, textures=unique.Count,rgbaBytes,bc7Bytes,reduction=1-(double)bc7Bytes/rgbaBytes };
    }
    static string Hash(byte[] bytes, int offset=0, int count=-1) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes,offset,count<0?bytes.Length-offset:count)).Replace("-", "").ToLowerInvariant(); }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    public static object ConvertUsedModels()
    {
        var roots = new[] { "Assets/UnsoundedSea/Scenes/MainMenu.unity", "Assets/UnsoundedSea/Scenes/Voyage.unity", "Assets/UnsoundedSea/Scenes/OceanBattle.unity" }
            .Concat(AssetDatabase.FindAssets("t:VoyageScenarioReference", new[] { "Assets/UnsoundedSea/Resources" }).Select(AssetDatabase.GUIDToAssetPath)).ToArray();
        var paths = AssetDatabase.GetDependencies(roots, true).Where(p => p.StartsWith("Assets/UnsoundedSea/Art/Models/", StringComparison.Ordinal) && p.EndsWith(".glb", StringComparison.Ordinal)).OrderBy(p => p).ToArray();
        var results = new List<object>();
        foreach (var path in paths)
        {
            results.Add(ConvertModel(path));
            File.WriteAllText("Logs/Performance/texture-progress.json", JsonConvert.SerializeObject(new { done=results.Count, total=paths.Length, path }));
            EditorUtility.UnloadUnusedAssetsImmediate();
        }
        return new { count=paths.Length, results };
    }
    public static object ConvertModel(string path)
    {
        if (EditorApplication.isPlaying || Enumerable.Range(0, EditorSceneManager.sceneCount).Any(i => EditorSceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Exit Play and save scene edits before converting textures.");
        if (!path.StartsWith("Assets/UnsoundedSea/Art/Models/", StringComparison.Ordinal) || !path.EndsWith(".glb", StringComparison.Ordinal))
            throw new ArgumentException("Only runtime GLBs may be converted.");
        var original = File.ReadAllBytes(path);
        if (BitConverter.ToUInt32(original, 0) != 0x46546C67 || BitConverter.ToUInt32(original, 4) != 2)
            throw new InvalidDataException("Expected GLB version 2.");
        int jsonLength = BitConverter.ToInt32(original, 12);
        var json = JObject.Parse(Encoding.UTF8.GetString(original, 20, jsonLength));
        var images = json["images"] as JArray;
        if (images == null || images.All(i => i["bufferView"] == null)) return new { path, status = "already external" };
        int binHeader = 20 + jsonLength, binStart = binHeader + 8;
        if (BitConverter.ToUInt32(original, binHeader + 4) != 0x004E4942) throw new InvalidDataException("Missing BIN chunk.");
        var sources = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().ToArray();
        var replacements = new Dictionary<Texture, Texture>();
        var records = new JArray();
        Folder(Root);
        foreach (var image in images)
        {
            if (image["bufferView"] == null) continue;
            if ((string)image["mimeType"] != "image/png") throw new InvalidDataException("Only lossless PNG extraction is supported.");
            var name = (string)image["name"];
            var source = sources.Single(t => t.name == name);
            var view = json["bufferViews"][(int)image["bufferView"]];
            int offset = (int?)view["byteOffset"] ?? 0, length = (int)view["byteLength"];
            var png = new byte[length]; Buffer.BlockCopy(original, binStart + offset, png, 0, length);
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            var texturePath = Root + "/" + Hash(png) + (srgb ? "_srgb.png" : "_linear.png");
            if (!File.Exists(texturePath)) File.WriteAllBytes(texturePath, png);
            else if (Hash(File.ReadAllBytes(texturePath)) != Hash(png)) throw new InvalidDataException("Texture hash conflict.");
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType = TextureImporterType.Default; // Preserve glTF RGB normals, with no Unity normal swizzle.
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = Math.Max(source.width, source.height);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.overridden = true; platform.maxTextureSize = importer.maxTextureSize;
            platform.format = TextureImporterFormat.BC7; platform.compressionQuality = 100;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture.width != source.width || texture.height != source.height || texture.mipmapCount != source.mipmapCount)
                throw new InvalidDataException("Texture dimensions or mip chain changed.");
            replacements.Add(source, texture);
            records.Add(new JObject { ["name"]=name, ["path"]=texturePath, ["sha256"]=Hash(png), ["width"]=texture.width,
                ["height"]=texture.height, ["srgb"]=srgb, ["format"]=texture.format.ToString() });
            image["uri"] = new Uri(Path.GetFullPath(path)).MakeRelativeUri(new Uri(Path.GetFullPath(texturePath))).ToString();
            ((JObject)image).Remove("bufferView"); ((JObject)image).Remove("mimeType");
        }
        // Custom water/landmark materials can reference embedded textures directly. Rebind via Editor API.
        var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/UnsoundedSea" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat", StringComparison.Ordinal))
            .Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m != null).ToArray();
        foreach (var material in materials)
        {
            var affected = material.GetTexturePropertyNames().Where(p => material.GetTexture(p) != null && replacements.ContainsKey(material.GetTexture(p))).ToArray();
            if (affected.Length == 0) continue;
            if (EditorUtility.IsDirty(material)) throw new InvalidOperationException("Unsaved material: " + AssetDatabase.GetAssetPath(material));
            foreach (var property in affected) material.SetTexture(property, replacements[material.GetTexture(property)]);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }
        var text = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        int padded = (text.Length + 3) & ~3;
        var output = new byte[20 + padded + original.Length - binHeader];
        Buffer.BlockCopy(original, 0, output, 0, 20);
        Buffer.BlockCopy(BitConverter.GetBytes(output.Length), 0, output, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(padded), 0, output, 12, 4);
        Buffer.BlockCopy(text, 0, output, 20, text.Length);
        for (int i = text.Length; i < padded; i++) output[20+i] = 32;
        Buffer.BlockCopy(original, binHeader, output, 20+padded, original.Length-binHeader);
        // BIN stays byte-for-byte identical: vertices, indices, skin weights and animations are untouched.
        var binHash = Hash(original.Skip(binStart).ToArray());
        File.WriteAllBytes(path, output);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().ToArray();
        if (embedded.Length != 0) throw new InvalidDataException("Some embedded textures remain: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(Manifest));
        var manifest = File.Exists(Manifest) ? JObject.Parse(File.ReadAllText(Manifest)) : new JObject {
            ["version"]=1, ["policy"]="Lossless PNG extraction; original resolution and mip chain; Standalone BC7 quality 100; RGB glTF normals; BIN unchanged.", ["models"]=new JObject() };
        manifest["models"][path] = new JObject { ["originalGlbSha256"]=Hash(original), ["runtimeGlbSha256"]=Hash(output),
            ["sourceBinSha256"]=binHash, ["textures"]=records };
        File.WriteAllText(Manifest, manifest.ToString(Formatting.Indented));
        return new { path, textures=records, status="converted", unchangedBinSha256=binHash };
    }
}
