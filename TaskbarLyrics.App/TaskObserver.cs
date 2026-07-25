using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal static class TaskObserver
{
    public static void Observe(Task? task, string operation)
    {
        if (task is null || task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = ObserveAsync(task, operation);
    }

    private static async Task ObserveAsync(Task task, string operation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected part of window and application shutdown.
        }
        catch (Exception exception)
        {
            Log.Error($"Background operation '{operation}' failed: {exception}");
        }
    }
}
