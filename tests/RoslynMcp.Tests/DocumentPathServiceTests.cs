using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DocumentPathServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BuildCompletion_EnrichesDistinctPhysicalFilesWithTheirOwnEndSpans(bool normalizedFallback)
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Distinct case-sensitive physical files require Unix."); return; }
        using var workspace = new AdhocWorkspace();
        var (solution, upper, lower) = CreateSolution(workspace);
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var warnings = compilation.GetDiagnostics().Where(item => item.Id == "CS0168").ToArray();
        Assert.AreEqual(2, warnings.Length);
        var first = warnings.Single(item => item.Location.SourceTree!.FilePath == upper.FilePath).Location.GetLineSpan();
        var second = warnings.Single(item => item.Location.SourceTree!.FilePath == lower.FilePath).Location.GetLineSpan();
        Assert.AreEqual(first.StartLinePosition, second.StartLinePosition);
        Assert.AreNotEqual(first.EndLinePosition, second.EndLinePosition, "Each physical file must require a different enrichment result.");
        string PrintedPath(string path) => normalizedFallback
            ? Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)) : path;
        var output = string.Join(Environment.NewLine, warnings.Select(item =>
        {
            var span = item.Location.GetLineSpan();
            return $"{PrintedPath(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): warning CS0168: unused local";
        }));
        var manager = new FailClosedWorkspaceManagerStub
        {
            GetCurrentSolutionHandler = _ => solution,
            GetCurrentVersionHandler = _ => 1
        };
        using var cache = new CompilationCache(manager);
        var service = new BuildService(manager, null!, cache, NullLogger<BuildService>.Instance);
        var target = Path.Combine(Path.GetTempPath(), "document-path-service", "Project.csproj");
        var plan = new BuildCommandPlan("paths", target, ["build", target], 1);
        var execution = new CommandExecutionDto("dotnet", plan.Arguments, Path.GetDirectoryName(target)!, target,
            0, true, 0, output, "");
        var result = await service.CompleteBuildAsync(new BuildCommandRun(plan, execution, 0), CancellationToken.None);
        Assert.AreEqual(2, result.Diagnostics.Count);
        foreach (var expected in warnings)
        {
            var span = expected.Location.GetLineSpan();
            var actual = result.Diagnostics.Single(item => Path.GetFullPath(item.FilePath!) == span.Path);
            Assert.AreEqual(span.EndLinePosition.Line + 1, actual.EndLine);
            Assert.AreEqual(span.EndLinePosition.Character + 1, actual.EndColumn);
        }
    }

    [TestMethod]
    public async Task DetachedSourceTreeCodeFix_UsesTheExactPhysicalDocument()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Distinct case-sensitive physical files require Unix."); return; }
        using var workspace = new AdhocWorkspace();
        var (solution, _, lower) = CreateSolution(workspace);
        var descriptor = new DiagnosticDescriptor("TEST001", "Test", "Test", "Test", DiagnosticSeverity.Warning, true);
        var detachedTree = CSharpSyntaxTree.ParseText(await lower.GetTextAsync(), path: lower.FilePath!);
        var diagnostic = Diagnostic.Create(descriptor, Location.Create(detachedTree, new TextSpan(0, 1)));
        Assert.IsNull(solution.GetDocument(detachedTree), "Exercise the path fallback rather than source-tree identity.");
        var provider = new RecordingProvider();
        var service = new SupportedFixEnumerationService(new Registry(provider), NullLogger.Instance, null);
        var result = await service.EnumerateAsync(diagnostic, solution, lower.FilePath!, CancellationToken.None);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(1, result.Fixes.Count);
        Assert.AreEqual(lower.Id, provider.DocumentId);
        Assert.AreEqual("Fix lower", result.Fixes[0].Title);
    }

    private static (Solution Solution, Document Upper, Document Lower) CreateSolution(AdhocWorkspace workspace)
    {
        var project = ProjectId.CreateNewId();
        var upper = DocumentId.CreateNewId(project);
        var lower = DocumentId.CreateNewId(project);
        var directory = Path.Combine(Path.GetTempPath(), "document-path-service");
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Project", "Project", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(upper, "upper", SourceText.From("class A { void M() { int unused; } }"), filePath: Path.Combine(directory, "Probe.cs"))
            .AddDocument(lower, "lower", SourceText.From("class B { void M() { int muchLongerUnused; } }"), filePath: Path.Combine(directory, "probe.cs"));
        return (solution, solution.GetDocument(upper)!, solution.GetDocument(lower)!);
    }

    private sealed class Registry(CodeFixProvider provider) : ICodeFixProviderRegistry
    {
        public IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? solution = null) => [provider];
        public CodeFixProvider? FirstProviderFor(string diagnosticId, Solution? solution = null) => provider;
    }

    private sealed class RecordingProvider : CodeFixProvider
    {
        internal DocumentId? DocumentId { get; private set; }
        public override ImmutableArray<string> FixableDiagnosticIds => ["TEST001"];
        public override FixAllProvider? GetFixAllProvider() => null;
        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            DocumentId = context.Document.Id;
            context.RegisterCodeFix(CodeAction.Create("Fix " + context.Document.Name,
                _ => Task.FromResult(context.Document), "path-fix"), context.Diagnostics[0]);
            return Task.CompletedTask;
        }
    }
}
