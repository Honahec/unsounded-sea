using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Runtime;

public static class AudioSettingsQA
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public static async Task<string> Run()
    {
        var asm=typeof(Editor).Assembly;var viewType=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(viewType);
        int original=(int)viewType.GetProperty("selectedSizeIndex",All).GetValue(view);
        var root=UnityEngine.Object.FindObjectsByType<UIDocument>().First(d=>d.GetComponent<GameSceneLoader>()==null).rootVisualElement;
        var button=root.Q<Button>("menu-settings");
        if(button==null)throw new Exception("Open MainMenu first");
        using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
        try
        {
            foreach(var wh in new[]{new[]{1920,1080},new[]{2560,1080},new[]{1600,1200}})
            {
                var sizesType=asm.GetType("UnityEditor.GameViewSizes");var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);var sizes=singleton.GetProperty("instance",All).GetValue(null);
                var groupType=asm.GetType("UnityEditor.GameViewSizeGroupType");var group=sizesType.GetMethod("GetGroup",All).Invoke(sizes,new[]{Enum.Parse(groupType,"Standalone")});
                var count=(int)group.GetType().GetMethod("GetBuiltinCount",All).Invoke(group,null)+(int)group.GetType().GetMethod("GetCustomCount",All).Invoke(group,null);
                int index=-1;
                for(int i=0;i<count;i++)
                {
                    var size=group.GetType().GetMethod("GetGameViewSize",All).Invoke(group,new object[]{i});
                    if((int)size.GetType().GetProperty("width",All).GetValue(size)==wh[0]&&(int)size.GetType().GetProperty("height",All).GetValue(size)==wh[1]){index=i;break;}
                }
                if(index<0)
                {
                    var sizeType=asm.GetType("UnityEditor.GameViewSize");var kind=asm.GetType("UnityEditor.GameViewSizeType");
                    var size=Activator.CreateInstance(sizeType,All,null,new object[]{Enum.Parse(kind,"FixedResolution"),wh[0],wh[1],"Audio QA "+wh[0]+"x"+wh[1]},null);
                    group.GetType().GetMethod("AddCustomSize",All).Invoke(group,new[]{size});index=count;
                }
                viewType.GetProperty("selectedSizeIndex",All).SetValue(view,index);view.Repaint();
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(Screen.width!=wh[0]||Screen.height!=wh[1]){if(DateTime.UtcNow>deadline)throw new Exception("Game View did not resize");await Task.Delay(50);}
                await Task.Delay(400);
                foreach(var id in new[]{"ambience-volume-up","ambience-volume-down","audio-dynamics-0","audio-dynamics-1","settings-back"})
                {
                    var element=root.Q<Button>(id);if(element==null)throw new Exception("Missing audio setting "+id);
                    var r=element.worldBound;if(r.width<1||r.height<1||r.xMin<0||r.yMin<0||r.xMax>root.worldBound.xMax||r.yMax>root.worldBound.yMax)throw new Exception("Audio setting out of bounds: "+id);
                }
                var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
                var bytes=(byte[])type.GetMethod("EncodeScreenToPng",All).Invoke(null,new object[]{Screen.width,Screen.height});
                Directory.CreateDirectory("Logs/AudioQA");File.WriteAllBytes("Logs/AudioQA/settings-"+wh[0]+"x"+wh[1]+".png",bytes);
            }
            return "Audio controls visible at 1920x1080, 2560x1080, 1600x1200; screenshots saved.";
        }
        finally
        {
            viewType.GetProperty("selectedSizeIndex",All).SetValue(view,original);view.Repaint();
            var back=root.Q<Button>("settings-back");if(back!=null){using var e=NavigationSubmitEvent.GetPooled();e.target=back;back.SendEvent(e);}
        }
    }
}
