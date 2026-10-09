namespace TaskbarLyrics.App;

internal interface IMediaPlaybackController
{
    Task ExecuteAsync(MediaHotkeyAction action, CancellationToken cancellationToken);
    Task SeekToAsync(TimeSpan position, CancellationToken cancellationToken);
}

internal interface IPlayerRecognitionController
{
    void SetRecognitionOrder(IReadOnlyList<string>? order, IReadOnlyCollection<string>? enabledSources = null,
        IReadOnlyCollection<string>? customSources = null);
}
