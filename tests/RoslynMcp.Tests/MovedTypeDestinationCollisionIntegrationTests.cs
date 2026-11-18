using System.Collections.Immutable;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MovedTypeDestinationCollisionIntegrationTests : IsolatedWorkspaceTestBase
{
    private const string TypeName = "MoveProbe";
    private const string SourceFile = "MoveProbe.cs";
    private const string ReceiverFile = "Existing.cs";
    private const string CollisionMessage =
        "The destination already contains a type with the same namespace, name, and arity. Choose a different destination.";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("Original", "Destination", "Destination", "MoveProbe", "MoveProbe", "")]
    [DataRow("Original", "Destination.event", "Destination.@event", "class", "@class", "<T>")]
    [DataRow(null, null, null, "MoveProbe", "MoveProbe", "")]
    public async Task MoveToProject_RefusesExistingReceiverType(
        string? sourceNamespace, string? requestedNamespace, string? receiverNamespace,
        string name, string identifier, string typeParameters)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", SourceFile);
        await File.WriteAllTextAsync(sourcePath,
            InNamespace(sourceNamespace, $"public partial class {identifier}{typeParameters} {{ public int Moved => 1; }}"));
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", ReceiverFile),
            InNamespace(receiverNamespace, $"public partial class {identifier}{typeParameters} {{ public int Existing => 2; }}"));
        await workspace.LoadAsync();

        var store = new PreviewStore();
        using var cache = new CompilationCache(WorkspaceManager);
        var service = new CrossProjectRefactoringService(WorkspaceManager, store, cache);
        await AssertRefusesCollisionAsync(workspace, store, workspace.GetPath("Contracts", SourceFile),
            "move_type_to_project_preview", () => CrossProjectRefactoringTools.PreviewMoveTypeToProject(
                WorkspaceExecutionGate, service, workspace.WorkspaceId, sourcePath, name, "Contracts",
                requestedNamespace, preserveNamespace: sourceNamespace is null));
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    public async Task ChangeNamespace_RefusesExistingTypeInTheProjectedNamespace(bool hasSibling, bool moveFile, bool nested)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source = "public partial class MoveProbe { public int Moved => 1; }";
        var members = hasSibling ? source + " internal class Sibling { }" : source;
        var sourceCode = nested ? $"namespace Outer {{ namespace Original {{ {members} }} }}" : InNamespace("Original", members);
        // The destination is the FULL namespace requested, nested source or not.
        const string receiverNamespace = "Destination";
        var sourcePath = workspace.GetPath("SampleLib", SourceFile);
        var destinationPath = workspace.GetPath("SampleLib", "Relocated.cs");
        await File.WriteAllTextAsync(sourcePath, sourceCode);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", ReceiverFile),
            InNamespace(receiverNamespace, "public partial class MoveProbe { public int Existing => 2; }"));
        await workspace.LoadAsync();

        var store = new PreviewStore();
        var service = new NamespaceRelocationService(WorkspaceManager, store, NullLogger<NamespaceRelocationService>.Instance);
        await AssertRefusesCollisionAsync(workspace, store, destinationPath, "change_type_namespace_preview",
            () => TypeMoveTools.PreviewChangeTypeNamespace(WorkspaceExecutionGate, service, workspace.WorkspaceId,
                TypeName, nested ? "Outer.Original" : "Original", "Destination", moveFile ? destinationPath : null));
    }

    [TestMethod]
    [DataRow("namespace Destination; public partial class MoveProbe<T> { }", false)]
    [DataRow("namespace Elsewhere; public partial class MoveProbe { }", false)]
    [DataRow("namespace Destination; file partial class MoveProbe { }", false)]
    [DataRow("namespace Destination; public partial class MoveProbe { }", true)]
    public async Task MoveToProject_AllowsDifferentReceiverIdentity(string existing, bool inReference)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        if (inReference)
        {
            AddProjectToCopiedSolution(workspace.RootPath, "References", "net10.0");
            var contractsPath = workspace.GetPath("Contracts", "Contracts.csproj");
            var contracts = XElement.Load(contractsPath);
            contracts.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                new XAttribute("Include", "../References/References.csproj"))));
            contracts.Save(contractsPath);
        }

        var sourcePath = workspace.GetPath("SampleLib", SourceFile);
        await File.WriteAllTextAsync(sourcePath, "namespace Original; public partial class MoveProbe { public int Moved => 1; }");
        await File.WriteAllTextAsync(workspace.GetPath(inReference ? "References" : "Contracts", ReceiverFile), existing);
        await workspace.LoadAsync();

        var store = new PreviewStore();
        using var cache = new CompilationCache(WorkspaceManager);
        var service = new CrossProjectRefactoringService(WorkspaceManager, store, cache);
        var json = await CrossProjectRefactoringTools.PreviewMoveTypeToProject(WorkspaceExecutionGate, service,
            workspace.WorkspaceId, sourcePath, TypeName, "Contracts", "Destination");
        await AssertSingleMovedDeclarationAsync(store, json, "Contracts", "Destination.MoveProbe");
    }

    [TestMethod]
    [DataRow(ReceiverBehavior.Fallback, false)]
    [DataRow(ReceiverBehavior.Fallback, true)]
    [DataRow(ReceiverBehavior.Augmentation, false)]
    [DataRow(ReceiverBehavior.Persistent, false)]
    [DataRow(ReceiverBehavior.RenamedPersistent, false)]
    public async Task MovePreview_RefusesOnlyPersistentGeneratedReceiverTypes(ReceiverBehavior behavior, bool relocate)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var targetProjectName = relocate ? "SampleLib" : "Contracts";
        if (relocate)
        {
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Consumer.cs"), """
                using Original;
                namespace Client;
                public class Consumer { public MoveProbe Create() => new(); }
                """);
        }
        else
        {
            AddProjectToCopiedSolution(workspace.RootPath, targetProjectName, "net10.0");
        }
        var sourcePath = workspace.GetPath("SampleLib", SourceFile);
        await File.WriteAllTextAsync(sourcePath, "namespace Original; public partial class MoveProbe { public int Moved => 1; }");
        await workspace.LoadAsync();

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var target = solution.Projects.Single(project => project.Name == targetProjectName)
            .AddAnalyzerReference(new ReceiverGeneratorReference(behavior));
        var compilation = await target.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var before = compilation.Assembly.GetTypeByMetadataName("Destination.MoveProbe");
        var augment = behavior == ReceiverBehavior.Augmentation;
        if (augment)
        {
            Assert.IsNull(before);
        }
        else
        {
            Assert.IsNotNull(before);
            var beforeReference = before.DeclaringSyntaxReferences.Single();
            Assert.IsInstanceOfType<SourceGeneratedDocument>(target.Solution.GetDocument(beforeReference.SyntaxTree));
            Assert.HasCount(1, before.GetMembers("Fallback"));
        }

        var manager = new GeneratorWorkspace(WorkspaceManager, target.Solution);
        var store = new PreviewStore();
        using var cache = new CompilationCache(manager);
        var service = new CrossProjectRefactoringService(manager, store, cache);
        var relocation = new NamespaceRelocationService(manager, store, NullLogger<NamespaceRelocationService>.Instance);
        Task<string> Preview() => relocate
            ? TypeMoveTools.PreviewChangeTypeNamespace(WorkspaceExecutionGate, relocation,
                workspace.WorkspaceId, TypeName, "Original", "Destination")
            : CrossProjectRefactoringTools.PreviewMoveTypeToProject(WorkspaceExecutionGate, service,
                workspace.WorkspaceId, sourcePath, TypeName, targetProjectName, "Destination");

        if (behavior is ReceiverBehavior.Persistent or ReceiverBehavior.RenamedPersistent)
        {
            await AssertRefusesCollisionAsync(workspace, store, workspace.GetPath("Contracts", SourceFile),
                "move_type_to_project_preview", Preview);
            return;
        }

        using var envelope = JsonDocument.Parse(await Preview());
        var token = envelope.RootElement.GetProperty("previewToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        var pending = entry.Value.ModifiedSolution;
        var updatedTarget = pending.GetProject(target.Id);
        Assert.IsNotNull(updatedTarget);
        var updatedCompilation = await updatedTarget.GetCompilationAsync();
        Assert.IsNotNull(updatedCompilation);
        var after = updatedCompilation.Assembly.GetTypeByMetadataName("Destination.MoveProbe");
        Assert.IsNotNull(after);
        Assert.HasCount(augment ? 2 : 1, after.DeclaringSyntaxReferences);
        Assert.HasCount(1, after.GetMembers("Moved"));
        Assert.HasCount(augment ? 1 : 0, after.GetMembers("Augmentation"));
        Assert.IsEmpty(after.GetMembers("Fallback"));
        await AssertCompilesAsync(pending);
    }

    [TestMethod]
    public async Task DestinationIdentity_DoesNotReportCollisionForAnInactiveDeclaration()
    {
        using var workspace = new AdhocWorkspace();
        var parseOptions = CSharpParseOptions.Default;
        var target = workspace.AddProject("Contracts", LanguageNames.CSharp).WithParseOptions(parseOptions)
            .AddDocument(ReceiverFile, "namespace Destination; public partial class MoveProbe { }").Project;
        const string projectedText = """
            namespace Destination;
            #if MOVE_PROBE_ACTIVE
            public partial class MoveProbe { public int Moved => 1; }
            #endif
            """;
        var projectedRoot = SyntaxFactory.ParseCompilationUnit(projectedText, options: parseOptions.WithPreprocessorSymbols("MOVE_PROBE_ACTIVE"));
        var declaration = projectedRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        var projected = target.AddDocument(SourceFile, projectedText);
        var receivingRoot = await projected.GetSyntaxRootAsync();
        Assert.IsNotNull(receivingRoot);
        Assert.IsEmpty(receivingRoot.DescendantNodes().OfType<TypeDeclarationSyntax>());
        var compilation = await target.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.Assembly.GetTypeByMetadataName("Destination.MoveProbe"));

        await MovedTypeDestinationIdentity.EnsureAvailableAsync(target, projected, declaration.Identifier.Span,
            projected.Project.Solution, originalDeclarationInTargetProject: null, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("Original", "@Moved", "Moved", "namespace Elsewhere; public partial class MoveProbe { }")]
    [DataRow("Outer.Original", "Destination", "Destination", "namespace Outer.Destination; public partial class MoveProbe { }")]
    [DataRow("Original", "Destination", "Destination", "namespace Destination; public partial class MoveProbe<T> { }")]
    public async Task ChangeNamespace_AllowsAnUnoccupiedIdentity(
        string fromNamespace, string toNamespace, string projectedNamespace, string existing)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source = "public partial class MoveProbe { public int Moved => 1; }";
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", SourceFile), fromNamespace == "Outer.Original"
            ? $"namespace Outer {{ namespace Original {{ {source} }} }}"
            : InNamespace(fromNamespace, source));
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", ReceiverFile), existing);
        await workspace.LoadAsync();

        var store = new PreviewStore();
        var service = new NamespaceRelocationService(WorkspaceManager, store, NullLogger<NamespaceRelocationService>.Instance);
        var json = await TypeMoveTools.PreviewChangeTypeNamespace(WorkspaceExecutionGate, service,
            workspace.WorkspaceId, TypeName, fromNamespace, toNamespace);
        await AssertSingleMovedDeclarationAsync(store, json, "SampleLib", projectedNamespace + ".MoveProbe");
    }

    private static string InNamespace(string? namespaceName, string member) =>
        namespaceName is null ? member : $"namespace {namespaceName}; {member}";

    private static async Task AssertRefusesCollisionAsync(IsolatedWorkspaceScope workspace, PreviewStore store,
        string newPath, string toolName, Func<Task<string>> preview)
    {
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(originalSolution);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var files = Snapshot(workspace.RootPath);

        var exception = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(preview);
        using var envelope = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, toolName));
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual(CollisionMessage, envelope.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(envelope.RootElement.TryGetProperty("previewToken", out _));
        Assert.AreEqual(0, StoreCount(store));
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(newPath));
    }

    private static async Task AssertSingleMovedDeclarationAsync(PreviewStore store, string json, string projectName,
        string metadataName)
    {
        using var envelope = JsonDocument.Parse(json);
        var token = envelope.RootElement.GetProperty("previewToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        var pending = await ReparseSerializedDocumentsAsync(entry.Value.ModifiedSolution);
        var compilation = await pending.Projects.Single(project => project.Name == projectName).GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var moved = compilation.Assembly.GetTypeByMetadataName(metadataName);
        Assert.IsNotNull(moved);
        Assert.HasCount(1, moved.DeclaringSyntaxReferences);
        Assert.HasCount(1, moved.GetMembers("Moved"));
        await AssertCompilesAsync(pending);
    }

    private sealed class GeneratorWorkspace(IWorkspaceManager inner, Solution solution) : DelegatingWorkspaceManager(inner)
    {
        public override Solution GetCurrentSolution(string workspaceId) => solution;
    }

    public enum ReceiverBehavior
    {
        Fallback,
        Augmentation,
        Persistent,
        RenamedPersistent,
    }

    private sealed class ReceiverGeneratorReference(ReceiverBehavior behavior) : AnalyzerReference
    {
        private readonly ISourceGenerator _generator = new ReceiverGenerator(behavior).AsSourceGenerator();

        public override string FullPath => string.Empty;
        public override object Id => typeof(ReceiverGeneratorReference);
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) =>
            language == LanguageNames.CSharp ? [_generator] : [];
    }

    private sealed class ReceiverGenerator(ReceiverBehavior behavior) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, (output, compilation) =>
            {
                var moved = compilation.Assembly.GetTypeByMetadataName("Destination.MoveProbe") is not null;
                if (behavior == ReceiverBehavior.Augmentation)
                {
                    if (moved)
                        output.AddSource("Augmentation.g.cs",
                            "namespace Destination; public partial class MoveProbe { public int Augmentation => 3; }");
                    return;
                }

                var usesOriginal = compilation.SyntaxTrees.Any(tree => tree.GetRoot(output.CancellationToken)
                    is CompilationUnitSyntax root && root.Usings.Any(import => import.Name?.ToString() == "Original"));
                if (behavior != ReceiverBehavior.Fallback || usesOriginal || !moved)
                {
                    var hint = behavior == ReceiverBehavior.RenamedPersistent && moved ? "RenamedFallback.g.cs" : "Fallback.g.cs";
                    output.AddSource(hint,
                        "namespace Destination; public partial class MoveProbe { public int Fallback => 2; }");
                }
            });
        }
    }
}
