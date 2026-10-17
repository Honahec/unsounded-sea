using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    /// <summary>Focused look development: preserves authored encounters, slots, rigs and non-M02 cameras.</summary>
    public static class BossAtmosphereSetup
    {
        const string Root = "Assets/UnsoundedSea/";
        const string Output = Root + "Art/Bosses/Atmosphere";
        static readonly string[] Names = { "TideBonesChoirmaster", "InvertedSurveyor", "UnlitFlagBearer", "StarlessWatcher" };
        static readonly Color[] Horizons = { new Color(.25f,.34f,.31f), new Color(.24f,.34f,.39f), new Color(.32f,.30f,.25f), new Color(.19f,.25f,.34f) };
        static readonly Color[] Zeniths = { new Color(.045f,.075f,.065f), new Color(.04f,.065f,.095f), new Color(.065f,.06f,.05f), new Color(.025f,.04f,.085f) };
        public static string Apply()
        {
            Check();
            for (int number = 2; number <= 5; number++) ApplyBoss(number);
            return "M02 airborne/direct sonic attack; four explicit storm skies, reflection cubemaps and source-texture material overrides. Existing M03-M05 compositions preserved.";
        }
        public static void ApplyBoss(int number)
        {
            Check();
            if (number < 2 || number > 5) throw new ArgumentOutOfRangeException(nameof(number));
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Root + "Art/Bosses", "Atmosphere");
            int index = number - 2;
            var skyPath = Output + "/M0" + number + "Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/SeaBoss/StormSky.mat")); AssetDatabase.CreateAsset(sky, skyPath); }
            sky.SetColor("_Horizon", Horizons[index]); sky.SetColor("_Zenith", Zeniths[index]);
            EditorUtility.SetDirty(sky); AssetDatabase.SaveAssetIfDirty(sky);
            var reflection = BakeReflection(number, sky);
            ConfigureActor(index);
            var path = Root + "Art/Bosses/M0" + number + "Stage.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var stage = root.GetComponent<BattleStage>();
                stage.skybox = sky;
                stage.battleCamera.clearFlags = CameraClearFlags.Skybox;
                var environment = root.GetComponent<BattleEnvironmentLighting>() ?? root.AddComponent<BattleEnvironmentLighting>();
                environment.sky = Color.Lerp(Horizons[index], Zeniths[index], .45f);
                environment.equator = Horizons[index] * .45f;
                environment.ground = Zeniths[index] * .45f;
                environment.reflection = reflection; environment.reflectionIntensity = .35f;
                if (number == 2)
                {
                    var anchor = stage.actorAnchors[0];
                    anchor.localPosition = new Vector3(-45, 50, 360); anchor.localScale = Vector3.one * 16;
                    var placement = anchor.GetComponent<BattleAnchor>();
                    placement.heave = 0; placement.waterRadius = 0; placement.emergeDepth = 0;
                    stage.battleCamera.transform.localPosition = new Vector3(72, 30, -40);
                    stage.battleCamera.transform.localRotation = Quaternion.LookRotation(new Vector3(-20, 24, 140) - stage.battleCamera.transform.localPosition);
                    stage.battleCamera.fieldOfView = 65;
                    var bounds = BattleActorView.MeasureLocalBounds(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Prefabs/" + Names[index] + ".prefab").transform);
                    var center = anchor.TransformPoint(bounds.center);
                    foreach (var light in root.GetComponentsInChildren<Light>())
                    {
                        if (light.name == "Sea creature soft fill") light.transform.position = center + new Vector3(65,30,-75);
                        if (light.name == "Sea creature rim") light.transform.position = center + new Vector3(-80,65,40);
                    }
                }
                if (number == 5)
                {
                    var bounds = BattleActorView.MeasureLocalBounds(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Prefabs/" + Names[index] + ".prefab").transform);
                    var center = stage.actorAnchors[0].TransformPoint(bounds.center);
                    foreach (var light in root.GetComponentsInChildren<Light>())
                    {
                        if (light.name == "Sea creature soft fill") { light.transform.position = center + new Vector3(65,30,-75); light.intensity = 8000; light.range = 340; }
                        if (light.name == "Sea creature rim") { light.transform.position = center + new Vector3(-80,65,40); light.intensity = 6500; light.range = 310; }
                        if (light.name == "Vessel exterior fill") { light.transform.position = stage.vessel.position + new Vector3(12,25,-15); light.intensity = 1200; light.range = 100; }
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Stop Play and save scene edits before configuring boss atmosphere.");
        }
        static void ConfigureActor(int index)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Models/" + Names[index] + "_BANG_LOD0.glb");
            var originals = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToDictionary(m=>m.name);
            var path = Root + "Art/Prefabs/" + Names[index] + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(existing =>
                    {
                        // Each override retains the imported material's name to make reruns source-based.
                        var original = originals[existing.name];
                        var materialPath = Output + "/" + Names[index] + "_" + original.name + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (material == null) { material = new Material(original); AssetDatabase.CreateAsset(material, materialPath); }
                        material.CopyPropertiesFromMaterial(original); material.name = original.name;
                        material.SetFloat("metallicFactor", new[] { .06f, .12f, .55f, .15f }[index]);
                        material.SetFloat("normalTexture_scale", new[] { .6f, .7f, .8f, .65f }[index]);
                        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                        return material;
                    }).ToArray();
                }
                if (index == 0)
                {
                    var motion = root.GetComponentInChildren<GiantBossMotion>();
                    GiantJointMotion Joint(string name) => motion.joints.Single(j=>j.bone.name == "GB_"+name);
                    motion.waveAmplitude = 0;
                    Joint("Body").idleShift = Vector3.up * .012f;
                    Joint("Body").attackEuler = new Vector3(1.5f,0,0);
                    Joint("Body").attackShift = new Vector3(0,0,-.008f);
                    Joint("Head").attackEuler = new Vector3(5,0,0);
                    Joint("Head").followEuler = new Vector3(1.3f,0,0);
                    Joint("Gullet").breathScale = new Vector3(.028f,.035f,.028f);
                    Joint("Fin_L").idleEuler = new Vector3(0,.3f,2.8f);
                    Joint("Fin_R").idleEuler = new Vector3(0,-.3f,-2.8f);
                    Joint("Fin_L").attackEuler = new Vector3(0,0,10);
                    Joint("Fin_R").attackEuler = new Vector3(0,0,-10);
                    motion.effectOrigin = Joint("Gullet").bone;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static Cubemap BakeReflection(int number, Material sky)
        {
            var path = Output + "/M0" + number + "Reflection.cubemap";
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null) { cube = new Cubemap(128, TextureFormat.RGBAHalf, true); AssetDatabase.CreateAsset(cube,path); }
            var go = new GameObject("Temporary sky reflection bake") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled=false; camera.clearFlags=CameraClearFlags.Skybox;
            camera.cullingMask=0; camera.fieldOfView=90; camera.aspect=1;
            var skybox=go.AddComponent<Skybox>(); skybox.material=sky;
            var rt=RenderTexture.GetTemporary(128,128,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var image=new Texture2D(128,128,TextureFormat.RGBAHalf,false,true);
            var previous=RenderTexture.active;
            var directions=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            try
            {
                camera.targetTexture=rt;
                for(int face=0;face<6;face++)
                {
                    camera.transform.rotation=Quaternion.LookRotation(directions[face],face==2?Vector3.back:face==3?Vector3.forward:Vector3.up);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
                    cube.SetPixels(image.GetPixels(),(CubemapFace)face);
                }
                cube.Apply(true,false); EditorUtility.SetDirty(cube); AssetDatabase.SaveAssetIfDirty(cube);
                return cube;
            }
            finally { RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
