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
        // Drowned Bell and Storm Gauntlet share this actor and stage. Keep their look
        // explicit: voyage/debug entry must not inherit another scene's sky or probes.
        public static string ApplyBellMother()
        {
            Check();
            const string actorPath = Root + "Art/Prefabs/BellMother.prefab";
            const string stagePath = Root + "Art/SeaBoss/DrownedBellStage.prefab";
            foreach (var path in new[] { actorPath, stagePath })
                if (EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path)))
                    throw new InvalidOperationException("Unsaved prefab edits: " + path);
            var actor = PrefabUtility.LoadPrefabContents(actorPath);
            try { ConfigureBellMotherSurface(actor); PrefabUtility.SaveAsPrefabAsset(actor, actorPath); }
            finally { PrefabUtility.UnloadPrefabContents(actor); }
            var stage = PrefabUtility.LoadPrefabContents(stagePath);
            try { ConfigureBellMotherStage(stage.GetComponent<BattleStage>()); PrefabUtility.SaveAsPrefabAsset(stage, stagePath); }
            finally { PrefabUtility.UnloadPrefabContents(stage); }
            return "Bell Mother / Storm Gauntlet: source PBR textures retained, neutral storm haze and explicit stage lighting.";
        }

        public static void ConfigureBellMotherSurface(GameObject actor)
        {
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Root + "Art/Bosses", "Atmosphere");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Models/BellMother_BANG_LOD0.glb");
            var originals = source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Distinct().ToDictionary(m => m.name);
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(existing =>
                {
                    var original = originals[existing.name];
                    var path = Output + "/BellMother_" + original.name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) { material = new Material(original); AssetDatabase.CreateAsset(material, path); }
                    material.CopyPropertiesFromMaterial(original); material.name = original.name;
                    material.SetFloat("metallicFactor", .18f);
                    material.SetFloat("roughnessFactor", 1.35f);
                    material.SetFloat("normalTexture_scale", .65f);
                    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                    return material;
                }).ToArray();
        }

        public static void ConfigureBellMotherStage(BattleStage stage)
        {
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Root + "Art/Bosses", "Atmosphere");
            var path = Output + "/M01Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/SeaBoss/StormSky.mat")); AssetDatabase.CreateAsset(sky, path); }
            sky.SetColor("_Horizon", new Color(.22f, .28f, .29f));
            EditorUtility.SetDirty(sky); AssetDatabase.SaveAssetIfDirty(sky);
            stage.skybox = sky; stage.fogColor = sky.GetColor("_Horizon");
            stage.battleCamera.clearFlags = CameraClearFlags.Skybox;
            var environment = stage.GetComponent<BattleEnvironmentLighting>() ?? stage.gameObject.AddComponent<BattleEnvironmentLighting>();
            environment.sky = new Color(.17f, .20f, .21f);
            environment.equator = new Color(.09f, .11f, .11f);
            environment.ground = new Color(.025f, .035f, .035f);
            environment.reflection = BakeReflection(1, sky); environment.reflectionIntensity = .35f;
            foreach (var light in stage.GetComponentsInChildren<Light>())
            {
                if (light.name == "Moon through fog") light.color = new Color(.72f, .76f, .73f);
                if (light.name == "Sea creature soft fill") light.color = new Color(.64f, .68f, .62f);
                if (light.name == "Sea creature rim") { light.color = new Color(.63f, .69f, .72f); light.intensity = 300; }
            }
        }

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
                    ConfigureChoirDeck(stage);
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
        public static string ConfigureChoirDeck()
        {
            Check();
            const string path=Root+"Art/Bosses/M02Stage.prefab";
            if(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path)))
                throw new InvalidOperationException("Unsaved M02 stage edits.");
            var root=PrefabUtility.LoadPrefabContents(path);
            try { ConfigureChoirDeck(root.GetComponent<BattleStage>()); PrefabUtility.SaveAsPrefabAsset(root,path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "M02 forward deck and two visible summons configured; boss, camera and rules preserved.";
        }
        static void ConfigureChoirDeck(BattleStage stage)
        {
            // Match the vessel's presentation scale to this distant giant-boss camera.
            // Face the open foredeck towards it so the cabin cannot hide summoned actors.
            stage.vessel.localPosition=new Vector3(-28,stage.vessel.localPosition.y,80);
            stage.vessel.localRotation=Quaternion.Euler(0,120,0);
            stage.vessel.localScale=Vector3.one*6;
            stage.vesselSampleExtents=new Vector2(15,42);
            for(int i=1;i<=2;i++)
            {
                stage.actorAnchors[i].localPosition=new Vector3(i==1?-1.2f:1.2f,1.46f,6.2f);
                stage.actorAnchors[i].localRotation=Quaternion.Euler(0,-20,0);
            }
            var lights=stage.GetComponentsInChildren<Light>();
            var deck=lights.Single(l=>l.name=="Cold deck fill");
            deck.transform.SetParent(stage.vessel,false);deck.transform.localPosition=new Vector3(0,5,4);
            deck.range=65;deck.intensity=1600;
            var exterior=lights.Single(l=>l.name=="Vessel exterior fill");
            exterior.transform.position=stage.vessel.position+new Vector3(18,28,-18);
            exterior.range=140;exterior.intensity=1800;
        }
        public static string ConfigureChoirMouth()
        {
            Check();
            const string path=Root+"Art/Prefabs/TideBonesChoirmaster.prefab";
            if(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path)))throw new InvalidOperationException("Unsaved choir actor edits.");
            var root=PrefabUtility.LoadPrefabContents(path);
            try { ConfigureChoirMouth(root.GetComponentInChildren<GiantBossMotion>()); PrefabUtility.SaveAsPrefabAsset(root,path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "Pressure front now starts at the head's snout; gullet charge and other bosses preserved.";
        }
        static void ConfigureChoirMouth(GiantBossMotion motion)
        {
            var head=motion.joints.Single(j=>j.bone.name=="GB_Head").bone;
            var skin=motion.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="Part_06_Mesh");
            var mesh=new Mesh();var bounds=new Bounds();
            try
            {
                skin.BakeMesh(mesh,true);bool first=true;
                foreach(var vertex in mesh.vertices)
                {
                    var point=motion.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            var mouth=head.Find("Mouth pressure origin");
            if(mouth==null){mouth=new GameObject("Mouth pressure origin").transform;mouth.SetParent(head,false);}
            // Part 06 is the rigid jaw/snout. Use its front opening, not the gullet joint behind the head.
            mouth.position=motion.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.35f,bounds.max.z-bounds.size.z*.02f));
            motion.sonicOrigin=mouth;
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
                    ConfigureChoirMouth(motion);
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
