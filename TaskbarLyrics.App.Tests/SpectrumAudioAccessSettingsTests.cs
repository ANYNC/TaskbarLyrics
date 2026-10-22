using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class SpectrumAudioAccessSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetPreservesConsentAndDisablesSpectrumWhileExplicitRevocationRemainsEffective(bool granted)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings
                {
                    SpectrumAudioAccessGranted = granted,
                    SpectrumDisplayMode = granted ? SpectrumDisplayMode.Always : SpectrumDisplayMode.Disabled,
                    WindowWidth = 620
                };
                using var offsets = new TrackLyricOffsetStore();
                var window = new SettingsWindow(settings, offsets,
                    () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                    (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
                var directory = Path.Combine(AppContext.BaseDirectory, $"spectrum-consent-{Guid.NewGuid():N}");
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void InvokeAsync(string method) => ((Task)typeof(SettingsWindow).GetMethod(method, flags)!
                        .Invoke(window, null)!).GetAwaiter().GetResult();

                    InvokeAsync("ResetDefaultsAsync");
                    Assert.Equal(granted, settings.SpectrumAudioAccessGranted);
                    Assert.Equal(SpectrumDisplayMode.Disabled, settings.SpectrumDisplayMode);
                    Assert.Equal(AppSettings.DefaultWindowWidth, settings.WindowWidth);

                    var payload = JsonSerializer.SerializeToElement(
                        typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null),
                        SettingsWebJson.Options);
                    Assert.Equal(granted, payload.GetProperty("spectrumAudioAccessGranted").GetBoolean());
                    Assert.Equal("Disabled", payload.GetProperty("spectrumDisplayMode").GetString());

                    var store = new SettingsStore(Path.Combine(directory, "settings.json"));
                    Assert.True(store.Save(settings));
                    var loaded = store.Load();
                    Assert.Equal(granted, loaded.SpectrumAudioAccessGranted);
                    Assert.Equal(SpectrumDisplayMode.Disabled, loaded.SpectrumDisplayMode);

                    InvokeAsync("RevokeSpectrumAudioAccessAsync");
                    Assert.False(settings.SpectrumAudioAccessGranted);
                    Assert.Equal(SpectrumDisplayMode.Disabled, settings.SpectrumDisplayMode);
                    InvokeAsync("ResetDefaultsAsync");
                    Assert.False(settings.SpectrumAudioAccessGranted);
                    Assert.True(store.Save(settings));
                    Assert.False(store.Load().SpectrumAudioAccessGranted);
                }
                finally
                {
                    window.Close();
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Spectrum-consent reset did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
