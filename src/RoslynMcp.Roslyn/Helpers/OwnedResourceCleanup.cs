namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Releases every owned resource and preserves the operation failure when cleanup also fails.</summary>
internal static class OwnedResourceCleanup
{
    internal const string OutcomeDataKey = "CleanupOutcomes";
    internal const int MaximumOutcomeCount = 16;
    internal const int MaximumOutcomeLength = 160;
    /// <summary>Attaches caller-supplied safe role/type labels without changing failure classification or cancellation identity.</summary>
    internal static void AttachOutcomes(Exception primaryFailure, IReadOnlyCollection<string> safeOutcomes)
    {
        if (safeOutcomes.Count == 0) return;
        var previous = primaryFailure.Data[OutcomeDataKey] as string[] ?? [];
        primaryFailure.Data[OutcomeDataKey] = previous.Concat(safeOutcomes).Distinct(StringComparer.Ordinal).Take(MaximumOutcomeCount)
            .Select(outcome => outcome.Length > MaximumOutcomeLength ? outcome[..MaximumOutcomeLength] : outcome).ToArray();
    }

    internal static void DisposePreservingFailure(string message, Exception? primaryFailure, params IDisposable?[] resources)
    {
        try { Dispose(message, null, resources); }
        catch (AggregateException cleanup) when (primaryFailure is not null)
        {
            AttachOutcomes(primaryFailure, cleanup.InnerExceptions.Select(failure =>
                "Resource cleanup failed (" + failure.GetType().Name + ").").ToArray());
        }
    }

    internal static IDisposable FromAction(Action action) => new ActionResource(action);

    private sealed class ActionResource(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    internal static void Dispose(string message, Exception? primaryFailure, params IDisposable?[] resources)
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            try { resource?.Dispose(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        if (failures is null) return;
        if (primaryFailure is not null) failures.Insert(0, primaryFailure);
        throw new AggregateException(message, failures);
    }
}
