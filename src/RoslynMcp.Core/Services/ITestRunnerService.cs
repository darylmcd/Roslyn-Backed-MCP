using System.Collections.Immutable;
using System.Text.Json.Serialization;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Executes <c>dotnet test</c> against a loaded workspace or specific test project and returns
/// structured pass/fail results.
/// </summary>
/// <remarks>
/// <para>
/// A test run is three steps so callers can hold the workspace gate only for the steps that touch
/// workspace state and run the long <c>dotnet test</c> command outside it:
/// <see cref="PrepareTestRunAsync"/> (gated read), <see cref="RunTestCommandAsync"/> (no gate;
/// bounded by the test timeout), and <see cref="CompleteTestRunAsync"/> (gated read).
/// <see cref="RunTestsAsync"/> composes the three required steps without any gate. Implement each
/// phase explicitly: preparation freezes evaluation and authorized dotnet configuration context;
/// execution consumes that context without resolving a different target or dropping state.
/// </para>
/// </remarks>
public interface ITestRunnerService
{
    Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);

    /// <summary>
    /// Resolves the test target and the execution plan (including the Microsoft.Testing.Platform
    /// plan) and records the workspace version.
    /// </summary>
    Task<TestRunPlan> PrepareTestRunAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);

    /// <summary>
    /// Runs the planned <c>dotnet test</c> and parses its results. Call outside the workspace
    /// gate: the command's own gates serialize it and its timeout covers queue wait plus
    /// execution. A timeout is reported as a <c>Timeout</c> failure envelope, not an exception.
    /// </summary>
    Task<TestRunResultDto> RunTestCommandAsync(TestRunPlan plan, CancellationToken ct);

    /// <summary>
    /// Adds the <see cref="TestRunWarnings.WorkspaceChangedDuringRun"/> warning when the workspace
    /// version moved while the command ran.
    /// </summary>
    Task<TestRunResultDto> CompleteTestRunAsync(TestRunPlan plan, TestRunResultDto result, CancellationToken ct);

    /// <summary>
    /// Completes the result without touching workspace state (workspace closed while the command
    /// ran), with the <see cref="TestRunWarnings.WorkspaceChangedDuringRun"/> warning.
    /// </summary>
    TestRunResultDto CompleteTestRunWithoutWorkspace(TestRunResultDto result) =>
        TestRunWarnings.WithWorkspaceChanged(result);
}

/// <summary>A resolved test target plus the workspace version it was resolved against.</summary>
/// <param name="WorkspaceId">Workspace the run belongs to.</param>
/// <param name="ProjectName">Caller-supplied project selector, or <see langword="null"/> for the workspace.</param>
/// <param name="Filter">Caller-supplied <c>dotnet test</c> filter, or <see langword="null"/>.</param>
/// <param name="EvaluationProperties">Immutable captured MSBuild globals, excluded from public serialization.</param>
/// <param name="Context">Immutable authorized nearest-global.json inventory, revalidated before execution.</param>
/// <param name="TargetPath">Resolved solution/project path. Real command execution refuses a missing target; no-workspace result fixtures may use <see langword="null"/>.</param>
/// <param name="RequiresMtpNative">Whether the target needs the Microsoft.Testing.Platform-native argument shape.</param>
/// <param name="NativeTestArguments">Immutable framework-specific native protocol arguments, excluded from public serialization.</param>
/// <param name="NoRestore">Whether the run must pass <c>--no-restore</c> because preparation bound an authorized restored dependency snapshot.</param>
/// <param name="WorkspaceVersion">Workspace version observed when the plan was made.</param>
public sealed record TestRunPlan(
    string WorkspaceId,
    string? ProjectName,
    string? Filter,
    [property: JsonIgnore] ImmutableDictionary<string, string> EvaluationProperties,
    [property: JsonIgnore] PreparedDotnetTestContext Context,
    [property: JsonIgnore] ImmutableArray<string> NativeTestArguments,
    string? TargetPath = null,
    bool RequiresMtpNative = false,
    bool NoRestore = false,
    int WorkspaceVersion = 0);

/// <summary>Warning identifiers carried by <see cref="TestRunResultDto.Warnings"/>.</summary>
public static class TestRunWarnings
{
    /// <summary>The workspace version moved (or the workspace closed) while the command ran ungated.</summary>
    public const string WorkspaceChangedDuringRun = "workspaceChangedDuringRun";

    /// <summary>Returns <paramref name="result"/> with <see cref="WorkspaceChangedDuringRun"/> appended once.</summary>
    public static TestRunResultDto WithWorkspaceChanged(TestRunResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var existing = result.Warnings ?? [];
        return existing.Contains(WorkspaceChangedDuringRun, StringComparer.Ordinal)
            ? result
            : result with { Warnings = [.. existing, WorkspaceChangedDuringRun] };
    }
}
