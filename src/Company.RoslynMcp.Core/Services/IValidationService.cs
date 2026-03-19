using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

/// <summary>
/// Provides build, test discovery, and test execution operations for a loaded workspace.
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// Runs <c>dotnet build</c> for the entire workspace and returns the result with diagnostics.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct);

    /// <summary>
    /// Runs <c>dotnet build</c> for a single project and returns the result with diagnostics.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectName">The name of the project to build.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct);

    /// <summary>
    /// Discovers test projects and test cases in the loaded workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct);

    /// <summary>
    /// Finds test cases that are semantically related to the symbol identified by <paramref name="locator"/>.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="locator">Identifies the symbol whose related tests should be found.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Runs <c>dotnet test</c> for the workspace or a specific project, optionally with a test filter expression.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectName">The project to run tests in, or <see langword="null"/> for all test projects.</param>
    /// <param name="filter">A <c>dotnet test</c> filter expression, or <see langword="null"/> to run all tests.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);

    /// <summary>
    /// Finds test cases related to the given set of changed files and returns a ready-to-use test filter expression.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePaths">The absolute paths to the changed source files.</param>
    /// <param name="maxResults">The maximum number of related test cases to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct);
}
