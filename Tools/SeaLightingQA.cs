using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnsoundedSea.Runtime;

// Run in the live sea Boss scene. Render the real water shader with isolated lighting;
// all temporary camera, material, lighting and pause changes are restored in finally.
public static class SeaLightingQA
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static object LightResponse()
    {
        var director = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        var surface = director.Stage.water.surface;
        var original = surface.sharedMaterial;
        var lights = UnityEngine.Object.FindObjectsByType<Light>();
        var main = lights.First(l => l.type == LightType.Directional);
        var intensities = lights.Select(l => l.intensity).ToArray();
        var color = main.color; var rotation = main.transform.rotation;
        bool paused = director.Paused, fog = RenderSettings.fog, asyncShaders = ShaderUtil.allowAsyncCompilation;
        var active = RenderTexture.active;
        var material = new Material(original);
        var cameraObject = new GameObject("Temporary water light QA");
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(director.Stage.battleCamera);
        camera.enabled = false; camera.cullingMask = 1 << 4;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var pointObject = new GameObject("Temporary water point light QA");
        var point = pointObject.AddComponent<Light>(); point.type=LightType.Point;
        point.transform.position=new Vector3(0,35,90); point.range=200; point.intensity=0;
        var rt = new RenderTexture(512, 288, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var texture = new Texture2D(512, 288, TextureFormat.RGBAFloat, false, true);
        Color[] Capture(float intensity, Color lightColor, Color tint)
        {
            main.intensity = intensity; main.color = lightColor; material.SetColor("_Specular_Color", tint);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 512, 288), 0, 0); texture.Apply();
            var pixels = texture.GetPixels();
            Check(pixels.All(c => !float.IsNaN(c.r+c.g+c.b) && !float.IsInfinity(c.r+c.g+c.b)), "Non-finite lighting pixels");
            return pixels;
        }
        double Difference(Color[] a, Color[] b) => a.Zip(b, (x,y) => (double)Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)).Average();
        try
        {
            director.SetPaused(true); RenderSettings.fog = false; ShaderUtil.allowAsyncCompilation = false;
            surface.sharedMaterial = material;
            material.SetFloat("_Reflection_Strength", 0); material.DisableKeyword("_ENABLEPLANERREFLECTION");
            material.SetColor("_Color_Deep", Color.black); material.SetColor("_Color_Shallow", Color.black);
            material.SetColor("_Wave_Top_Color", Color.clear); material.SetColor("_SurfFoam_Color", Color.black);
            material.SetFloat("_Enable_Intersection", 0); material.SetFloat("_Enable_SurfaceFoam", 0);
            for (int i=0; i<lights.Length; i++) lights[i].intensity=0;
            var view=(camera.transform.position-new Vector3(60,0,30)).normalized;
            main.transform.rotation=Quaternion.LookRotation(-Vector3.Reflect(-view,Vector3.up));
            var baseline=Capture(0,Color.white,Color.black);
            var dark=Capture(0,Color.white,Color.white);
            var single=Capture(1,Color.white,Color.white);
            var twice=Capture(2,Color.white,Color.white);
            var red=Capture(1,Color.red,Color.white);
            var blue=Capture(1,Color.blue,Color.white);
            double darkDifference=Difference(baseline,dark), energy=Difference(single,dark), doubleEnergy=Difference(twice,dark);
            Check(darkDifference<0.00001,"Water specular glows without any light");
            Check(energy>0.00001,"Directional light does not illuminate the water highlight");
            Check(Math.Abs(doubleEnergy/energy-2)<0.05,"Specular does not track light intensity");
            double redR=red.Average(c=>(double)c.r),redB=red.Average(c=>(double)c.b);
            double blueR=blue.Average(c=>(double)c.r),blueB=blue.Average(c=>(double)c.b);
            Check(redR>redB+0.00001&&blueB>blueR+0.00001,"Highlight ignores light colour");
            point.intensity=500; point.color=Color.red;
            var pointRed=Capture(0,Color.white,Color.white);
            point.color=Color.blue;
            var pointBlue=Capture(0,Color.white,Color.white);
            double pointR=pointRed.Average(c=>(double)c.r),pointB=pointBlue.Average(c=>(double)c.b);
            Check(pointR>0.00001&&pointB>0.00001,"Forward+ point lights do not illuminate water");
            Check(pointRed.Average(c=>(double)c.b)<0.00001&&pointBlue.Average(c=>(double)c.r)<0.00001,"Point highlight ignores light colour");
            return new { darkDifference, energy, intensityRatio=doubleEnergy/energy, redR, redB, blueR, blueB, pointR, pointB, pixels=512*288 };
        }
        finally
        {
            surface.sharedMaterial=original;
            for(int i=0;i<lights.Length;i++)lights[i].intensity=intensities[i];
            main.color=color;main.transform.rotation=rotation;
            RenderSettings.fog=fog;ShaderUtil.allowAsyncCompilation=asyncShaders;RenderTexture.active=active;
            rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(pointObject);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(material);
            director.SetPaused(paused);
        }
    }

    public static async Task<object> ChartAndCards()
    {
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        var root=UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        void Click(string id)
        {
            var b=root.Q<Button>(id);Check(b!=null&&b.enabledInHierarchy,"Missing button "+id);
            using(var e=NavigationSubmitEvent.GetPooled()){e.target=b;b.SendEvent(e);}
        }
        var turn=d.View.Turn;var actions=d.View.Actions;
        Click("battle-chart-toggle");await Task.Delay(100);
        Check(d.Paused&&root.Q(className:"battle-chart-overlay")!=null,"Chart did not pause battle");
        var water=d.Stage.water;float clock=water!=null?water.SeaTime:0;await Task.Delay(150);
        Check(water==null||clock==water.SeaTime,"Chart did not freeze ocean");
        Click("battle-chart-close");Check(!d.Paused,"Chart did not resume battle");
        d.SetPaused(true);Click("battle-chart-toggle");Click("battle-chart-close");
        Check(d.Paused,"Chart lost prior pause state");d.SetPaused(false);await Task.Delay(150);
        var cards=root.Query<Button>(className:"card-anchor").ToList().OrderBy(c=>c.tabIndex).ToList();
        var parent=cards[0].parent;
        root.Focus(); await Task.Delay(200);
        float minVisible=1,maxVisible=0,minGap=100,maxGap=0;
        foreach(var card in cards)
        {
            var face=card.Q(className:"sea-card");
            var rest=face.worldBound;
            float visible=(root.worldBound.yMax-rest.yMin)/rest.height;
            minVisible=Mathf.Min(minVisible,visible); maxVisible=Mathf.Max(maxVisible,visible);
            Check(visible>0.4f&&visible<0.6f,"Resting card is not half hidden: "+visible);
        }
        foreach(var card in cards)
        {
            // Exercise the hover callback independently of keyboard focus.
            using(var enter=PointerEnterEvent.GetPooled()){enter.target=card;card.SendEvent(enter);}
            await Task.Delay(200);
            Check(parent.Children().Last()==card,"Inspected card is covered by a neighbour");
            var face=card.Q(className:"sea-card");var bounds=face.worldBound;
            float gap=root.worldBound.yMax-bounds.yMax;
            minGap=Mathf.Min(minGap,gap);maxGap=Mathf.Max(maxGap,gap);
            Check(gap>=5&&gap<=45&&bounds.yMin>=0,"Inspected card clipped or raised too far: "+gap);
            Check(Mathf.Abs(bounds.width/card.layout.width-1.1f)<0.01f,"Inspection did not enlarge card");
            foreach(var point in new[]{new Vector2(bounds.xMin+8,bounds.yMin+8),new Vector2(bounds.xMax-8,bounds.yMax-8),bounds.center})
            {
                var hit=root.panel.Pick(point);
                Check(hit==card||card.Contains(hit),"Expanded card is not fully clickable");
            }
            using(var leave=PointerLeaveEvent.GetPooled()){leave.target=card;card.SendEvent(leave);}
            await Task.Delay(200);
            Check(cards.SequenceEqual(parent.Children()),"Resting fan order was not restored");
        }
        cards[2].Focus();await Task.Delay(200);
        Check(cards[2].ClassListContains("inspecting")&&parent.Children().Last()==cards[2],"Keyboard focus does not inspect card");
        root.Focus();await Task.Delay(200);
        Check(cards.SequenceEqual(parent.Children()),"Focus exit did not restore fan order");
        // Unaffordable/disabled cards must still reveal their rules without becoming playable.
        var disabled=cards[0];bool wasEnabled=disabled.enabledSelf;
        try
        {
            disabled.SetEnabled(false);
            using(var enter=PointerEnterEvent.GetPooled()){enter.target=disabled;disabled.SendEvent(enter);}
            await Task.Delay(200);
            Check(disabled.ClassListContains("inspecting"),"Disabled card cannot be inspected");
            using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=disabled;disabled.SendEvent(submit);}
            Check(d.View.Hand.Count==cards.Count,"Disabled card became playable");
            using(var leave=PointerLeaveEvent.GetPooled()){leave.target=disabled;disabled.SendEvent(leave);}
        }
        finally {disabled.SetEnabled(wasEnabled);}
        Check(d.View.Turn==turn&&d.View.Actions==actions,"UI inspection changed battle state");
        return new{cards=cards.Count,chartFreezesOcean=water!=null,priorPausePreserved=true,restingOrderRestored=true,disabledCardPreview=true,minVisible,maxVisible,minGap,maxGap,turn,actions};
    }
}
