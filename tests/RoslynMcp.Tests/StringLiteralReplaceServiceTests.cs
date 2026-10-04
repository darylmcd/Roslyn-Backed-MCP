using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <see cref="StringLiteralReplaceService.PreviewReplaceAsync"/>.
/// Backlog row replace-string-literals-preview-throws-on-zero-match: the zero-match path
/// previously threw <see cref="InvalidOperationException"/>; now it must return a
/// structured empty preview (empty token, empty changes list, descriptive Description)
/// matching the shape used by FixAllService.
/// </summary>
// donotparallelize-audit-wave-27: [DoNotParallelize] removed. Tests only call
// StringLiteralReplaceService.PreviewReplaceAsync on the shared sample workspace obtained through
// the synchronized WorkspaceIdCache; the zero-match path returns an empty preview without storing
// a PreviewStore token, and the empty-list path throws before touching the workspace. The
// class-owned service instance is created once in ClassInit. No LoadAsync/ReloadAsync/Close, no
// *_apply, no static or environment mutation. Verified with 3x repeated concurrent runs alongside
// wave-27 siblings and parallel-enabled classes, green every time.
[TestClass]
public sealed class StringLiteralReplaceServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static StringLiteralReplaceService Service { get; set; } = null!;


    [TestMethod]
    public async Task PreviewReplace_ArgumentRefusals_PublishMemberConstraints()
    {
        foreach (var replacements in new IReadOnlyList<StringLiteralReplacementDto>?[] { null, Array.Empty<StringLiteralReplacementDto>() })
            await BulkRefactoringTests.AssertPublicRefusalAsync(
                () => Service.PreviewReplaceAsync("unused", replacements!, new RestructureScope(null, null), CancellationToken.None),
                "replace_string_literals_preview", "replacements", "At least one replacement is required.");
        foreach (var value in new string?[] { null, "" })
            await BulkRefactoringTests.AssertPublicRefusalAsync(
                () => Service.PreviewReplaceAsync("unused", new[] { new StringLiteralReplacementDto(value!, "C:/private/expression-secret", null) },
                    new RestructureScope(null, null), CancellationToken.None),
                "replace_string_literals_preview", "replacements", "replacement.literalValue must be non-empty.", "C:/private/expression-secret");
        foreach (var expression in new string?[] { null, "", "  " })
            await BulkRefactoringTests.AssertPublicRefusalAsync(
                () => Service.PreviewReplaceAsync("unused", new[] { new StringLiteralReplacementDto("C:/private/literal-secret", expression!, null) },
                    new RestructureScope(null, null), CancellationToken.None),
                "replace_string_literals_preview", "replacements", "replacement.replacementExpression must be non-empty.", "C:/private/literal-secret");
    }

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        Service = new StringLiteralReplaceService(WorkspaceManager, PreviewStore);
    }

    [TestMethod]
    public async Task PreviewReplace_ZeroMatches_ReturnsEmptyPreviewInsteadOfThrowing()
    {
        // A literal that cannot appear anywhere in the sample workspace. Pre-fix this threw
        // `InvalidOperationException("... no matching literals found in scope.")`; now the
        // service must return a structured empty response so callers can treat "no matches"
        // as a benign outcome rather than an error.
        var replacements = new[]
        {
            new StringLiteralReplacementDto(
                LiteralValue: "UNLIKELY_LITERAL_THAT_DOES_NOT_EXIST_XYZ_8f2a1c",
                ReplacementExpression: "Constants.Unused",
                UsingNamespace: null),
        };

        var preview = await Service.PreviewReplaceAsync(
            WorkspaceId,
            replacements,
            new RestructureScope(FilePath: null, ProjectName: null),
            CancellationToken.None);

        Assert.AreEqual(0, preview.Changes.Count, "Empty preview must report zero changes.");
        Assert.AreEqual(string.Empty, preview.PreviewToken,
            "Empty preview must use an empty token — there is nothing to redeem.");
        StringAssert.Contains(preview.Description, "No matching string literals",
            "Description must explain why the preview is empty.");
        Assert.IsNull(preview.Warnings);
    }

    [TestMethod]
    public async Task PreviewReplace_EmptyReplacementList_StillThrowsArgumentException()
    {
        // Guard that the zero-match relaxation didn't weaken input validation. A caller
        // passing an empty replacements array is a programming error, distinct from a
        // well-formed replacement that simply doesn't match anything in scope.
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            Service.PreviewReplaceAsync(
                WorkspaceId,
                replacements: Array.Empty<StringLiteralReplacementDto>(),
                new RestructureScope(FilePath: null, ProjectName: null),
                CancellationToken.None));
    }
}

[TestClass]
public sealed class StringLiteralReplaceSemanticTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreviewReplace_ConstantReference_SkipsOwnInitializer(bool separateDocument)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var declarationPath = workspace.GetPath("SampleLib", "LiteralConstants.cs");
        var consumerPath = workspace.GetPath("SampleLib", "LiteralConsumer.cs");
        var declaration = "namespace SampleLib; public static class LiteralConstants { public const string Prefix = \"report:\"; public const string Other = \"report:\"; }";
        var consumer = "public static class LiteralConsumer { public static string Value = \"report:\"; }";
        await File.WriteAllTextAsync(declarationPath, declaration);
        if (separateDocument)
            await File.WriteAllTextAsync(consumerPath, "namespace SampleLib; " + consumer);
        else
            await File.AppendAllTextAsync(declarationPath, consumer);
        await workspace.LoadAsync();

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspace.WorkspaceId,
            [new StringLiteralReplacementDto("report:", "LiteralConstants.Prefix", null)],
            new RestructureScope(null, "SampleLib"), CancellationToken.None);
        var candidate = PreviewStore.Retrieve(preview.PreviewToken!)!.Value.ModifiedSolution;
        var changedDeclaration = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == declarationPath).GetTextAsync()).ToString();
        StringAssert.Contains(changedDeclaration, "Prefix = \"report:\"");
        StringAssert.Contains(changedDeclaration, "Other = LiteralConstants.Prefix");
        var changedConsumer = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == (separateDocument ? consumerPath : declarationPath)).GetTextAsync()).ToString();
        StringAssert.Contains(changedConsumer, "Value = LiteralConstants.Prefix");
        var compilation = await candidate.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsFalse(compilation!.GetDiagnostics().Any(d => d.Id == "CS0110"));
    }

    [TestMethod]
    public async Task PreviewReplace_UnboundSymbol_RefusesRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "LiteralUnbound.cs");
        await File.WriteAllTextAsync(path, "namespace SampleLib; public static class LiteralUnbound { public const string Prefix = \"report:\"; }");
        await workspace.LoadAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
                workspace.WorkspaceId,
                [new StringLiteralReplacementDto("report:", "MissingType.Prefix", null)],
                new RestructureScope(path, null), CancellationToken.None));
    }

    [TestMethod]
    public async Task PreviewReplace_LocalConstantReference_SkipsOwnInitializer()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "LocalPrefix.cs");
        await File.WriteAllTextAsync(path,
            "namespace SampleLib; public static class LocalPrefix { public static string Get() { const string Prefix = \"report:\"; const string Other = \"report:\"; return \"report:\"; } }");
        await workspace.LoadAsync();

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspace.WorkspaceId,
            [new StringLiteralReplacementDto("report:", "Prefix", null)],
            new RestructureScope(path, null), CancellationToken.None);
        var candidate = PreviewStore.Retrieve(preview.PreviewToken!)!.Value.ModifiedSolution;
        var changed = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == path).GetTextAsync()).ToString();
        StringAssert.Contains(changed, "const string Prefix = \"report:\"");
        StringAssert.Contains(changed, "const string Other = Prefix");
        StringAssert.Contains(changed, "return Prefix");
        var compilation = await candidate.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsFalse(compilation!.GetDiagnostics().Any(d => d.Id == "CS0841"));
    }

    [TestMethod]
    public async Task PreviewReplace_UsingNamespace_BindsCrossDocumentConstant()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var declarationPath = workspace.GetPath("SampleLib", "ExternalPrefix.cs");
        var consumerPath = workspace.GetPath("SampleLib", "ExternalConsumer.cs");
        await File.WriteAllTextAsync(declarationPath,
            "namespace Prefixes; public static class ExternalPrefix { public const string Value = \"report:\"; }");
        await File.WriteAllTextAsync(consumerPath,
            "namespace SampleLib; public static class ExternalConsumer { public static string Value = \"report:\"; }");
        await workspace.LoadAsync();

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspace.WorkspaceId,
            [new StringLiteralReplacementDto("report:", "ExternalPrefix.Value", "Prefixes")],
            new RestructureScope(null, "SampleLib"), CancellationToken.None);
        var candidate = PreviewStore.Retrieve(preview.PreviewToken!)!.Value.ModifiedSolution;
        var declaration = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == declarationPath).GetTextAsync()).ToString();
        var consumer = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == consumerPath).GetTextAsync()).ToString();
        StringAssert.Contains(declaration, "Value = \"report:\"");
        StringAssert.Contains(consumer, "using Prefixes;");
        StringAssert.Contains(consumer, "Value = ExternalPrefix.Value");
    }

    [TestMethod]
    public async Task PreviewReplace_LiteralConcatenation_RemainsSyntaxOnly()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "LiteralConcat.cs");
        await File.WriteAllTextAsync(path, "namespace SampleLib; public static class LiteralConcat { public const string Value = \"dec\"; }");
        await workspace.LoadAsync();

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspace.WorkspaceId,
            [new StringLiteralReplacementDto("dec", "\"d\" + \"ec\"", null)],
            new RestructureScope(path, null), CancellationToken.None);
        var candidate = PreviewStore.Retrieve(preview.PreviewToken!)!.Value.ModifiedSolution;
        var changed = (await candidate.Projects.SelectMany(p => p.Documents)
            .Single(d => d.FilePath == path).GetTextAsync()).ToString();
        StringAssert.Contains(changed, "Value = \"d\" + \"ec\"");
    }
}
