using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public partial class App
{
    private void OnBrowserSiteRequested(string origin)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsExiting || !BrowserSiteRule.TryCreate(origin, null, out var rule)) return;
            var settings = Settings.Clone();
            if (!settings.BrowserSites.Any(site => site.Matches(rule.Origin)) && settings.BrowserSites.Count < 100)
                settings.BrowserSites.Add(rule);
            settings.BrowserWebsiteRecognitionEnabled = true;
            SaveSettings(settings);
            if (_settingsWindow is not null)
                TaskObserver.Observe(_settingsWindow.ApplyExternalSettingsAsync(Settings.Clone()), "browser site settings refresh");
        }));
    }
}
