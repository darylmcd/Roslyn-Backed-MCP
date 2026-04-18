using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="DuplicateMethodDetectorService"/>. Uses an in-memory
/// <see cref="AdhocWorkspace"/> for fast, isolated runs — the detector works off
/// syntax trees, so no MSBuildWorkspace is required.
/// </summary>
[TestClass]
public sealed class DuplicateMethodDetectorTests
{
    private const string WorkspaceId = "dup-test-ws";

    [TestMethod]
    public async Task FindDuplicatedMethods_StructurallyIdenticalBodies_Cluster()
    {
        // Two methods with identical structure: different names, different local names,
        // different formatting — the canonical form collapses all of that to the same key.
        var source = """
            namespace Sample;
            public class Helper
            {
                public int AddOne(int value)
                {
                    var temp = value + 1;
                    var result = temp * 2;
                    var doubled = result + result;
                    var halved = doubled / 2;
                    var finalValue = halved - 1;
                    return finalValue;
                }

                public int Alternate(int input)
                {
                    var x = input + 1;
                    var y = x * 2;
                    var z = y + y;
                    var w = z / 2;
                    var v = w - 1;
                    return v;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count, "Expected one cluster for the two structurally identical methods.");
        Assert.AreEqual(2, groups[0].MemberCount);
        Assert.AreEqual(1.0, groups[0].Similarity);
        CollectionAssert.AreEquivalent(
            new[] { "AddOne", "Alternate" },
            groups[0].Methods.Select(m => m.MethodName).ToArray());
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_UnrelatedMethods_DoNotCluster()
    {
        // Three structurally distinct methods — no two should collapse to the same canonical form.
        var source = """
            namespace Sample;
            public class Unrelated
            {
                public string Describe(string input)
                {
                    if (input is null) return "null";
                    var length = input.Length;
                    var trimmed = input.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    return $"{upper}:{length}";
                }

                public int Compute(int a, int b)
                {
                    var sum = a + b;
                    for (var i = 0; i < a; i++)
                    {
                        sum += i;
                    }
                    return sum;
                }

                public bool Validate(object payload)
                {
                    if (payload is null) throw new System.ArgumentNullException(nameof(payload));
                    var type = payload.GetType();
                    var name = type.FullName;
                    return name is { Length: > 0 };
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 5, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Structurally distinct methods must not cluster. Got: " +
            string.Join(", ", groups.SelectMany(g => g.Methods.Select(m => m.MethodName))));
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_OverloadsWithIdenticalBodies_Cluster()
    {
        // Overload-handling invariant: bucketing is by body shape, not method name —
        // so two overloads that happen to share an identical body DO cluster.
        var source = """
            namespace Sample;
            public class Overloads
            {
                public int Process(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }

                public int Process(long value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return (int)tally;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        // The two bodies differ only at the final return expression: one is `return tally;`,
        // the other is `return (int)tally;`. That's a structural difference (an extra cast
        // node) — so they should NOT cluster. This verifies the inverse of the above
        // invariant: overloads with genuinely-different bodies do not collide.
        Assert.AreEqual(0, groups.Count,
            "Overloads with different bodies must not cluster.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_OverloadsWithTrulyIdenticalBodies_Cluster()
    {
        // Same name, same body shape (including the final return) — should cluster.
        var source = """
            namespace Sample;
            public class Overloads
            {
                public int Process(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }

                public int Process(int value, int unused)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count, "Overloads with identical bodies must cluster.");
        Assert.AreEqual(2, groups[0].MemberCount);
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ShortBodies_AreIgnored()
    {
        // Bodies below MinLines produce too many false-positive clusters (one-liners like
        // trivial forwarders). Default minLines=10 silently skips them.
        var source = """
            namespace Sample;
            public class ShortHelpers
            {
                public int Add(int a) => a + 1;
                public int Bump(int a) => a + 1;
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 10, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Expression-bodied one-liners should be filtered out by the default MinLines.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_LiteralValuesDistinguishGroups()
    {
        // Two methods whose structure is identical except for the literal values they
        // return. The canonical form preserves literals so they should NOT cluster — two
        // helpers returning "admin" vs "user" role strings are semantically different.
        var source = """
            namespace Sample;
            public class RoleHelpers
            {
                public string Admin()
                {
                    var label = "admin";
                    var prefix = label + "-";
                    var suffix = prefix + "role";
                    var full = suffix.ToLowerInvariant();
                    var trimmed = full.Trim();
                    return trimmed;
                }

                public string User()
                {
                    var label = "user";
                    var prefix = label + "-";
                    var suffix = prefix + "role";
                    var full = suffix.ToLowerInvariant();
                    var trimmed = full.Trim();
                    return trimmed;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Bodies differing only on literal values should NOT cluster — preserves literal semantics.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_GeneratedFiles_AreExcluded()
    {
        // A method in a .g.cs file must be ignored even if it structurally matches a
        // user-authored duplicate. Autogens are outside the target audience.
        var normalSource = """
            namespace Sample;
            public class Hand
            {
                public int Do(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b + b;
                    var d = c / 2;
                    var e = d - 1;
                    return e;
                }
            }
            """;
        var generatedSource = """
            namespace Sample;
            public class Auto
            {
                public int Do(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b + b;
                    var d = c / 2;
                    var e = d - 1;
                    return e;
                }
            }
            """;

        var service = BuildServiceWithSources(
            ("Hand.cs", normalSource),
            ("Auto.g.cs", generatedSource));

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            ".g.cs files must be excluded from duplicate-method detection.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_AbstractAndInterfaceMembers_AreSkipped()
    {
        // Abstract declarations and interface method headers have no body — they must
        // never be considered for bucketing (a bucket of empty strings would cluster
        // every abstract method together and be useless).
        var source = """
            namespace Sample;
            public interface IShape
            {
                int Area();
                int Perimeter();
            }
            public abstract class Base
            {
                public abstract int Area();
                public abstract int Perimeter();
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 1, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Abstract methods and interface declarations have no body — must not cluster.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ProjectFilter_ScopesScanToProject()
    {
        // Load two projects: the structurally-matching methods sit in different projects.
        // When `ProjectFilter` targets only one project, cross-project clustering
        // disappears because only one member ends up in the bucket.
        var (service, adhoc) = CreateAdhocServiceWithTwoProjects();
        _ = adhoc;

        var filtered = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50, ProjectFilter = "ProjectA" },
            default);

        Assert.AreEqual(0, filtered.Count,
            "ProjectFilter should scope the scan so cross-project duplicates disappear.");

        var unfiltered = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, unfiltered.Count,
            "Without a filter, the cross-project duplicate should still cluster.");
    }

    /// <summary>
    /// Builds a service backed by an <see cref="AdhocWorkspace"/> containing a single
    /// document with the given source. Returns the configured service; out-parameter
    /// exposes the underlying workspace for tests that need it.
    /// </summary>
    private static DuplicateMethodDetectorService BuildServiceWithSource(string source, out AdhocWorkspace workspace)
    {
        return BuildServiceWithSourcesCore(out workspace, ("Sample.cs", source));
    }

    private static DuplicateMethodDetectorService BuildServiceWithSources(params (string fileName, string source)[] docs)
    {
        return BuildServiceWithSourcesCore(out _, docs);
    }

    private static DuplicateMethodDetectorService BuildServiceWithSourcesCore(out AdhocWorkspace workspace, params (string fileName, string source)[] docs)
    {
        workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "TestProject.csproj"));
        workspace.AddProject(projectInfo);

        foreach (var (fileName, source) in docs)
        {
            var docId = DocumentId.CreateNewId(projectId);
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
        }

        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        var cache = new CompilationCache(wsManager);
        return new DuplicateMethodDetectorService(
            wsManager,
            cache,
            NullLogger<DuplicateMethodDetectorService>.Instance);
    }

    /// <summary>
    /// Build an AdhocWorkspace with two projects so we can exercise
    /// <see cref="DuplicateMethodAnalysisOptions.ProjectFilter"/> behaviour.
    /// </summary>
    private static (DuplicateMethodDetectorService service, AdhocWorkspace workspace) CreateAdhocServiceWithTwoProjects()
    {
        const string bodySource = """
            public int Compute(int value)
            {
                var tally = value;
                tally += 1;
                tally += 1;
                tally += 1;
                tally += 1;
                tally += 1;
                return tally;
            }
            """;

        var adhoc = new AdhocWorkspace();
        foreach (var projectName in new[] { "ProjectA", "ProjectB" })
        {
            var projectId = ProjectId.CreateNewId();
            adhoc.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name: projectName,
                assemblyName: projectName,
                language: LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), $"{projectName}.csproj")));

            var docId = DocumentId.CreateNewId(projectId);
            var fileName = $"{projectName}.cs";
            var fullPath = Path.Combine(Path.GetTempPath(), fileName);
            var source = $"namespace {projectName};\npublic class Helper\n{{\n    {bodySource}\n}}\n";
            adhoc.AddDocument(DocumentInfo.Create(
                docId,
                fileName,
                filePath: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(
                        SourceText.From(source),
                        VersionStamp.Create(),
                        fullPath))));
        }

        var wsManager = new TestWorkspaceManager(WorkspaceId, adhoc);
        var cache = new CompilationCache(wsManager);
        var service = new DuplicateMethodDetectorService(
            wsManager,
            cache,
            NullLogger<DuplicateMethodDetectorService>.Instance);
        return (service, adhoc);
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in for the detector tests. Only
    /// <see cref="GetCurrentSolution"/> and the two version hooks the compilation cache
    /// calls are wired; the rest throw to surface accidental coupling.
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
