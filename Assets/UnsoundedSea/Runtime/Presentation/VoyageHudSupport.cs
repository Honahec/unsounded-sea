using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed partial class VoyageHud
    {
        private bool supportOpen;
        private int supportTab;
        private static readonly string[] SupportTabs = { "筹备", "物资", "船员", "档案", "仪式" };
        private static readonly string[] SupportImages = { "supply", "supply", "rescue", "archive", "ritual" };

        private void OpenSupport(int tab)
        {
            supportTab = tab;
            supportOpen = true;
            Refresh();
        }

        private void BuildSupportManifest()
        {
            PrepareModal();
            var scrim = Element("voyage-overlay");
            scrim.pickingMode = PickingMode.Position;
            var sheet = Element("action-book");
            sheet.AddToClassList("support-manifest");
            sheet.name = "support-manifest";
            sheet.Add(Text("船上清单", "destination-title"));
            sheet.Add(Text($"补给 {Session.Supplies}/8   物资 {Session.Inventory.Count}/6   带入 {Session.CarriedItems.Count}/2   装备 {Session.Equipment.Count}/3   船员 {Session.CrewIds.Count}/4", "muted"));
            var tabs = Element("support-tabs");
            for (var index = 0; index < SupportTabs.Length; index++)
            {
                var tab = index;
                var button = Action("support-tab-" + index, SupportTabs[index], "secondary", () => OpenSupport(tab));
                button.EnableInClassList("selected", supportTab == index);
                tabs.Add(button);
            }
            sheet.Add(tabs);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "support-scroll" };
            scroll.AddToClassList("support-scroll");
            var art = new VisualElement { name = "support-illustration", pickingMode = PickingMode.Ignore };
            art.AddToClassList("support-illustration");
            art.style.backgroundImage = Resources.Load<Texture2D>("SupportArt/" + SupportImages[supportTab]);
            sheet.Insert(0, art);
            switch (supportTab)
            {
                case 0:
                    scroll.Add(Text(Session.PreparationBriefing, "body"));
                    var destination = Session.Selected ?? Session.Current;
                    scroll.Add(Text(destination.Name, "panel-title"));
                    scroll.Add(Text(PreviewRoute(Session, destination), "body"));
                    scroll.Add(Text("已带入 · " + string.Join("、", Session.CarriedItems.Select(id => SupportCatalog.Item(id).Name)), "muted"));
                    scroll.Add(Text("调查工具 · " + string.Join("、", Session.ToolIds.Select(id => SupportCatalog.Item(id).Name)), "muted"));
                    scroll.Add(Action("support-action-book", Session.CanEditDeck ? "整备行动簿" : "查看行动簿", "secondary", () => { supportOpen = false; deckOpen = true; Refresh(); }));
                    break;
                case 1:
                    scroll.Add(Text(Session.CanEditDeck ? "可调整携带 · 每场两份物资" : "配装仅限整备点", "muted"));
                    foreach (var group in Session.Inventory.GroupBy(id => id))
                    {
                        var item = SupportCatalog.Item(group.Key);
                        SupportEntry(scroll, item.Name + " ×" + group.Count() + " · 带入 " + Session.CarriedItems.Count(id => id == group.Key), item.Description, true, item);
                    }
                    foreach (var id in Session.OwnedEquipment)
                    {
                        var item = SupportCatalog.Item(id);
                        SupportEntry(scroll, item.Name + (Session.Equipment.Contains(id) ? " · 已装备" : " · 未装备"), item.Description, true, item);
                    }
                    foreach (var id in Session.LearnedToolIds)
                    {
                        var item = SupportCatalog.Item(id);
                        SupportEntry(scroll, item.Name + (Session.ToolIds.Contains(id) ? " · 已装备" : " · 未装备"), item.Description, true, item);
                    }
                    SupportEntry(scroll, "旧海图 ×" + Session.ChartUses + " · 测绘望远镜 ×" + Session.LookoutUses, "选路时使用；没有新航路不扣次数，每幕恢复。");
                    scroll.Add(Action("support-tools", Session.CanEditDeck ? "调整调查工具" : "查看调查工具", "secondary", () => { supportOpen = false; deckOpen = true; Refresh(); }));
                    break;
                case 2:
                    scroll.Add(Text("每人每场一次 · 同回合至多一次支援", "muted"));
                    foreach (var id in Session.CrewIds)
                    {
                        var crew = SupportCatalog.CrewMember(id);
                        SupportEntry(scroll, crew.Name + " · " + (Session.InjuredCrew.Contains(id) ? "受伤" : Session.TrustedCrew.Contains(id) ? "信任" : "初识"),
                            SupportCatalog.CrewDescription(crew.Kind));
                    }
                    if (Session.EscortedCrew != null)
                        SupportEntry(scroll, "待邀请 · " + SupportCatalog.CrewMember(Session.EscortedCrew).Name, "加入后，下场提供支援。");
                    break;
                case 3:
                    foreach (var marker in Session.Markers)
                        SupportEntry(scroll, MarkerName(marker.Id), "到期：" + marker.Expiry + " · 来源：" + marker.Source);
                    foreach (var fact in Session.Knowledge) SupportEntry(scroll, "航程情报", fact);
                    foreach (var entry in Session.Archive)
                        SupportEntry(scroll, "本局档案 · " + entry.SubjectType, entry.Text + "\n来源：" + entry.Source);
                    foreach (var entry in CardDeckPreparation.StoredArchive)
                        SupportEntry(scroll, "长期见闻 · " + entry.SubjectType, entry.Text + "\n仅供查阅，不自动确认当前现场。");
                    scroll.Add(Text("阅读不收费；旧档用于本场仍需对照旧档，并以当前观察核验。", "muted"));
                    break;
                case 4:
                    scroll.Add(Text(Session.RitualStatus, "body"));
                    break;
            }
            var choices = Session.GetSupportChoices().Where(choice => VoyageSession.IsLoadoutChoice(choice)&&SupportChoiceTab(choice.Id) == supportTab).ToArray();
            if (choices.Length > 0) scroll.Add(Text("配装", "section-label"));
            foreach (var choice in choices)
            {
                var selected = choice;
                var row = Element("support-entry");
                row.tooltip = choice.Description;
                var button = Action("support-" + choice.Id, choice.Title, "secondary", () => Change(() => Session.ManageSupport(selected.Id), selected.Title), choice.Enabled);
                button.tooltip = choice.Description;
                var item = ChoiceItem(choice.Id);
                if (item != null)
                {
                    var applicability = BossApplicability.ItemLabel(item);
                    if (!string.IsNullOrEmpty(applicability)) UiKeywords.Attach(button, applicability);
                }
                row.Add(button);
                var note = supportTab == 1 ? MaterialChoiceNote(choice) : choice.Description;
                if (!string.IsNullOrEmpty(note)) row.Add(Text(note, "node-choice-description"));
                scroll.Add(row);
            }
            sheet.Add(scroll);
            ScrollWheel.ForwardFrom(sheet, scroll);
            sheet.Add(Action("support-manifest-close", "返回航程 / Esc", "primary", () => { supportOpen = false; Refresh(); }));
            scrim.Add(sheet);
            root.Add(scrim);
            root.schedule.Execute(() => root.Q<Button>("support-tab-" + supportTab)?.Focus());
        }

        internal static string PreviewRoute(VoyageSession session, VoyageStop stop)
        {
            var preview = session.PreviewStop(stop.Id);
            if (stop.Kind != RunNodeKind.Battle) return preview;
            // Read the encounter's possible channels, never its seed or locked current intent.
            var enemies = VoyageEncounterCatalog.Resolve(stop.EncounterId).encounter.enemies;
            var channels = enemies.SelectMany(enemy => enemy.intents).Where(intent => intent.amount > 0).Select(intent => intent.channel).Distinct().ToArray();
            var danger = string.Join("、", channels.Select(channel => channel == DamageChannel.Health ? "生命伤害" : "理智冲击"));
            var style=session.PreviewStyle(stop);
            return (style==EnemyStyle.Hidden?"隐匿型":"节奏型") + " · " + danger +(stop.Perilous?"\n险航 · 双敌 · 两次收获":"")+"\n"+preview;
        }

        private static void SupportEntry(VisualElement parent, string title, string detail, bool tooltipOnly = false, ItemSpec item = null)
        {
            var entry = Element("support-entry");
            var applicability = item == null ? "" : BossApplicability.ItemLabel(item);
            var heading = Text(string.IsNullOrEmpty(applicability) ? title : title + " · " + applicability, "node-choice-title");
            heading.pickingMode = PickingMode.Position;
            heading.tooltip = detail;
            if (!string.IsNullOrEmpty(applicability)) UiKeywords.Attach(heading, applicability);
            entry.Add(heading);
            if (!tooltipOnly) entry.Add(Text(detail, "node-choice-description"));
            parent.Add(entry);
        }

        private static string MaterialChoiceNote(VoyageNodeChoice choice)
        {
            var verb = choice.Id.Split(':')[0].Split('@')[0];
            string note;
            switch (verb)
            {
                case "starter": note = "三选一 · 每航程一次"; break;
                case "equip": note = ""; break;
                case "pack": note = choice.Description.Split('·')[0].Trim(); break;
                case "unpack": note = "移回库存"; break;
                case "buy": note = choice.Description.Split('。')[0]; break;
                case "tea": note = "消耗 1 份 · 恢复 5 理智"; break;
                case "gunsmith": note = choice.Description.Split('。')[0]; break;
                case "repair-boat": note = "支付 1 补给 · 轮机长首次免费"; break;
                default: note = choice.Description; break;
            }
            if (choice.Enabled) return note;
            switch (verb)
            {
                case "equip": return "不可用 · 装备栏已满或引擎冲突";
                case "pack": return "不可用 · 带入栏已满、库存不足或工具上限";
                case "buy": return "不可用 · 补给不足或库存已满";
                case "tea": return "不可用 · 理智已满";
                case "gunsmith": return "不可用 · 库存已满或次数已达上限";
                case "repair-boat": return "不可用 · 已取得工具或补给不足";
                default: return string.IsNullOrEmpty(note) ? "当前不可用" : note + " · 当前不可用";
            }
        }

        private static int SupportChoiceTab(string id)
        {
            var verb = id.Split(':')[0].Split('@')[0];
            switch (verb)
            {
                case "ritual": return 4;
                case "hire": case "rescue": case "accept": case "escort-decline":
                case "treat": case "trust": case "risk": case "crew": return 2;
                case "survey": case "archive": case "specimen": case "voucher": case "gaze": return 3;
                case "starter": case "equip": case "pack": case "unpack":
                case "buy": case "tea": case "gunsmith": case "repair-boat": return 1;
                default: return 0;
            }
        }


        private static ItemSpec ChoiceItem(string id)
        {
            var parts = id.Split('@')[0].Split(':');
            if (parts.Length < 2) return null;
            return SupportCatalog.Items.FirstOrDefault(item => item.Id == parts[1]);
        }

        private static string MarkerName(string id) => id == "surveyed-beacon" ? "已测航标" : id == "docking-voucher" ? "接驳凭证" : id == "sealed-specimen" ? "封存标本" : id;
    }
}
