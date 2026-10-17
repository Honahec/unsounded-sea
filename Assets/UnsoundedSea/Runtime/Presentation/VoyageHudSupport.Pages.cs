using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed partial class VoyageHud
    {
        private string manifestInspectedId;

        private static VisualElement ManifestSection(VisualElement parent, string title, string count = null)
        {
            var section = Element("manifest-section");
            var head = Element("manifest-section-head");
            head.Add(Text(title, "manifest-section-title"));
            if (!string.IsNullOrEmpty(count)) head.Add(Text(count, "manifest-count"));
            section.Add(head); parent.Add(section); return section;
        }

        private static SeaUiArt ManifestIcon(string motif)
        {
            var icon = new SeaUiArt(motif); icon.AddToClassList("manifest-icon"); return icon;
        }

        private static string ItemIcon(ItemSpec item) => item.Kind == ItemKind.Equipment ? "seal"
            : item.Kind == ItemKind.Tool ? item.Id == "lantern" ? "lantern" : item.Id == "listener" ? "ear" : "shell"
            : item.Id == "i10" ? "heart" : item.Id == "i11" || item.Id == "i12" ? "eye" : "scroll";

        private static void ManifestSlots(VisualElement parent, string title, IReadOnlyList<string> ids, int capacity, string emptyIcon)
        {
            var section = ManifestSection(parent, title, ids.Count + " / " + capacity);
            var slots = Element("manifest-slots");
            for (int i = 0; i < capacity; i++)
            {
                var slot = Element("manifest-slot");
                if (i < ids.Count)
                {
                    var item = SupportCatalog.Item(ids[i]);
                    slot.Add(ManifestIcon(ItemIcon(item))); slot.Add(Text(item.Name, "manifest-slot-name"));
                    slot.pickingMode = PickingMode.Position; slot.tooltip = UiRules.Describe(item.Description);
                    UiKeywords.Attach(slot, item.Description);
                }
                else
                {
                    slot.AddToClassList("empty"); slot.Add(ManifestIcon(emptyIcon)); slot.Add(Text("空位", "manifest-slot-name"));
                }
                slots.Add(slot);
            }
            section.Add(slots);
        }

        private void BuildPreparationManifest(VisualElement parent)
        {
            var destination = Session.Selected ?? Session.Current;
            var location = Element("manifest-destination");
            location.Add(ManifestIcon("compass"));
            var copy = Element("manifest-destination-copy");
            copy.Add(Text(Session.Selected == null ? "当前停泊" : "下一航点", "manifest-eyebrow"));
            copy.Add(Text(destination.Name, "manifest-section-title"));
            copy.Add(Text(destination.Clue, "manifest-caption"));
            location.Add(copy); parent.Add(location);
            ManifestSlots(parent, "战斗物资", Session.CarriedItems, 2, "scroll");
            ManifestSlots(parent, "调查工具", Session.ToolIds.Concat(Session.CarriedItems.Where(id => id == "i09")).ToArray(), 2, "lantern");
            ManifestSlots(parent, "随身装备", Session.Equipment, 3, "seal");
            if (!Session.IsDebug)
            {
                var briefing = new Foldout { text = "海域威胁", value = false, name = "manifest-briefing" };
                briefing.AddToClassList("manifest-record-group");
                briefing.Add(Text(Session.PreparationBriefing.Split('\n')[0].Replace("威胁：", ""), "manifest-record-body"));
                parent.Add(briefing);
            }
        }

        private void BuildInventoryManifest(VisualElement parent, VisualElement inspectorParent)
        {
            if (!Session.CanEditDeck) parent.Add(Text("抵达补给船后可更换行装", "manifest-caption"));
            var detail = Element("manifest-item-detail"); detail.name = "manifest-item-detail";
            var detailTitle = Text("", "manifest-section-title");
            var detailBody = Text("", "manifest-record-body");
            detail.Add(detailTitle); detail.Add(detailBody); inspectorParent.Add(detail);
            void Inspect(ItemSpec item)
            {
                manifestInspectedId = item.Id;
                detailTitle.text = item.Name;
                var tag = BossApplicability.ItemLabel(item);
                detailBody.text = UiRules.Describe(item.Description) + (string.IsNullOrEmpty(tag) ? "" : "\n" + tag);
                foreach (var card in parent.Query<VisualElement>(className: "manifest-item").ToList())
                    card.EnableInClassList("selected", card.name == "manifest-item-" + item.Id);
            }
            var choices = Session.GetSupportChoices().Where(VoyageSession.IsLoadoutChoice).ToArray();
            void ItemCard(VisualElement grid, string id, string status, int quantity, IEnumerable<VoyageNodeChoice> actions)
            {
                var item = SupportCatalog.Item(id);
                var card = Element("manifest-item"); card.name = "manifest-item-" + id; card.pickingMode = PickingMode.Position;
                card.focusable = true;
                card.tooltip = UiRules.Describe(item.Description);
                var header = Element("manifest-item-head"); header.Add(ManifestIcon(ItemIcon(item)));
                header.Add(Text(item.Name, "manifest-item-name"));
                if (quantity > 1) header.Add(Text("×" + quantity, "manifest-count"));
                card.Add(header); card.Add(Text(status, "manifest-item-status"));
                var buttons = Element("manifest-item-actions");
                foreach (var choice in actions)
                {
                    var action = choice;
                    var caption = choice.Id.StartsWith("pack:", StringComparison.Ordinal) ? "携带"
                        : choice.Id.StartsWith("unpack:", StringComparison.Ordinal) ? "放回"
                        : Session.Equipment.Contains(id) ? "卸下" : "装备";
                    var button = Action("support-" + choice.Id, caption, "secondary",
                        () => Change(() => Session.ManageSupport(action.Id)), choice.Enabled);
                    button.tooltip = choice.Enabled ? item.Description : choice.DisabledReason;
                    button.RegisterCallback<FocusInEvent>(_ => Inspect(item));
                    buttons.Add(button);
                }
                if (buttons.childCount > 0) card.Add(buttons);
                var blocked = actions.FirstOrDefault(c => !c.Enabled);
                if (blocked != null) card.Add(Text(blocked.DisabledReason, "manifest-blocked-reason"));
                card.RegisterCallback<PointerEnterEvent>(_ => Inspect(item));
                card.RegisterCallback<FocusInEvent>(_ => Inspect(item));
                card.EnableInClassList("selected", manifestInspectedId == id);
                grid.Add(card);
            }
            var consumables = ManifestSection(parent, "物资箱", "随身 " + Session.CarriedItems.Count + "/2  ·  总计 " + Session.Inventory.Count + "/6");
            var consumableGrid = Element("manifest-item-grid"); consumables.Add(consumableGrid);
            foreach (var group in Session.Inventory.GroupBy(id => id))
            {
                var carried = Session.CarriedItems.Count(id => id == group.Key);
                var status = group.Key == "i12" ? "休整用品" : carried > 0 ? "随身携带 ×" + carried : "留在船上";
                ItemCard(consumableGrid, group.Key, status, group.Count(), choices.Where(c =>
                    c.Id == "pack:" + group.Key && carried < group.Count() || c.Id == "unpack:" + group.Key));
            }
            if (Session.Inventory.Count == 0) consumables.Add(Text("物资箱空了", "manifest-caption"));
            var equipment = ManifestSection(parent, "装备", Session.Equipment.Count + " / 3");
            var equipmentGrid = Element("manifest-item-grid"); equipment.Add(equipmentGrid);
            foreach (var id in Session.OwnedEquipment)
                ItemCard(equipmentGrid, id, Session.Equipment.Contains(id) ? "已装备" : "收存中", 1, choices.Where(c => c.Id == "equip:" + id));
            var tools = ManifestSection(parent, "调查工具", (Session.ToolIds.Count + Session.CarriedItems.Count(id => id == "i09")) + " / 2");
            var toolGrid = Element("manifest-item-grid"); tools.Add(toolGrid);
            foreach (var id in Session.LearnedToolIds)
                ItemCard(toolGrid, id, Session.ToolIds.Contains(id) ? "随身携带" : "留在船上", 1, Array.Empty<VoyageNodeChoice>());
            tools.Add(Action("support-tools", Session.CanEditDeck ? "更换调查工具" : "查看调查工具", "secondary",
                () => { supportOpen = false; deckOpen = true; Refresh(); }));
            var navigation = Element("manifest-navigation-tools");
            navigation.Add(Text("旧海图  ×" + Session.ChartUses, "manifest-caption"));
            navigation.Add(Text("望远镜  ×" + Session.LookoutUses, "manifest-caption")); parent.Add(navigation);
            var owned = Session.Inventory.Concat(Session.OwnedEquipment).Concat(Session.LearnedToolIds).ToArray();
            var first = owned.FirstOrDefault();
            var inspected = owned.Contains(manifestInspectedId) ? SupportCatalog.Item(manifestInspectedId) : first == null ? null : SupportCatalog.Item(first);
            if (inspected != null) Inspect(inspected);
        }

        private void BuildCrewManifest(VisualElement parent)
        {
            var section = ManifestSection(parent, "船员", Session.CrewIds.Count + " / 4");
            section.tooltip = "每人每场可支援一次；同一回合只能呼叫一名船员。";
            var grid = Element("manifest-item-grid"); section.Add(grid);
            foreach (var id in Session.CrewIds)
            {
                var crew = SupportCatalog.CrewMember(id); var names = crew.Name.Split(new[] { " · " }, StringSplitOptions.None);
                var card = Element("manifest-crew");
                var header = Element("manifest-item-head");
                header.Add(ManifestIcon(crew.Kind == CrewSupportKind.Medic ? "heart" : crew.Kind == CrewSupportKind.Listener ? "ear" : "compass"));
                header.Add(Text(names[0], "manifest-item-name"));
                header.Add(Text(Session.InjuredCrew.Contains(id) ? "受伤" : "待命", "manifest-item-status"));
                card.Add(header);
                card.Add(Text(names.Length > 1 ? names[1] : "船员", "manifest-caption"));
                card.Add(Text(UiRules.Describe(SupportCatalog.CrewDescription(crew.Kind)), "manifest-record-body"));
                if (Session.TrustedCrew.Contains(id)) card.Add(Text("信任", "manifest-item-status"));
                grid.Add(card);
            }
            if (Session.EscortedCrew != null)
                parent.Add(Text("等待接驳 · " + SupportCatalog.CrewMember(Session.EscortedCrew).Name, "manifest-caption"));
        }

        private void BuildArchiveManifest(VisualElement parent)
        {
            foreach (var marker in Session.Markers)
            {
                var clue = Element("manifest-clue"); clue.Add(ManifestIcon("scroll"));
                var copy = Element("manifest-destination-copy"); copy.Add(Text(MarkerName(marker.Id), "manifest-item-name"));
                copy.Add(Text(marker.Id == "docking-voucher" ? "下次招募少付 1 补给"
                    : marker.Id == "sealed-specimen" ? "可在档案航点交付" : "下一场战斗生效", "manifest-caption"));
                clue.Add(copy); parent.Add(clue);
            }
            var past = CardDeckPreparation.StoredArchive;
            if (Session.Knowledge.Count == 0 && Session.Archive.Count == 0 && past.Count == 0 && Session.Markers.Count == 0)
            {
                ManifestEmpty(parent, "scroll", "尚无调查记录", "调查所得会记录在这里"); return;
            }
            if (Session.Knowledge.Count > 0)
            {
                var group = new Foldout { text = "海域情报  " + Session.Knowledge.Count, value = true };
                group.AddToClassList("manifest-record-group");
                foreach (var fact in Session.Knowledge) group.Add(Text(fact.Replace("幕末威胁简报：", ""), "manifest-record-body"));
                parent.Add(group);
            }
            void ArchiveGroup(string title, IReadOnlyList<Evidence> entries, bool expanded)
            {
                if (entries.Count == 0) return;
                var group = new Foldout { text = title + "  " + entries.Count, value = expanded };
                group.AddToClassList("manifest-record-group");
                if (!expanded) group.tooltip = "过往调查记录；用于当前遭遇时仍需核验。";
                foreach (var evidence in entries)
                {
                    var record = Element("manifest-record");
                    record.Add(Text(evidence.Rule == "source-v1" ? "来源核验" : evidence.Rule == "anchor-v1" ? "锚点核验" : "调查记录", "manifest-item-name"));
                    record.Add(Text(evidence.Text, "manifest-record-body"));
                    record.tooltip = "记录方式：" + evidence.Source; group.Add(record);
                }
                parent.Add(group);
            }
            ArchiveGroup("本次航程", Session.Archive, true);
            ArchiveGroup("过往见闻", past, false);
        }

        private void BuildRitualManifest(VisualElement parent)
        {
            if (!Session.KnowsEchoRitual) { ManifestEmpty(parent, "seal", "尚未掌握仪式", null); return; }
            var ritual = Element("manifest-ritual"); ritual.Add(ManifestIcon("seal"));
            ritual.Add(Text("遮断回声", "manifest-section-title"));
            ritual.Add(Text(Session.Loadout.EchoRitual ? "已备妥" : "未备妥", "manifest-item-status"));
            ritual.Add(Text("消耗 3 理智，压制一只正在攻击的召唤物。", "manifest-record-body"));
            ritual.tooltip = "在补给船筹备；下一场战斗可用一次。"; parent.Add(ritual);
        }

        private static void ManifestEmpty(VisualElement parent, string icon, string title, string caption)
        {
            var empty = Element("manifest-empty"); empty.Add(ManifestIcon(icon));
            empty.Add(Text(title, "manifest-section-title"));
            if (!string.IsNullOrEmpty(caption)) empty.Add(Text(caption, "manifest-caption")); parent.Add(empty);
        }
    }
}
