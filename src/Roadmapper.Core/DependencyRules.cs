namespace Roadmapper.Core;

public static class DependencyRules
{
    /// <summary>Adding predecessor → successor creates a cycle iff the predecessor is already reachable from the successor.</summary>
    public static bool WouldCreateCycle(IEnumerable<(int PredecessorId, int SuccessorId)> existing, int predecessorId, int successorId)
    {
        if (predecessorId == successorId) return true;

        var next = existing.ToLookup(e => e.PredecessorId, e => e.SuccessorId);
        var stack = new Stack<int>([successorId]);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == predecessorId) return true;
            if (!seen.Add(node)) continue;
            foreach (var n in next[node]) stack.Push(n);
        }
        return false;
    }

    /// <summary>Finish-to-start: the successor should start after the predecessor's (inclusive) end date.</summary>
    public static bool IsScheduleConflict(RoadmapItem predecessor, RoadmapItem successor) =>
        predecessor.EndDate is { } end && successor.StartDate is { } start && start <= end;
}
