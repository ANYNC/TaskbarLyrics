using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TaskbarFreeSpaceLayoutTests
{
    [Theory]
    [InlineData(360, 800, true, 0, 352)]
    [InlineData(180, 800, true, 0, 172)]
    [InlineData(90, 800, true, 808, 420)]
    [InlineData(0, 1440, false, 0, 0)]
    [InlineData(600, 800, true, 0, 420)]
    public void LeftAnchorUsesTheNearestAvailableRegion(
        double occupiedStart, double occupiedEnd, bool visible, double expectedStart, double expectedLength)
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(1440, 420, 0,
            [new TaskbarInterval(occupiedStart, occupiedEnd)], out var placement);
        Assert.Equal(visible, placed);
        Assert.Equal(new TaskbarInterval(expectedStart, expectedStart + expectedLength), placement);
    }

    [Fact]
    public void CustomOffsetCanMoveLeftToFitRequestedWidth()
    {
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1440, 420, 120,
            [new TaskbarInterval(360, 800)], out var placement));

        Assert.Equal(new TaskbarInterval(0, 352), placement);
    }

    [Theory]
    [InlineData(0, 808)]
    [InlineData(900, 900)]
    public void ChangingPreferredPositionRecomputesTheNearestPlacement(double preferredStart, double expectedLeft)
    {
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1440, 420, preferredStart,
            [new TaskbarInterval(50, 800)], out var placement));

        Assert.Equal(new TaskbarInterval(expectedLeft, expectedLeft + 420), placement);
    }

    [Theory]
    [InlineData(107, false)]
    [InlineData(108, true)]
    public void AppliesMinimumDisplayWidthAfterSafetyGap(double iconsLeft, bool expectedVisible)
    {
        Assert.Equal(expectedVisible, TaskbarFreeSpaceLayout.TryPlace(1440, 420, 0,
            [new TaskbarInterval(iconsLeft, 1440)], out var placement));
        Assert.Equal(expectedVisible ? new TaskbarInterval(0, 100) : default, placement);
    }

    [Theory]
    [InlineData(0, 0, 120)]
    [InlineData(400, 700, 900)]
    [InlineData(800, 700, 900)]
    public void SelectsNearestPlacementRatherThanAlwaysMovingRightOrPreferringFullWidth(
        double preferredStart, double expectedStart, double expectedEnd)
    {
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1000, 200, preferredStart,
            [new TaskbarInterval(120, 700), new TaskbarInterval(900, 1000)],
            out var placement, safetyGap: 0));

        Assert.Equal(new TaskbarInterval(expectedStart, expectedEnd), placement);
    }

    [Fact]
    public void MovesLyricsLeftWhenPreferredAreaIsCrowded()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 250,
            preferredStart: 360,
            occupied: [new TaskbarInterval(340, 900)],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(82, 332), placement);
    }

    [Fact]
    public void ReturnsFalseWhenNoGapCanHoldMinimumWidth()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 300,
            requestedLength: 200,
            preferredStart: 0,
            occupied: [new TaskbarInterval(50, 250)],
            out var placement);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Fact]
    public void MergesMultipleAndOverlappingControlsBeforeSelectingGap()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 200,
            preferredStart: 450,
            occupied:
            [
                new TaskbarInterval(500, 650),
                new TaskbarInterval(300, 520),
                new TaskbarInterval(800, 850)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(658, 792), placement);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(400, 400)]
    [InlineData(900, 800)]
    public void ClampsLeftCenterAndRightPreferredPositions(double preferredStart, double expectedLeft)
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 200,
            preferredStart,
            occupied: [],
            out var placement);

        Assert.True(placed);
        Assert.Equal(expectedLeft, placement.Start);
        Assert.Equal(200, placement.Length);
    }

    [Fact]
    public void TiePrefersTheWiderSafeGap()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 250,
            preferredStart: 100,
            occupied:
            [
                new TaskbarInterval(100, 200),
                new TaskbarInterval(400, 1000)
            ],
            out var placement,
            safetyGap: 0,
            minimumLength: 50);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(200, 400), placement);
    }

    [Fact]
    public void NegativePreferredPositionAndOutOfBoundsControlsAreClampedOrIgnored()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 100,
            preferredStart: -50,
            occupied:
            [
                new TaskbarInterval(-20, 100),
                new TaskbarInterval(900, 1200),
                new TaskbarInterval(-100, -1),
                new TaskbarInterval(1100, 1200)
            ],
            out var placement,
            safetyGap: 0,
            minimumLength: 50);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(100, 200), placement);
    }

    [Fact]
    public void IgnoresMalformedAndNonFiniteOccupiedIntervals()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 500,
            requestedLength: 200,
            preferredStart: 100,
            occupied:
            [
                new TaskbarInterval(200, 100),
                new TaskbarInterval(100, 100),
                new TaskbarInterval(double.NaN, 200),
                new TaskbarInterval(200, double.PositiveInfinity)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(100, 300), placement);
    }

    [Fact]
    public void SelectsRightSideGapWhenControlsCoverTheLeftSide()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 200,
            preferredStart: 700,
            occupied: [new TaskbarInterval(0, 600)],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(700, 900), placement);
    }

    [Fact]
    public void SelectsCenterGapBetweenControlsOnBothSides()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 1000,
            requestedLength: 200,
            preferredStart: 400,
            occupied:
            [
                new TaskbarInterval(0, 300),
                new TaskbarInterval(700, 1000)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(400, 600), placement);
    }

    [Fact]
    public void NarrowBarFitsByShrinkingWithoutEnlargingTheRequestedWidth()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 180,
            requestedLength: 420,
            preferredStart: 50,
            occupied: [],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarInterval(0, 180), placement);
    }

    [Fact]
    public void NarrowBarWithoutMinimumSafeWidthReturnsFalse()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength: 99,
            requestedLength: 420,
            preferredStart: 0,
            occupied: [],
            out var placement);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Theory]
    [InlineData(0, 200, 0, 8, 100)]
    [InlineData(double.NaN, 200, 0, 8, 100)]
    [InlineData(double.PositiveInfinity, 200, 0, 8, 100)]
    [InlineData(500, 0, 0, 8, 100)]
    [InlineData(500, double.NaN, 0, 8, 100)]
    [InlineData(500, double.PositiveInfinity, 0, 8, 100)]
    [InlineData(500, 200, double.NaN, 8, 100)]
    [InlineData(500, 200, double.PositiveInfinity, 8, 100)]
    [InlineData(500, 200, 0, -1, 100)]
    [InlineData(500, 200, 0, double.NaN, 100)]
    [InlineData(500, 200, 0, double.PositiveInfinity, 100)]
    [InlineData(500, 200, 0, 8, 0)]
    [InlineData(500, 200, 0, 8, double.NaN)]
    [InlineData(500, 200, 0, 8, double.PositiveInfinity)]
    [InlineData(500, 50, 0, 8, 100)]
    public void RejectsInvalidEssentialArguments(
        double taskbarLength,
        double requestedLength,
        double preferredStart,
        double safetyGap,
        double minimumLength)
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarLength,
            requestedLength,
            preferredStart,
            [],
            out var placement,
            safetyGap,
            minimumLength);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Fact]
    public void RejectsNullOccupiedIntervals()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            500,
            200,
            0,
            null!,
            out var placement);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Fact]
    public void DeterministicRandomizedPlacementsStaySafeAndWithinRequestedWidth()
    {
        var random = new Random(0x5EED);
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var taskbarLength = random.Next(1000, 2001);
            var minimumLength = random.Next(80, 181);
            var requestedLength = random.Next(minimumLength, 451);
            var preferredStart = random.Next(-200, taskbarLength + 201);
            var safetyGap = random.Next(0, 17);
            var occupied = new List<TaskbarInterval>();
            var occupiedCount = random.Next(0, 21);
            for (var intervalIndex = 0; intervalIndex < occupiedCount; intervalIndex++)
            {
                var start = random.Next(630, taskbarLength + 101);
                var end = start + random.Next(1, 251);
                occupied.Add(new TaskbarInterval(start, end));
            }

            var placed = TaskbarFreeSpaceLayout.TryPlace(
                taskbarLength,
                requestedLength,
                preferredStart,
                occupied,
                out var placement,
                safetyGap,
                minimumLength);

            Assert.True(placed, $"Iteration {iteration} should retain the reserved left-side gap.");
            Assert.InRange(placement.Start, 0, taskbarLength);
            Assert.InRange(placement.End, placement.Start, taskbarLength);
            Assert.InRange(placement.Length, minimumLength, requestedLength);
            AssertDoesNotIntersectOccupiedIntervals(placement, taskbarLength, occupied, safetyGap);
        }
    }

    private static void AssertDoesNotIntersectOccupiedIntervals(
        TaskbarInterval placement,
        double taskbarLength,
        IReadOnlyList<TaskbarInterval> occupied,
        double safetyGap)
    {
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

            var expandedStart = Math.Max(0, Math.Clamp(interval.Start, 0, taskbarLength) - safetyGap);
            var expandedEnd = Math.Min(taskbarLength, Math.Clamp(interval.End, 0, taskbarLength) + safetyGap);
            Assert.True(
                placement.End <= expandedStart || placement.Start >= expandedEnd,
                $"Placement {placement} intersects expanded occupied interval [{expandedStart}, {expandedEnd}].");
        }
    }
}
