using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExtractSharedExpressionNamespaceIdentityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractSharedExpression_CrossTypeCallBindsToFullNestedIdentity(bool callerSharesDocument)
    {
        const string ownerSource = """
            namespace @event.Outer
            {
                namespace Inner
                {
                    public class Container
                    {
                        public class Owner
                        {
                            public int Read(int input) => input * 31;
                        }
                    }
                }
            }
            """;
        const string callerSource = """
            namespace Inner
            {
                public class Owner
                {
                    public static int SharedValue(int input) => -1;
                }
            }

            namespace Caller
            {
                public class Consumer
                {
                    public int Read(int input) => input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "internal", "event.Outer.Inner.Container+Owner", callerSharesDocument);
    }

    [TestMethod]
    public async Task ExtractSharedExpression_OtherPartOfGenericOwnerCanCallPrivateHelper()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Owner<T>
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Owner<T>
            {
                public int ReadAgain(int input) => input * 31;
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "private", "Outer.Inner.Owner`1");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_OtherPartialDeclarationDoesNotCallLocalDelegate()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Owner
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Owner
            {
                public int ReadAgain(int input)
                {
                    System.Func<int, int> SharedValue = value => -1;
                    _ = SharedValue;
                    return input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "private", "Outer.Inner.Owner");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_SameDeclarationDoesNotCallLocalDelegate()
    {
        const string source = """
            namespace Outer.Inner;
            public class Owner
            {
                public int Read(int input)
                {
                    System.Func<int, int> SharedValue = value => -1;
                    _ = SharedValue;
                    return input * 31;
                }
                public int ReadAgain(int input) => input * 31;
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            source, "", "private", "Outer.Inner.Owner", callerSharesDocument: true);
    }

    [TestMethod]
    public async Task ExtractSharedExpression_SameDeclarationDoesNotCallParameter()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", "SharedExpressionProbe.cs");
        var workspaceId = workspace.WorkspaceId;
        var root = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(path)).GetRoot();
        var load = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Load");
        var normalized = load.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "normalized");
        var expression = Assert.IsInstanceOfType<InvocationExpressionSyntax>(normalized.Initializer?.Value);
        var span = expression.GetLocation().GetLineSpan();
        var json = await ExtractMethodTools.PreviewExtractSharedExpressionToHelper(
            WorkspaceExecutionGate, ExtractMethodService, workspaceId, path,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            "filePath", "private", allowCrossFile: false);
        const string call = "global::SampleLib.SharedExpressionProbe.filePath(filePath)";
        StringAssert.Contains(json, call);
        using var preview = JsonDocument.Parse(json);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        await GetCleanCompilationAsync(GetPreviewSolution(token));

        var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
        using var applied = JsonDocument.Parse(appliedJson);
        Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
        await GetCleanCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        StringAssert.Contains(await File.ReadAllTextAsync(path), call);
    }

    [TestMethod]
    public async Task ExtractSharedExpression_NestedGenericOwnerKeepsItsEnclosingTypeArgument()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Container<T>
            {
                public class Owner
                {
                    public int Read(int input) => input * 31;
                }
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Container<T>
            {
                public class Caller
                {
                    public int ReadAgain(int input) => input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "internal", "Outer.Inner.Container`1+Owner");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsOwnerTypeParameterFromUnrelatedScope()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public class Owner<T>
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public class Caller<T>
            {
                public int Read(int input) => input * 31;
            }
            """;

        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "NamespaceExpressionOwner.cs");
        await File.WriteAllTextAsync(ownerPath, ownerSource);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "NamespaceExpressionCaller.cs"), callerSource);
        var workspaceId = await workspace.LoadAsync();
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewSharedExpressionAsync(workspaceId, ownerPath, ownerSource, "internal"));
        StringAssert.Contains(error.PublicMessage, "declaring type");
        FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
    }

    private static async Task AssertSharedExpressionPreviewAndApplyAsync(
        string ownerSource, string callerSource, string accessibility, string ownerMetadataName,
        bool callerSharesDocument = false)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "NamespaceExpressionOwner.cs");
        var callerPath = ownerPath;
        if (callerSharesDocument)
        {
            ownerSource = callerSource + Environment.NewLine + ownerSource;
        }
        else
        {
            callerPath = workspace.GetPath("SampleLib", "NamespaceExpressionCaller.cs");
            await File.WriteAllTextAsync(callerPath, callerSource);
        }

        await File.WriteAllTextAsync(ownerPath, ownerSource);
        var workspaceId = await workspace.LoadAsync();
        var json = await PreviewSharedExpressionAsync(workspaceId, ownerPath, ownerSource, accessibility);
        using var preview = JsonDocument.Parse(json);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        Assert.AreEqual(callerSharesDocument ? 1 : 2, preview.RootElement.GetProperty("changes").GetArrayLength(), json);
        await AssertCallsBindAsync(GetPreviewSolution(token), callerPath, ownerMetadataName, callerSharesDocument ? 2 : 1);

        var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
        using var applied = JsonDocument.Parse(appliedJson);
        Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
        await AssertCallsBindAsync(
            WorkspaceManager.GetCurrentSolution(workspaceId), callerPath, ownerMetadataName, callerSharesDocument ? 2 : 1);
    }

    private static Task<string> PreviewSharedExpressionAsync(
        string workspaceId, string ownerPath, string ownerSource, string accessibility)
    {
        var span = CSharpSyntaxTree.ParseText(ownerSource).GetRoot().DescendantNodes()
            .OfType<BinaryExpressionSyntax>().Last().GetLocation().GetLineSpan();
        return ExtractMethodTools.PreviewExtractSharedExpressionToHelper(
            WorkspaceExecutionGate, ExtractMethodService, workspaceId, ownerPath,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            "SharedValue", accessibility, allowCrossFile: true);
    }

    private static async Task AssertCallsBindAsync(Solution solution, string path, string ownerMetadataName, int expectedCount)
    {
        var compilation = await GetCleanCompilationAsync(solution);
        var expected = compilation.GetTypeByMetadataName(ownerMetadataName);
        Assert.IsNotNull(expected);
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        var calls = (await tree.GetRootAsync()).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.HasCount(expectedCount, calls);
        var model = compilation.GetSemanticModel(tree);
        foreach (var call in calls)
        {
            var target = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
            Assert.IsNotNull(target, call.ToString());
            Assert.AreEqual("SharedValue", target.Name);
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(expected, target.ContainingType), target.ToDisplayString());
        }
    }

    private static Solution GetPreviewSolution(string token)
    {
        var pending = PreviewStore.Retrieve(token);
        Assert.IsNotNull(pending);
        return pending.Value.ModifiedSolution;
    }

    private static async Task<Compilation> GetCleanCompilationAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        AssertNoCompilerErrors(compilation);
        return compilation;
    }

    private static void AssertNoCompilerErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.IsFalse(errors.Any(), string.Join(Environment.NewLine, errors));
    }
}
