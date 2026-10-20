namespace TaskbarLyrics.App;

internal readonly record struct TaskbarHorizontalInterval(double Start, double End)
{
    public double Width => End - Start;
}

internal static class TaskbarFreeSpaceLayout
{
    public const double MinimumDisplayWidth = 100;

    public static bool TryPlace(
        double taskbarWidth,
        double requestedWidth,
        double preferredLeft,
        IReadOnlyList<TaskbarHorizontalInterval> occupied,
        out TaskbarHorizontalInterval placement,
        double safetyGap = 8,
        double minimumWidth = MinimumDisplayWidth)
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

        var left = Math.Clamp(preferredLeft, 0, Math.Max(0, taskbarWidth - requestedWidth));
        var availableEnd = taskbarWidth;
        foreach (var interval in merged)
        {
            if (interval.End <= left)
            {
                continue;
            }
            if (interval.Start <= left)
            {
                return false;
            }
            availableEnd = interval.Start;
            break;
        }

        var width = Math.Min(requestedWidth, availableEnd - left);
        if (width < minimumWidth) return false;
        placement = new TaskbarHorizontalInterval(left, left + width);
        return true;
    }

    private static List<TaskbarHorizontalInterval> Merge(
        List<TaskbarHorizontalInterval> sortedIntervals)
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
}
