using Wings.Application.Common;

namespace Wings.Application.Execution;

public sealed record PlannerNode(Guid Id, Guid? TestCaseId, Guid? FeatureId, string Name);

public sealed record DependencyPlan(IReadOnlyDictionary<Guid, int> Levels, IReadOnlyDictionary<Guid, List<Guid>> Prerequisites);

/// <summary>
/// Orders the tests of a run. Test B depends on test A when B's test case depends on A's test case, or B's feature depends
/// (prerequisite, setup or data) on A's feature. Each test gets a wave = 1 + the highest wave of its prerequisites; tests in
/// the same wave are independent and may run in parallel. Creation sequence numbers play no part in this.
/// </summary>
public static class DependencyPlanner
{
    public static DependencyPlan Plan(IReadOnlyList<PlannerNode> nodes, IEnumerable<(Guid From, Guid To)> testCaseEdges, IEnumerable<(Guid From, Guid To)> featureEdges)
    {
        var tcDeps = testCaseEdges.ToLookup(e => e.From, e => e.To);
        var featureDeps = featureEdges.ToLookup(e => e.From, e => e.To);
        var byCase = nodes.Where(n => n.TestCaseId is not null).ToLookup(n => n.TestCaseId!.Value);
        var byFeature = nodes.Where(n => n.FeatureId is not null).ToLookup(n => n.FeatureId!.Value);

        var prerequisites = new Dictionary<Guid, List<Guid>>();
        foreach (var n in nodes)
        {
            var pre = new HashSet<Guid>();
            if (n.TestCaseId is { } tc) foreach (var dep in tcDeps[tc]) foreach (var p in byCase[dep]) pre.Add(p.Id);
            if (n.FeatureId is { } f) foreach (var dep in featureDeps[f]) foreach (var p in byFeature[dep]) pre.Add(p.Id);
            pre.Remove(n.Id);
            if (pre.Count > 0) prerequisites[n.Id] = [.. pre];
        }

        var levels = new Dictionary<Guid, int>();
        var visiting = new HashSet<Guid>();
        var names = nodes.ToDictionary(n => n.Id, n => n.Name);
        int Level(Guid id, Stack<Guid> path)
        {
            if (levels.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id))
            {
                var cycle = path.Reverse().SkipWhile(x => x != id).Append(id).Select(x => names[x]);
                throw new ValidationException("dependencies", $"Circular dependency between tests: {string.Join(" → ", cycle)}.");
            }
            path.Push(id);
            var level = prerequisites.TryGetValue(id, out var pre) ? pre.Max(p => Level(p, path)) + 1 : 0;
            path.Pop();
            visiting.Remove(id);
            return levels[id] = level;
        }
        foreach (var n in nodes) Level(n.Id, new Stack<Guid>());
        return new DependencyPlan(levels, prerequisites);
    }
}
