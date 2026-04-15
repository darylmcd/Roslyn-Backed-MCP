using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 10: statically maps productive source symbols to the test methods that reference them,
/// without running tests. Complements the runtime <c>test_coverage</c> tool with a fast,
/// reference-based "is this symbol tested at all" indicator.
/// </summary>
public interface ITestReferenceMapService
{
    Task<TestReferenceMapDto> BuildAsync(string workspaceId, string? projectName, CancellationToken ct);
}

/// <summary>
/// Output of <see cref="ITestReferenceMapService.BuildAsync"/>.
/// </summary>
/// <param name="CoveredSymbols">Productive symbols that at least one test method references statically.</param>
/// <param name="UncoveredSymbols">Productive symbols with no static test references.</param>
/// <param name="CoveragePercent">Ratio <c>covered/(covered+uncovered)</c> × 100, rounded to one decimal.</param>
/// <param name="InspectedTestProjects">Test projects actually scanned during the sweep.</param>
/// <param name="Notes">Structural caveats the caller should surface (e.g., reflection-heavy code, DI-driven tests).</param>
/// <param name="MockDriftWarnings">
/// Item 9 (v1.18, <c>test-mock-drift-new-interface-usage</c>): when a test class uses
/// <c>NSubstitute.Substitute.For&lt;TInterface&gt;()</c> but production code calls a method on
/// <c>TInterface</c> that the test fixture never stubs (no <c>.Returns(...)</c> /
/// <c>.ReturnsForAnyArgs(...)</c>), the unconfigured method returns <c>default(T)</c> at runtime
/// — often producing silent test passes that mask broken behavior. Each warning identifies one
/// missing stub.
/// </param>
public sealed record TestReferenceMapDto(
    IReadOnlyList<CoveredSymbolDto> CoveredSymbols,
    IReadOnlyList<string> UncoveredSymbols,
    double CoveragePercent,
    IReadOnlyList<string> InspectedTestProjects,
    IReadOnlyList<string> Notes,
    IReadOnlyList<MockDriftWarningDto> MockDriftWarnings);

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
