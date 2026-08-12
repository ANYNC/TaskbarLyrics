using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public static partial class LyricSearchPlanner
{
    public static LyricSearchPlan CreatePlan(TrackIdentity track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            throw new ArgumentException("Track title cannot be empty.", nameof(track));
        }

        var artists = ExpandArtists(track.Artists);
        var variants = new List<SearchQueryVariant>();
        AddVariant(variants, "exact", track.Title, artists, track, []);

        var normalizedTitle = LyricMatcher.NormalizeForSearch(track.Title);
        var normalizedArtists = artists
            .Select(LyricMatcher.NormalizeForSearch)
            .Where(value => value.Length > 0)
            .ToArray();
        if (!string.Equals(normalizedTitle, track.Title, StringComparison.Ordinal) ||
            !artists.SequenceEqual(normalizedArtists, StringComparer.Ordinal))
        {
            AddVariant(
                variants,
                "normalized",
                normalizedTitle,
                normalizedArtists,
                track,
                ["punctuation-script-diacritic-normalization"]);
        }

        if (artists.Length > 1)
        {
            AddVariant(
                variants,
                "primary-artist",
                track.Title,
                [artists[0]],
                track,
                ["primary-artist-only"]);
        }

        var relaxedTitle = RelaxTitle(track.Title);
        if (relaxedTitle.Length > 0 &&
            !string.Equals(relaxedTitle, track.Title, StringComparison.Ordinal))
        {
            AddVariant(
                variants,
                "relaxed-title",
                relaxedTitle,
                artists,
                track,
                ["removed-feature-or-bracket-suffix"]);
        }

        var versionRelaxedTitle = RelaxDelimitedVersionSuffix(track.Title);
        if (versionRelaxedTitle.Length > 0 &&
            !string.Equals(versionRelaxedTitle, track.Title, StringComparison.Ordinal))
        {
            AddVariant(
                variants,
                "version-relaxed-title",
                versionRelaxedTitle,
                artists,
                track,
                ["removed-delimited-version-suffix"]);
        }

        return new LyricSearchPlan(track, variants);
    }

    private static string[] ExpandArtists(IReadOnlyList<string> artists)
    {
        return artists
            .SelectMany(artist => ArtistSeparatorRegex().Split(artist))
            .Select(artist => artist.Trim())
            .Where(artist => artist.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddVariant(
        List<SearchQueryVariant> variants,
        string id,
        string title,
        IReadOnlyList<string> artists,
        TrackIdentity track,
        IReadOnlyList<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            variants.Any(variant =>
                string.Equals(variant.Title, title, StringComparison.Ordinal) &&
                variant.Artists.SequenceEqual(artists, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        variants.Add(new SearchQueryVariant(
            id,
            title,
            artists,
            track.Album,
            track.Duration,
            reasons));
    }

    private static string RelaxTitle(string title)
    {
        var relaxed = title.Trim();
        while (true)
        {
            var next = BracketOrFeatureSuffixRegex().Replace(relaxed, string.Empty).Trim();
            if (string.Equals(next, relaxed, StringComparison.Ordinal))
            {
                return relaxed;
            }

            relaxed = next;
        }
    }

    private static string RelaxDelimitedVersionSuffix(string title)
    {
        var delimiters = DelimitedSuffixSeparatorRegex().Matches(title);
        if (delimiters.Count == 0)
        {
            return title;
        }

        var delimiter = delimiters[delimiters.Count - 1];
        var suffix = title[(delimiter.Index + delimiter.Length)..];
        return LyricMatcher.ContainsContentVersionMarker(suffix)
            ? title[..delimiter.Index].Trim()
            : title;
    }

    [GeneratedRegex(@"\s*(?:,|&|/|、|，|;|；|\bfeat\.?\b|\bft\.?\b|\bwith\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparatorRegex();

    [GeneratedRegex(@"\s*(?:[\(\[\{（【].*?[\)\]\}）】]|\b(?:feat\.?|ft\.?|with)\b.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex BracketOrFeatureSuffixRegex();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex DelimitedSuffixSeparatorRegex();
}
