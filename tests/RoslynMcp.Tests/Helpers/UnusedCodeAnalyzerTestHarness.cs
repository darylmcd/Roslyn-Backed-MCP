using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Owns the workspace and cache for a single-document unused-code analysis fixture.</summary>
internal sealed class UnusedCodeAnalyzerTestHarness : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly CompilationCache _cache;

    public FailClosedWorkspaceManagerStub WorkspaceManager { get; }
    public UnusedCodeAnalyzer Analyzer { get; }

    public UnusedCodeAnalyzerTestHarness(
        string workspaceId,
        AdhocWorkspace workspace,
        Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>>? referenceFinder = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspace = workspace;
        CompilationCache? cache = null;
        try
        {
            WorkspaceManager = new FailClosedWorkspaceManagerStub
            {
                GetCurrentSolutionHandler = id => id == workspaceId
                    ? workspace.CurrentSolution
                    : throw new InvalidOperationException($"Unknown workspace {id}"),
                GetCurrentVersionHandler = _ => 1,
            };
            cache = new CompilationCache(WorkspaceManager);
            _cache = cache;
            Analyzer = referenceFinder is null
                ? new UnusedCodeAnalyzer(WorkspaceManager, _cache, NullLogger<UnusedCodeAnalyzer>.Instance, exceptionReporter)
                : new UnusedCodeAnalyzer(WorkspaceManager, _cache, NullLogger<UnusedCodeAnalyzer>.Instance, referenceFinder, exceptionReporter);
        }
        catch (Exception setupFailure)
        {
            DisposeResources(setupFailure, cache, workspace);
            throw;
        }
    }

    public static AdhocWorkspace CreateWorkspace(string source, IEnumerable<MetadataReference> metadataReferences)
    {
        var workspace = new AdhocWorkspace();
        try
        {
            var projectId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name: "TestAsm",
                assemblyName: "TestAsm",
                language: LanguageNames.CSharp,
                filePath: Path.Combine(TestTempRoot.Current, "TestAsm.csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: metadataReferences));
            var fullPath = Path.Combine(TestTempRoot.Current, "Sample.cs");
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                "Sample.cs",
                filePath: fullPath,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create(), fullPath))));
            return workspace;
        }
        catch (Exception setupFailure)
        {
            DisposeResources(setupFailure, workspace);
            throw;
        }
    }

    public void Dispose() => DisposeResources(null, _cache, _workspace);

    private static void DisposeResources(Exception? primaryFailure, params IDisposable?[] resources)
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                (failures ??= []).Add(cleanupFailure);
            }
        }
        if (failures is not null)
        {
            if (primaryFailure is not null)
                failures.Insert(0, primaryFailure);
            throw new AggregateException("Analyzer fixture cleanup failed.", failures);
        }
    }
}
