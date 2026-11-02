using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class BrowserArtworkReaderTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWioAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.1.2")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fd00::1")]
    [InlineData("198.18.0.32")]
    public void PageArtworkCannotTurnPublicSiteAccessIntoPrivateNetworkAccess(string address) =>
        Assert.False(BrowserArtworkReader.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("198.18.0.32", true)]
    [InlineData("198.19.255.255", true)]
    [InlineData("::ffff:198.18.0.32", true)]
    [InlineData("198.17.255.255", false)]
    [InlineData("198.20.0.1", false)]
    public void ProxyFakeIpAliasesAreRecognizedInBenchmarkRange(string address, bool expected) =>
        Assert.Equal(expected, BrowserArtworkReader.IsProxyAliasAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("198.18.0.32", false, false)]
    [InlineData("198.18.0.32", true, true)]
    [InlineData("127.0.0.1", true, false)]
    [InlineData("8.8.8.8", false, true)]
    public void ProxyAliasesAreAcceptedOnlyWhenResolvedFromAHostName(string address, bool resolvedFromHostName, bool expected) =>
        Assert.Equal(expected, BrowserArtworkReader.IsAllowedResolvedAddress(IPAddress.Parse(address), resolvedFromHostName));

    [Fact]
    public void ExplicitlyAuthorizedPrivateSiteCanReadItsOwnArtworkWithoutGrantingOtherOrigins()
    {
        Assert.True(BrowserArtworkReader.IsAllowedAddress(new("http://192.168.1.10:5666/cover?id=1"), "http://192.168.1.10:5666"));
        Assert.False(BrowserArtworkReader.IsAllowedAddress(new("http://192.168.1.10:5667/cover"), "http://192.168.1.10:5666"));
        Assert.False(BrowserArtworkReader.IsAllowedAddress(new("http://127.0.0.1/cover"), "https://music.test"));
    }

    [Fact]
    public async Task LoadsBoundedArtworkAndDoesNotFollowARedirectIntoAnUnlistedPrivateOrigin()
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/redirect") return new(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("http://127.0.0.1/private") } };
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return result;
        }));
        var bytes = await BrowserArtworkReader.ReadAsync([new("https://cdn.test/cover", "256x256", "image/png")], CancellationToken.None, client);
        Assert.Equal(Png, bytes);
        Assert.Null(await BrowserArtworkReader.ReadAsync([new("https://cdn.test/redirect", "", "")], CancellationToken.None, client));
        Assert.Equal(2, requests.Count);
    }

    [Theory]
    [InlineData("text/html", 16)]
    [InlineData("image/png", 16)]
    [InlineData("image/png", 1048577)]
    public async Task RejectsWrongTypesSignaturesAndOversizedImages(string type, int length)
    {
        using var client = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[length]) };
            response.Content.Headers.ContentType = new(type);
            return response;
        }));
        Assert.Null(await BrowserArtworkReader.ReadAsync([new("https://cdn.test/cover", "", "")], CancellationToken.None, client));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
