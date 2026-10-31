using TaskbarLyrics.Core.Utilities;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Http;

namespace TaskbarLyrics.App;

internal sealed record UpdateNotes(string Markdown, string Html, bool Complete)
{
    public bool IsCumulative { get; init; }
}
internal static class UpdateReleaseNotes
{
    internal const int MaximumLength = 512 * 1024;
    public static async Task<UpdateNotes> LoadAsync(UpdateChecker.GitHubRelease target, string current,
        CancellationToken cancellationToken = default)
    {
        var releases = new List<UpdateChecker.GitHubRelease> { target };
        var complete = false;
        if (UpdateChecker.TryParseVersion(current, out var currentVersion) &&
            UpdateChecker.TryParseVersion(target.TagName, out var targetVersion) && targetVersion != currentVersion)
        {
            try
            {
                for (var page = 1; page <= 20; page++)
                {
                    var entries = await UpdateChecker.ReadAsync<List<UpdateChecker.GitHubRelease>>(
                        $"https://api.github.com/repos/ANYNC/TaskbarLyrics/releases?per_page=100&page={page}", cancellationToken)
                        ?? throw new JsonException("Missing release list.");
                    releases.AddRange(entries);
                    if (entries.Count < 100) { complete = true; break; }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or JsonException or TaskCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Debug($"Cumulative release notes incomplete: {ex.GetType().Name}");
            }
        }
        else complete = true;
        return Merge(releases, target, current, complete);
    }

    internal static UpdateNotes Merge(IEnumerable<UpdateChecker.GitHubRelease> releases,
        UpdateChecker.GitHubRelease target, string current, bool complete)
    {
        if (!UpdateChecker.TryParseVersion(current, out var from) || !UpdateChecker.TryParseVersion(target.TagName, out var to))
            return new("", "", false);
        var candidates = releases.Where(r => r is not null && !r.Draft && !r.Prerelease &&
            UpdateChecker.TryParseVersion(r.TagName, out var version) && version > from && version <= to)
            .DistinctBy(r => r.Id).GroupBy(r => { if (!UpdateChecker.TryParseVersion(r.TagName, out var v)) throw new JsonException("Invalid release version."); return v; })
            .OrderByDescending(g => g.Key).ToList();
        var selected = new List<UpdateChecker.GitHubRelease>();
        if (to < from)
        {
            var installedRelease = releases.FirstOrDefault(r => r is not null && !r.Draft && !r.Prerelease &&
                UpdateChecker.TryParseVersion(r.TagName, out var version) && version == from);
            selected.Add(installedRelease ?? target);
        }
        else selected.Add(target);
        foreach (var group in candidates)
        {
            if (group.Count() > 1) complete = false;
            if (group.Key == to) continue; // The fixed latest release owns the target version.
            if (group.Count() == 1) selected.Add(group.Single());
        }
        var markdown = new StringBuilder();
        var html = new StringBuilder();
        foreach (var release in selected)
        {
            var title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName ?? "" : release.Name;
            var body = release.Body ?? "";
            var rendered = string.IsNullOrWhiteSpace(release.BodyHtml) ? UpdateMarkdown.ToHtml(body) : release.BodyHtml;
            if (Encoding.UTF8.GetByteCount(body) + Encoding.UTF8.GetByteCount(rendered) +
                Encoding.UTF8.GetByteCount(title) > MaximumLength ||
                markdown.Length + html.Length + body.Length + rendered.Length + title.Length * 2 > MaximumLength)
            { complete = false; break; }
            if (html.Length > 0) { html.Append("<hr/>"); markdown.Append("\n\n---\n\n"); }
            markdown.Append("## ").Append(title).Append("\n\n").Append(body);
            html.Append("<h2>").Append(WebUtility.HtmlEncode(title)).Append("</h2>").Append(rendered);
        }
        return new(markdown.ToString(), html.ToString(), complete)
        {
            IsCumulative = to > from && selected.Count > 1
        };
    }
}
