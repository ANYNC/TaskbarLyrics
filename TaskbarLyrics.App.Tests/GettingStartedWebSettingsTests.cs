using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class GettingStartedWebSettingsTests
{
    [Theory]
    [InlineData("\"gettingStarted\"", true)]
    [InlineData("\"sources\"", false)]
    [InlineData("\"unknown\"", false)]
    [InlineData("123", false)]
    [InlineData("{}", false)]
    [InlineData("null", false)]
    public void OnlyAValidDisplayedGettingStartedPageMarksTheGuideSeen(string payload, bool expected)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings { HasSeenGettingStarted = false };
                using var offsets = new TrackLyricOffsetStore();
                var window = new SettingsWindow(settings, offsets,
                    () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                    (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    window.NavigateToPageAsync("gettingStarted").GetAwaiter().GetResult();
                    Assert.False(settings.HasSeenGettingStarted);
                    Assert.Equal("gettingStarted", typeof(SettingsWindow).GetField("_pendingPage", flags)!.GetValue(window));
                    var message = SettingsWebMessageRouter.Parse(
                        $"{{\"version\":1,\"type\":\"settingsPageChanged\",\"payload\":{payload}}}");
                    Assert.NotNull(message);
                    var task = (Task)typeof(SettingsWindow).GetMethod("HandleSettingsPageChangedAsync", flags)!
                        .Invoke(window, [message.Value])!;
                    task.GetAwaiter().GetResult();
                    Assert.Equal(expected, settings.HasSeenGettingStarted);
                    Assert.False((bool)typeof(SettingsWindow).GetField("_isTrackOffsetsPageActive", flags)!.GetValue(window)!);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Getting-started interaction did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
