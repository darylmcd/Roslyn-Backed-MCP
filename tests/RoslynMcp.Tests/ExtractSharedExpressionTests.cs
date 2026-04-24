using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Integration tests for <c>extract_shared_expression_to_helper_preview</c>. Uses
/// <c>SharedExpressionProbe.cs</c> — two public methods each contain the same
/// <c>System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar)</c>
/// pattern (the concrete PR #178 <c>NormalizeFilePathForResource</c> shape).
/// </summary>
[TestClass]
public sealed class ExtractSharedExpressionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Primary validation per the plan: two methods share the normalization expression;
    /// the preview must synthesize a helper and rewrite both call sites so each method
    /// invokes the new helper.
    /// </summary>
    [TestMethod]
    public async Task ExtractSharedExpression_TwoSitesInSameType_SynthesizesHelperAndRewritesBoth()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");

        // Line 18, cols 13..104 (1-based, end exclusive) wraps the expression
        // `System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar)`.
        var result = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
            workspace.WorkspaceId,
            filePath,
            exampleStartLine: 18, exampleStartColumn: 13,
            exampleEndLine: 18, exampleEndColumn: 104,
            helperName: "NormalizeFilePath",
            helperAccessibility: "private",
            allowCrossFile: false,
            CancellationToken.None);

        Assert.IsNotNull(result.PreviewToken, "Preview should produce a token.");
        Assert.AreEqual(1, result.Changes.Count,
            $"Expected exactly one file change (same-type scope). Changes: {result.Changes.Count}");

        var diff = result.Changes[0].UnifiedDiff;

        // The helper must appear in the diff.
        StringAssert.Contains(diff, "NormalizeFilePath",
            $"Helper method name should appear in the diff. Diff:\n{diff}");
        StringAssert.Contains(diff, "private static",
            $"Helper should be rendered as `private static`. Diff:\n{diff}");
        StringAssert.Contains(diff, "string filePath",
            $"Helper should accept the free variable `filePath` as a parameter. Diff:\n{diff}");
        StringAssert.Contains(diff, "return System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar);",
            $"Helper body should contain the canonical normalization expression. Diff:\n{diff}");

        // Both call sites must be rewritten to invoke the helper.
        var helperInvocationCount = CountOccurrences(diff, "+            NormalizeFilePath(filePath)");
        Assert.AreEqual(2, helperInvocationCount,
            $"Expected two rewritten call sites (one per method). Found {helperInvocationCount}. Diff:\n{diff}");
    }

    /// <summary>
    /// Error path — when only one instance of the expression exists, the preview must refuse.
    /// The tool is specifically for N≥2 call sites; a single-site extraction belongs to
    /// <c>extract_method_preview</c>.
    /// </summary>
    [TestMethod]
    public async Task ExtractSharedExpression_SingleOccurrence_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        // RefactoringProbe has only one `Math.PI * radius * radius` expression.
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspace.WorkspaceId,
                filePath,
                exampleStartLine: 22, exampleStartColumn: 16,
                exampleEndLine: 22, exampleEndColumn: 39,
                helperName: "Area",
                helperAccessibility: "private",
                allowCrossFile: false,
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "Only one occurrence",
            $"Error should name the single-occurrence rejection. Got: {ex.Message}");
    }

    /// <summary>
    /// Empty helper name is rejected with <see cref="ArgumentException"/>.
    /// </summary>
    [TestMethod]
    public async Task ExtractSharedExpression_EmptyHelperName_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspace.WorkspaceId,
                filePath,
                exampleStartLine: 18, exampleStartColumn: 13,
                exampleEndLine: 18, exampleEndColumn: 104,
                helperName: "",
                helperAccessibility: "private",
                allowCrossFile: false,
                CancellationToken.None));
    }

    /// <summary>
    /// Unsupported accessibility token is rejected (only private / internal / public are accepted).
    /// </summary>
    [TestMethod]
    public async Task ExtractSharedExpression_InvalidAccessibility_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspace.WorkspaceId,
                filePath,
                exampleStartLine: 18, exampleStartColumn: 13,
                exampleEndLine: 18, exampleEndColumn: 104,
                helperName: "NormalizeFilePath",
                helperAccessibility: "protected",
                allowCrossFile: false,
                CancellationToken.None));
    }

    /// <summary>
    /// Apply after preview: the synthesized helper and rewritten call sites compile cleanly.
    /// Guards against parameter-list or return-type inference mistakes that would produce
    /// CS0103 / CS0029 at the rewrite sites.
    /// </summary>
    [TestMethod]
    public async Task ExtractSharedExpression_ApplyAfterPreview_CompilationSucceeds()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");

        var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
            workspace.WorkspaceId,
            filePath,
            exampleStartLine: 18, exampleStartColumn: 13,
            exampleEndLine: 18, exampleEndColumn: 104,
            helperName: "NormalizeFilePath",
            helperAccessibility: "private",
            allowCrossFile: false,
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Apply should succeed.");

        var compileResult = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId, new CompileCheckOptions(), CancellationToken.None);
        Assert.IsTrue(compileResult.Success,
            "Compilation must succeed after extract-shared-expression apply. Errors: " +
            $"{string.Join("; ", compileResult.Diagnostics?.Select(d => $"{d.Id}: {d.Message}") ?? [])}");
    }

    private static int CountOccurrences(string source, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
    }
}
