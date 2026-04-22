using System.Net.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="UnusedCodeAnalyzer.FindDuplicateHelpersAsync"/>. Uses an
/// in-memory <see cref="AdhocWorkspace"/> seeded with the BCL reference set so that
/// semantic-model binding reaches <c>System.String</c>, <c>System.ArgumentException</c>,
/// etc. — the detector needs to see that a delegation target lives in a non-source
/// assembly to flag a helper as a library reinvention.
/// </summary>
[TestClass]
public sealed class DuplicateHelperDetectionTests
{
    private const string WorkspaceId = "dup-helper-test-ws";

    [TestMethod]
    public async Task FindDuplicateHelpers_ExpressionBodiedDelegateToBcl_IsDetected()
    {
        // The canonical shape from the initiative: an internal static class whose only
        // method is an expression-bodied forwarder into a BCL primitive.
        const string source = """
            namespace Sample;
            internal static class StringHelper
            {
                public static bool IsNullOrWhiteSpace(string s) => string.IsNullOrWhiteSpace(s);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count, "Expected a single hit for the BCL-forwarding helper.");
        var hit = hits[0];
        Assert.AreEqual("IsNullOrWhiteSpace", hit.SymbolName);
        Assert.AreEqual("StringHelper", hit.ContainingType);
        Assert.IsTrue(
            hit.CanonicalTarget.Contains("IsNullOrWhiteSpace", StringComparison.Ordinal),
            $"Canonical target should point at string.IsNullOrWhiteSpace; got '{hit.CanonicalTarget}'.");
        // BCL symbols live in System.Private.CoreLib on .NET Core / modern runtimes; the
        // assembly name is stable across test hosts. Loosely assert it's non-empty and
        // not the current (test) assembly.
        Assert.IsFalse(string.IsNullOrEmpty(hit.CanonicalTargetAssembly));
        Assert.AreNotEqual("TestAsm", hit.CanonicalTargetAssembly);
        Assert.AreEqual("high", hit.Confidence, "Expression-bodied single delegation is a high-confidence hit.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_DomainSpecificWrapper_IsNotDetected()
    {
        // The negative half of the validation fixture: a helper that does real work
        // (composition, formatting) rather than a pure library-primitive re-wrap. The
        // body is larger than 2 statements and its final statement is a .NET method
        // call — but the body as a whole is NOT a re-wrap.
        const string source = """
            namespace Sample;
            internal static class StringHelper
            {
                public static string NormalizeForMyDomain(string s)
                {
                    var trimmed = s.Trim();
                    var lowered = trimmed.ToLowerInvariant();
                    var prefixed = "my-" + lowered;
                    return prefixed.Replace(' ', '-');
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Multi-statement domain-specific wrappers must not be flagged as library reinventions. " +
            "Got: " + string.Join(", ", hits.Select(h => h.SymbolName)));
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_NullGuardPlusDelegation_IsMediumConfidence()
    {
        // Two-statement shape — first is a guard (not itself an invocation), second is
        // the delegation. Conservative detection: flagged, but with medium confidence
        // since the helper adds a caller-friendly NRE-avoidance layer on top of the
        // BCL primitive.
        const string source = """
            namespace Sample;
            internal static class StringHelper
            {
                public static bool IsNullOrWhiteSpace(string s)
                {
                    if (s is null) return true;
                    return string.IsNullOrWhiteSpace(s);
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("medium", hits[0].Confidence, "Two-statement guard + delegation is medium confidence.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_PublicHelperOnPublicStaticClass_IsNotDetected()
    {
        // Effective accessibility = Public (method public on public static class). That's
        // a library-API surface, not a private reinvention — out of scope for this tool.
        const string source = """
            namespace Sample;
            public static class StringHelper
            {
                public static bool IsNullOrWhiteSpace(string s) => string.IsNullOrWhiteSpace(s);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Public helpers on public static classes are library API, not reinventions.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_HelperDelegatingToSameSolution_IsNotDetected()
    {
        // The delegation target must live in a non-source (referenced) assembly. A helper
        // whose body calls another symbol in the same solution is a legitimate internal
        // re-export, not a BCL/NuGet reinvention.
        const string source = """
            namespace Sample;
            internal static class Forwarder
            {
                public static int Double(int x) => InternalMath.Double(x);
            }
            internal static class InternalMath
            {
                public static int Double(int x) => x + x;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Helpers delegating to same-solution targets must not be flagged.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_HelperOnNonStaticClass_IsNotDetected()
    {
        // The detector gates on the containing type being `static class` — the idiomatic
        // helper shape. Methods on a regular (non-static) class are out of scope even if
        // they happen to be a single BCL delegation.
        const string source = """
            namespace Sample;
            internal class NotAHelper
            {
                public static bool IsNullOrWhiteSpace(string s) => string.IsNullOrWhiteSpace(s);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Only methods on static helper classes are in scope.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_ExtensionMethodForwardingToBcl_IsDetected()
    {
        // An extension method that forwards (on first-arg `this`) to the equivalent BCL
        // static. This is the most common "reinvented extension" shape.
        const string source = """
            namespace Sample;
            internal static class StringExtensions
            {
                public static bool IsBlank(this string s) => string.IsNullOrWhiteSpace(s);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("IsBlank", hits[0].SymbolName);
        Assert.AreEqual("StringExtensions", hits[0].ContainingType);
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_HttpClientForwarder_IsNotDetected_ByDefault()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;
            namespace Sample;
            internal static class IntegrationTestHttpExtensions
            {
                public static Task<HttpResponseMessage> GetRelativeAsync(HttpClient client, string uri) => client.GetAsync(uri);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(
            source,
            MetadataReference.CreateFromFile(typeof(HttpClient).Assembly.Location));

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "HttpClient delegation helpers should be filtered as framework glue by default.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_HttpClientForwarder_IsDetected_WhenExclusionOff()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;
            namespace Sample;
            internal static class IntegrationTestHttpExtensions
            {
                public static Task<HttpResponseMessage> GetRelativeAsync(HttpClient client, string uri) => client.GetAsync(uri);
            }
            """;

        var compileErrors = await GetCompilationErrorsAsync(
            source,
            MetadataReference.CreateFromFile(typeof(HttpClient).Assembly.Location));
        Assert.AreEqual(
            string.Empty,
            compileErrors,
            "In-memory fixture must compile so GetSymbolInfo binds HttpClient.GetAsync.");

        var analyzer = BuildAnalyzerWithSource(
            source,
            MetadataReference.CreateFromFile(typeof(HttpClient).Assembly.Location));

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions { ExcludeFrameworkWrappers = false },
            default);

        Assert.AreEqual(1, hits.Count, "Opt-out should surface System.Net.Http glue forwarders again.");
        Assert.IsTrue(
            hits[0].CanonicalTarget.Contains("GetAsync", StringComparison.Ordinal),
            $"Expected HttpClient.GetAsync; got '{hits[0].CanonicalTarget}'.");
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_LimitCapsResults()
    {
        // Confirm the `Limit` option clamps the result count even when more hits exist.
        const string source = """
            namespace Sample;
            internal static class StringHelper
            {
                public static bool IsBlankA(string s) => string.IsNullOrWhiteSpace(s);
                public static bool IsBlankB(string s) => string.IsNullOrWhiteSpace(s);
                public static bool IsBlankC(string s) => string.IsNullOrWhiteSpace(s);
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDuplicateHelpersAsync(
            WorkspaceId,
            new DuplicateHelperAnalysisOptions { Limit = 2 },
            default);

        Assert.AreEqual(2, hits.Count, "Result count must be capped by Limit.");
    }

    /// <summary>
    /// Builds an <see cref="UnusedCodeAnalyzer"/> against a single-file AdhocWorkspace
    /// with the BCL reference loaded. The semantic model must bind <c>string</c>-rooted
    /// calls to <see cref="System.String"/> in <c>System.Private.CoreLib</c> for the
    /// detector's non-source-assembly check to fire.
    /// </summary>
    private static UnusedCodeAnalyzer BuildAnalyzerWithSource(string source, params MetadataReference[] additionalMetadataReferences)
    {
        var workspace = CreateAdhocWorkspace(source, additionalMetadataReferences);
        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        var cache = new CompilationCache(wsManager);
        return new UnusedCodeAnalyzer(
            wsManager,
            cache,
            NullLogger<UnusedCodeAnalyzer>.Instance);
    }

    private static AdhocWorkspace CreateAdhocWorkspace(string source, params MetadataReference[] additionalMetadataReferences)
    {
        var metadataRefs = new List<MetadataReference>(1 + additionalMetadataReferences.Length)
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        if (additionalMetadataReferences.Length > 0)
        {
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            // System.Net.Http (and other satellite facades) need facades next to System.Private.CoreLib
            // in lightweight AdhocWorkspace fixtures (matches CS0012 chains in real solutions).
            foreach (var facade in new[]
                     {
                         "System.Runtime.dll",
                         "System.Private.Uri.dll",
                         "System.Net.Primitives.dll",
                     })
            {
                var path = Path.Combine(coreDir, facade);
                if (File.Exists(path))
                    metadataRefs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        metadataRefs.AddRange(additionalMetadataReferences);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestAsm",
            assemblyName: "TestAsm",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "TestAsm.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: metadataRefs.ToArray());
        workspace.AddProject(projectInfo);

        var docId = DocumentId.CreateNewId(projectId);
        var fileName = "Sample.cs";
        var fullPath = Path.Combine(Path.GetTempPath(), fileName);
        workspace.AddDocument(DocumentInfo.Create(
            docId,
            fileName,
            filePath: fullPath,
            loader: TextLoader.From(
                TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create(),
                    fullPath))));

        return workspace;
    }

    private static async Task<string> GetCompilationErrorsAsync(string source, params MetadataReference[] additionalMetadataReferences)
    {
        var workspace = CreateAdhocWorkspace(source, additionalMetadataReferences);
        var project = workspace.CurrentSolution.Projects.First();
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
        var errors = compilation!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        return errors.Length == 0 ? string.Empty : string.Join(Environment.NewLine, errors.Select(d => d.ToString()));
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in — only the surface the analyzer
    /// and the compilation cache touch is wired; the rest throws to surface accidental
    /// coupling.
    /// </summary>
    private sealed class TestWorkspaceManager : IWorkspaceManager
    {
        private readonly string _workspaceId;
        private readonly AdhocWorkspace _workspace;

        public event Action<string>? WorkspaceClosed;
        public event Action<string>? WorkspaceReloaded;

        public TestWorkspaceManager(string workspaceId, AdhocWorkspace workspace)
        {
            _workspaceId = workspaceId;
            _workspace = workspace;
        }

        public void RaiseWorkspaceClosed(string workspaceId) => WorkspaceClosed?.Invoke(workspaceId);
        public void RaiseWorkspaceReloaded(string workspaceId) => WorkspaceReloaded?.Invoke(workspaceId);

        public Solution GetCurrentSolution(string workspaceId)
        {
            return workspaceId == _workspaceId
                ? _workspace.CurrentSolution
                : throw new InvalidOperationException($"Unknown workspace {workspaceId}");
        }

        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public bool ContainsWorkspace(string workspaceId) => workspaceId == _workspaceId;
        public bool IsStale(string workspaceId) => false;
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
    }
}
