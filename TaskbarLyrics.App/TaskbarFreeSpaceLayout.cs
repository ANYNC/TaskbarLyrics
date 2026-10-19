namespace TaskbarLyrics.App;

internal readonly record struct TaskbarHorizontalInterval(double Start, double End)
{
    public double Width => End - Start;
}

internal static class TaskbarFreeSpaceLayout
{
    public static bool TryPlace(
        double taskbarWidth,
        double requestedWidth,
        double preferredLeft,
        IReadOnlyList<TaskbarHorizontalInterval> occupied,
        out TaskbarHorizontalInterval placement,
        double safetyGap = 8,
        double minimumWidth = 100)
    {
        placement = default;
        if (!double.IsFinite(taskbarWidth) || taskbarWidth <= 0 ||
            !double.IsFinite(requestedWidth) || requestedWidth <= 0 ||
            !double.IsFinite(preferredLeft) ||
            !double.IsFinite(safetyGap) || safetyGap < 0 ||
            !double.IsFinite(minimumWidth) || minimumWidth <= 0 ||
            requestedWidth < minimumWidth ||
            occupied is null)
        {
            return false;
        }

        var blocked = new List<TaskbarHorizontalInterval>(occupied.Count);
        foreach (var interval in occupied)
        {
            if (!double.IsFinite(interval.Start) ||
                !double.IsFinite(interval.End) ||
                interval.End <= interval.Start ||
                interval.End <= 0 ||
                interval.Start >= taskbarWidth)
            {
                continue;
            }

            var clippedStart = Math.Clamp(interval.Start, 0, taskbarWidth);
            var clippedEnd = Math.Clamp(interval.End, 0, taskbarWidth);
            var expandedStart = Math.Max(0, clippedStart - safetyGap);
            var expandedEnd = Math.Min(taskbarWidth, clippedEnd + safetyGap);
            if (expandedEnd > expandedStart)
            {
                blocked.Add(new TaskbarHorizontalInterval(expandedStart, expandedEnd));
            }
        }

        blocked.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var merged = Merge(blocked);

        var found = false;
        var bestPlacement = default(TaskbarHorizontalInterval);
        var gapStart = 0d;
        foreach (var interval in merged)
        {
            ConsiderGap(gapStart, interval.Start);
            gapStart = interval.End;
        }

        ConsiderGap(gapStart, taskbarWidth);
        if (!found)
        {
            return false;
        }

        placement = bestPlacement;
        return true;

        void ConsiderGap(double start, double end)
        {
            var gapWidth = end - start;
            if (gapWidth < minimumWidth)
            {
                return;
            }

            var width = Math.Min(requestedWidth, gapWidth);
            var candidateStart = Math.Clamp(preferredLeft, start, end - width);
            var distanceComparison = found
                ? CompareDistance(candidateStart, bestPlacement.Start, preferredLeft, taskbarWidth)
                : -1;
            if (!found || distanceComparison < 0 ||
                (distanceComparison == 0 && width > bestPlacement.Width))
            {
                found = true;
                bestPlacement = new TaskbarHorizontalInterval(candidateStart, candidateStart + width);
            }
        }
    }

    private static IReadOnlyList<TaskbarHorizontalInterval> Merge(
        IReadOnlyList<TaskbarHorizontalInterval> sortedIntervals)
    {
        if (sortedIntervals.Count < 2)
        {
            return sortedIntervals;
        }

        var merged = new List<TaskbarHorizontalInterval>(sortedIntervals.Count);
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
        double preferredLeft,
        double taskbarWidth)
    {
        if (preferredLeft < 0)
        {
            return candidateStart.CompareTo(otherStart);
        }

        if (preferredLeft > taskbarWidth)
        {
            return otherStart.CompareTo(candidateStart);
        }

        return Math.Abs(candidateStart - preferredLeft)
            .CompareTo(Math.Abs(otherStart - preferredLeft));
    }
}
