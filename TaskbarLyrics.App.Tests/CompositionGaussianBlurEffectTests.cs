using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.UI.Composition;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CompositionGaussianBlurEffectTests
{
    private static readonly float[] BlurAmounts = [16f, 24f, 36f];
    [Fact]
    public void ExportsInspectableSourceAndD2DInteropOnTheSameComObject()
    {
        var effect = new CompositionGaussianBlurEffect();
        var pointer = Marshal.GetComInterfaceForObject(effect, typeof(CompositionGaussianBlurEffect.IGraphicsEffectAbi));
        try
        {
            foreach (var type in new[] { typeof(CompositionGaussianBlurEffect.IInspectableAbi),
                typeof(CompositionGaussianBlurEffect.IGraphicsEffectSourceAbi), typeof(CompositionGaussianBlurEffect.IGraphicsEffectD2D1Interop) })
            {
                var id = type.GUID;
                Assert.Equal(0, Marshal.QueryInterface(pointer, ref id, out var queried));
                Assert.NotEqual(IntPtr.Zero, queried);
                Marshal.Release(queried);
            }
        }
        finally { Marshal.Release(pointer); }
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(uint.MaxValue)]
    public void RejectsUnknownPropertiesAndSourcesWithoutReturningOwnedPointers(uint index)
    {
        var effect = new CompositionGaussianBlurEffect();
        Assert.Equal(unchecked((int)0x80070057), effect.GetProperty(index, out var property));
        Assert.Equal(IntPtr.Zero, property);
        Assert.Equal(unchecked((int)0x80070057), effect.GetSource(index, out var source));
        Assert.Equal(IntPtr.Zero, source);
        Assert.Equal(unchecked((int)0x80070057), effect.GetNamedPropertyMapping("Unknown", out _, out _));
    }

    [Fact]
    public async Task CompilesAndUpdatesBlurAmountWithoutRecreatingTheBrush()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using (var context = new FloatingMaterialCompositor())
                    {
                        using var factory = new CompositionGaussianBlurEffect().CreateFactory(context.Compositor);
                        using var brush = factory.CreateBrush();
                        using var source = context.Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 80, 100, 120));
                        brush.SetSourceParameter(CompositionGaussianBlurEffect.SourceParameter, source);
                        var started = Stopwatch.GetTimestamp();
                        while (factory.LoadStatus == CompositionEffectFactoryLoadStatus.Pending &&
                            Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                        Assert.Equal(CompositionEffectFactoryLoadStatus.Success, factory.LoadStatus);
                        Assert.Null(factory.ExtendedError);
                        foreach (var amount in BlurAmounts)
                        {
                            brush.Properties.InsertScalar(CompositionGaussianBlurEffect.AmountProperty, amount);
                            Assert.Equal(CompositionGetValueStatus.Succeeded,
                                brush.Properties.TryGetScalar(CompositionGaussianBlurEffect.AmountProperty, out var actual));
                            Assert.Equal(amount, actual);
                        }
                    }
                    await FloatingMaterialCompositor.WaitForPendingShutdownAsync();
                    completed.SetResult();
                }
                catch (Exception ex) { completed.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "The effect dispatcher did not shut down.");
    }
}
