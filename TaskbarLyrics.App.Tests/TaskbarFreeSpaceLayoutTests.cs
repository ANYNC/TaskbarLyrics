using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TaskbarFreeSpaceLayoutTests
{
    [Fact]
    public void FitsLongLyricsBeforeStartAndSearchControls()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1440,
            requestedWidth: 420,
            preferredLeft: 0,
            occupied: [new TaskbarHorizontalInterval(360, 600)],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(0, 352), placement);
    }

    [Fact]
    public void MovesLyricsLeftWhenPreferredAreaIsCrowded()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 250,
            preferredLeft: 360,
            occupied: [new TaskbarHorizontalInterval(340, 900)],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(82, 332), placement);
    }

    [Fact]
    public void ReturnsFalseWhenNoGapCanHoldMinimumWidth()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 300,
            requestedWidth: 200,
            preferredLeft: 0,
            occupied: [new TaskbarHorizontalInterval(50, 250)],
            out var placement);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Fact]
    public void MergesMultipleAndOverlappingControlsBeforeSelectingGap()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 200,
            preferredLeft: 450,
            occupied:
            [
                new TaskbarHorizontalInterval(500, 650),
                new TaskbarHorizontalInterval(300, 520),
                new TaskbarHorizontalInterval(800, 850)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(658, 792), placement);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(400, 400)]
    [InlineData(900, 800)]
    public void ClampsLeftCenterAndRightPreferredPositions(double preferredLeft, double expectedLeft)
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 200,
            preferredLeft,
            occupied: [],
            out var placement);

        Assert.True(placed);
        Assert.Equal(expectedLeft, placement.Start);
        Assert.Equal(200, placement.Width);
    }

    [Fact]
    public void TiePrefersTheWiderSafeGap()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 250,
            preferredLeft: 100,
            occupied:
            [
                new TaskbarHorizontalInterval(100, 200),
                new TaskbarHorizontalInterval(400, 1000)
            ],
            out var placement,
            safetyGap: 0,
            minimumWidth: 50);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(200, 400), placement);
    }

    [Fact]
    public void NegativePreferredPositionAndOutOfBoundsControlsAreClampedOrIgnored()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 100,
            preferredLeft: -50,
            occupied:
            [
                new TaskbarHorizontalInterval(-20, 100),
                new TaskbarHorizontalInterval(900, 1200),
                new TaskbarHorizontalInterval(-100, -1),
                new TaskbarHorizontalInterval(1100, 1200)
            ],
            out var placement,
            safetyGap: 0,
            minimumWidth: 50);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(100, 200), placement);
    }

    [Fact]
    public void IgnoresMalformedAndNonFiniteOccupiedIntervals()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 500,
            requestedWidth: 200,
            preferredLeft: 100,
            occupied:
            [
                new TaskbarHorizontalInterval(200, 100),
                new TaskbarHorizontalInterval(100, 100),
                new TaskbarHorizontalInterval(double.NaN, 200),
                new TaskbarHorizontalInterval(200, double.PositiveInfinity)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(100, 300), placement);
    }

    [Fact]
    public void SelectsRightSideGapWhenControlsCoverTheLeftSide()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 200,
            preferredLeft: 700,
            occupied: [new TaskbarHorizontalInterval(0, 600)],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(700, 900), placement);
    }

    [Fact]
    public void SelectsCenterGapBetweenControlsOnBothSides()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 200,
            preferredLeft: 400,
            occupied:
            [
                new TaskbarHorizontalInterval(0, 300),
                new TaskbarHorizontalInterval(700, 1000)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(400, 600), placement);
    }

    [Fact]
    public void IgnoresFiniteOccupiedIntervalsFarOutsideTaskbarCoordinates()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 1000,
            requestedWidth: 200,
            preferredLeft: 100,
            occupied:
            [
                new TaskbarHorizontalInterval(-double.MaxValue, -double.MaxValue / 2),
                new TaskbarHorizontalInterval(double.MaxValue / 2, double.MaxValue)
            ],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(100, 300), placement);
    }

    [Fact]
    public void ExtremelyNegativePreferenceStillSelectsTheNearestLargeCoordinateGap()
    {
        var taskbarWidth = Math.ScaleB(1d, 1023);
        var firstGapStart = taskbarWidth / 4;
        var requestedWidth = Math.ScaleB(1d, 970);
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth,
            requestedWidth,
            -double.MaxValue,
            [
                new TaskbarHorizontalInterval(0, firstGapStart),
                new TaskbarHorizontalInterval(taskbarWidth / 2, firstGapStart * 3)
            ],
            out var placement,
            safetyGap: 0,
            minimumWidth: requestedWidth);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(firstGapStart, firstGapStart + requestedWidth), placement);
    }

    [Fact]
    public void VeryLargeFiniteSafetyGapClampsToTheBarWithoutWrapping()
    {
        var taskbarWidth = Math.ScaleB(1d, 1023);
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth,
            requestedWidth: 100,
            preferredLeft: 0,
            occupied: [new TaskbarHorizontalInterval(taskbarWidth / 4, taskbarWidth / 2)],
            out var placement,
            safetyGap: double.MaxValue);

        Assert.False(placed);
        Assert.Equal(default, placement);
    }

    [Fact]
    public void NarrowBarFitsByShrinkingWithoutEnlargingTheRequestedWidth()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 180,
            requestedWidth: 420,
            preferredLeft: 50,
            occupied: [],
            out var placement);

        Assert.True(placed);
        Assert.Equal(new TaskbarHorizontalInterval(0, 180), placement);
    }

    [Fact]
    public void NarrowBarWithoutMinimumSafeWidthReturnsFalse()
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth: 99,
            requestedWidth: 420,
            preferredLeft: 0,
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
        double taskbarWidth,
        double requestedWidth,
        double preferredLeft,
        double safetyGap,
        double minimumWidth)
    {
        var placed = TaskbarFreeSpaceLayout.TryPlace(
            taskbarWidth,
            requestedWidth,
            preferredLeft,
            [],
            out var placement,
            safetyGap,
            minimumWidth);

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
            var taskbarWidth = random.Next(1000, 2001);
            var minimumWidth = random.Next(80, 181);
            var requestedWidth = random.Next(minimumWidth, 451);
            var preferredLeft = random.Next(-200, taskbarWidth + 201);
            var safetyGap = random.Next(0, 17);
            var occupied = new List<TaskbarHorizontalInterval>();
            var occupiedCount = random.Next(0, 21);
            for (var intervalIndex = 0; intervalIndex < occupiedCount; intervalIndex++)
            {
                var start = random.Next(630, taskbarWidth + 101);
                var end = start + random.Next(1, 251);
                occupied.Add(new TaskbarHorizontalInterval(start, end));
            }

            var placed = TaskbarFreeSpaceLayout.TryPlace(
                taskbarWidth,
                requestedWidth,
                preferredLeft,
                occupied,
                out var placement,
                safetyGap,
                minimumWidth);

            Assert.True(placed, $"Iteration {iteration} should retain the reserved left-side gap.");
            Assert.InRange(placement.Start, 0, taskbarWidth);
            Assert.InRange(placement.End, placement.Start, taskbarWidth);
            Assert.InRange(placement.Width, minimumWidth, requestedWidth);
            AssertDoesNotIntersectOccupiedIntervals(placement, taskbarWidth, occupied, safetyGap);
        }
    }

    private static void AssertDoesNotIntersectOccupiedIntervals(
        TaskbarHorizontalInterval placement,
        double taskbarWidth,
        IReadOnlyList<TaskbarHorizontalInterval> occupied,
        double safetyGap)
    {
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

            var expandedStart = Math.Max(0, Math.Clamp(interval.Start, 0, taskbarWidth) - safetyGap);
            var expandedEnd = Math.Min(taskbarWidth, Math.Clamp(interval.End, 0, taskbarWidth) + safetyGap);
            Assert.True(
                placement.End <= expandedStart || placement.Start >= expandedEnd,
                $"Placement {placement} intersects expanded occupied interval [{expandedStart}, {expandedEnd}].");
        }
    }
}
