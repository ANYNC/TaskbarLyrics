namespace TaskbarLyrics.App;

internal interface IMediaPlaybackController
{
    Task ExecuteAsync(MediaHotkeyAction action, CancellationToken cancellationToken);
}

internal interface IPlayerRecognitionController
{
    void SetRecognitionOrder(IReadOnlyList<string>? order, IReadOnlyCollection<string>? enabledSources = null);
}
