namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Runs every cleanup step and reports all failures after the last step has had a chance to run.
/// Cleanup callers must never hide the original failure or skip later resource release.
/// </summary>
internal static class CleanupFailureCollector
{
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

    internal static Func<ValueTask> FromAction(Action action) => () =>
    {
        action();
        return ValueTask.CompletedTask;
    };
}
