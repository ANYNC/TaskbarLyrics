namespace TaskbarLyrics.App;

internal readonly record struct TaskbarInterval(double Start, double End)
{
    public double Length => End - Start;
}

internal static class TaskbarFreeSpaceLayout
{
    public const double MinimumDisplayLength = 100;

    public static bool TryPlace(
        double taskbarLength,
        double requestedLength,
        double preferredStart,
        IReadOnlyList<TaskbarInterval> occupied,
        out TaskbarInterval placement,
        double safetyGap = 8,
        double minimumLength = MinimumDisplayLength)
    {
        placement = default;
        if (!double.IsFinite(taskbarLength) || taskbarLength <= 0 ||
            !double.IsFinite(requestedLength) || requestedLength <= 0 ||
            !double.IsFinite(preferredStart) ||
            !double.IsFinite(safetyGap) || safetyGap < 0 ||
            !double.IsFinite(minimumLength) || minimumLength <= 0 ||
            requestedLength < minimumLength ||
            occupied is null)
        {
            return false;
        }

        var blocked = new List<TaskbarInterval>(occupied.Count);
        foreach (var interval in occupied)
        {
            if (!double.IsFinite(interval.Start) ||
                !double.IsFinite(interval.End) ||
                interval.End <= interval.Start ||
                interval.End <= 0 ||
                interval.Start >= taskbarLength)
            {
                continue;
            }

            var clippedStart = Math.Clamp(interval.Start, 0, taskbarLength);
            var clippedEnd = Math.Clamp(interval.End, 0, taskbarLength);
            var expandedStart = Math.Max(0, clippedStart - safetyGap);
            var expandedEnd = Math.Min(taskbarLength, clippedEnd + safetyGap);
            if (expandedEnd > expandedStart)
            {
                blocked.Add(new TaskbarInterval(expandedStart, expandedEnd));
            }
        }

        blocked.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var merged = Merge(blocked);

        var found = false;
        var bestPlacement = default(TaskbarInterval);
        var gapStart = 0d;
        foreach (var interval in merged)
        {
            ConsiderGap(gapStart, interval.Start);
            gapStart = interval.End;
        }

        ConsiderGap(gapStart, taskbarLength);
        if (!found)
        {
            return false;
        }

        placement = bestPlacement;
        return true;

        void ConsiderGap(double start, double end)
        {
            var gapLength = end - start;
            if (gapLength < minimumLength)
            {
                return;
            }

            var length = Math.Min(requestedLength, gapLength);
            var candidateStart = Math.Clamp(preferredStart, start, end - length);
            var distanceComparison = found
                ? CompareDistance(candidateStart, bestPlacement.Start, preferredStart, taskbarLength)
                : -1;
            if (!found || distanceComparison < 0 ||
                (distanceComparison == 0 && length > bestPlacement.Length))
            {
                found = true;
                bestPlacement = new TaskbarInterval(candidateStart, candidateStart + length);
            }
        }
    }

    private static List<TaskbarInterval> Merge(
        List<TaskbarInterval> sortedIntervals)
    {
        if (sortedIntervals.Count < 2)
        {
            return sortedIntervals;
        }

        var merged = new List<TaskbarInterval>(sortedIntervals.Count);
        var current = sortedIntervals[0];
        for (var index = 1; index < sortedIntervals.Count; index++)
        {
            var next = sortedIntervals[index];
            if (next.Start <= current.End)
            {
                current = current with { End = Math.Max(current.End, next.End) };
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    private static int CompareDistance(
        double candidateStart,
        double otherStart,
        double preferredStart,
        double taskbarLength)
    {
        if (preferredStart < 0)
        {
            return candidateStart.CompareTo(otherStart);
        }

        if (preferredStart > taskbarLength)
        {
            return otherStart.CompareTo(candidateStart);
        }

        return Math.Abs(candidateStart - preferredStart)
            .CompareTo(Math.Abs(otherStart - preferredStart));
    }
}
