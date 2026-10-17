using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// CLI acceptance uses the real UI, legal commands, and separate on-disk voyage storage.
// Game View is resized BEFORE capture; output pixels are never stretched between aspect ratios.
public static class UiExperienceQA
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(1600,1200)};
    static readonly List<object> Captures=new List<object>();
    static readonly List<object> Journey=new List<object>();
    static string Dir=>Path.GetFullPath("Logs/UiExperience");
    static VisualElement Root=>UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
    static BattleDirector Director=>UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Status(string stage)=>File.WriteAllText(Path.Combine(Dir,"status.json"),JsonConvert.SerializeObject(new{stage,captures=Captures.Count,journey=Journey},Formatting.Indented));
    static async Task Wait(Func<bool> condition,string label)
    {for(int i=0;i<400;i++){if(condition())return;await Task.Delay(50);}throw new InvalidOperationException("Wait failed: "+label);}
    static void Click(string name)
    {
        var button=Root.Q<Button>(name);Check(button!=null&&button.enabledInHierarchy,"Missing or disabled UI: "+name);
        button.Focus();using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    static void Key(KeyCode key)
    {using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=key})){e.target=Root;Root.SendEvent(e);}}
    static void RightClick(VisualElement target)
    {using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=1,mousePosition=target.worldBound.center})){e.target=target;target.SendEvent(e);}}
    static async Task Load(string scene)
    {SceneManager.LoadScene(scene);await Task.Delay(500);await Wait(()=>SceneManager.GetActiveScene().name==scene&&UnityEngine.Object.FindAnyObjectByType<UIDocument>()!=null,scene);}
    public static async Task<object> Resize(int width,int height)
    {
        var asm=typeof(Editor).Assembly;var viewType=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(viewType);
        if(!SessionState.GetBool("UiExperienceQA.sizeSaved",false)){SessionState.SetInt("UiExperienceQA.originalSize",(int)viewType.GetProperty("selectedSizeIndex",All).GetValue(view));SessionState.SetBool("UiExperienceQA.sizeSaved",true);}
        var sizesType=asm.GetType("UnityEditor.GameViewSizes");var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);var sizes=singleton.GetProperty("instance",All).GetValue(null);
        var groupType=asm.GetType("UnityEditor.GameViewSizeGroupType");var group=sizesType.GetMethod("GetGroup",All).Invoke(sizes,new[]{Enum.Parse(groupType,"Standalone")});
        var builtins=(int)group.GetType().GetMethod("GetBuiltinCount",All).Invoke(group,null);var customs=(int)group.GetType().GetMethod("GetCustomCount",All).Invoke(group,null);
        int index=-1;
        for(int i=0;i<builtins+customs;i++)
        {
            var size=group.GetType().GetMethod("GetGameViewSize",All).Invoke(group,new object[]{i});
            if((int)size.GetType().GetProperty("width",All).GetValue(size)==width&&(int)size.GetType().GetProperty("height",All).GetValue(size)==height){index=i;break;}
        }
        if(index<0)
        {
            var sizeType=asm.GetType("UnityEditor.GameViewSize");var kind=asm.GetType("UnityEditor.GameViewSizeType");
            var size=Activator.CreateInstance(sizeType,All,null,new object[]{Enum.Parse(kind,"FixedResolution"),width,height,"UI acceptance "+width+"x"+height},null);
            group.GetType().GetMethod("AddCustomSize",All).Invoke(group,new[]{size});index=builtins+customs;
        }
        viewType.GetProperty("selectedSizeIndex",All).SetValue(view,index);view.Repaint();
        await Wait(()=>Screen.width==width&&Screen.height==height,"actual Game View "+width+"x"+height);await Task.Delay(300);
        return new{width=Screen.width,height=Screen.height,rootWidth=Root.resolvedStyle.width,rootHeight=Root.resolvedStyle.height};
    }
    static async Task Capture(string page)
    {
        Status(page);
        foreach(var size in Sizes)
        {
            var dimensions=await Resize(size.x,size.y);
            var path=Path.Combine(Dir,size.x+"x"+size.y,page+".png");Directory.CreateDirectory(Path.GetDirectoryName(path));
            var captureType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x=>x!=null);
            var bytes=(byte[])captureType.GetMethod("EncodeScreenToPng",All).Invoke(null,new object[]{size.x,size.y});File.WriteAllBytes(path,bytes);
            var overlays=Root.Query<VisualElement>().ToList().Where(x=>new[]{"debrief-sheet","region-intro-sheet","reward-replacement-sheet","crew-selection-sheet","relay-board","salt-board"}.Any(x.ClassListContains)).Select(x=>new{x.name,classes=string.Join(" ",x.GetClasses()),bounds=x.worldBound.ToString()}).ToArray();
            Captures.Add(new{page,path,dimensions,scene=SceneManager.GetActiveScene().name,debug=VoyageRuntime.IsDebugSession,overlays});
        }
        File.WriteAllText(Path.Combine(Dir,"captures.json"),JsonConvert.SerializeObject(Captures,Formatting.Indented));
    }
    static async Task DebugBattle(string id,string region,bool boss)
    {
        VoyageRuntime.StartDebugEncounter(id,region,boss);await Task.Delay(800);await Wait(()=>Director?.View!=null&&!Director.Busy,id);
    }
    static async Task DebugNode(RunNodeKind kind)
    {VoyageRuntime.StartDebugNode("fog",kind);await Task.Delay(650);await Wait(()=>Root.Q<Button>("voyage-support-manifest")!=null,"node "+kind);}
    public static string StartLayouts(){Directory.CreateDirectory(Dir);_ = RunLayouts();return "running; see Logs/UiExperience/status.json";}
    static async Task RunLayouts()
    {
        try
        {
            Check(Application.isPlaying,"Enter Play Mode first.");VoyageRuntime.BeginValidation(Path.Combine(Dir,"layout-save"));await Load("MainMenu");
            await Capture("main-menu");Click("menu-crew");await Capture("crew-selection");Click("crew-selection-close");
            await DebugBattle("voyage-fog-4","fog",false);await Capture("ordinary-encounter");
            var source=Director.View.Tools.FirstOrDefault(x=>x.Definition.Kind==InvestigationKind.SoundSource&&x.Enabled&&x.Targets.Any(t=>t.Enabled));
            if(source!=null){var r=Director.Submit(BattleCommand.Investigate(source.Definition.Id,source.Targets.First(x=>x.Enabled).Id));Check(r.Success,r.Error);await Wait(()=>!Director.Busy,"investigation");await Capture("current-source");}
            foreach(var item in new[]{("fog-visitors","fog"),("m02","bones"),("m03","inverted"),("m04","fleet"),("m05","abyss")})
            {await DebugBattle(item.Item1,item.Item2,true);await Capture(item.Item1);}
            foreach(var kind in new[]{RunNodeKind.Supply,RunNodeKind.Event,RunNodeKind.Archive}){await DebugNode(kind);await Capture(kind.ToString().ToLowerInvariant()+"-node");}
            await DebugNode(RunNodeKind.Supply);Click("arrival-action-book");await Capture("action-book");Click("action-book-close");
            Click("voyage-menu");await Capture("pause-menu");Click("pause-resume");
            Status("layouts-complete");
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Dir,"layout-error.txt"),e.ToString());Status("layouts-failed: "+e.Message);}
    }
    static int Threat(EnemyView enemy,bool sanity)
    {
        if(enemy.Health<=0)return 0;
        if(!enemy.IntentConfirmed)return sanity?2:5;
        var m=Regex.Match(enemy.Intent,@"(\d+) 点(生命伤害|理智冲击)");
        return m.Success&&(m.Groups[2].Value=="理智冲击")==sanity?int.Parse(m.Groups[1].Value):0;
    }
    static BattleCommand Suggest(BattleView v)
    {
        if(v.PendingChoice!=null)return BattleCommand.Reorder(v.PendingChoice.Options[0].Id);
        if(v.ObjectiveTarget>0&&v.Enemies.Any(x=>x.Health>0))
        {
            var seal=v.Hand.FirstOrDefault(x=>x.Definition.Id=="E01-01"&&x.Enabled);if(seal!=null)return BattleCommand.Play(seal.InstanceId);
        }
        var tool=v.Tools.FirstOrDefault(x=>x.Definition.Kind==InvestigationKind.VisibleAction&&x.Enabled&&x.Targets.Any(t=>t.Enabled&&v.Enemies.Any(e=>e.Id==t.Id&&!e.IntentConfirmed)));
        if(tool!=null)return BattleCommand.Investigate(tool.Definition.Id,tool.Targets.First(t=>t.Enabled&&v.Enemies.Any(e=>e.Id==t.Id&&!e.IntentConfirmed)).Id);
        var crew=v.Supports.FirstOrDefault(x=>x.Enabled&&x.Definition.Kind==CrewSupportKind.Medic&&(v.Health<=52||v.Sanity<=25));if(crew!=null)return BattleCommand.Support(crew.Definition.Id,crew.Targets.FirstOrDefault(x=>x.Enabled)?.Id);
        if(v.Health<38){var medicine=v.SupplyActions.FirstOrDefault(x=>x.Id=="i10"&&x.Enabled);if(medicine!=null)return BattleCommand.Consume(medicine.Id,medicine.Targets.FirstOrDefault(x=>x.Enabled)?.Id);}
        double best=0;BattleCommand chosen=null;
        var hp=Math.Max(0,v.Enemies.Sum(e=>Threat(e,false))-v.Block);var sanity=Math.Max(0,v.Enemies.Sum(e=>Threat(e,true))-v.Ward);
        foreach(var card in v.Hand.Where(x=>x.Enabled&&!x.IsEncounter))
        foreach(var target in card.Targets.Count==0?new string[]{null}:card.Targets.Where(x=>x.Enabled).Select(x=>x.Id))
        {
            int n=CardCatalog.Number(card.Definition.Id);var enemy=v.Enemies.FirstOrDefault(x=>target==x.Id||target=="enemy:"+x.Id);double score=0;
            if(n<=18&&enemy!=null){var damage=n==2?(enemy.IntentConfirmed?9:5):n==12?(v.Block>=6?7:5):card.Definition.Effects[0].Amount;score=Math.Min(enemy.Health,damage)*1.3+(damage>=enemy.Health?Threat(enemy,false)+Threat(enemy,true)*1.5+8:0);if(n==12)score+=Math.Min(hp,3);}
            switch(n)
            {
                case 19:score=Math.Min(hp,10)*1.1;break;case 20:score=Math.Min(sanity,6)*1.8;break;case 21:score=Math.Min(hp,7)*1.1+Math.Min(sanity,3)*1.8;break;
                case 22:score=Math.Min(4,60-v.Health)*1.4;break;case 37:score=3;break;case 39:score=enemy!=null&&!enemy.IntentConfirmed?5:2;break;
                case 41:score=2.5;break;case 55:if(enemy!=null)score=Math.Min(enemy.Health,4)*1.3+(enemy.IntentConfirmed&&enemy.Intent.Contains("可打断")?Threat(enemy,false)+Threat(enemy,true)*1.8:0);break;
                case 71:score=1;break;case 16:score=enemy==null?0:Math.Min(enemy.Health,3)*1.3;break;case 38:score=3;break;
                case 23:score=Math.Min(sanity,enemy==null?0:Threat(enemy,true))*1.8+2;break;case 26:score=Math.Min(hp,5)+Math.Min(sanity,2)*1.8;break;
                case 34:score=Math.Min(hp,6);break;case 50:score=4;break;
            }
            score/=Math.Max(1,card.ActionCost);if(score>best){best=score;chosen=BattleCommand.Play(card.InstanceId,target);}
        }
        return chosen??BattleCommand.EndTurn();
    }
    public static string StartCampaign(){Directory.CreateDirectory(Dir);_ = RunCampaign();return "running; see Logs/UiExperience/status.json";}
    static async Task RunCampaign()
    {
        var reducedHadKey=PlayerPrefs.HasKey("settings.reducedMotion");var reduced=PlayerPrefs.GetInt("settings.reducedMotion",0);
        try
        {
            Check(Application.isPlaying,"Enter Play Mode first.");
            VoyageRuntime.BeginValidation(Path.Combine(Dir,"campaign-save"));VoyageRuntime.StartNew(7);await Load("Voyage");
            bool replaced=false,capturedMap=false,capturedDebrief=false,capturedReward=false;string added=null;var battleCommands=new List<BattleCommand>();
            for(int step=0;step<2000&&VoyageRuntime.Session.Act==1;step++)
            {
                var s=VoyageRuntime.Session;Status("campaign: "+s.Phase+" "+s.Current.Id);
                if(s.DebriefPending)
                {
                    if(!capturedDebrief){await Capture("battle-debrief");var scrim=Root.Q("voyage-flow");Check(!Root.Q<Button>("voyage-menu").enabledInHierarchy,"Underlying menu stayed active.");Key(KeyCode.B);Check(Root.Q("action-book")==null,"Book opened behind summary.");capturedDebrief=true;}
                    Click("voyage-flow-continue");continue;
                }
                switch(s.Phase)
                {
                    case VoyagePhase.ChoosingRoute:
                        if(!capturedMap){Click("voyage-chart-toggle");await Capture("voyage-map");Click("voyage-chart-toggle");capturedMap=true;}
                        Click("route-"+s.Choices[0].Id);Click("set-sail");await Wait(()=>s.Phase!=VoyagePhase.Sailing,"sailing");break;
                    case VoyagePhase.Arrived:
                        if(s.Current.Kind==RunNodeKind.Battle||s.Current.Kind==RunNodeKind.Boss){Click("arrival-investigate");await Task.Delay(500);await Wait(()=>Director?.View!=null,"real battle");battleCommands.Clear();}
                        else if(s.CanContinue)Click("arrival-continue");
                        else
                        {
                            if(s.Current.Kind==RunNodeKind.Supply)await Capture("campaign-supply");
                            var preferred=new[]{s.Sanity<17?"calm":s.Health<36?"repair":"rest","rest","seal","observe","listen","tow","depart","leave","briefing"};
                            var choice=preferred.Select(id=>s.GetNodeChoices().FirstOrDefault(x=>x.Id==id&&x.Enabled)).FirstOrDefault(x=>x!=null)??s.GetNodeChoices().First(x=>x.Enabled);
                            Click("node-choice-"+choice.Id);
                        }
                        break;
                    case VoyagePhase.Encounter:
                        await Wait(()=>Director?.View!=null&&!Director.Busy,"command feedback");
                        if(s.Phase!=VoyagePhase.Encounter)break;
                        var before=Director.View;Check(before.Phase!=BattlePhase.Defeat,"Campaign lost at "+s.Current.EncounterId);
                        var command=Suggest(before);var result=Director.Submit(command);Check(result.Success,result.Error);
                        battleCommands.Add(command);
                        if(Director.View.Phase==BattlePhase.Victory)
                        {
                            var final=Director.View;Journey.Add(new{stop=s.Current.Id,encounter=s.Current.EncounterId,final.Turn,final.Health,final.Sanity,final.HealthDamageTaken,final.SanityDamageTaken,commands=battleCommands.ToArray(),deck=s.DeckIds.ToArray()});
                            await Wait(()=>UnityEngine.Object.FindAnyObjectByType<VoyageHud>()!=null,"return after real victory");
                        }
                        break;
                    case VoyagePhase.Reward:
                        if(!capturedReward){await Capture("reward-choice");capturedReward=true;}
                        if(!replaced&&s.RewardCards.Count>0)
                        {
                            var reward=s.RewardCards.OrderBy(x=>new[]{16,3,14,38,50,9}.Contains(CardCatalog.Number(x.Id))?0:1).First();added=reward.Id;var old="c071";
                            Click("reward-"+added);var snapshot=JsonConvert.SerializeObject(s.Export());Click("reward-replace");await Capture("d1-replacement");
                            Key(KeyCode.Escape);Check(snapshot==JsonConvert.SerializeObject(s.Export()),"Esc consumed a reward.");
                            Click("reward-replace");RightClick(Root.Q("reward-replacement"));Check(snapshot==JsonConvert.SerializeObject(s.Export()),"Right click consumed a reward.");
                            Click("reward-replace");Click("reward-replacement-cancel");Check(snapshot==JsonConvert.SerializeObject(s.Export()),"Cancel consumed a reward.");
                            Click("reward-replace");Click("replace-deck-"+old);Check(s.DeckIds.Contains(added)&&!s.DeckIds.Contains(old),"D1 did not replace the deck.");replaced=true;
                        }
                        else Click("reward-supplies");
                        break;
                    case VoyagePhase.ActComplete:Click("region-bones");break;
                    case VoyagePhase.Failed:throw new InvalidOperationException("Campaign failed.");
                    default:throw new InvalidOperationException("Unexpected campaign phase: "+s.Phase);
                }
                await Task.Delay(80);
            }
            Check(VoyageRuntime.Session.Act==2&&replaced,"Did not enter act 2 through legal play and D1.");
            Check(Journey.Count>=5,"Expected four ordinary victories and the act boss.");
            Check(Journey.Skip(1).Any(),"No battle after replacement.");
            await Capture("region-intro");PlayerPrefs.SetInt("settings.reducedMotion",1);await Load("Voyage");Check(VoyageRuntime.Session.RegionIntroPending,"Reduced motion skipped the region flow.");await Capture("region-intro-reduced-motion");Click("voyage-flow-continue");
            File.WriteAllText(Path.Combine(Dir,"campaign-result.json"),JsonConvert.SerializeObject(new{success=true,seed=7,added,actualVictories=Journey,act=VoyageRuntime.Session.Act,deferred=new[]{"perilous encounter/rewards","enemy style/surge"}},Formatting.Indented));Status("campaign-complete");
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Dir,"campaign-error.txt"),e.ToString());Status("campaign-failed: "+e.Message);}
        finally{if(reducedHadKey)PlayerPrefs.SetInt("settings.reducedMotion",reduced);else PlayerPrefs.DeleteKey("settings.reducedMotion");PlayerPrefs.Save();}
    }
}
