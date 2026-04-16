using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 10: statically maps productive source symbols to the test methods that reference them,
/// without running tests. Complements the runtime <c>test_coverage</c> tool with a fast,
/// reference-based "is this symbol tested at all" indicator.
/// </summary>
public interface ITestReferenceMapService
{
    /// <param name="workspaceId">Session identifier from <c>workspace_load</c>.</param>
    /// <param name="projectName">
    /// dr-9-5-bug-pagination-001: optional project-scope filter. Pre-fix, the parameter was
    /// accepted but only influenced which TEST projects got scanned, so projectName pointing
    /// at a productive project (the more natural shape — "is MY project tested?") produced an
    /// unfiltered result set. Post-fix:
    /// <list type="bullet">
    ///   <item>productive project → <see cref="CoveredSymbols"/> and <see cref="UncoveredSymbols"/> are filtered to symbols declared in that project.</item>
    ///   <item>test project → test-scan scope shrinks to that test project.</item>
    ///   <item>name matches neither → <see cref="InvalidOperationException"/>.</item>
    /// </list>
    /// </param>
    /// <param name="offset">
    /// dr-9-5-bug-pagination-001: 0-based start index applied to the combined covered+uncovered list.
    /// Clamps to [0, total].
    /// </param>
    /// <param name="limit">
    /// Maximum symbols returned per page. Clamps to [1, 500]. Default 200 matches the other
    /// paginated tools.
    /// </param>
    Task<TestReferenceMapDto> BuildAsync(
        string workspaceId,
        string? projectName,
        int offset,
        int limit,
        CancellationToken ct);
}

/// <summary>
/// Output of <see cref="ITestReferenceMapService.BuildAsync"/>.
/// </summary>
/// <param name="CoveredSymbols">Productive symbols that at least one test method references statically. Paginated slice when <c>offset</c>/<c>limit</c> were narrower than the full set.</param>
/// <param name="UncoveredSymbols">Productive symbols with no static test references. Paginated slice.</param>
/// <param name="CoveragePercent">Ratio <c>covered/(covered+uncovered)</c> × 100, rounded to one decimal. Always computed against the full (pre-pagination) counts so the verdict doesn't change with page size.</param>
/// <param name="InspectedTestProjects">Test projects actually scanned during the sweep.</param>
/// <param name="Notes">Structural caveats the caller should surface (e.g., reflection-heavy code, DI-driven tests).</param>
/// <param name="MockDriftWarnings">Item 9 (v1.18) — NSubstitute mock drift findings. Not paginated (typically small).</param>
/// <param name="Offset">Echoes the effective (clamped) offset so callers can paginate without re-computing.</param>
/// <param name="Limit">Echoes the effective (clamped) limit.</param>
/// <param name="TotalCoveredCount">Full covered-symbol count before pagination.</param>
/// <param name="TotalUncoveredCount">Full uncovered-symbol count before pagination.</param>
/// <param name="HasMore">True when <c>Offset + CoveredSymbols.Count + UncoveredSymbols.Count</c> is less than the combined total.</param>
public sealed record TestReferenceMapDto(
    IReadOnlyList<CoveredSymbolDto> CoveredSymbols,
    IReadOnlyList<string> UncoveredSymbols,
    double CoveragePercent,
    IReadOnlyList<string> InspectedTestProjects,
    IReadOnlyList<string> Notes,
    IReadOnlyList<MockDriftWarningDto> MockDriftWarnings,
    int Offset,
    int Limit,
    int TotalCoveredCount,
    int TotalUncoveredCount,
    bool HasMore);

/// <summary>One covered-symbol row with referencing test method names.</summary>
public sealed record CoveredSymbolDto(
    string SymbolDisplay,
    IReadOnlyList<string> ReferencingTestMethods);

/// <summary>One mock-drift finding: an interface method called by production but not stubbed in the matching test class.</summary>
public sealed record MockDriftWarningDto(
    string TestClassDisplay,
    string MockedInterfaceDisplay,
    string MissingStubMethod,
    string ProductionCallerDisplay);
