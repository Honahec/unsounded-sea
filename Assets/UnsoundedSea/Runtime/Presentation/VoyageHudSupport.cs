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
        private static readonly string[] SupportTabs = { "整备", "行装", "船员", "档案", "仪式" };
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
            var head = Element("manifest-head");
            head.Add(Text("船上清单", "destination-title"));
            var supplies = Element("manifest-supplies");
            supplies.Add(ManifestIcon("lantern"));
            supplies.Add(Text("补给 " + Session.Supplies, "manifest-count"));
            head.Add(supplies); sheet.Add(head);
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
            sheet.Insert(1, Element("support-illustration-veil"));
            switch (supportTab)
            {
                case 0:
                    BuildPreparationManifest(scroll);
                    break;
                case 1:
                    BuildInventoryManifest(scroll, sheet);
                    break;
                case 2:
                    BuildCrewManifest(scroll);
                    break;
                case 3:
                    BuildArchiveManifest(scroll);
                    break;
                case 4:
                    BuildRitualManifest(scroll);
                    break;
            }
            sheet.Add(scroll);
            ScrollWheel.ForwardFrom(sheet, scroll);
            sheet.Add(Action("support-manifest-close", "返回航程", "primary", () => { supportOpen = false; Refresh(); }));
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

        private static string MaterialChoiceNote(VoyageNodeChoice choice)
        {
            if (!choice.Enabled) return string.IsNullOrEmpty(choice.DisabledReason) ? choice.Description : choice.DisabledReason;
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
            return note;
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
