using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarLyrics.App;

namespace TaskbarLyrics.Diagnostics;

// Opt-in desktop diagnostics. No App, settings store, media session, startup
// registration or user-data database is created by these commands.
internal static class TaskbarSpaceDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                throw new PlatformNotSupportedException("Taskbar space diagnostics require an interactive Windows 11 desktop.");
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            if (args.Length == 1 && args[0] == "verify")
            {
                var report = await VerifyOnDispatcherAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args.Length is 1 or 2 && args[0] == "benchmark")
            {
                var seconds = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 15;
                if (seconds is < 5 or > 60) throw new ArgumentOutOfRangeException(nameof(args));
                Console.WriteLine(JsonSerializer.Serialize(await BenchmarkAsync(seconds), JsonOptions));
                return 0;
            }
            Console.Error.WriteLine("Usage: --taskbar-space verify | --taskbar-space benchmark [5..60 seconds per trial]");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<object> BenchmarkAsync(int seconds)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var native))
            throw new InvalidOperationException("An interactive Windows taskbar is required.");
        var bounds = ToRect(native);
        var full = ReadFullTree(taskbar, bounds, CancellationToken.None);
        var scoped = TaskbarOccupiedSpaceProvider.ReadOccupiedBounds(taskbar, bounds, CancellationToken.None);
        if (full is null || scoped is null || !Canonical(full).SequenceEqual(Canonical(scoped)))
            throw new InvalidOperationException("Scoped and full-tree reads did not identify the same occupied regions; retry with a stable taskbar.");

        var trials = new List<object>();
        // Alternate order so a single warmup or transient desktop load does not
        // systematically favor one reader. The baseline retains the scheduler.
        foreach (var mode in new[] { "idle", "full-tree", "scoped", "scoped", "full-tree", "idle" })
        {
            var samples = new ConcurrentBag<double>();
            var errors = new ConcurrentDictionary<string, int>();
            var attempts = 0;
            var unexpected = 0;
            TaskbarInterval[]? Reader(IntPtr handle, Rect rect, CancellationToken token)
            {
                var started = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref attempts);
                try
                {
                    var result = mode switch
                    {
                        "idle" => [],
                        "full-tree" => ReadFullTree(handle, rect, token),
                        _ => TaskbarOccupiedSpaceProvider.ReadOccupiedBounds(handle, rect, token)
                    };
                    if (result is null) errors.AddOrUpdate("Unavailable", 1, (_, count) => count + 1);
                    else samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    return result;
                }
                catch (Exception exception)
                {
                    var key = $"{exception.GetType().Name}:0x{exception.HResult:X8}";
                    errors.AddOrUpdate(key, 1, (_, count) => count + 1);
                    if (exception is not (ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException))
                        Interlocked.Increment(ref unexpected);
                    throw;
                }
            }
            using var provider = new TaskbarOccupiedSpaceProvider(Reader, TimeProvider.System);
            provider.GetOccupiedBounds(taskbar, bounds);
            await provider.PendingRead.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(550);
            samples.Clear();
            errors.Clear();
            attempts = 0;
            unexpected = 0;
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                provider.GetOccupiedBounds(taskbar, bounds);
                await Task.Delay(60);
            }
            await provider.PendingRead.WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = timer.Elapsed.TotalSeconds;
            process.Refresh();
            var values = samples.Order().ToArray();
            if (values.Length == 0 || unexpected > 0)
                throw new InvalidOperationException($"No usable benchmark for {mode}: {JsonSerializer.Serialize(errors)}");
            trials.Add(new
            {
                Mode = mode,
                Seconds = Math.Round(elapsed, 2),
                Queries = values.Length,
                Attempts = attempts,
                TransientErrors = errors,
                MedianQueryMs = Math.Round(values[values.Length / 2], 3),
                P95QueryMs = Math.Round(values[(int)Math.Floor((values.Length - 1) * .95)], 3),
                CpuPercent = Math.Round((process.TotalProcessorTime - cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 4),
                AllocatedMB = Math.Round((GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1048576d, 3)
            });
        }
        return new
        {
            Scope = "Isolated geometry-query client, not whole-application or Explorer CPU",
            LogicalProcessors = Environment.ProcessorCount,
            OccupiedRegions = Canonical(scoped).Length,
            Trials = trials
        };
    }

    private static TaskbarInterval[] Canonical(IEnumerable<TaskbarInterval> ranges) =>
        ranges.Distinct().OrderBy(range => range.Start).ThenBy(range => range.End).ToArray();

    // A reproducible reference for the initial broad scan; used only by the
    // developer benchmark, never by the production positioning path.
    private static TaskbarInterval[]? ReadFullTree(IntPtr taskbar, Rect bounds, CancellationToken token)
    {
        var root = AutomationElement.FromHandle(taskbar);
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, root.Current.ProcessId),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
            new OrCondition(new[] { ControlType.Button, ControlType.CheckBox, ControlType.RadioButton, ControlType.Edit, ControlType.ListItem }
                .Select(type => (System.Windows.Automation.Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, type)).ToArray()));
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        using var activation = cache.Activate();
        var controls = root.FindAll(TreeScope.Descendants, condition);
        var ranges = new List<TaskbarInterval>();
        for (var index = 0; index < controls.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var rect = controls[index].Cached.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && rect.IntersectsWith(bounds))
                ranges.Add(bounds.Height > bounds.Width
                    ? new TaskbarInterval(rect.Top, rect.Bottom)
                    : new TaskbarInterval(rect.Left, rect.Right));
        }
        return ranges.Count > 0 ? ranges.ToArray() : null;
    }

    private static Task<object> VerifyOnDispatcherAsync()
    {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { completion.TrySetResult(await VerifyAsync()); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            }));
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "Taskbar space verification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task<object> VerifyAsync()
    {
        var results = new List<object>();
        var settings = new AppSettings { UseFloatingWindow = false, WindowWidth = 420 };
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) throw new InvalidOperationException("Interactive Explorer is required.");
        var fixture = CreateWindow();
        Window? icon = null;
        try
        {
            using var anchor = new EmbeddedTaskbarAnchor();
            anchor.Attach(fixture, settings, null);
            await Task.Delay(1200);
            var before = await CheckLiveAsync("Before", fixture, taskbar);
            results.Add(before);
            icon = CreateWindow(showInTaskbar: true);
            await Task.Delay(1400);
            results.Add(await CheckLiveAsync("ExtraTaskbarIcon", fixture, taskbar));
            icon.Close();
            icon = null;
            await Task.Delay(1400);
            results.Add(await CheckLiveAsync("IconClosed", fixture, taskbar));
        }
        finally { icon?.Close(); fixture.Close(); }

        var parent = CreateWindow();
        var child = CreateWindow();
        try
        {
            var parentHandle = new WindowInteropHelper(parent).Handle;
            var childHandle = new WindowInteropHelper(child).Handle;
            TaskbarNativeMethods.SetWindowPos(parentHandle, IntPtr.Zero, -1400, 40, 1000, 60,
                TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
            SetParent(childHandle, parentHandle);
            var style = TaskbarNativeMethods.GetWindowLongPtr(childHandle, -16).ToInt64();
            TaskbarNativeMethods.SetWindowLongPtr(childHandle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
            var clock = new ProbeClock();
            var spaceStage = 0;
            var sidebarScale = TaskbarPlacementService.GetPixelsPerDip(child);
            using var provider = new TaskbarOccupiedSpaceProvider((_, rect, token) =>
            {
                token.ThrowIfCancellationRequested();
                var vertical = rect.Height > rect.Width;
                var origin = vertical ? rect.Top : rect.Left;
                var end = vertical ? rect.Bottom : rect.Right;
                var scale = vertical ? sidebarScale : 1;
                return spaceStage switch
                {
                    0 => [new(origin + 90 * scale, origin + 300 * scale)],
                    1 => [new(origin, end)],
                    _ => [new(origin + 400 * scale, end)]
                };
            }, clock);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            GetWindowRect(parentHandle, out var parentRect);
            provider.GetOccupiedBounds(parentHandle, ToRect(parentRect));
            await provider.PendingRead;
            anchor.Position(childHandle, parentHandle, child, settings, null);
            GetWindowRect(childHandle, out var relocated);
            var safetyGapPixels = 8 * TaskbarPlacementService.GetPixelsPerDip(child);
            Require(relocated.Right > relocated.Left && relocated.Left >= parentRect.Left + 300 + safetyGapPixels - 1 &&
                relocated.Right <= parentRect.Right,
                "Narrow left region did not relocate to the available right region.");
            results.Add(new { Stage = "RelocateAcrossControls", Passed = true, Left = relocated.Left, Width = relocated.Right - relocated.Left });

            settings.TaskbarAvoidanceEnabled = false;
            Require(anchor.Position(childHandle, parentHandle, child, settings, null), "Disabled avoidance positioning failed.");
            GetWindowRect(childHandle, out var configured);
            settings.TaskbarAvoidanceEnabled = true;
            spaceStage = 1;
            clock.Advance(TimeSpan.FromMilliseconds(500));
            provider.GetOccupiedBounds(parentHandle, ToRect(parentRect));
            await provider.PendingRead;
            anchor.Position(childHandle, parentHandle, child, settings, null);
            GetWindowRect(childHandle, out var fallback);
            Require(fallback.Equals(configured) && fallback.Right > fallback.Left,
                "Fully occupied taskbar did not use the configured fallback bounds.");
            spaceStage = 2;
            clock.Advance(TimeSpan.FromMilliseconds(500));
            provider.GetOccupiedBounds(parentHandle, ToRect(parentRect));
            await provider.PendingRead;
            anchor.Position(childHandle, parentHandle, child, settings, null);
            GetWindowRect(childHandle, out var restored);
            Require(restored.Right > restored.Left && restored.Left == parentRect.Left && restored.Right < parentRect.Left + 400,
                "Negative-origin surface did not recover into the free interval.");
            results.Add(new { Stage = "NoSpaceFallbackAndNegativeOriginRecovery", Passed = true, RestoredWidth = restored.Right - restored.Left });

            settings.YOffset = 40;
            foreach (var (side, left, top) in new[] { ("Left", -1400, -600), ("Right", 1900, 40) })
            {
                TaskbarNativeMethods.SetWindowPos(parentHandle, IntPtr.Zero, left, top,
                    (int)Math.Round(60 * sidebarScale), (int)Math.Round(900 * sidebarScale),
                    TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
                GetWindowRect(parentHandle, out parentRect);
                spaceStage = 0;
                clock.Advance(TimeSpan.FromMilliseconds(500));
                provider.GetOccupiedBounds(parentHandle, ToRect(parentRect));
                await provider.PendingRead;
                Require(anchor.Position(childHandle, parentHandle, child, settings, null), "Sidebar positioning failed.");
                GetWindowRect(childHandle, out var relocatedSidebar);
                Require(relocatedSidebar.Right - relocatedSidebar.Left == parentRect.Right - parentRect.Left &&
                    relocatedSidebar.Left == parentRect.Left && relocatedSidebar.Bottom <= parentRect.Bottom &&
                    relocatedSidebar.Top >= parentRect.Top + 308 * sidebarScale - 1,
                    $"{side} sidebar did not relocate below the occupied controls.");

                results.Add(new
                {
                    Stage = $"{side}SidebarPlacement",
                    Passed = true,
                    Scale = sidebarScale,
                    Top = relocatedSidebar.Top,
                    Height = relocatedSidebar.Bottom - relocatedSidebar.Top
                });

            }
        }
        finally { child.Close(); parent.Close(); }
        return new { Platform = Environment.OSVersion.VersionString, Results = results };
    }

    private static Window CreateWindow(bool showInTaskbar = false)
    {
        var window = new Window
        {
            Title = "Taskbar space diagnostic (temporary)",
            Width = 420,
            Height = 44,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowActivated = false,
            ShowInTaskbar = showInTaskbar,
            WindowState = showInTaskbar ? WindowState.Minimized : WindowState.Normal,
            Left = -10000,
            Top = -10000
        };
        window.Show();
        return window;
    }

    private static async Task<object> CheckLiveAsync(string stage, Window window, IntPtr taskbar)
    {
        GetWindowRect(taskbar, out var bar);
        var readStarted = Stopwatch.GetTimestamp();
        var occupied = await Task.Run(() => TaskbarOccupiedSpaceProvider.ReadOccupiedBounds(
            taskbar, ToRect(bar), CancellationToken.None));
        var readMilliseconds = Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds;
        GetWindowRect(new WindowInteropHelper(window).Handle, out var lyric);
        Require(occupied is not null && lyric.Right > lyric.Left,
            $"Live taskbar geometry unavailable: Stage={stage}, Lyric={lyric.Left}..{lyric.Right}, " +
            $"Taskbar={bar.Left}..{bar.Right}, Occupied={JsonSerializer.Serialize(occupied)}.");
        var gap = 8 * TaskbarPlacementService.GetPixelsPerDip(window);
        var vertical = bar.Bottom - bar.Top > bar.Right - bar.Left;
        var minimumGap = occupied!.Min(range => vertical
            ? Math.Max(range.Start - lyric.Bottom, lyric.Top - range.End)
            : Math.Max(range.Start - lyric.Right, lyric.Left - range.End));
        Require(minimumGap >= gap - 1,
            $"Live lyric safety gap was not preserved: Stage={stage}, ReadMs={readMilliseconds:F1}, " +
            $"Lyric={lyric.Left}..{lyric.Right}, Gap={gap}, " +
            $"Occupied={JsonSerializer.Serialize(occupied)}.");
        return new
        {
            Stage = stage,
            Passed = true,
            Left = lyric.Left,
            Width = lyric.Right - lyric.Left,
            SafetyGapPixels = gap,
            MinimumMeasuredGapPixels = minimumGap
        };
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static Rect ToRect(TaskbarNativeMethods.NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private sealed class ProbeClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out TaskbarNativeMethods.NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
}
