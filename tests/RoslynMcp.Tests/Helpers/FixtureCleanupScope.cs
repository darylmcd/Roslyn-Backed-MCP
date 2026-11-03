using System.Runtime.ExceptionServices;

namespace RoslynMcp.Tests.Helpers;

internal static class FixtureCleanupScope
{
    internal static void Run(Action body, Action cleanup)
    {
        Exception? primaryFailure = null;
        try
        {
            body();
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
        }

        try
        {
            cleanup();
        }
        catch (Exception cleanupFailure) when (primaryFailure is not null)
        {
            throw new AggregateException("Test body and fixture cleanup both failed.", primaryFailure, cleanupFailure);
        }

        if (primaryFailure is not null)
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }

    internal static Task RunAsync(Func<Task> body, Action cleanup) =>
        RunAsync(body, CleanupFailureCollector.FromAction(cleanup));

    internal static async Task RunAsync(Func<Task> body, params Func<ValueTask>[] cleanupSteps)
    {
        Exception? primaryFailure = null;
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
        }

        await CleanupFailureCollector.RunAfterFailureAsync(
            "Test body and fixture cleanup both failed.",
            primaryFailure,
            cleanupSteps).ConfigureAwait(false);

        if (primaryFailure is not null)
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }
}
