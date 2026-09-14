using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class TypeMoveTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("public class Outer { private class Target {} } public class Sibling {}", "Nested types")]
    [DataRow("public class Outer { private class Target {} } public class Target {}", "ambiguous")]
    [DataRow("namespace First { public class Target {} } namespace Second { public class Target {} }", "ambiguous")]
    [DataRow("namespace First { public enum Target {} } namespace Second { public class Target {} }", "ambiguous")]
    public async Task MoveType_UnsafeSelection_RefusesWithoutMutation(string source, string reason)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "Selection.cs");
        await File.WriteAllTextAsync(sourcePath, source);
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspaceId);

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                workspaceId, sourcePath, "Target", null, CancellationToken.None));

        StringAssert.Contains(error.Message, reason);
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(sourcePath));
        Assert.IsFalse(File.Exists(workspace.GetPath("SampleLib", "Target.cs")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MoveType_PreservesNamespaceAndImportBinding_AfterApply(bool fileScoped)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "Binding.cs");
        const string members = """
            using Imported;
            using Alias = System.Uri;
            using static System.Math;
            public class Moved
            {
                public List<int> Custom { get; } = new();
                public Alias Address { get; } = new("https://example.invalid");
                public RootAlias Id { get; } = default;
                public ProjectAlias Duration { get; } = default;
                public double Magnitude => Abs(-1);
            }
            public enum Sibling { Value }
            """;
        var scope = fileScoped
            ? "namespace Outer.Inner;\n" + members
            : "namespace Outer { using Alias = System.Guid; namespace Inner {\n" + members + "\n} }";
        await File.WriteAllTextAsync(sourcePath,
            "global using ProjectAlias = System.TimeSpan;\nusing RootAlias = System.Guid;\n" + scope);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Imported.cs"),
            "namespace Imported { public class List<T> {} }");
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);
        var before = await GetMovedTypeAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        var expectedBindings = before.GetMembers().OfType<IPropertySymbol>()
            .Select(p => p.Name + ":" + p.Type.ToDisplayString()).ToArray();

        var preview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            workspaceId, sourcePath, "Moved", null, CancellationToken.None);
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);

        var after = await GetMovedTypeAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        CollectionAssert.AreEqual(expectedBindings, after.GetMembers().OfType<IPropertySymbol>()
            .Select(p => p.Name + ":" + p.Type.ToDisplayString()).ToArray());
        Assert.AreEqual(before.DeclaredAccessibility, after.DeclaredAccessibility);
        Assert.AreEqual("Moved.cs", Path.GetFileName(after.Locations.Single().SourceTree!.FilePath));
        var movedText = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "Moved.cs"));
        Assert.IsFalse(movedText.Contains("System.Collections.Generic", StringComparison.Ordinal));
        Assert.IsFalse(movedText.Contains("global using", StringComparison.Ordinal));
    }

    private static async Task<INamedTypeSymbol> GetMovedTypeAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        var type = compilation.GetTypeByMetadataName("Outer.Inner.Moved");
        Assert.IsNotNull(type);
        return type;
    }

    [TestMethod]
    public async Task MoveType_UsingCleanup_PropagatesCancellationAndCanRetry()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Cleanup", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = project.AddDocument("Cleanup.cs", SourceText.From("class C {}"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Roslyn.Services.TypeMoveService.RemoveUnusedUsingsAsync(
                document.Project.Solution, document.Id, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);

        var recovered = await Roslyn.Services.TypeMoveService.RemoveUnusedUsingsAsync(
            document.Project.Solution, document.Id, CancellationToken.None);
        Assert.IsNotNull(recovered.GetDocument(document.Id));
    }

    // Direct tool calls use an explicitly configured, connected test server. The separate
    // rejection test supplies a server whose configured boundary does not cover the source path.
    [TestMethod]
    public async Task PreviewMoveTypeToFile_Tool_ConfiguredServer_ProducesPreview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class ToolPreviewKitten : IAnimal\n{\n    public string Name => \"ToolKitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var json = await TypeMoveTools.PreviewMoveTypeToFile(
            await GetPathAuthorizedServerAsync(),
            WorkspaceExecutionGate,
            TypeMoveService,
            wsId,
            doc.FilePath!,
            "ToolPreviewKitten",
            null,
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(json));
        StringAssert.Contains(json, "previewToken");
    }

    [TestMethod]
    public async Task PreviewMoveTypeToFile_Tool_OutOfRootPath_RejectsWithArgumentException()
    {
        // Root-boundary regression: mirrors
        // TypeExtractionTests.PreviewExtractType_Tool_OutOfRootPath_RejectsWithArgumentException.
        // A real MCP client/server pair (McpRootsTestServerFactory) configures a root that does
        // NOT cover the workspace's source file, so move_type_to_file_preview must reject before
        // dispatching to the service.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class RootRejectKitten : IAnimal\n{\n    public string Name => \"RootRejectKitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var sanctionedRoot = Path.Combine(Path.GetTempPath(), "roots-boundary-sanctioned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sanctionedRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot, CancellationToken.None);

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                TypeMoveTools.PreviewMoveTypeToFile(
                    session.Server,
                    WorkspaceExecutionGate,
                    TypeMoveService,
                    wsId,
                    doc.FilePath!,
                    "RootRejectKitten",
                    null,
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
        }
        finally
        {
            Directory.Delete(sanctionedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task MoveType_FromMultiTypeFile_CreatesPreview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Add a second class to Cat.cs so it becomes a multi-type file before loading.
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var result = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
    }

    [TestMethod]
    public async Task MoveType_SingleTypeFile_ToolBoundaryPreservesRecoveryMessage()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true);

        var server = await GetPathAuthorizedServerAsync();
        var json = await ToolExecutionTestHarness.RunAsync("move_type_to_file_preview", () =>
            TypeMoveTools.PreviewMoveTypeToFile(
                server, WorkspaceExecutionGate, TypeMoveService,
                wsId, doc.FilePath!, "Dog", null, CancellationToken.None));

        using var envelope = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual(
            "Source file contains only one top-level type. " +
            "To move or rename the file, use move_file_preview instead. " +
            "move_type_to_file_preview is for extracting one type out of a file that contains multiple top-level types.",
            envelope.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task MoveType_SingleTypeFile_ErrorMessagePointsAtMoveFilePreview()
    {
        // Regression guard: when a caller tries to "rename" a single-type file via
        // move_type_to_file_preview, the error must point them at move_file_preview
        // instead of surfacing the misleading "nested types cannot be extracted" wording.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "Dog", null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "move_file_preview",
            $"Error message must point callers at move_file_preview; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("Nested types cannot be extracted", StringComparison.Ordinal),
            $"Misleading 'Nested types cannot be extracted' wording must not appear; got: {ex.Message}");
    }

    [TestMethod]
    public async Task MoveType_NonExistentType_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "NonExistentType", null, CancellationToken.None));

        AssertPublicRefusal(ex, "Type was not found in the source document. Use document_symbols to select a declaration from that file.");
    }

    [TestMethod]
    public async Task MoveType_EnumDeclaration_ThrowsStructuredTypeKindError()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Append an enum so Cat.cs becomes a multi-type file (a sibling class is required by
        // the single-type guard) but the *target* of the move is the enum.
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic enum IngestionTerminalStatus\n{\n    Pending,\n    Completed,\n    Failed,\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "IngestionTerminalStatus", null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "type-kind Enum not supported",
            $"Expected structured type-kind error citing Enum; got: {ex.Message}");
        // Must NOT degrade into the misleading "not found" wording.
        Assert.IsFalse(ex.Message.Contains("not found", StringComparison.Ordinal),
            $"Enum should be reported as unsupported kind, not 'not found': {ex.Message}");
    }

    [TestMethod]
    public async Task MoveType_DelegateDeclaration_ThrowsStructuredTypeKindError()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic delegate void NotificationHandler(string message);\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "NotificationHandler", null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "type-kind Delegate not supported",
            $"Expected structured type-kind error citing Delegate; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("not found", StringComparison.Ordinal),
            $"Delegate should be reported as unsupported kind, not 'not found': {ex.Message}");
    }

    [TestMethod]
    public async Task MoveType_MissingDocument_ReturnsSafeRecoveryMessage()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var missingPath = workspace.GetPath("SampleLib", "private-missing-source.cs");

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                workspace.WorkspaceId, missingPath, "Unused", null, CancellationToken.None));

        AssertPublicRefusal(ex, "Source document was not found in the workspace. Use workspace_list and document lookup tools to select a loaded C# source file.");
    }

    [TestMethod]
    public async Task MoveType_ExistingTarget_ReturnsSafeRecoveryMessage()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(sourcePath, "\npublic class TargetCollisionKitten {}\n");
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                workspaceId, sourcePath, "TargetCollisionKitten",
                workspace.GetPath("SampleLib", "Dog.cs"), CancellationToken.None));

        AssertPublicRefusal(ex, "Target file already exists in the workspace. Choose a different targetFilePath and retry.");
    }

    private static void AssertPublicRefusal(PublicInvalidOperationException exception, string expectedMessage)
    {
        Assert.AreEqual(expectedMessage, exception.Message);
        using var envelope = JsonDocument.Parse(
            ToolErrorHandler.ClassifyAndFormat(exception, "move_type_to_file_preview"));
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual(expectedMessage, envelope.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task MoveType_NewFile_HasNoLeadingBlankLine()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var preview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);

        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);

        var newFilePath = workspace.GetPath("SampleLib", "Kitten.cs");
        Assert.IsTrue(File.Exists(newFilePath), "Kitten.cs should have been created.");

        var content = await File.ReadAllTextAsync(newFilePath, CancellationToken.None);
        var normalized = content.Replace("\r\n", "\n");

        Assert.IsFalse(normalized.StartsWith('\n'),
            $"New file must not start with a blank line. Actual head: {normalized[..Math.Min(80, normalized.Length)]}");
    }

    [TestMethod]
    public async Task MoveType_NewFile_NoStrayBlankLineRuns()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var preview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);

        var content = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "Kitten.cs"), CancellationToken.None);
        var normalized = content.Replace("\r\n", "\n");

        // Two blank lines in a row would manifest as three consecutive newlines.
        Assert.IsFalse(normalized.Contains("\n\n\n", StringComparison.Ordinal),
            "New file should not contain runs of 2+ blank lines (three consecutive newlines).");
    }
}
