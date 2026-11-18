using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// relocation-using-semantic-identity copy site (StringLiteralReplaceService): the existing-import
/// check compared the syntax <c>Name.ToString()</c> with the caller's namespace, and the synthesized
/// import parsed that caller string without escaping keyword segments.
/// </summary>
[TestClass]
public sealed class StringLiteralReplaceImportSpellingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task DoesNotDuplicateAnImportWrittenInAnEquivalentSpelling()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "LitConsts.cs"),
            "namespace Lit.Consts.Sub\n{\n    public static class LitConsts\n    {\n        public const string Hello = \"hello-literal\";\n    }\n}\n");
        var consumerPath = workspace.GetPath("SampleLib", "LitConsumer.cs");
        await File.WriteAllTextAsync(consumerPath,
            "using Lit . Consts . Sub ;\n\nnamespace Consumers;\n\npublic sealed class LitConsumer\n{\n    public string M() => Echo(\"hello-literal\");\n\n    private static string Echo(string value) => value;\n}\n");
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspaceId,
            [new StringLiteralReplacementDto("hello-literal", "LitConsts.Hello", "Lit.Consts.Sub")],
            new RestructureScope(FilePath: consumerPath, ProjectName: null),
            CancellationToken.None);

        var diff = preview.Changes.Single().UnifiedDiff;
        StringAssert.Contains(diff, "LitConsts.Hello");
        Assert.IsFalse(diff.Split('\n').Any(line => line.StartsWith("+using ", StringComparison.Ordinal)),
            $"The namespace is already imported; no second import may be added:\n{diff}");
    }

    [TestMethod]
    public async Task EscapesKeywordSegmentsInTheSynthesizedImport()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "LitKeywordConsts.cs"),
            "namespace Lit.Consts.@class\n{\n    public static class LitKeywordConsts\n    {\n        public const string Hello = \"keyword-literal\";\n    }\n}\n");
        var consumerPath = workspace.GetPath("SampleLib", "LitKeywordConsumer.cs");
        await File.WriteAllTextAsync(consumerPath,
            "namespace Consumers;\n\npublic sealed class LitKeywordConsumer\n{\n    public string M() => Echo(\"keyword-literal\");\n\n    private static string Echo(string value) => value;\n}\n");
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);

        var preview = await new StringLiteralReplaceService(WorkspaceManager, PreviewStore).PreviewReplaceAsync(
            workspaceId,
            [new StringLiteralReplacementDto("keyword-literal", "LitKeywordConsts.Hello", "Lit.Consts.class")],
            new RestructureScope(FilePath: consumerPath, ProjectName: null),
            CancellationToken.None);

        StringAssert.Contains(preview.Changes.Single().UnifiedDiff, "+using Lit.Consts.@class;");
    }
}
