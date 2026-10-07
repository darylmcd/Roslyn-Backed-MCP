using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

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

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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

        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
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

        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
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

    [TestMethod]
    [DataRow(0, 9, 15, 36, "startLine")]
    [DataRow(-1, 9, 15, 36, "startLine")]
    [DataRow(int.MinValue, 9, 15, 36, "startLine")]
    [DataRow(int.MaxValue, 9, 15, 36, "startLine")]
    [DataRow(13, 0, 15, 36, "startColumn")]
    [DataRow(13, -1, 15, 36, "startColumn")]
    [DataRow(13, int.MinValue, 15, 36, "startColumn")]
    [DataRow(13, int.MaxValue, 15, 36, "startColumn")]
    [DataRow(13, 100, 15, 36, "startColumn")]
    [DataRow(13, 9, 0, 39, "endLine")]
    [DataRow(13, 9, -1, 39, "endLine")]
    [DataRow(13, 9, int.MinValue, 39, "endLine")]
    [DataRow(13, 9, int.MaxValue, 39, "endLine")]
    [DataRow(13, 9, 15, 0, "endColumn")]
    [DataRow(13, 9, 15, -1, "endColumn")]
    [DataRow(13, 9, 15, int.MinValue, "endColumn")]
    [DataRow(13, 9, 15, int.MaxValue, "endColumn")]
    [DataRow(13, 9, 15, 100, "endColumn")]
    public async Task ArgumentRefusals_SharedCoordinatesAreBounded(
        int startLine, int startColumn, int endLine, int endColumn, string parameter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var error = await Assert.ThrowsExactlyAsync<PublicArgumentOutOfRangeException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(workspace.WorkspaceId,
                workspace.GetPath("SampleLib", "RefactoringProbe.cs"),
                startLine, startColumn, endLine, endColumn, "Extracted", "private", false, CancellationToken.None));
        Assert.AreEqual("example" + char.ToUpperInvariant(parameter[0]) + parameter[1..], error.ParamName);
        StringAssert.Contains(error.PublicMessage, error.ParamName!);
    }

    [TestMethod]
    [DataRow(15, 9, 13, 9, "exampleStartLine")]
    [DataRow(13, 10, 13, 9, "exampleStartColumn")]
    public async Task ArgumentRefusals_SharedReversedSpanNamesStart(
        int startLine, int startColumn, int endLine, int endColumn, string parameter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(workspace.WorkspaceId, workspace.GetPath("SampleLib", "RefactoringProbe.cs"),
                startLine, startColumn, endLine, endColumn, "Extracted", "private", false, CancellationToken.None));
        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow(" PRIVATE ")]
    [DataRow("internal")]
    [DataRow("public")]
    public async Task ArgumentRefusals_SharedAccessibilityAndLineEndRemainValid(string accessibility)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(await File.ReadAllTextAsync(path));
        var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
            workspace.WorkspaceId, path, 18, 13, 18, text.Lines[17].Span.Length + 1,
            "Extracted", accessibility, false, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
    }

    [TestMethod]
    public async Task ArgumentRefusals_SharedEqualSpanHasNamedCallerRefusal()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(workspace.WorkspaceId,
                workspace.GetPath("SampleLib", "SharedExpressionProbe.cs"),
                18, 13, 18, 13, "Extracted", "private", false, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(13, 9, 12)]
    [DataRow(11, 12, 15)]
    [DataRow(26, 12, 18)]
    [DataRow(1, 11, 20)]
    [DataRow(12, 5, 6)]
    [DataRow(22, 16, 20)]
    [DataRow(13, 9, 9)]
    public async Task ArgumentRefusals_SharedTypeAndEmptySelectionsAreCallerErrors(int line, int start, int end)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        try
        {
            var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspace.WorkspaceId, workspace.GetPath("SampleLib", "RefactoringProbe.cs"),
                line, start, line, end, "Extracted", "private", false, CancellationToken.None);
            Assert.Fail("Type-position selection unexpectedly produced a preview: " + preview.Description);
        }
        catch (Exception error) when (error is not AssertFailedException)
        {
            Console.WriteLine("TYPE-POSITION:" + line + ":" + start + ":" + end + ":" + error);
            Assert.IsInstanceOfType<PublicArgumentException>(error);
            Assert.AreEqual("exampleStartColumn", ((ArgumentException)error).ParamName);
        }
    }

    [TestMethod]
    public async Task ArgumentRefusals_SharedValueIdentifiersIgnoreMatchingTypeNames()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "TypeValueProbe.cs");
        var source = """
            namespace SampleLib;
            public sealed class TypeValueProbe
            {
                public TypeValueProbe? Field;
                public int Compute(int TypeValueProbe)
                {
                    var a = TypeValueProbe;
                    var b = TypeValueProbe;
                    return a + b;
                }
            }
            """;
        await File.WriteAllTextAsync(path, source, CancellationToken.None);
        var id = await workspace.LoadAsync(CancellationToken.None);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot();
        var expression = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.EqualsValueClauseSyntax>().First().Value;
        var location = expression.GetLocation().GetLineSpan();
        var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(id, path,
            location.StartLinePosition.Line + 1, location.StartLinePosition.Character + 1,
            location.EndLinePosition.Line + 1, location.EndLinePosition.Character + 1,
            "ReadValue", "private", false, CancellationToken.None);
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test-value-identifier", CancellationToken.None);
        Assert.IsTrue(result.Success);
        var compile = await CompileCheckService.CheckAsync(id, new CompileCheckOptions(), CancellationToken.None);
        Assert.IsTrue(compile.Success, string.Join("; ", compile.Diagnostics?.Select(d => d.Message) ?? []));
        var changed = await File.ReadAllTextAsync(path);
        StringAssert.Contains(changed, "TypeValueProbe? Field");
        StringAssert.Contains(changed, "ReadValue(TypeValueProbe)");
    }

    [TestMethod]
    public async Task ArgumentRefusals_SharedPartialValueOverlapRemainsValid()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");
        var source = await File.ReadAllTextAsync(path);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot();
        var identifier = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
            .First(node => node.Identifier.ValueText == "filePath");
        var position = identifier.GetLocation().GetLineSpan().StartLinePosition;
        var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
            workspace.WorkspaceId, path, position.Line + 1, position.Character + 1,
            position.Line + 1, position.Character + 2, "ReadPath", "private", false, CancellationToken.None);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test-partial-value", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        var compile = await CompileCheckService.CheckAsync(workspace.WorkspaceId, new CompileCheckOptions(), CancellationToken.None);
        Assert.IsTrue(compile.Success, string.Join("; ", compile.Diagnostics?.Select(d => d.Message) ?? []));
    }

}
