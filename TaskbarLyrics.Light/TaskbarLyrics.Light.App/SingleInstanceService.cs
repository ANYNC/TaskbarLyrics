using System.IO;
using System.IO.Pipes;

namespace TaskbarLyrics.Light.App;

internal static class SingleInstanceService
{
    private const string ActivationPipeName = "ANYNC.TaskbarLyrics.Light.Activation";
    private const string LockFileName = "TaskbarLyrics.Light.lock";

    private static FileStream? _lockFile;
    private static bool _didStartupCheck;

    public static bool EnsureCurrentInstance()
    {
        return _didStartupCheck
            ? _lockFile is not null
            : TryClaimCurrentProcess();
    }

    public static bool TryClaimCurrentProcess()
    {
        _didStartupCheck = true;

        if (TryAcquireLockFile())
        {
            return true;
        }

        SignalRunningInstanceAsync().GetAwaiter().GetResult();
        return false;
    }

    public static void Release()
    {
        _lockFile?.Dispose();
        _lockFile = null;
    }

    private static bool TryAcquireLockFile()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics.Light");
        Directory.CreateDirectory(appDataPath);

        var lockPath = Path.Combine(appDataPath, LockFileName);
        try
        {
            _lockFile = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task ListenForActivationAsync(Func<Task> activateAsync, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                await activateAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task SignalRunningInstanceAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                ActivationPipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(500);
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }
}
