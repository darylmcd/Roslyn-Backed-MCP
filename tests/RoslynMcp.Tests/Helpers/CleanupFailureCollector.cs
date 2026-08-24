namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Runs every cleanup step and reports all failures after the last step has had a chance to run.
/// Cleanup callers must never hide the original failure or skip later resource release.
/// </summary>
internal static class CleanupFailureCollector
{
    internal static ValueTask DeleteDirectoriesAsync(
        IEnumerable<string> directories,
        Action<string>? deleteDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(directories);

        deleteDirectory ??= static directory =>
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        };

        return RunAsync(
            "Failed to delete one or more temp directories created by this test.",
            directories.Select(directory => FromAction(() => deleteDirectory(directory))).ToArray());
    }

    internal static async ValueTask RunAsync(
        string failureMessage,
        params Func<ValueTask>[] cleanupSteps)
    {
        List<Exception>? failures = null;
        foreach (var cleanupStep in cleanupSteps)
        {
            try
            {
                await cleanupStep().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(failureMessage, failures);
        }
    }

    internal static async ValueTask RunAfterFailureAsync(
        string failureMessage,
        Exception? primaryFailure,
        params Func<ValueTask>[] cleanupSteps)
    {
        try
        {
            await RunAsync(failureMessage, cleanupSteps).ConfigureAwait(false);
        }
        catch (AggregateException cleanupFailure) when (primaryFailure is not null)
        {
            throw new AggregateException(
                failureMessage,
                [primaryFailure, .. cleanupFailure.InnerExceptions]);
        }
    }

    internal static Func<ValueTask> FromAction(Action action) => () =>
    {
        action();
        return ValueTask.CompletedTask;
    };
}
