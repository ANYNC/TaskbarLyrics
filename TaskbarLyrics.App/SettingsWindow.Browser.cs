using System.ComponentModel;
using System.IO;
using System.Text.Json;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public partial class SettingsWindow
{
    private string? _lastBrowserState;
    private static IReadOnlyList<BrowserConnectionStatus> BrowserConnections =>
        (System.Windows.Application.Current as App)?.BrowserSessions?.GetStatus() ?? [];

    private async Task PushBrowserStateAsync()
    {
        if (!_isWebReady || _isClosed || SettingsWebView.CoreWebView2 is null) return;
        var connections = BrowserConnections;
        var json = JsonSerializer.Serialize(connections, SettingsWebJson.Options);
        if (_lastBrowserState == json) return;
        _lastBrowserState = json;
        await SettingsWebView.ExecuteScriptAsync(WebViewMessageScriptFactory.Dispatch("settingsApp", "browserState", new { connections }));
    }

    internal bool ApplyBrowserSiteAction(string type, JsonElement? value)
    {
        _settings.BrowserSites = BrowserSiteRule.Normalize(_settings.BrowserSites);
        if (value is not { ValueKind: JsonValueKind.Object } payload) return false;
        if (type == "browserSiteAdd")
        {
            if (!payload.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.String) return false;
            var name = payload.TryGetProperty("name", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : null;
            if (_settings.BrowserSites.Count >= 100 || !BrowserSiteRule.TryCreate(address.GetString(), name, out var rule) ||
                _settings.BrowserSites.Any(site => site.Matches(rule.Origin))) return false;
            _settings.BrowserSites.Add(rule);
            _settings.BrowserWebsiteRecognitionEnabled = true;
            return true;
        }
        if (type == "browserSitesReorder")
        {
            if (!payload.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != _settings.BrowserSites.Count) return false;
            var order = ids.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray();
            if (order.Distinct(StringComparer.Ordinal).Count() != _settings.BrowserSites.Count ||
                order.Any(id => !_settings.BrowserSites.Any(site => site.Id == id))) return false;
            _settings.BrowserSites = order.Select(id => _settings.BrowserSites.First(site => site.Id == id)).ToList();
            return true;
        }
        if (!payload.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String) return false;
        var index = _settings.BrowserSites.FindIndex(site => site.Id == idElement.GetString());
        if (index < 0) return false;
        if (type == "browserSiteRemove") { _settings.BrowserSites.RemoveAt(index); return true; }
        if (type != "browserSiteToggle" || !payload.TryGetProperty("enabled", out var enabled) ||
            enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        _settings.BrowserSites[index] = _settings.BrowserSites[index] with { Enabled = enabled.GetBoolean() };
        return true;
    }

    private async Task HandleBrowserSiteActionAsync(string type, JsonElement? value)
    {
        if (ApplyBrowserSiteAction(type, value)) await SaveSettingsAndNotifyWebAsync();
        else await PushBrowserResultAsync(Text.T("UI_BrowserInvalidSite"));
        await PushSettingsToWebAsync();
    }

    private async Task InstallBrowserExtensionAsync(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.String } kind || kind.GetString() is not ("Chrome" or "Edge")) return;
        try
        {
            var browser = kind.GetString()!;
            BrowserExtensionInstallation.OpenInstallation(browser);
            await PushBrowserResultAsync(Text.T(BrowserExtensionInstallation.StoreId(browser) is null
                ? "UI_BrowserDevelopmentInstall" : "UI_BrowserConfirmInstall"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException)
        { await PushBrowserResultAsync(Text.T("UI_BrowserInstallFailed")); }
    }

    private Task PushBrowserResultAsync(string message) => SettingsWebView.CoreWebView2 is null ? Task.CompletedTask :
        SettingsWebView.ExecuteScriptAsync(WebViewMessageScriptFactory.Dispatch("settingsApp", "browserActionResult", new { message }));
}
