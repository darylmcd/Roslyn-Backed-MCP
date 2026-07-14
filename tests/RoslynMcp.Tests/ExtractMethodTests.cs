using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Integration tests for the custom extract_method_preview / extract_method_apply pipeline.
/// Uses RefactoringProbe.cs in the sample solution as the extraction target.
/// </summary>
[TestClass]
public sealed class ExtractMethodTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Extract 3 statements (lines 13-15) from ComputeAndPrint into a new method.
    /// Verifies preview produces a diff with the new method and the call site.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_ThreeStatements_ProducesPreviewWithNewMethod()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var result = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 13, startColumn: 9,
            endLine: 15, endColumn: 39,
            "ComputeCore",
            CancellationToken.None);

        Assert.IsNotNull(result.PreviewToken);
        Assert.IsTrue(result.Changes.Count > 0, "Expected at least one file change.");
        Assert.IsTrue(result.Description.Contains("ComputeCore"),
            "Description should mention the extracted method name.");

        // The diff should contain the new method name
        var diff = result.Changes[0].UnifiedDiff;
        Assert.IsTrue(diff.Contains("ComputeCore"),
            "Diff should contain the extracted method.");
    }

    /// <summary>
    /// Preview + apply extract method, then verify the solution compiles.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_PreviewAndApply_CompilationSucceeds()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var preview = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 13, startColumn: 9,
            endLine: 15, endColumn: 39,
            "ComputeCore",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, "Apply should succeed.");

        // Verify compilation
        var compileResult = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId, new CompileCheckOptions(), CancellationToken.None);

        Assert.IsTrue(compileResult.Success,
            $"Compilation should succeed after extract method. Errors: " +
            $"{string.Join("; ", compileResult.Diagnostics?.Select(d => $"{d.Id}: {d.Message}") ?? [])}");
    }

    /// <summary>
    /// Extract a statement block where a variable flows out (used after selection).
    /// Verifies the extracted method has a return value.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_WithReturnValue_ProducesReturnStatement()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // Select lines 13-14: var sum = a + b; var doubled = sum * 2;
        // 'doubled' flows out (used on line 15 and returned on line 16)
        var result = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 13, startColumn: 9,
            endLine: 14, endColumn: 34,
            "ComputeDoubled",
            CancellationToken.None);

        Assert.IsNotNull(result.PreviewToken);
        var diff = result.Changes[0].UnifiedDiff;
        Assert.IsTrue(diff.Contains("return"),
            "Diff should contain a return statement for the outflowing variable.");
    }

    /// <summary>
    /// Reject extraction when selection contains return statements.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_WithReturnStatement_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // Select lines 13-16 including "return doubled;"
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ExtractMethodService.PreviewExtractMethodAsync(
                workspace.WorkspaceId,
                filePath,
                startLine: 13, startColumn: 9,
                endLine: 16, endColumn: 25,
                "BadExtract",
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "return");
    }

    /// <summary>
    /// Empty method name is rejected.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_EmptyName_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ExtractMethodService.PreviewExtractMethodAsync(
                workspace.WorkspaceId,
                filePath,
                startLine: 13, startColumn: 9,
                endLine: 14, endColumn: 34,
                "",
                CancellationToken.None));
    }

    /// <summary>
    /// extract-method-apply-var-redeclaration: when the single flowsOut variable is declared
    /// OUTSIDE the extracted region (only reassigned inside), the call site must emit a plain
    /// assignment `result = M(...)` rather than `var result = M(...)` to avoid CS0136 + CS0841.
    /// Apply must succeed and the resulting solution must compile.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_ReassignsExistingLocal_EmitsAssignmentNotVarDecl()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // ReassignedLocalScenario: lines 39-40 reassign the existing `result` local (declared on
        // line 38, OUTSIDE the selection). `result` flows IN and flows OUT, but it is NOT in
        // VariablesDeclared, so the call site must use plain assignment.
        var preview = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 39, startColumn: 9,
            endLine: 40, endColumn: 30,
            "TransformResult",
            CancellationToken.None);

        var diff = preview.Changes[0].UnifiedDiff;
        Assert.IsTrue(diff.Contains("result=TransformResult") || diff.Contains("result = TransformResult"),
            $"Expected plain assignment 'result = TransformResult(...)' at the call site. Diff:\n{diff}");
        Assert.IsFalse(diff.Contains("var result=TransformResult") || diff.Contains("var result = TransformResult"),
            $"Must NOT emit `var result = TransformResult(...)` — that re-declares the existing local. Diff:\n{diff}");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Apply must succeed.");

        var compileResult = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId, new CompileCheckOptions(), CancellationToken.None);
        Assert.IsTrue(compileResult.Success,
            $"Compilation must succeed after extract; before fix it produced CS0136 + CS0841. Errors: " +
            $"{string.Join("; ", compileResult.Diagnostics?.Select(d => $"{d.Id}: {d.Message}") ?? [])}");
    }

    /// <summary>
    /// Sanity: when the flowsOut variable IS declared inside the region (the original test case
    /// at lines 13-14 with `var doubled = sum * 2;`), the call site still uses `var x = M(...)`
    /// so we can introduce the new local. Guards against an over-correction in the previous test.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_DeclaredAndFlowsOut_StillEmitsVarDecl()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // Lines 13-14: var sum = a + b; var doubled = sum * 2;
        // `doubled` is declared inside AND flows out (used on line 15 + return).
        var preview = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 13, startColumn: 9,
            endLine: 14, endColumn: 34,
            "ComputeDoubled",
            CancellationToken.None);

        var diff = preview.Changes[0].UnifiedDiff;
        Assert.IsTrue(diff.Contains("var doubled=ComputeDoubled") || diff.Contains("var doubled = ComputeDoubled"),
            $"Expected `var doubled = ComputeDoubled(...)` since the local is introduced by the region. Diff:\n{diff}");
    }

    /// <summary>
    /// extract-method-preview-same-block-scope-false-negative (gh #744): selecting a single
    /// multi-line if-block where `endColumn` lands on the closing `}` used to throw the
    /// "All selected statements must be in the same block scope" guard, because the old
    /// `selectionSpan.Contains(s.Span)` filter rejected the outer if-statement (its
    /// exclusive `Span.End` exceeded `selectionSpan.End`), leaving only the nested-block
    /// body children whose parent differed from any outer statements that might have been
    /// captured. The start-anchor filter restores correct collection of the outer if.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_SingleIfBlockEndingAtClosingBrace_Succeeds()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // IfBlockScenario lines 56-60: select the entire if-statement.
        //   Line 56: "        if (result > 0)"            (if keyword starts at col 9, 1-based)
        //   Line 60: "        }"                           (closing brace at col 9, 1-based)
        // `endColumn: 9` puts the selection end exactly on the `}` character —
        // the exclusive `Span.End` of the if-statement is one past `}`, so the
        // pre-fix `Contains` predicate dropped the if-statement entirely and
        // the same-block-scope guard fired the false-negative.
        var preview = await ExtractMethodService.PreviewExtractMethodAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 56, startColumn: 9,
            endLine: 60, endColumn: 9,
            "AmplifyResult",
            CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken,
            "Preview must produce a token — the if-block is a valid extraction target.");
        Assert.IsTrue(preview.Changes.Count > 0, "Expected at least one file change.");

        var diff = preview.Changes[0].UnifiedDiff;
        Assert.IsTrue(diff.Contains("AmplifyResult"),
            $"Diff should contain the extracted method name. Diff:\n{diff}");
    }
}
