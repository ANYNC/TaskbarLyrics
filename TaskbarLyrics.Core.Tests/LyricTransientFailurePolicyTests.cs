using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricTransientFailurePolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public void ClassifiesHttpStatusWithoutRetryingPermanentResponses(HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, LyricTransientFailurePolicy.IsTransient(new HttpRequestException("request", null, status)));

    [Theory]
    [InlineData(SocketError.HostNotFound, true)]
    [InlineData(SocketError.TryAgain, true)]
    [InlineData(SocketError.NoData, true)]
    [InlineData(SocketError.TimedOut, true)]
    [InlineData(SocketError.ConnectionReset, true)]
    [InlineData(SocketError.NetworkUnreachable, true)]
    [InlineData(SocketError.AccessDenied, false)]
    public void ClassifiesDirectAndWrappedSocketFailures(SocketError error, bool expected)
    {
        var exception = new SocketException((int)error);
        Assert.Equal(expected, LyricTransientFailurePolicy.IsTransient(exception));
        Assert.Equal(expected, LyricTransientFailurePolicy.IsTransient(new HttpRequestException("network", exception)));
    }

    [Fact]
    public void DistinguishesNetworkErrorsFromTlsParsingAndProgrammingFailures()
    {
        Assert.True(LyricTransientFailurePolicy.IsTransient(new HttpRequestException("network")));
        Assert.True(LyricTransientFailurePolicy.IsTransient(new TimeoutException()));
        Assert.False(LyricTransientFailurePolicy.IsTransient(new HttpRequestException(HttpRequestError.SecureConnectionError)));
        Assert.False(LyricTransientFailurePolicy.IsTransient(new HttpRequestException("tls", new AuthenticationException())));
        Assert.False(LyricTransientFailurePolicy.IsTransient(new FormatException()));
        Assert.False(LyricTransientFailurePolicy.IsTransient(new InvalidOperationException()));
        Assert.False(LyricTransientFailurePolicy.IsTransient(new OperationCanceledException()));
    }
}
