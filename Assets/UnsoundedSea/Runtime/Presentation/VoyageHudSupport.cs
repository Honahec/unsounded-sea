using System.Linq;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed partial class VoyageHud
    {
        private bool supportOpen;
        private void BuildSupportManifest()
        {
            PrepareModal();
            var scrim=Element("voyage-overlay");scrim.pickingMode=PickingMode.Position;var sheet=Element("action-book");
            sheet.Add(Text("船上清单","destination-title"));
            sheet.Add(Text($"补给 {Session.Supplies}/8 · 物资 {Session.Inventory.Count}/6 · 战斗带入 {Session.CarriedItems.Count}/2 · 装备 {Session.Equipment.Count}/3 · 被注视 {Session.Watched}/3","body"));
            var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.style.flexGrow=1;sheet.Add(scroll);
            foreach(var id in Session.CrewIds){var crew=SupportCatalog.CrewMember(id);scroll.Add(Text(crew.Name+" · "+(Session.InjuredCrew.Contains(id)?"受伤":Session.TrustedCrew.Contains(id)?"信任":"初识")+"\n"+SupportCatalog.CrewDescription(crew.Kind),"body"));}
            if(Session.EscortedCrew!=null)scroll.Add(Text("待护送（不提供支援）："+SupportCatalog.CrewMember(Session.EscortedCrew).Name,"body"));
            scroll.Add(Text("库存："+string.Join("、",Session.Inventory.GroupBy(x=>x).Select(g=>SupportCatalog.Item(g.Key).Name+" ×"+g.Count())),"body"));
            scroll.Add(Text("已带入："+string.Join("、",Session.CarriedItems.Select(x=>SupportCatalog.Item(x).Name)),"body"));
            foreach(var id in Session.Equipment)scroll.Add(Text("装备 · "+SupportCatalog.Item(id).Name+"："+SupportCatalog.Item(id).Description,"body"));
            foreach(var marker in Session.Markers)scroll.Add(Text("标记 · "+MarkerName(marker.Id)+" · 到期："+marker.Expiry+" · 来源："+marker.Source,"body"));
            foreach(var entry in Session.Archive)scroll.Add(Text("本局档案 · "+entry.Text+" · "+entry.SubjectType+" · 来源："+entry.Source,"body"));
            foreach(var entry in CardDeckPreparation.StoredArchive)scroll.Add(Text("长期见闻 · "+entry.Text+" · "+entry.SubjectType+"（不自动确认现场）","body"));
            var choices=Session.GetSupportChoices();
            scroll.Add(Text(choices.Count==0?"当前航点没有可执行的整备服务。":"可执行操作（价格、效果与替换在下方说明）","body"));
            foreach(var choice in choices)
            {
                var selected=choice;
                var button=Action("support-"+choice.Id,choice.Title+"\n"+choice.Description,"node-choice",()=>Change(()=>Session.ManageSupport(selected.Id)),choice.Enabled);
                button.style.whiteSpace=WhiteSpace.Normal;button.style.minHeight=52;scroll.Add(button);
            }
            sheet.Add(Action("support-manifest-close","返回航程 / Esc","primary",()=>{supportOpen=false;Refresh();}));
            scrim.Add(sheet);root.Add(scrim);root.schedule.Execute(()=>root.Q<Button>("support-manifest-close")?.Focus());
        }
        private static string MarkerName(string id)=>id=="surveyed-beacon"?"已测航标":id=="docking-voucher"?"接驳凭证":"封存标本";
    }
}
