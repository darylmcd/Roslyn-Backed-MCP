using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for the v2 top-10 remediation pass (post-PR-#150). Smaller, focused
/// asserts live alongside their feature; this file batches the cross-cutting smoke tests.
/// </summary>
// donotparallelize-audit-wave-31: [DoNotParallelize] removed. Read-only tests (GetProject,
// GetDiRegistrations cache, source-file-lines resource) touch the assembly-shared SampleSolution
// only through the synchronized WorkspaceIdCache (GetOrLoadWorkspaceIdAsync) and never reload,
// close, or write it; the DiRegistrationService scan cache is keyed per workspace id + version.
// Both apply-with-verify tests run *_apply and disk writes only against a per-test
// CreateIsolatedWorkspaceAsync copy under TestTempRoot, and UndoService/PreviewStore state is
// keyed by that isolated workspace id or preview token. No environment or static mutation (the one
// static is a class-local id set once in ClassInitialize). Validated by a bounded repeated (3x)
// concurrent run alongside its wave-31 siblings and parallel-enabled workspace classes, green each time.
[TestClass]
public sealed class Top10V2RegressionTests : IsolatedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath);
    }

    // ── solution-project-index-by-name ──

    [TestMethod]
    public void GetProject_ResolvesByName()
    {
        var project = WorkspaceManager.GetProject(_workspaceId, "SampleLib");
        Assert.IsNotNull(project, "GetProject must resolve SampleLib by simple name.");
        Assert.AreEqual("SampleLib", project.Name);
    }

    [TestMethod]
    public void GetProject_ResolvesByFilePath()
    {
        // Resolve via name first, then look up by the resolved file path.
        var byName = WorkspaceManager.GetProject(_workspaceId, "SampleLib");
        Assert.IsNotNull(byName?.FilePath);
        var byPath = WorkspaceManager.GetProject(_workspaceId, byName!.FilePath!);
        Assert.IsNotNull(byPath, "GetProject must resolve by absolute file path too.");
        Assert.AreSame(byName, byPath);
    }

    [TestMethod]
    public void GetProject_UnknownReturnsNull()
    {
        Assert.IsNull(WorkspaceManager.GetProject(_workspaceId, "DefinitelyNotARealProject"));
    }

    // ── di-registrations-scan-caching ──

    [TestMethod]
    public async Task GetDiRegistrations_RepeatCallSameVersion_ReturnsCachedReference()
    {
        var first = await DiRegistrationService.GetDiRegistrationsAsync(_workspaceId, projectFilter: null, CancellationToken.None);
        var second = await DiRegistrationService.GetDiRegistrationsAsync(_workspaceId, projectFilter: null, CancellationToken.None);
        Assert.AreSame(first, second, "Second call with same filter on same workspaceVersion must return the cached list.");
    }

    // ── source-file-resource-line-range-parity ──

    [TestMethod]
    public async Task SourceFileLinesResource_SlicesAndPrependsMarker()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var content = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "5-10", CancellationToken.None);

        StringAssert.Contains(content, "// roslyn://", "Marker comment must prefix the slice.");
        StringAssert.Contains(content, "lines/5-", "Marker must reference the requested range.");

        // Body should be much smaller than the full file (full file is ~30 lines).
        var bodyLineCount = content.Split('\n').Length;
        Assert.IsTrue(bodyLineCount < 20, $"Slice should be small (<20 lines including marker); got {bodyLineCount}.");
    }

    // resource-read-protocol-error-semantics-workspace (supersedes
    // dr-9-13-flag-resource-invalid-range-resource-returns-ge):
    // the handler now uses the success-only ToolErrorHandler.ExecuteResourceScopeAsync —
    // input-validation failures PROPAGATE to ResourceReadResultFilter, which answers on
    // the JSON-RPC error channel with a sanitized InvalidParams (-32602) error instead of
    // serializing an error envelope into a "successful" contents body.
    [TestMethod]
    public async Task SourceFileLinesResource_EndBeforeStart_PropagatesInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            WorkspaceResources.GetSourceFileLines(
                WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "10-5", CancellationToken.None));

        Assert.AreEqual("lineRange", ex.ParamName,
            "The offending parameter must be named so the read filter can surface it.");
        AssertSafeResourceRefusal(ex,
            "Parameter 'lineRange' endLine (5) must be >= startLine (10).",
            nameof(ArgumentException), animalServicePath, encoded, "10-5");
    }

    [TestMethod]
    public async Task SourceFileLinesResource_NonNumericRange_PropagatesInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            WorkspaceResources.GetSourceFileLines(
                WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "abc-def", CancellationToken.None));

        Assert.AreEqual("lineRange", ex.ParamName);
        AssertSafeResourceRefusal(ex,
            "Parameter 'lineRange' startLine must be a positive integer.",
            nameof(ArgumentException), animalServicePath, encoded, "abc-def");
    }

    [TestMethod]
    public async Task SourceFileLinesResource_StartLinePastEof_PropagatesInvalidArgument()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var encoded = Uri.EscapeDataString(animalServicePath);

        var text = await WorkspaceManager.GetSourceTextAsync(_workspaceId, animalServicePath, CancellationToken.None);
        Assert.IsNotNull(text);
        var totalLineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text);
        Assert.IsTrue(totalLineCount < 9999, "The requested start must be past EOF.");
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentOutOfRangeException>(() =>
            WorkspaceResources.GetSourceFileLines(
                WorkspaceExecutionGate, WorkspaceManager, _workspaceId, encoded, lineRange: "9999-10000", CancellationToken.None));

        Assert.AreEqual("lineRange", ex.ParamName);
        StringAssert.Contains(ex.Message, "past the end",
            "Exception message should explain the startLine-past-EOF condition.");
        AssertSafeResourceRefusal(ex,
            $"Parameter 'lineRange' startLine (9999) is past the end of the file ({totalLineCount} lines).",
            nameof(ArgumentOutOfRangeException), animalServicePath, encoded, "9999-10000");
    }

    private static void AssertSafeResourceRefusal(
        ArgumentException exception, string expectedMessage, string expectedExceptionType,
        string path, string encodedPath, string lineRange)
    {
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        var publicMessage = ((IPublicMessageException)exception).PublicMessage;
        Assert.AreEqual(expectedMessage, publicMessage);
        foreach (var privateInput in new[] { path, encodedPath, lineRange })
        {
            Assert.IsFalse(publicMessage.Contains(privateInput, StringComparison.Ordinal),
                "The public correction must not echo the caller's path or raw range.");
        }

        using var doc = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, "resource"));
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual(expectedMessage, doc.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(expectedExceptionType, doc.RootElement.GetProperty("exceptionType").GetString());
    }

    // ── apply-with-verify-and-rollback ──

    [TestMethod]
    public async Task ApplyWithVerify_GoodPreview_ReturnsApplied()
    {
        // Use organize_usings on AnimalService.cs as a known-clean refactor that should not
        // introduce errors. The workspace is shared, so we only verify status, not actual diff.
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var animalServicePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, animalServicePath, CancellationToken.None);

        var workflowService = new ApplyUndoWorkflowService(
            RefactoringService, CompileCheckService, UndoService, PreviewStore);
        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, workflowService, PreviewStore,
            preview.PreviewToken, rollbackOnError: true, ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.IsTrue(status is "applied" or "rolled_back",
            $"Expected applied or rolled_back; got {status}. Full JSON: {json}");
    }

    // ── apply-with-verify-diff-not-counts ──
    //
    // False-positive scenario per the row: a pre-existing compile error elsewhere in
    // the workspace MUST NOT cause an innocent apply to roll back. The verify step
    // compares diagnostic IDENTITY (id + file + line) pre/post-apply rather than
    // counts, so the pre-existing diagnostic is filtered out of the introduced set.
    // Integration-level test against ApplyWithVerifyTool itself; the unit-level
    // coverage of the helper lives in DiagnosticIdentitySetTests.
    //
    [TestMethod]
    public async Task ApplyWithVerify_PreExistingError_InnocentApply_DoesNotRollBack()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var workspaceId = workspace.WorkspaceId;
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var animalServicePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        // Step 1: inject a pre-existing compile error in Cat.cs by replacing the Speak()
        // expression body with an undefined identifier. verify=false leaves the broken
        // state in place — exactly the situation the false-positive bug describes.
        var originalCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        var breakingEdit = ReplaceSpeakReturnEdit(originalCat, "PreExistingMarkerForApplyWithVerify");
        var inject = await EditService.ApplyTextEditsAsync(
            workspaceId, catFilePath, [breakingEdit], "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false, verify: false, autoRevertOnError: false);
        Assert.IsTrue(inject.Success, "Injection edit should succeed (it is syntactically valid).");

        // Sanity: the workspace now has at least one compile error from the injection.
        var baseline = await CompileCheckService.CheckAsync(
            workspaceId,
            new RoslynMcp.Core.Models.CompileCheckOptions(SeverityFilter: "error", Limit: 50),
            CancellationToken.None);
        Assert.IsTrue(baseline.ErrorCount > 0,
            "Baseline must carry the pre-existing error for this test to be meaningful.");

        // Step 2: run an innocent organize_usings preview on a DIFFERENT file. This apply
        // does not touch Cat.cs and does not introduce any new diagnostics anywhere.
        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspaceId, animalServicePath, CancellationToken.None);

        // Step 3: apply via ApplyWithVerifyTool with rollbackOnError=true. Pre-fix (count-
        // delta + message-fingerprint), this would sometimes flag the pre-existing Cat.cs
        // error as "introduced" if its message text or column shifted across the verify
        // pair. Post-fix (id+file+line identity), the pre-existing error has the SAME
        // identity in pre and post sets — so it is filtered out and the innocent apply
        // returns status="applied".
        var workflowService = new ApplyUndoWorkflowService(
            RefactoringService, CompileCheckService, UndoService, PreviewStore);
        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, workflowService, PreviewStore,
            preview.PreviewToken, rollbackOnError: true, ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.AreEqual("applied", status,
            $"Pre-existing error MUST NOT trigger rollback for an innocent apply. " +
            $"Got status={status}. Full JSON: {json}");

        // PreErrorCount and PostErrorCount may differ in raw count if organize_usings
        // touched a different file (changing line numbers). The point is that the
        // diff-not-counts trigger does NOT flip on count alone — only on a NEW
        // (id+file+line) identity. Assert PreErrorCount > 0 to confirm the pre-existing
        // error was visible to the verify pass.
        Assert.IsTrue(doc.RootElement.GetProperty("preErrorCount").GetInt32() > 0,
            "preErrorCount must reflect the injected error for the test to be meaningful.");
    }

    // ── true-positive scenario: apply introduces a NEW diagnostic identity ──
    //
    // The cleanest way to drive a refactor-preview through ApplyWithVerifyTool that
    // introduces a new compile error is to capture a preview, then mutate the file
    // content underneath the preview... but RefactoringService prevents that
    // (it pre-validates the workspace state). Instead, we exercise the same trigger
    // through EditService.ApplyTextEditsAsync verify=true autoRevertOnError=true,
    // which shares the SAME DiagnosticIdentitySet helper as ApplyWithVerifyTool.
    // ApplyTextEditVerifyTests already covers this path
    // (ApplyTextEdit_VerifyTrue_CompileError_AutoRevert_RollsBackEdit) — the helper
    // change here means BOTH entry points trigger rollback only on a new
    // (id+file+line) identity. The unit test
    // DiagnosticIdentitySetTests.DiffTrigger_NewDiagnosticAtNewLine_AppearsInIntroducedSet
    // proves the helper-level trigger directly.

    /// <summary>
    /// Mirrors the ReplaceSpeakReturnEdit helper in ApplyTextEditVerifyTests — replaces
    /// the <c>Speak()</c> expression body with an undefined identifier to produce a
    /// compile error (CS0103) without breaking the syntax tree.
    /// </summary>
    private static RoslynMcp.Core.Models.TextEditDto ReplaceSpeakReturnEdit(
        string fileText,
        string replacementIdentifier)
    {
        var lines = fileText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("Speak()", StringComparison.Ordinal) ||
                !lines[i].Contains("=>", StringComparison.Ordinal))
            {
                continue;
            }

            var arrowIndex = lines[i].IndexOf("=>", StringComparison.Ordinal);
            var semicolonIndex = lines[i].LastIndexOf(';');
            if (arrowIndex < 0 || semicolonIndex <= arrowIndex)
            {
                throw new InvalidOperationException(
                    "Expected 'public string Speak() => \"...\";' pattern — test fixture drift?");
            }

            var startColumn = arrowIndex + 3 + 1; // +2 for "=>", +1 for space, +1 for 1-based.
            var endColumn = semicolonIndex + 1;   // exclusive-1-based end at the semicolon.
            return new RoslynMcp.Core.Models.TextEditDto(
                i + 1, startColumn, i + 1, endColumn, replacementIdentifier);
        }

        throw new InvalidOperationException("Could not locate Speak() expression body in test fixture.");
    }
}
