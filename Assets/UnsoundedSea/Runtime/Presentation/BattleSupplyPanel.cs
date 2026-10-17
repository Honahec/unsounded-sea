using System;
using System.Linq;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal static class BattleSupplyPanel
    {
        public static VisualElement Build(BattleView view,Action<BattleCommand> submit,bool active,Action<Button,bool> availability=null)
        {
            var panel=new VisualElement();
            void Enable(Button button,bool eligible){if(availability!=null)availability(button,eligible);else button.SetEnabled(eligible);}
            foreach(var action in view.SupplyActions)
            {
                var supply=action;
                var options=new VisualElement();options.style.display=DisplayStyle.None;
                BattleCommand Command(string target)=>supply.Consumable?BattleCommand.Consume(supply.Id,target):BattleCommand.Investigate(supply.Id,target);
                var button=new Button(()=>{if(supply.Targets.Count==0)submit(Command(null));else options.style.display=options.style.display==DisplayStyle.None?DisplayStyle.Flex:DisplayStyle.None;}){name="supply-"+supply.Id,text=supply.Name+" ×"+supply.Uses,tooltip=supply.Description};
                button.AddToClassList("tool");Enable(button,active&&supply.Enabled&&view.PendingChoice==null);panel.Add(button);
                foreach(var target in supply.Targets){var choice=target;var pick=new Button(()=>submit(Command(choice.Id))){name="supply-target-"+supply.Id+"-"+choice.Id,text=choice.Name,tooltip=choice.Detail};pick.AddToClassList("tool");Enable(pick,active&&supply.Enabled&&choice.Enabled&&view.PendingChoice==null);options.Add(pick);}
                panel.Add(options);
            }
            if(view.Equipment.Count>0||view.SurveyedBeacon)
            {
                var fold=new Foldout{text="装备与航标",value=false};fold.AddToClassList("tool-name");
                foreach(var id in view.Equipment){var label=new Label(SupportCatalog.Item(id).Name+"："+SupportCatalog.Item(id).Description);label.style.whiteSpace=WhiteSpace.Normal;fold.Add(label);}
                if(view.SurveyedBeacon)fold.Add(new Label("已测航标：使用后或本场战斗结束时失效。"));
                panel.Add(fold);
            }
            return panel;
        }
        public static string EvidenceLabel(Evidence entry,int currentTurn)
        {
            var scope=entry.Scope==EvidenceScope.Turn?(entry.Turn==currentTurn?"当回合":"历史观察") : entry.Scope==EvidenceScope.Encounter?"本场":entry.Scope==EvidenceScope.Run?"本局":"长期";
            var verified=entry.Rule=="source-v1"?"来源已验证":entry.Rule=="anchor-v1"?"锚点已验证":"";
            return scope+" · "+entry.Source+(verified==""?"":" · "+verified);
        }
    }
}
