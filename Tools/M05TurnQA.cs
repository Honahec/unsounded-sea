using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

public static class M05TurnQA
{
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static async Task Ready()
    {
        for(var i=0;i<600;i++){if(Director!=null&&!Director.Busy)return;await Task.Delay(50);}
        throw new Exception("M05 presentation did not unlock.");
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void CheckVital(VisualElement root,string name,int value,int max)
    {
        Require(root.Q<Label>("battle-"+name).text==value+" / "+max,name+" number is not the actual hit snapshot.");
        var width=root.Q(className:name).Q(className:"vital-fill").style.width.value;
        Require(width.unit==LengthUnit.Percent&&Mathf.Abs(width.value-100f*value/max)<.01f,name+" fill disagrees with its number.");
    }
    public static Task<string> Reduced() => Main(true);
    public static async Task<string> Main(bool reduced=false)
    {
        var saved=File.Exists(VoyageRuntime.SavePath)?File.ReadAllText(VoyageRuntime.SavePath):null;
        var old=GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion=reduced;
            VoyageRuntime.StartDebugEncounter("m05","abyss",true);await Task.Delay(500);await Ready();
            var d=Director;
            for(var i=0;i<5;i++)
            {
                if(d.View.Actions==0){Require(d.Submit(BattleCommand.EndTurn()).Success,"Preparation turn failed.");await Ready();}
                var card=d.View.Hand.Single(c=>c.Definition.Id=="E05-01");
                Require(d.Submit(BattleCommand.Play(card.InstanceId)).Success,"Summon failed.");await Ready();
            }
            Require(d.Submit(BattleCommand.EndTurn()).Success,"Activation turn failed.");await Ready();
            int hitCount=0;
            for(var turn=0;turn<2;turn++)
            {
                var root=d.GetComponent<UIDocument>().rootVisualElement;
                var health=d.View.Health;var sanity=d.View.Sanity;
                var observed=new List<ResolvedEnemyAction>();string failure=null;
                Action<ResolvedEnemyAction> onHit=a=>
                {
                    try
                    {
                        CheckVital(root,"health",a.Health,d.scenario.encounter.playerHealth);
                        CheckVital(root,"sanity",a.Sanity,d.scenario.encounter.playerSanity);
                        Require(d.Busy,"Input unlocked before hit sequence completed.");
                        observed.Add(a);
                    }
                    catch(Exception e){failure=e.Message;}
                };
                d.PresentedEnemyAction+=onHit;
                try
                {
                    Require(d.Submit(BattleCommand.EndTurn()).Success,"Observed enemy turn failed.");
                    var expected=d.View.ResolvedEnemyActions.ToArray();
                    Require(expected.Last().EnemyId==d.View.Enemies[0].Id,"Boss was not last in actual rule resolution.");
                    CheckVital(root,"health",health,d.scenario.encounter.playerHealth);
                    CheckVital(root,"sanity",sanity,d.scenario.encounter.playerSanity);
                    d.SetPaused(true);var pausedCount=observed.Count;await Task.Delay(250);
                    Require(observed.Count==pausedCount,"Hits continued while paused.");d.SetPaused(false);
                    for(var i=0;i<300&&observed.Count==0&&failure==null;i++)await Task.Delay(20);
                    Require(failure==null,failure);Require(observed.Count>0,"No first visual hit.");
                    d.SetPaused(true);var first=observed.Last();var frozenCount=observed.Count;
                    await Task.Delay(200);
                    CheckVital(root,"health",first.Health,d.scenario.encounter.playerHealth);
                    CheckVital(root,"sanity",first.Sanity,d.scenario.encounter.playerSanity);
                    Require(observed.Count==frozenCount,"Next enemy attacked during pause.");
                    d.SetPaused(false);
                    await Ready();await Task.Delay(100);
                    Require(failure==null,failure);
                    Require(observed.Select(a=>a.EnemyId).SequenceEqual(expected.Select(a=>a.EnemyId)),"Visual hit order/count differed from resolved attacks.");
                    CheckVital(root,"health",d.View.Health,d.scenario.encounter.playerHealth);
                    CheckVital(root,"sanity",d.View.Sanity,d.scenario.encounter.playerSanity);
                    hitCount+=observed.Count;
                }
                finally{d.PresentedEnemyAction-=onHit;}
            }
            Require(saved==(File.Exists(VoyageRuntime.SavePath)?File.ReadAllText(VoyageRuntime.SavePath):null),"Formal save changed.");
            return "reduced="+reduced+", synchronized hits="+hitCount+", guard-before-boss order, paused freeze, numbers/fills and save preservation passed.";
        }
        finally{GameSettings.ReducedMotion=old;}
    }
}
