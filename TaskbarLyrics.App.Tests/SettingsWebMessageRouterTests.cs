using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsWebMessageRouterTests
{
    [Fact]
    public void ParseDeserializesTheV1UpdateEnvelope()
    {
        var message = SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"update\",\"payload\":{\"key\":\"fontSize\",\"value\":18}}");

        Assert.NotNull(message);
        Assert.Equal("update", message.Type);
        Assert.Equal("fontSize", message.Key);
        Assert.Equal(18, message.Value!.Value.GetInt32());
    }

    [Fact]
    public void ParseDeserializesTheV1PreviewUpdateEnvelope()
    {
        var message = SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"previewUpdate\",\"payload\":{\"key\":\"xOffset\",\"value\":120}}");

        Assert.NotNull(message);
        Assert.Equal("previewUpdate", message.Type);
        Assert.Equal("xOffset", message.Key);
        Assert.Equal(120, message.Value!.Value.GetInt32());
    }

    [Fact]
    public void ParsePreservesTheLyricDiagnosticCandidateModeWithoutFetchMetadata()
    {
        var message = SettingsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"applyLyricDiagnosticCandidate\",\"payload\":{\"providerId\":\"QQMusic\",\"candidateId\":\"candidate-1\",\"mode\":\"remember\"}}");

        Assert.NotNull(message);
        Assert.Equal("applyLyricDiagnosticCandidate", message.Type);
        Assert.Equal("QQMusic", message.Payload!.Value.GetProperty("providerId").GetString());
        Assert.Equal("candidate-1", message.Payload!.Value.GetProperty("candidateId").GetString());
        Assert.Equal("remember", message.Payload!.Value.GetProperty("mode").GetString());
        Assert.False(message.Payload.Value.TryGetProperty("fetchMetadata", out _));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("remember")]
    public void TryParseLyricDiagnosticCandidateApplyRequestAcceptsKnownModes(string mode)
    {
        var message = SettingsWebMessageRouter.Parse(
            $"{{\"version\":1,\"type\":\"applyLyricDiagnosticCandidate\",\"payload\":{{\"providerId\":\"QQMusic\",\"candidateId\":\"candidate-1\",\"mode\":\"{mode}\"}}}}");

        Assert.True(SettingsWebMessageRouter.TryParseLyricDiagnosticCandidateApplyRequest(
            message!.Value,
            out var request));
        Assert.Equal(mode, request!.Mode.ToString(), ignoreCase: true);
        Assert.Equal("QQMusic", request.ProviderId);
        Assert.Equal("candidate-1", request.CandidateId);
    }

    [Fact]
    public void TryParseLyricDiagnosticCandidateApplyRequestDefaultsLegacyPayloadToCurrent()
    {
        var message = SettingsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"applyLyricDiagnosticCandidate\",\"payload\":{\"providerId\":\"QQMusic\",\"candidateId\":\"candidate-1\"}}");

        Assert.True(SettingsWebMessageRouter.TryParseLyricDiagnosticCandidateApplyRequest(
            message!.Value,
            out var request));
        Assert.Equal(LyricDiagnosticApplyMode.Current, request!.Mode);
    }

    [Theory]
    [InlineData("forever")]
    [InlineData("")]
    public void TryParseLyricDiagnosticCandidateApplyRequestRejectsUnknownModes(string mode)
    {
        var message = SettingsWebMessageRouter.Parse(
            $"{{\"version\":1,\"type\":\"applyLyricDiagnosticCandidate\",\"payload\":{{\"providerId\":\"QQMusic\",\"candidateId\":\"candidate-1\",\"mode\":\"{mode}\"}}}}");

        Assert.False(SettingsWebMessageRouter.TryParseLyricDiagnosticCandidateApplyRequest(
            message!.Value,
            out _));
    }

    [Fact]
    public void ParseWhenMessageIsEmptyReturnsNull()
    {
        Assert.Null(SettingsWebMessageRouter.Parse(""));
    }

    [Fact]
    public void ParseWhenVersionIsUnsupportedReturnsNull()
    {
        Assert.Null(SettingsWebMessageRouter.Parse("{\"version\":2,\"type\":\"ready\",\"payload\":{}}"));
        Assert.Null(SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"ready\",\"payload\":"));
    }
}
