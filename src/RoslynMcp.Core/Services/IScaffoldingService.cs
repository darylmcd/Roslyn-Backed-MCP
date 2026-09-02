using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for scaffolding new source file templates within a workspace.
/// </summary>
public interface IScaffoldingService
{
    /// <summary>
    /// Previews scaffolding a new type declaration file in the specified project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The scaffolding parameters, including type kind, name, and optional base type/interfaces.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct);

    /// <summary>
    /// Previews scaffolding a test file for a target type or method in a test project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The scaffolding parameters, including the test project name and target type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="testNameSuggestionProvider">Optional provider used only when <see cref="ScaffoldTestDto.UseSampling"/> is true.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(
        string workspaceId,
        ScaffoldTestDto request,
        CancellationToken ct,
        ITestNameSuggestionProvider? testNameSuggestionProvider = null);

    /// <summary>
    /// Previews scaffolding test files for multiple target types in a single composite preview.
    /// Reuses one workspace snapshot across targets to avoid per-target compilation cost.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The batch scaffolding parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTestBatchAsync(string workspaceId, ScaffoldTestBatchDto request, CancellationToken ct);

    /// <summary>
    /// Previews scaffolding the FIRST test file for a target service that has no existing
    /// fixture in the destination test project. Inspects the service's constructor and public
    /// methods, derives boilerplate shape from up to three most-recently-modified sibling
    /// fixtures, and emits one <c>&lt;Service&gt;Tests.cs</c> with ClassInitialize / service
    /// instantiation + one smoke-test per public method. Distinct from
    /// <see cref="PreviewScaffoldTestAsync"/> which adds a single method-focused test to an
    /// existing fixture.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The first-test-file scaffolding parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldFirstTestFileAsync(string workspaceId, ScaffoldFirstTestFileDto request, CancellationToken ct);
}

/// <summary>
/// Transport-neutral bridge for opt-in test-method-name suggestions during scaffold preview.
/// Implementations may use MCP sampling, fixtures, or a deterministic fake in tests.
///
/// <para>Two-phase contract (scaffold-sampling-mrtr-replay-cost): a transport whose exchange
/// spans more than one round trip of the same logical call — MCP sampling over MRTR, where the
/// first leg terminates with an input-required signal and the client retries the whole
/// <c>tools/call</c> — answers <see cref="TryConsumePendingSuggestion"/> on the retry leg. Callers
/// MUST probe that first and only build the (expensive) suggestion context when it returns false,
/// so the semantic work behind <see cref="ScaffoldTestNameSuggestionContext"/> is paid on exactly
/// one leg of the exchange instead of every replay.</para>
/// </summary>
public interface ITestNameSuggestionProvider
{
    Task<TestNameSuggestionResult> SuggestTestNameAsync(ScaffoldTestNameSuggestionContext context, CancellationToken ct);

    /// <summary>
    /// Attempts to consume a suggestion the client already answered on an earlier round trip of
    /// THIS logical request, without issuing a new suggestion request and without a context.
    /// </summary>
    /// <param name="result">
    /// The already-answered suggestion (or its sanitized deterministic fallback) when this returns
    /// true; an empty result otherwise.
    /// </param>
    /// <returns>
    /// True when this request carried an answer that was consumed here. The default implementation
    /// returns false: a single-round-trip provider (fixture, deterministic fake, in-process model)
    /// has no prior leg to consume from, which is the correct answer rather than a compatibility
    /// stub — it keeps <see cref="SuggestTestNameAsync"/> the only path such a provider needs.
    /// </returns>
    bool TryConsumePendingSuggestion(out TestNameSuggestionResult result)
    {
        result = new TestNameSuggestionResult(null);
        return false;
    }
}

public sealed record ScaffoldTestNameSuggestionContext(
    string TargetTypeName,
    string TargetMethodName,
    string? TargetMethodSignature,
    string? TargetNamespace,
    IReadOnlyList<string> SiblingTestMethodNames);

public sealed record TestNameSuggestionResult(string? MethodName, string? Warning = null);
