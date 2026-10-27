using System.Runtime.InteropServices;
using System.Windows;

namespace TaskbarLyrics.App;

// Keep native UIA objects inside one MTA query. Using WPF's AutomationElement
// here creates managed Win32 proxy providers that accumulate on repeated reads.
internal static class NativeTaskbarAutomation
{
    private const int BoundingRectangleProperty = 30001;
    private const int ProcessIdProperty = 30002;
    private const int ControlTypeProperty = 30003;
    private const int IsOffscreenProperty = 30022;
    private const int Children = 2;
    private const int Descendants = 4;
    private static readonly int[] InteractiveTypes = [50000, 50002, 50013, 50004, 50007];

    internal static TaskbarInterval[]? ReadOccupiedBounds(IntPtr taskbar, Rect taskbarBounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("Taskbar UI Automation queries require an MTA worker.");

        using var references = new ComReferences();
        var automation = references.Own((INativeAutomation)new NativeAutomation());
        var root = references.Own(automation.ElementFromHandle(taskbar));
        if (root.GetCurrentPropertyValue(ProcessIdProperty) is not int owner || owner <= 0) return null;
        var explorerOnly = Property(ProcessIdProperty, owner);
        var visible = Property(IsOffscreenProperty, false);
        var interactive = Property(ControlTypeProperty, InteractiveTypes[0]);
        foreach (var type in InteractiveTypes.Skip(1))
            interactive = references.Own(automation.CreateOrCondition(interactive, Property(ControlTypeProperty, type)));
        var controlsCondition = references.Own(automation.CreateAndCondition(explorerOnly,
            references.Own(automation.CreateAndCondition(visible, interactive))));

        // As before, enter only Explorer-owned immediate children. A whole-tree
        // search also visits embedded third-party windows, including our WebViews.
        var hosts = references.Own(root.FindAll(Children, explorerOnly));
        var cache = references.Own(automation.CreateCacheRequest());
        cache.AddProperty(BoundingRectangleProperty);
        cache.SetTreeScope(1); // TreeScope_Element
        cache.SetAutomationElementMode(0); // AutomationElementMode_None: rectangles only
        var occupied = new HashSet<TaskbarInterval>();
        for (var hostIndex = 0; hostIndex < hosts.GetLength(); hostIndex++)
        {
            token.ThrowIfCancellationRequested();
            var host = references.Own(hosts.GetElement(hostIndex));
            if (host.GetCurrentPropertyValue(ControlTypeProperty) is int controlType &&
                InteractiveTypes.Contains(controlType) && host.GetCurrentPropertyValue(IsOffscreenProperty) is false)
                Add(host.GetCurrentPropertyValue(BoundingRectangleProperty));

            var controls = references.Own(host.FindAllBuildCache(Descendants, controlsCondition, cache));
            for (var index = 0; index < controls.GetLength(); index++)
            {
                token.ThrowIfCancellationRequested();
                var control = references.Own(controls.GetElement(index));
                Add(control.GetCachedPropertyValue(BoundingRectangleProperty));
            }
        }

        // A missing tree is unknown, never permission to overlap the whole bar.
        return occupied.Count > 0 ? occupied.ToArray() : null;

        INativeAutomationCondition Property(int id, object value) =>
            references.Own(automation.CreatePropertyCondition(id, value));

        void Add(object value)
        {
            if (value is not double[] { Length: 4 } coordinates ||
                coordinates.Any(number => !double.IsFinite(number)) || coordinates[2] <= 0 || coordinates[3] <= 0)
                return;
            var rect = new Rect(coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
            if (rect.IntersectsWith(taskbarBounds))
                occupied.Add(taskbarBounds.Height > taskbarBounds.Width
                    ? new TaskbarInterval(rect.Top, rect.Bottom)
                    : new TaskbarInterval(rect.Left, rect.Right));
        }
    }

    private sealed class ComReferences : IDisposable
    {
        private readonly List<object> _owned = [];
        public T Own<T>(T value) where T : class
        {
            _owned.Add(value);
            return value;
        }

        public void Dispose()
        {
            // Each marshalled return owns one RCW reference. Do not FinalRelease:
            // UIA may return the same COM identity more than once in a query.
            for (var index = _owned.Count - 1; index >= 0; index--)
                Marshal.ReleaseComObject(_owned[index]);
            _owned.Clear();
        }
    }
}
