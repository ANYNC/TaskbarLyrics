using System.Reflection;
using System.Windows.Interop;
using Windows.UI.Composition;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void NativeBackgroundFadeCompletesAndReversalCannotHideTheReopenedWindow()
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var context = new FloatingMaterialCompositor();
            using var material = new FloatingMaterialWindow(context);
            try
            {
                material.Position(FloatingLyricsPanelController.GetBounds(owner), 1, new WindowInteropHelper(owner).Handle);
                material.SetVisible(true);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.True(IsWindowVisible(material.Handle));
                Assert.Equal(1, ReadMaterialOpacity(material));

                material.SetVisible(false);
                PumpMessages(TimeSpan.FromMilliseconds(50));
                material.SetVisible(true);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.True(IsWindowVisible(material.Handle));
                Assert.Equal(1, ReadMaterialOpacity(material));

                // Repeating a pending hide must not restart the animation or its completion.
                material.SetVisible(false);
                for (var step = 0; step < 6; step++)
                {
                    PumpMessages(TimeSpan.FromMilliseconds(50));
                    material.SetVisible(false);
                }
                Assert.False(IsWindowVisible(material.Handle));
                Assert.Equal(0, ReadMaterialOpacity(material));
                Assert.Equal(0, TaskbarNativeMethods.GetWindowLongPtr(material.Handle, -20).ToInt64() & 0x00080000);
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateBackgroundVisibilityCancelsAnyPendingAnimationCompletion(bool visible)
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var context = new FloatingMaterialCompositor();
            using var material = new FloatingMaterialWindow(context);
            try
            {
                material.Position(FloatingLyricsPanelController.GetBounds(owner), 1, new WindowInteropHelper(owner).Handle);
                material.SetVisible(visible, immediate: true);
                material.SetVisible(!visible);
                PumpMessages(TimeSpan.FromMilliseconds(50));
                material.SetVisible(visible, immediate: true);
                Assert.Equal(visible, IsWindowVisible(material.Handle));
                Assert.Equal(visible ? 1 : 0, ReadMaterialOpacity(material));
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.Equal(visible, IsWindowVisible(material.Handle));
                Assert.Equal(visible ? 1 : 0, ReadMaterialOpacity(material));

                material.SetVisible(!visible);
                material.Dispose();
                PumpMessages(TimeSpan.FromMilliseconds(300));
            }
            finally { owner.Close(); }
        });
    }

    private static float ReadMaterialOpacity(FloatingMaterialWindow material)
    {
        var visual = (ContainerVisual)typeof(FloatingMaterialWindow)
            .GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(material)!;
        return visual.Opacity;
    }
}
