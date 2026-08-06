namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <c>ValidationService</c> build and test timeouts.
/// </summary>
public sealed record ValidationServiceOptions
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

    /// <summary>
    /// Gets the maximum time allowed for a NuGet vulnerability scan
    /// (<c>dotnet list package --vulnerable</c>) before it is cancelled.
    /// Defaults to 5 minutes. <c>dotnet list package --vulnerable</c> fetches the full package
    /// registration + vulnerability metadata from nuget.org rather than just the resolved
    /// versions a normal restore needs, so a cold per-account NuGet HTTP cache (e.g. the
    /// self-hosted CI runner's service account, which has its own separate profile/cache from
    /// an interactive dev session) can take noticeably longer than a warm-cache run. 2 minutes
    /// was observed to be too tight under a cold cache; 5 minutes gives headroom without masking
    /// a genuinely hung process.
    /// </summary>
    public TimeSpan VulnerabilityScanTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the maximum time allowed for the best-effort revert issued when
    /// <c>apply_with_verify</c> observes cancellation after a successful apply.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ApplyRevertTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the maximum time allowed for the <c>git status --porcelain</c> invocation that
    /// <c>validate_recent_git_changes</c> uses to derive its changed-file scope.
    /// Defaults to 10 seconds. Distinct from the broader per-phase validation timeout: this one
    /// bounds only the git subprocess. When it fires the bundle can no longer trust an otherwise
    /// <c>clean</c> verdict and reports <c>git-status-unknown</c> instead, so a repository large
    /// or cold enough to routinely exceed 10 seconds should raise this rather than live with a
    /// degraded verdict.
    /// </summary>
    public TimeSpan GitStatusTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
