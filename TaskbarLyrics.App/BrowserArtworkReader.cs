using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

// Page-supplied addresses are untrusted. Resolve and connect to the validated
// address together, including after redirects; never forward browser cookies.
internal static class BrowserArtworkReader
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly HttpRequestOptionsKey<bool> AllowPrivateOrigin = new("TaskbarLyrics.AuthorizedArtworkOrigin");
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = ConnectPublicAsync
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static async Task<byte[]?> ReadAsync(IReadOnlyList<BrowserArtwork> artwork, CancellationToken token,
        HttpClient? client = null, string? origin = null)
    {
        foreach (var candidate in artwork.OrderBy(image => SizeDistance(image.Sizes)))
        {
            if (!BrowserMediaDetailsParser.ArtworkAddress(candidate.Src)) continue;
            var uri = new Uri(candidate.Src);
            try
            {
                for (var redirects = 0; redirects <= 3; redirects++)
                {
                    if (!IsAllowedAddress(uri, origin)) break;
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Options.Set(AllowPrivateOrigin, SameOrigin(uri, origin));
                    using var response = await (client ?? Client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
                    {
                        uri = new Uri(uri, location);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumBytes ||
                        response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif")) break;
                    await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    using var bytes = new MemoryStream();
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        if (bytes.Length + read > MaximumBytes) return null;
                        bytes.Write(buffer, 0, read);
                    }
                    var result = bytes.ToArray();
                    if (IsRasterImage(result)) return result;
                    break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException)
            {
                // Full URLs may carry signed query parameters; log only the class.
                Log.Diagnostic("BROWSER", $"ArtworkReadFailed Error={exception.GetType().Name}");
            }
        }
        return null;
    }

    internal static bool IsPublicHost(Uri uri) => uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 &&
        uri.IsDefaultPort && !uri.IsLoopback && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) ? IsPublicAddress(address) : uri.IdnHost.Contains('.'));

    internal static bool IsAllowedAddress(Uri uri, string? origin) => BrowserMediaDetailsParser.ArtworkAddress(uri.AbsoluteUri) &&
        (SameOrigin(uri, origin) || IsPublicHost(uri));

    private static bool SameOrigin(Uri uri, string? origin) => Uri.TryCreate(origin, UriKind.Absolute, out var source) &&
        string.Equals(source.GetLeftPart(UriPartial.Authority), uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xE0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
            !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
            !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
            !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    // Local proxy clients commonly answer DNS queries in the RFC 2544 benchmark
    // range and route the original host name through their tunnel. Accept that
    // alias only for DNS names, never for an IP literal supplied by a page.
    internal static bool IsProxyAliasAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && bytes[0] == 198 && bytes[1] is 18 or 19;
    }

    internal static bool IsAllowedResolvedAddress(IPAddress address, bool resolvedFromHostName) =>
        IsPublicAddress(address) || resolvedFromHostName && IsProxyAliasAddress(address);

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
        var allowPrivate = context.InitialRequestMessage.Options.TryGetValue(AllowPrivateOrigin, out var allowed) && allowed;
        var resolvedFromHostName = !IPAddress.TryParse(context.DnsEndPoint.Host.Trim('[', ']'), out _);
        foreach (var address in addresses.Where(address => allowPrivate || IsAllowedResolvedAddress(address, resolvedFromHostName)))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("No reachable public artwork address.");
    }

    private static int SizeDistance(string sizes)
    {
        var size = sizes.Split(['x', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(size, out var pixels) && pixels is > 0 and <= 16384 ? Math.Abs(pixels - 256) : int.MaxValue;
    }

    private static bool IsRasterImage(byte[] bytes) => bytes.Length >= 12 &&
        (bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }) ||
         bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
         bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8) ||
         bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8));
}
