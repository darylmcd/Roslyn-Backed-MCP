using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FixAllEquivalenceKeyIntegrationTests
{
    [TestMethod]
    [DataRow("document", "key")]
    [DataRow("project", "key")]
    [DataRow("solution", "key")]
    [DataRow("document", null)]
    [DataRow("project", null)]
    [DataRow("solution", null)]
    public async Task BatchFixer_UsesActualKeyAndVerifiesReduction(string scope, string? key)
    {
        using var fixture = new Fixture();
        var provider = new UnusedVariableProvider(key, mixedKeys: key is not null);
        var service = fixture.CreateService(provider);
        foreach (var project in fixture.Solution.Projects)
            _ = await fixture.Cache.GetCompilationAsync("fixture", project, CancellationToken.None);

        var preview = await service.PreviewFixAllAsync("fixture", "CS0168", scope,
            fixture.FirstDocument.FilePath, fixture.FirstDocument.Project.Name, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken), preview.GuidanceMessage);
        var expected = key is null ? (scope == "solution" ? 6 : scope == "project" ? 4 : 2)
            : scope == "solution" ? 3 : scope == "project" ? 2 : 1;
        Assert.AreEqual(expected, preview.FixedCount);
        Assert.AreEqual(PreviewKind.FixAll, fixture.Store.PeekKind(preview.PreviewToken));
        Assert.IsNotNull(fixture.Store.PeekChangedPaths(preview.PreviewToken));
        var entry = fixture.Store.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(entry);
        StringAssert.Contains(entry.Value.Description, $"{expected} occurrences");
        var actualBefore = await CountAsync(fixture.Solution);
        var actualAfter = await CountAsync(entry.Value.ModifiedSolution);
        Assert.AreEqual(expected, actualBefore - actualAfter);
        var firstText = (await entry.Value.ModifiedSolution.GetDocument(fixture.FirstDocument.Id)!.GetTextAsync()).ToString();
        Assert.DoesNotContain("int first;", firstText);
        if (key is not null)
        {
            StringAssert.Contains(firstText, "int other;");
            Assert.IsNotNull(preview.GuidanceMessage);
            StringAssert.Contains(preview.GuidanceMessage, "code_fix_preview");
        }
        else
            Assert.DoesNotContain("int other;", firstText);
        Assert.AreSame(fixture.Solution, fixture.Workspace.CurrentSolution);
        Assert.IsNotNull(fixture.Store.Retrieve(preview.PreviewToken));
        var cached = await fixture.Cache.GetCompilationAsync("fixture", fixture.FirstDocument.Project, CancellationToken.None);
        Assert.IsNotNull(cached);
        Assert.HasCount(4, cached.GetDiagnostics().Where(d => d.Id == "CS0168").ToArray());
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("noop")]
    [DataRow("no-reduction")]
    [DataRow("zero-reduction")]
    [DataRow("unchanged")]
    [DataRow("missing-document")]
    [DataRow("missing-other-document")]
    [DataRow("missing-other-project")]
    [DataRow("missing-project")]
    public async Task IneffectiveOrUnverifiableAction_DoesNotStorePreview(string behavior)
    {
        using var fixture = new Fixture();
        var service = fixture.CreateService(new UnusedVariableProvider("key", behavior: behavior));
        var preview = await service.PreviewFixAllAsync("fixture", "CS0168",
            behavior == "missing-other-project" ? "solution" : "project",
            null, fixture.FirstDocument.Project.Name, CancellationToken.None);
        Assert.AreEqual(0, preview.FixedCount);
        Assert.AreEqual(string.Empty, preview.PreviewToken);
        Assert.HasCount(0, preview.Changes);
        Assert.IsNotNull(preview.GuidanceMessage);
        StringAssert.Contains(preview.GuidanceMessage, "code_fix_preview");
        Assert.AreSame(fixture.Solution, fixture.Workspace.CurrentSolution);
    }

    [TestMethod]
    public async Task Cancellation_DoesNotProduceToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = fixture.CreateService(new UnusedVariableProvider("key"));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.PreviewFixAllAsync(
            "fixture", "CS0168", "solution", null, null, cancellation.Token));
    }

    [TestMethod]
    public async Task MissingInitialCompilation_FailsClosed()
    {
        using var fixture = new Fixture();
        var manager = new FailClosedWorkspaceManagerStub
        {
            GetCurrentSolutionHandler = _ => fixture.Solution,
            GetCurrentVersionHandler = _ => 1,
        };
        using var cache = new CompilationCache(manager, _ => Task.FromResult<Compilation?>(null), null);
        var service = new FixAllService(manager, fixture.Store, cache, null!,
            NullLogger<FixAllService>.Instance, [new UnusedVariableProvider("key")]);
        var preview = await service.PreviewFixAllAsync("fixture", "CS0168", "solution", null, null, CancellationToken.None);
        Assert.AreEqual(0, preview.FixedCount);
        Assert.AreEqual(string.Empty, preview.PreviewToken);
        StringAssert.Contains(preview.GuidanceMessage!, "compilation is unavailable");
    }

    [TestMethod]
    public async Task CandidateCollection_MissingOriginalCompilationIsUnavailable()
    {
        using var fixture = new Fixture();
        var solution = fixture.Solution.AddProject(ProjectId.CreateNewId(), "Unsupported", "Unsupported", "NoCompilation");
        var collector = new FixAllDiagnosticCollector(fixture.Cache);
        var context = new FixAllDiagnosticCollectionContext("fixture", solution, "CS0168",
            FixAllScope.Solution, fixture.FirstDocument, fixture.FirstDocument.Project, []);
        var result = await collector.CollectCandidateAsync(context, solution, CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task CandidateCollection_UsesOriginalProjectIdsAndCountsAddedScopedDocuments()
    {
        using var fixture = new Fixture();
        var collector = new FixAllDiagnosticCollector(fixture.Cache);
        var context = new FixAllDiagnosticCollectionContext("fixture", fixture.Solution, "CS0168",
            FixAllScope.Solution, fixture.FirstDocument, fixture.FirstDocument.Project, []);
        var candidate = fixture.Solution.AddProject(ProjectId.CreateNewId(), "Unsupported", "Unsupported", "NoCompilation")
            .AddDocument(DocumentId.CreateNewId(fixture.FirstDocument.Project.Id), "Added.cs",
                SourceText.From("class Added { void M() { int extra; } }"));
        var result = await collector.CollectCandidateAsync(context, candidate, CancellationToken.None);
        Assert.IsNotNull(result);
        Assert.AreEqual(7, result.Values.Sum(items => items.Length));
    }

    [TestMethod]
    public async Task CandidateCollection_HonorsCancellation()
    {
        using var fixture = new Fixture();
        var collector = new FixAllDiagnosticCollector(fixture.Cache);
        var context = new FixAllDiagnosticCollectionContext("fixture", fixture.Solution,
            "CS0168", FixAllScope.Solution, fixture.FirstDocument, fixture.FirstDocument.Project, []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            collector.CollectCandidateAsync(context, fixture.Solution, cancellation.Token));
    }

    private static async Task<int> CountAsync(Solution solution)
    {
        var count = 0;
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            count += compilation.GetDiagnostics().Count(d => d.Id == "CS0168");
        }
        return count;
    }

    private sealed class Fixture : IDisposable
    {
        public AdhocWorkspace Workspace { get; } = new(MefHostServices.Create(
            MefHostServices.DefaultAssemblies.Add(typeof(DiagnosticQueryServiceRegressionTests.NoCompilationLanguageService).Assembly)));
        public Solution Solution => Workspace.CurrentSolution;
        public PreviewStore Store { get; } = new();
        public CompilationCache Cache { get; }
        public Document FirstDocument => Solution.Projects.First().Documents.First();
        private readonly FailClosedWorkspaceManagerStub _manager;

        public Fixture()
        {
            var solution = Workspace.CurrentSolution;
            for (var projectIndex = 0; projectIndex < 2; projectIndex++)
            {
                var projectId = ProjectId.CreateNewId();
                solution = solution.AddProject(projectId, $"P{projectIndex}", $"P{projectIndex}", LanguageNames.CSharp)
                    .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                var documentCount = projectIndex == 0 ? 2 : 1;
                for (var index = 0; index < documentCount; index++)
                {
                    var name = $"D{projectIndex}{index}";
                    solution = solution.AddDocument(DocumentId.CreateNewId(projectId), name + ".cs",
                        SourceText.From($"class {name} {{ void M() {{ int first; int other; }} }}"),
                        filePath: Path.Combine(Path.GetTempPath(), "FixAllFixture", name + ".cs"));
                }
            }
            Assert.IsTrue(Workspace.TryApplyChanges(solution));
            _manager = new FailClosedWorkspaceManagerStub
            {
                GetCurrentSolutionHandler = _ => Solution,
                GetCurrentVersionHandler = _ => 1,
            };
            Cache = new CompilationCache(_manager);
        }

        public FixAllService CreateService(CodeFixProvider provider) => new(
            _manager, Store, Cache, null!, NullLogger<FixAllService>.Instance, [provider]);

        public void Dispose()
        {
            Cache.Dispose();
            Workspace.Dispose();
        }
    }

    private sealed class UnusedVariableProvider(string? key, bool mixedKeys = false, string behavior = "fix") : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0168"];
        public override FixAllProvider GetFixAllProvider() => behavior is "no-reduction" or "zero-reduction" or "unchanged" or "missing-project" or "missing-document" or "missing-other-document" or "missing-other-project"
            ? new IneffectiveFixAllProvider(behavior) : WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            if (behavior == "none") return;
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            Assert.IsNotNull(root);
            var variable = root.FindNode(context.Span).FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            Assert.IsNotNull(variable);
            var selectedKey = mixedKeys && variable.Identifier.ValueText == "other" ? "other-key" : key;
            context.RegisterCodeFix(CodeAction.Create("Remove unused variable", _ =>
                Task.FromResult(behavior == "noop" ? context.Document
                    : context.Document.WithSyntaxRoot(root.RemoveNode(variable.Parent!.Parent!, SyntaxRemoveOptions.KeepNoTrivia)!)),
                selectedKey), context.Diagnostics);
        }
    }

    private sealed class IneffectiveFixAllProvider(string behavior) : FixAllProvider
    {
        public override Task<CodeAction?> GetFixAsync(FixAllContext context)
        {
            CodeAction action = CodeAction.Create("Ineffective", _ =>
            {
                var candidate = behavior switch
                {
                    "missing-project" => context.Solution.RemoveProject(context.Project.Id),
                    "missing-document" => context.Solution.RemoveDocument(context.Document!.Id),
                    "missing-other-document" => context.Solution.RemoveDocument(context.Project.Documents.Last().Id),
                    "missing-other-project" => context.Solution.RemoveProject(context.Solution.ProjectIds.Last()),
                    "unchanged" => context.Solution,
                    _ => context.Solution.WithDocumentText(context.Document!.Id,
                        SourceText.From(behavior == "zero-reduction"
                            ? "class Changed { void M() { int first; int other; } }"
                            : "class Changed { void M() { int first; int other; int extra; } }")),
                };
                return Task.FromResult(candidate);
            }, context.CodeActionEquivalenceKey);
            return Task.FromResult<CodeAction?>(action);
        }
    }
}
