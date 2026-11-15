using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectPartialTypeMoveIntegrationTests : IsolatedWorkspaceTestBase
{
    private const string TypeName = "PartialMoveProbe";
    private const string SourceNamespace = "Original";
    private const string DestinationNamespace = "Destination";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MovePreview_RefusesMutuallyDependentPartialDeclarations(bool sameFile)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "PartialMoveProbe.cs");
        const string firstPart = """
            namespace Original;
            public partial class PartialMoveProbe
            {
                public int Run() => Offset();
                private int Value => 41;
            }
            """;
        const string secondPart = """
            public partial class PartialMoveProbe
            {
                private int Offset() => Value + 1;
            }
            """;
        if (sameFile)
        {
            await File.WriteAllTextAsync(sourcePath, firstPart + "\n" + secondPart);
        }
        else
        {
            await File.WriteAllTextAsync(sourcePath, firstPart);
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "PartialMoveProbe.Other.cs"),
                "namespace Original;\n" + secondPart);
        }

        await workspace.LoadAsync();
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var symbol = await GetSourceSymbolAsync(solution);
        Assert.HasCount(2, symbol.DeclaringSyntaxReferences);
        Assert.AreEqual(sameFile ? 1 : 2, symbol.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree).Distinct().Count());
        await AssertRefusesMovesAsync(workspace, sourcePath);
    }

    [TestMethod]
    public async Task MovePreview_RefusesSourceGeneratedPartialDeclaration()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "PartialMoveProbe.cs");
        await File.WriteAllTextAsync(sourcePath, """
            using System.Text.RegularExpressions;

            namespace Original;
            public static partial class PartialMoveProbe
            {
                [GeneratedRegex("^[a-z]+$")]
                private static partial Regex Pattern();

                public static bool Matches(string value) => Pattern().IsMatch(value);
            }
            """);

        await workspace.LoadAsync();
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var symbol = await GetSourceSymbolAsync(solution);
        Assert.IsTrue(symbol.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree.FilePath == sourcePath));
        Assert.IsTrue(symbol.DeclaringSyntaxReferences.Any(reference =>
            solution.GetDocument(reference.SyntaxTree) is SourceGeneratedDocument),
            "The loaded type must have a generator-owned declaration in addition to its physical declaration.");
        await AssertRefusesMovesAsync(workspace, sourcePath);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("partial ")]
    public async Task MovePreview_PreservesSingleDeclarationMoves(string modifier)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "PartialMoveProbe.cs");
        await File.WriteAllTextAsync(sourcePath, $"namespace Original; public {modifier}class PartialMoveProbe {{ }}");
        await workspace.LoadAsync();
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var symbol = await GetSourceSymbolAsync(solution);
        Assert.HasCount(1, symbol.DeclaringSyntaxReferences);

        var store = new PreviewStore();
        using var cache = new CompilationCache(WorkspaceManager);
        var service = new CrossProjectRefactoringService(WorkspaceManager, store, cache);
        var json = await CrossProjectRefactoringTools.PreviewMoveTypeToProject(
            WorkspaceExecutionGate, service, workspace.WorkspaceId, sourcePath, TypeName, "Contracts", DestinationNamespace);
        using var envelope = JsonDocument.Parse(json);
        var token = envelope.RootElement.GetProperty("previewToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        var pending = await ReparseSerializedDocumentsAsync(entry.Value.ModifiedSolution);
        var destination = pending.Projects.Single(project => project.Name == "Contracts");
        Assert.IsTrue(destination.Documents.Any(document => document.FilePath == workspace.GetPath("Contracts", "PartialMoveProbe.cs")));
        var compilation = await destination.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName(DestinationNamespace + "." + TypeName));
        Assert.IsFalse(pending.Projects.Single(project => project.Name == "SampleLib").Documents.Any(document => document.FilePath == sourcePath));
        await AssertCompilesAsync(pending);
    }

    private static async Task<INamedTypeSymbol> GetSourceSymbolAsync(Solution solution)
    {
        var project = solution.Projects.Single(project => project.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.HasCount(0, errors, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        var symbol = compilation.GetTypeByMetadataName(SourceNamespace + "." + TypeName);
        Assert.IsNotNull(symbol);
        return symbol;
    }

    private static async Task AssertRefusesMovesAsync(IsolatedWorkspaceScope workspace, string sourcePath)
    {
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var files = Snapshot(workspace.RootPath);
        var crossProjectTarget = workspace.GetPath("Contracts", "PartialMoveProbe.cs");
        var relocationTarget = workspace.GetPath("SampleLib", "MovedProbe.cs");
        var store = new PreviewStore();
        var relocation = new NamespaceRelocationService(WorkspaceManager, store, NullLogger<NamespaceRelocationService>.Instance);
        var existingRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TypeMoveTools.PreviewChangeTypeNamespace(WorkspaceExecutionGate, relocation, workspace.WorkspaceId,
                TypeName, SourceNamespace, DestinationNamespace, relocationTarget));
        StringAssert.Contains(existingRefusal.Message, "matched multiple files:");
        Assert.AreEqual(0, StoreCount(store));

        using var cache = new CompilationCache(WorkspaceManager);
        var service = new CrossProjectRefactoringService(WorkspaceManager, store, cache);
        var exception = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            CrossProjectRefactoringTools.PreviewMoveTypeToProject(WorkspaceExecutionGate, service, workspace.WorkspaceId,
                sourcePath, TypeName, "Contracts", DestinationNamespace));

        var json = ToolErrorHandler.ClassifyAndFormat(exception, "move_type_to_project_preview");
        using var envelope = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", envelope.RootElement.GetProperty("exceptionType").GetString());
        Assert.AreEqual("Moving a type with multiple declarations is unsupported. Select a type with a single declaration.",
            envelope.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(envelope.RootElement.TryGetProperty("previewToken", out _));
        Assert.AreEqual(0, StoreCount(store));
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(crossProjectTarget));
        Assert.IsFalse(File.Exists(relocationTarget));
    }
}
