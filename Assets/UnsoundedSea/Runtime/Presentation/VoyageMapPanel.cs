using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class VoyageMapPanel : VisualElement
    {
        public VoyageChart Chart { get; }

        public VoyageMapPanel(VoyageSession session, Action<string> select, bool interactive)
        {
            AddToClassList("voyage-map-panel");
            var style = Resources.Load<StyleSheet>("VoyageMap");
            if (style != null) styleSheets.Add(style);

            var heading = new VisualElement { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("map-heading");
            var title = new Label($"第 {session?.Act ?? 0} 幕 · {RegionName(session?.RegionId)}航路图") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("map-title"); heading.Add(title);
            var note = new Label(session == null ? "无远征记录" : $"航点 {session.VisitedThisAct} / {VoyageSession.ActLength} · 仅绘已知水域") { pickingMode = PickingMode.Ignore };
            note.AddToClassList("map-note"); heading.Add(note); Add(heading);

            if (session == null)
            {
                var empty = new Label("独立遭遇 / 无航程海图") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("map-empty"); Add(empty); return;
            }

            Chart = new VoyageChart(session, select, interactive);
            var chartScroll = new ScrollView(ScrollViewMode.Vertical) { name = "voyage-map-scroll" };
            ScrollWheel.ForwardFrom(this, chartScroll);
            chartScroll.AddToClassList("map-chart-scroll");
            chartScroll.contentContainer.style.alignItems = Align.Stretch;
            chartScroll.Add(Chart);
            Add(chartScroll);
            // Open centred on the current stop so the previous and next layers are both in view without scrolling.
            var focusId = "node-" + (session.Phase == VoyagePhase.Sailing && session.Selected != null ? session.Selected.Id : session.Current.Id);
            void Centre(GeometryChangedEvent _)
            {
                var node = Chart.Q<Button>(focusId);
                var viewport = chartScroll.contentViewport.resolvedStyle.height;
                var height = Chart.resolvedStyle.height;
                if (node == null || float.IsNaN(viewport) || viewport <= 0 || float.IsNaN(height) || height <= 0) return;
                var y = node.resolvedStyle.top;
                if (float.IsNaN(y)) return;
                chartScroll.scrollOffset = new Vector2(0, Mathf.Clamp(y - viewport * .5f, 0, Mathf.Max(0, height - viewport)));
                Chart.UnregisterCallback<GeometryChangedEvent>(Centre);
            }
            Chart.RegisterCallback<GeometryChangedEvent>(Centre);
            var legend = new VisualElement { pickingMode = PickingMode.Ignore };
            legend.AddToClassList("map-legend");
            AddLegend(legend, "current", "在此"); AddLegend(legend, "available", "可航行");
            AddLegend(legend, "selected", "已选"); AddLegend(legend, "visited", "已航经");
            AddLegend(legend, "charted", "已测"); Add(legend);
        }

        private static void AddLegend(VisualElement legend, string state, string text)
        {
            var entry = new VisualElement { pickingMode = PickingMode.Ignore }; entry.AddToClassList("map-legend-item");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore }; mark.AddToClassList("map-legend-mark"); mark.AddToClassList(state);
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList("map-legend-label");
            entry.Add(mark); entry.Add(label); legend.Add(entry);
        }
        private static string RegionName(string id) => id == "fog" ? "灰雾航道" : id == "bones" ? "尸骸之海" :
            id == "inverted" ? "倒悬潮庭" : id == "fleet" ? "无灯裂海" : id == "abyss" ? "无星深渊" : "远征";
    }

    internal sealed class VoyageChart : VisualElement
    {
        private readonly VoyageStop departure, destination;
        private readonly string selectedId;
        private readonly IReadOnlyList<string> route;
        private readonly IReadOnlyList<VoyageRoute> links;
        private readonly HashSet<string> choices;
        private readonly Dictionary<string, VoyageStop> stops;
        private readonly VisualElement ship;
        private static readonly Vector2[][] Currents =
        {
            new[] { new Vector2(1, 82), new Vector2(12, 76), new Vector2(7, 64), new Vector2(18, 56), new Vector2(11, 42), new Vector2(20, 31), new Vector2(14, 17) },
            new[] { new Vector2(99, 20), new Vector2(88, 28), new Vector2(94, 40), new Vector2(83, 51), new Vector2(91, 66), new Vector2(80, 78) },
            new[] { new Vector2(31, 4), new Vector2(39, 12), new Vector2(53, 8), new Vector2(66, 15), new Vector2(76, 10) }
        };

        public VoyageChart(VoyageSession session, Action<string> select, bool interactive)
        {
            departure = session.Current;
            destination = session.Phase == VoyagePhase.Sailing && session.Selected != null ? session.Selected : departure;
            selectedId = session.Selected?.Id;
            route = session.Visited;
            links = session.KnownRoutes;
            choices = new HashSet<string>(session.Choices.Select(x => x.Id));
            stops = session.KnownStops.ToDictionary(x => x.Id);
            AddToClassList("nautical-chart"); name = "voyage-chart"; pickingMode = PickingMode.Ignore;

            foreach (var stop in session.KnownStops)
            {
                var id = stop.Id;
                var node = new Button(() => select?.Invoke(id)) { name = "node-" + id };
                node.AddToClassList("chart-node");
                var current = id == departure.Id;
                var state = current ? "current" : selectedId == id ? "selected" : choices.Contains(id) ? "available" : route.Contains(id) ? "visited" : "charted";
                node.AddToClassList(state);
                node.SetEnabled(interactive && session.Phase == VoyagePhase.ChoosingRoute && choices.Contains(id));
                node.style.left = Length.Percent(stop.X); node.style.top = Length.Percent(100 - stop.Y);
                var glyph = new ChartNodeGlyph(stop.Kind) { pickingMode = PickingMode.Ignore }; glyph.AddToClassList("node-glyph"); node.Add(glyph);
                var title = new Label(stop.Name) { pickingMode = PickingMode.Ignore }; title.AddToClassList("node-name");
                title.AddToClassList(stop.Y > 78 ? "label-below" : "label-above"); node.Add(title);
                if (stop.Perilous)
                {
                    var perilous = new Label("险") { name = "perilous-" + id, pickingMode = PickingMode.Ignore };
                    perilous.AddToClassList("node-perilous");
                    node.Add(perilous);
                }
                node.tooltip = $"{StateName(state)} · {KindName(stop.Kind)} · {stop.Name}{(stop.Perilous ? "\n双敌 · 两次收获" : "")}\n{stop.Clue}\n{VoyageHud.PreviewRoute(session, stop)}";
                Add(node);
            }

            ship = new VisualElement { name = "chart-ship", pickingMode = PickingMode.Ignore }; ship.AddToClassList("chart-ship"); Add(ship); SetProgress(0);
            var compass = new SeaUiArt("compass") { pickingMode = PickingMode.Ignore }; compass.AddToClassList("chart-compass"); Add(compass);
            if (session.KnownStops.All(x => x.Layer < 5))
            {
                var fog = new Label("未测海雾") { pickingMode = PickingMode.Ignore }; fog.AddToClassList("chart-fog"); Add(fog);
            }
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            ship.style.left = Length.Percent(Mathf.Lerp(departure.X, destination.X, progress));
            ship.style.top = Length.Percent(100 - Mathf.Lerp(departure.Y, destination.Y, progress));
        }

        private static string StateName(string state) => state == "current" ? "在此" : state == "selected" ? "已选航向" : state == "available" ? "可航行" : state == "visited" ? "已航经" : "已测";
        private static string KindName(RunNodeKind kind) => kind == RunNodeKind.Battle ? "调查遭遇" :
            kind == RunNodeKind.Boss ? "区域威胁" : kind == RunNodeKind.Supply ? "安全补给" :
            kind == RunNodeKind.Archive ? "档案线索" : kind == RunNodeKind.Reward ? "行动收获" : "海上事件";
        private Vector2 Point(float x, float y) => new Vector2(contentRect.width * x / 100, contentRect.height * (100 - y) / 100);

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            p.lineWidth = 1; p.strokeColor = new Color(0.73f, 0.59f, 0.35f, 0.25f);
            DrawFrame(p, 5, 5, contentRect.width - 10, contentRect.height - 10);
            DrawTicks(p);
            foreach (var current in Currents) DrawCurrent(p, current);
            foreach (var link in links)
            {
                if (!stops.TryGetValue(link.FromId, out var from) || !stops.TryGetValue(link.ToId, out var to)) continue;
                var travelled = false;
                for (var i = 1; i < route.Count; i++) if (route[i - 1] == from.Id && route[i] == to.Id) { travelled = true; break; }
                var selected = from.Id == departure.Id && selectedId == to.Id;
                var available = from.Id == departure.Id && choices.Contains(to.Id);
                p.lineWidth = selected ? 3 : available || travelled ? 2 : 1.4f;
                p.strokeColor = selected ? new Color(0.91f, 0.69f, 0.28f, 1) : travelled ? new Color(0.38f, 0.7f, 0.62f, 0.9f) : available ? new Color(0.78f, 0.58f, 0.25f, 0.9f) : new Color(0.64f, 0.68f, 0.55f, 0.55f);
                DrawDashedCurve(p, Point(from.X, from.Y), Point(to.X, to.Y), selected || available ? 1f : 0.64f);
            }
        }

        private void DrawFrame(Painter2D p, float x, float y, float width, float height)
        {
            p.BeginPath(); p.MoveTo(new Vector2(x + 8, y)); p.LineTo(new Vector2(x + width - 13, y + 3));
            p.LineTo(new Vector2(x + width, y + 12)); p.LineTo(new Vector2(x + width - 4, y + height - 9));
            p.LineTo(new Vector2(x + width - 17, y + height)); p.LineTo(new Vector2(x + 10, y + height - 3));
            p.LineTo(new Vector2(x, y + height - 15)); p.LineTo(new Vector2(x + 3, y + 9)); p.ClosePath(); p.Stroke();
        }

        private void DrawTicks(Painter2D p)
        {
            p.strokeColor = new Color(0.73f, 0.59f, 0.35f, 0.3f); p.lineWidth = 1;
            for (var i = 10; i < 100; i += 10)
            {
                var x = contentRect.width * i / 100; var y = contentRect.height * i / 100;
                p.BeginPath(); p.MoveTo(new Vector2(x, 7)); p.LineTo(new Vector2(x, i % 20 == 0 ? 16 : 12)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(7, y)); p.LineTo(new Vector2(i % 20 == 0 ? 16 : 12, y)); p.Stroke();
            }
        }

        private void DrawCurrent(Painter2D p, Vector2[] points)
        {
            p.strokeColor = new Color(0.28f, 0.63f, 0.61f, 0.22f); p.lineWidth = 1.3f;
            p.BeginPath(); p.MoveTo(Point(points[0].x, points[0].y));
            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = points[Mathf.Max(0, i - 1)]; var b = points[i];
                var c = points[i + 1]; var d = points[Mathf.Min(points.Length - 1, i + 2)];
                for (var step = 1; step <= 12; step++)
                {
                    float t = step / 12f;
                    var sample = .5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                    p.LineTo(Point(sample.x, sample.y));
                }
            }
            p.Stroke();
        }

        private static void DrawDashedCurve(Painter2D p, Vector2 from, Vector2 to, float bend)
        {
            var delta = to - from;
            var normal = delta.sqrMagnitude > 0 ? new Vector2(-delta.y, delta.x).normalized : Vector2.zero;
            var control = (from + to) * 0.5f + normal * Mathf.Min(34, delta.magnitude * 0.15f) * bend;
            const int segments = 24;
            for (var i = 0; i < segments; i += 2)
            {
                var a = i / (float)segments; var b = Mathf.Min(1, (i + 1) / (float)segments);
                Vector2 Curve(float t) => (1 - t) * (1 - t) * from + 2 * (1 - t) * t * control + t * t * to;
                p.BeginPath(); p.MoveTo(Curve(a)); p.LineTo(Curve(b)); p.Stroke();
            }
        }
    }

    internal sealed class ChartNodeGlyph : VisualElement
    {
        private readonly RunNodeKind kind;
        public ChartNodeGlyph(RunNodeKind kind) { this.kind = kind; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; var c = contentRect.center; var s = Mathf.Min(contentRect.width, contentRect.height) * 0.34f;
            p.lineWidth = 1.7f; p.strokeColor = new Color32(232, 214, 169, 255); p.fillColor = new Color32(12, 31, 32, 255);
            p.BeginPath();
            if (kind == RunNodeKind.Battle) { p.MoveTo(c + new Vector2(-s, s * .45f)); p.LineTo(c + new Vector2(s, s * .45f)); p.LineTo(c + new Vector2(s * .55f, s)); p.LineTo(c + new Vector2(-s * .6f, s)); p.ClosePath(); }
            else if (kind == RunNodeKind.Boss) { p.MoveTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(s, s)); p.LineTo(c); p.LineTo(c + new Vector2(-s, s)); p.ClosePath(); }
            else if (kind == RunNodeKind.Supply) { p.MoveTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(s * .7f, 0)); p.LineTo(c + new Vector2(0, s)); p.LineTo(c + new Vector2(-s * .7f, 0)); p.ClosePath(); }
            else if (kind == RunNodeKind.Archive) { p.MoveTo(c + new Vector2(-s, -s * .7f)); p.LineTo(c + new Vector2(s, -s * .7f)); p.LineTo(c + new Vector2(s, s * .7f)); p.LineTo(c + new Vector2(-s, s * .7f)); p.ClosePath(); }
            else { p.Arc(c, s * .72f, 0, 360); }
            p.Fill(); p.Stroke();
            if (kind == RunNodeKind.Battle) { p.BeginPath(); p.MoveTo(c + new Vector2(0, s * .4f)); p.LineTo(c + new Vector2(0, -s)); p.LineTo(c + new Vector2(s * .55f, -s * .25f)); p.LineTo(c); p.Stroke(); }
            else if (kind == RunNodeKind.Archive) { p.BeginPath(); p.MoveTo(c + new Vector2(0, -s * .7f)); p.LineTo(c + new Vector2(0, s * .7f)); p.Stroke(); }
        }
    }
}
