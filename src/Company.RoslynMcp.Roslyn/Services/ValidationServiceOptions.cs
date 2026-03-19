namespace Company.RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <c>ValidationService</c> build and test timeouts.
/// </summary>
public sealed class ValidationServiceOptions
{
    /// <summary>
    /// Gets the maximum time allowed for a build operation before it is cancelled.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the maximum time allowed for a test run before it is cancelled.
    /// Defaults to 10 minutes.
    /// </summary>
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the maximum number of related test files to consider when invoking
    /// <c>FindRelatedTestsForFilesAsync</c>.
    /// Defaults to 25.
    /// </summary>
    public int MaxRelatedFiles { get; init; } = 25;
}
