using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

public static class AudioSetup
{
    public static string Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode before importing the audio bank.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        const string root="Assets/UnsoundedSea/Audio";
        var paths=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".wav")||p.EndsWith(".ogg")).OrderBy(p=>p).ToArray();
        foreach(var path in paths)
        {
            var importer=AssetImporter.GetAtPath(path) as AudioImporter;
            if(importer==null)throw new Exception("Audio importer missing: "+path);
            bool longClip=path.Contains("/Music/")||path.Contains("/Ambience/");
            var sample=importer.defaultSampleSettings;
            sample.loadType=longClip?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat=longClip?AudioCompressionFormat.Vorbis:AudioCompressionFormat.PCM;
            sample.quality=.72f;sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
            sample.preloadAudioData=!longClip;
            importer.defaultSampleSettings=sample;importer.forceToMono=!longClip;
            importer.loadInBackground=longClip;
            importer.SaveAndReimport();
        }
        const string bankPath="Assets/UnsoundedSea/Resources/SeaAudioBank.asset";
        var bank=AssetDatabase.LoadAssetAtPath<SeaAudioBank>(bankPath);
        if(bank==null){bank=ScriptableObject.CreateInstance<SeaAudioBank>();AssetDatabase.CreateAsset(bank,bankPath);}
        bank.cues=paths.Where(p=>p.Contains("/SFX/")).GroupBy(p=>Regex.Replace(Path.GetFileNameWithoutExtension(p),"_[0-9]+$","")).Select(g=>new SeaAudioCue{id=g.Key,gain=.85f,clips=g.Select(p=>AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray()}).ToArray();
        bank.music=new[]{"harbor","voyage","deck","boss"}.Select(id=>new SeaMusic{id=id,stems=Enumerable.Range(0,3).Select(i=>AssetDatabase.LoadAssetAtPath<AudioClip>(root+"/Music/music_"+id+"_"+i+".ogg")).ToArray()}).ToArray();
        bank.sea=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"/Ambience/sea.ogg");
        bank.wind=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"/Ambience/wind.ogg");
        bank.wake=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"/Ambience/wake.ogg");
        if(bank.music.Any(m=>m.stems.Any(c=>c==null))||bank.cues.Any(c=>c.clips.Any(a=>a==null))||bank.sea==null||bank.wind==null||bank.wake==null)throw new Exception("Incomplete audio generation.");
        EditorUtility.SetDirty(bank);AssetDatabase.SaveAssets();
        return $"Imported {paths.Length} clips; {bank.cues.Length} cues; {bank.music.Length} three-stem scores. Scene and prefab assets unchanged.";
    }
}
