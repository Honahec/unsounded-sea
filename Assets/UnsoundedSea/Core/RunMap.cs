using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum RunNodeKind { Battle, Event, Supply, Archive, Boss }

    public sealed class RunNode
    {
        public string Id { get; }
        public RunNodeKind Kind { get; }
        public string Clue { get; }
        public string ContentId { get; }
        public IReadOnlyList<string> Next { get; }
        public RunNode(string id, RunNodeKind kind, string clue, string contentId, IEnumerable<string> next)
        { Id = Require.Id(id); Kind = Require.Defined(kind); Clue = Require.Id(clue); ContentId = Require.Id(contentId); Next = Require.Copy(next); }
    }

    // Navigation and completion are separate: selecting a node never silently completes its encounter.
    public sealed class RunMap
    {
        private readonly Dictionary<string, RunNode> nodes;
        private readonly HashSet<string> visited = new HashSet<string>();
        public string CurrentId { get; private set; }
        public bool CurrentCompleted { get; private set; }
        public bool IsFinished => CurrentCompleted && nodes[CurrentId].Next.Count == 0;
        public IReadOnlyList<string> Visited => visited.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        public IReadOnlyList<RunNode> Choices => CurrentCompleted ? nodes[CurrentId].Next.Where(x => !visited.Contains(x)).Select(x => nodes[x]).ToList().AsReadOnly() : new List<RunNode>().AsReadOnly();

        public RunMap(IEnumerable<RunNode> nodes, string startId)
        {
            this.nodes = Require.Copy(nodes).ToDictionary(x => x.Id, StringComparer.Ordinal);
            if (!this.nodes.ContainsKey(startId)) throw new ArgumentException("Missing starting node.");
            foreach (var node in this.nodes.Values)
                if (node.Next.Distinct().Count() != node.Next.Count || node.Next.Any(x => !this.nodes.ContainsKey(x)))
                    throw new ArgumentException("Duplicate or dangling map edge.");
            var active = new HashSet<string>(); var completed = new HashSet<string>();
            Visit(startId, active, completed);
            if (completed.Count != this.nodes.Count) throw new ArgumentException("Unreachable map node.");
            CurrentId = startId; visited.Add(startId);
        }
        private void Visit(string id, HashSet<string> active, HashSet<string> completed)
        {
            if (active.Contains(id)) throw new ArgumentException("Run map must be acyclic.");
            if (!completed.Add(id)) return;
            active.Add(id); foreach (var next in nodes[id].Next) Visit(next, active, completed); active.Remove(id);
        }
        public void CompleteCurrent() { CurrentCompleted = true; }
        public bool TryMove(string id)
        {
            if (!CurrentCompleted || !nodes[CurrentId].Next.Contains(id) || visited.Contains(id)) return false;
            CurrentId = id; CurrentCompleted = false; visited.Add(id); return true;
        }
    }
}
