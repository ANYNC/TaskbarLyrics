using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Light.App;

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
        }
        catch (Exception exception)
        {
            Log.Error($"后台任务失败 '{operation}': {exception}");
        }
    }
}
