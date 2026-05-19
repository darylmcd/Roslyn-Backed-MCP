using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the partial-coverage path for <c>test_coverage</c> — backlog row
/// <c>test-coverage-fail-fast-on-missing-coverlet</c> (P2). Pre-fix the tool fail-fasted
/// the entire call when ANY in-scope test project lacked the <c>coverlet.collector</c>
/// NuGet package. Post-fix: the tool partitions the projects, runs per-project coverage
/// on the ones that DO have the collector, surfaces the skipped names in the new
/// top-level <c>coverageGaps</c> field, and reports <c>success=true</c>. The all-projects-
/// lack-coverlet regression still triggers the existing <c>CoverletMissing</c> envelope —
/// covered by the regression test below.
/// </summary>
[TestClass]
public sealed class TestCoveragePartialCoverletTests
{
    /// <summary>
    /// (1) When in-scope test projects split into with-coverlet AND without-coverlet, the
    /// tool MUST run partial coverage on the with-coverlet projects and surface the skipped
    /// names in <c>coverageGaps</c>. <c>success</c> remains <c>true</c> and the
    /// <c>failureEnvelope</c> is null — the gap is a recoverable condition, not a failure.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_MixedCoverlet_ReturnsPartialCoverageWithGaps()
    {
        // Build two on-disk csproj files: one with the coverlet.collector reference and one
        // without. FindTestProjectsWithoutCoverlet reads from disk so we need real files.
        using var fixture = new CoverletProjectFixture();
        var withCoverletPath = fixture.WriteCsproj("WithCoverlet", includesCoverletCollector: true);
        var withoutCoverletPath = fixture.WriteCsproj("WithoutCoverlet", includesCoverletCollector: false);

        var gate = new PassthroughGate();
        var workspace = new MixedCoverletWorkspaceManager(withCoverletPath, withoutCoverletPath);
        // Use a runner that writes a fake Cobertura XML into the coverage results directory
        // so the parser produces a real TestCoverageResultDto rather than the no-coverage-
        // file fallback. The runner inspects the --results-directory argument and drops a
        // minimal but valid Cobertura document there.
        var runner = new CoberturaWritingDotnetCommandRunner();

        var json = await TestCoverageTools.RunTestCoverageCore(
            gate,
            workspace,
            runner,
            workspaceId: "ws-coverage-partial",
            projectName: null,
            deprecation: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("success").GetBoolean(),
            "Mixed-coverlet runs must report success=true — partial coverage is a recoverable state.");
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("failureEnvelope").ValueKind,
            "failureEnvelope must be null when partial coverage succeeds.");

        var coverageGaps = root.GetProperty("coverageGaps");
        Assert.AreEqual(JsonValueKind.Array, coverageGaps.ValueKind,
            "coverageGaps must be a JSON array of project names when partial coverage is returned.");
        var gapNames = coverageGaps.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual(1, gapNames.Count, "Exactly one project lacked coverlet in this fixture.");
        Assert.AreEqual("WithoutCoverlet", gapNames[0],
            "coverageGaps must contain the name of the project that was skipped.");

        // The classic modules / line-coverage fields must remain populated from the per-project
        // Cobertura run on WithCoverlet.
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("modules").ValueKind);
        Assert.IsTrue(root.GetProperty("modules").GetArrayLength() > 0,
            "modules array must be populated from the per-project Cobertura output.");
        Assert.AreEqual(JsonValueKind.Number, root.GetProperty("lineCoveragePercent").ValueKind,
            "lineCoveragePercent must be a number, not null, when the per-project run produced a Cobertura file.");
    }

    /// <summary>
    /// (2) Regression guard: when EVERY in-scope test project lacks <c>coverlet.collector</c>,
    /// the pre-existing fail-fast path must remain reachable. <c>success=false</c>,
    /// <c>failureEnvelope.errorKind=CoverletMissing</c>, <c>missingPackages</c> populated.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_AllProjectsLackCoverlet_FailFastsWithCoverletMissingEnvelope()
    {
        using var fixture = new CoverletProjectFixture();
        var withoutA = fixture.WriteCsproj("WithoutCoverletA", includesCoverletCollector: false);
        var withoutB = fixture.WriteCsproj("WithoutCoverletB", includesCoverletCollector: false);

        var gate = new PassthroughGate();
        var workspace = new MixedCoverletWorkspaceManager(
            withCoverletProjectPath: null,
            withoutCoverletProjectPath: null,
            extraProjects: [
                ("WithoutCoverletA", withoutA, true),
                ("WithoutCoverletB", withoutB, true),
            ]);
        // Runner must NOT be invoked on the all-missing path. The throwing stub guards that
        // contract — if execution reaches the dotnet test step the test will fail with the
        // throw-on-invocation message instead of the fail-fast envelope assertion.
        var runner = new ThrowingDotnetCommandRunner(
            new InvalidOperationException("fail-fast path must not invoke dotnet test"));

        var json = await TestCoverageTools.RunTestCoverageCore(
            gate,
            workspace,
            runner,
            workspaceId: "ws-coverage-allmissing",
            projectName: null,
            deprecation: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.GetProperty("success").GetBoolean(),
            "All-projects-lack-coverlet must still fail-fast with success=false.");

        var envelope = root.GetProperty("failureEnvelope");
        Assert.AreEqual(JsonValueKind.Object, envelope.ValueKind);
        Assert.AreEqual("CoverletMissing", envelope.GetProperty("errorKind").GetString());

        var missingPackages = envelope.GetProperty("missingPackages");
        Assert.AreEqual(JsonValueKind.Array, missingPackages.ValueKind);
        Assert.AreEqual(2, missingPackages.GetArrayLength(),
            "Both lacking projects must surface in missingPackages.");

        // coverageGaps is the partial-success surface and must NOT be populated on the
        // all-missing fail-fast path (the missingPackages field inside failureEnvelope is the
        // canonical surface there). The serializer emits null for unset coverageGaps.
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("coverageGaps").ValueKind,
            "coverageGaps must be null on the all-missing fail-fast path — failureEnvelope.missingPackages is the canonical surface.");
    }

    /// <summary>
    /// (3) When every in-scope test project HAS coverlet.collector, the classic single-
    /// dotnet-test path must still apply: <c>success=true</c>, <c>coverageGaps</c> null, no
    /// per-project iteration. This guards against the partial-path accidentally triggering
    /// for the all-with-coverlet happy case.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_AllProjectsHaveCoverlet_ClassicPathNoGaps()
    {
        using var fixture = new CoverletProjectFixture();
        var pathA = fixture.WriteCsproj("WithCoverletA", includesCoverletCollector: true);
        var pathB = fixture.WriteCsproj("WithCoverletB", includesCoverletCollector: true);

        var gate = new PassthroughGate();
        var workspace = new MixedCoverletWorkspaceManager(
            withCoverletProjectPath: null,
            withoutCoverletProjectPath: null,
            extraProjects: [
                ("WithCoverletA", pathA, true),
                ("WithCoverletB", pathB, true),
            ]);
        var runner = new CoberturaWritingDotnetCommandRunner();

        var json = await TestCoverageTools.RunTestCoverageCore(
            gate,
            workspace,
            runner,
            workspaceId: "ws-coverage-allcoverlet",
            projectName: null,
            deprecation: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("failureEnvelope").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("coverageGaps").ValueKind,
            "All-projects-have-coverlet must NOT populate coverageGaps — that field is partial-only.");
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    /// <summary>
    /// Same shape as the gate stub in <see cref="TestCoverageFailureEnvelopeTests"/>.
    /// Duplicated here because that file's nested classes are <c>private sealed</c> and
    /// therefore not shared infrastructure.
    /// </summary>
    private sealed class PassthroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) { }
    }

    /// <summary>
    /// Workspace stub that returns a status with a mixed list of test projects pointing at
    /// real on-disk .csproj files. <see cref="TestCoverageTools.PartitionTestProjectsByCoverlet"/>
    /// reads from disk to detect coverlet.collector — the fixture writes the files for us.
    /// </summary>
    private sealed class MixedCoverletWorkspaceManager : IWorkspaceManager
    {
        private readonly IReadOnlyList<ProjectStatusDto> _projects;

        public MixedCoverletWorkspaceManager(
            string? withCoverletProjectPath,
            string? withoutCoverletProjectPath,
            IReadOnlyList<(string Name, string Path, bool IsTest)>? extraProjects = null)
        {
            var projects = new List<ProjectStatusDto>();
            if (withCoverletProjectPath is not null)
                projects.Add(BuildProjectStatus("WithCoverlet", withCoverletProjectPath, isTest: true));
            if (withoutCoverletProjectPath is not null)
                projects.Add(BuildProjectStatus("WithoutCoverlet", withoutCoverletProjectPath, isTest: true));
            if (extraProjects is not null)
            {
                foreach (var (name, path, isTest) in extraProjects)
                    projects.Add(BuildProjectStatus(name, path, isTest));
            }
            _projects = projects;
        }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => true;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => BuildStatus(workspaceId);
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(BuildStatus(workspaceId));
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        private WorkspaceStatusDto BuildStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\fake\\workspace\\Sample.slnx",
                WorkspaceVersion: 1,
                SnapshotToken: "snapshot",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: _projects.Count,
                DocumentCount: 0,
                Projects: _projects,
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []);

        private static ProjectStatusDto BuildProjectStatus(string name, string filePath, bool isTest) =>
            new(
                Name: name,
                FilePath: filePath,
                DocumentCount: 0,
                ProjectReferences: [],
                TargetFrameworks: ["net10.0"],
                IsTestProject: isTest,
                AssemblyName: name,
                OutputType: "Exe");
    }

    /// <summary>
    /// Runner stub that writes a minimal valid Cobertura XML document into the
    /// <c>--results-directory</c> on every invocation. The test_coverage tool's
    /// per-project loop calls this once per with-coverlet project; each call drops a
    /// uniquely-named coverage file so the aggregator sees multiple inputs.
    /// </summary>
    private sealed class CoberturaWritingDotnetCommandRunner : IDotnetCommandRunner
    {
        private int _invocationCount;

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            // Locate the --results-directory argument so we know where to drop the fake
            // Cobertura XML. The classic and partial paths both pass it.
            string? resultsDir = null;
            for (var i = 0; i < arguments.Count - 1; i++)
            {
                if (string.Equals(arguments[i], "--results-directory", StringComparison.Ordinal))
                {
                    resultsDir = arguments[i + 1];
                    break;
                }
            }

            if (resultsDir is not null)
            {
                var perRunDir = Path.Combine(resultsDir, $"run-{Interlocked.Increment(ref _invocationCount):D3}");
                Directory.CreateDirectory(perRunDir);
                var xmlPath = Path.Combine(perRunDir, "coverage.cobertura.xml");
                File.WriteAllText(xmlPath, BuildMinimalCoberturaXml(_invocationCount), Encoding.UTF8);
            }

            return Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: 0,
                Succeeded: true,
                DurationMs: 100,
                StdOut: "Test Run Successful.",
                StdErr: string.Empty));
        }

        // Minimal but well-formed Cobertura document: one package with one class and one
        // line. The hit count varies per invocation so the aggregator's weighted average
        // produces a non-trivial line-coverage number.
        private static string BuildMinimalCoberturaXml(int seed) =>
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <coverage line-rate="0.5" branch-rate="0.25" version="1.9" timestamp="0" lines-covered="1" lines-valid="2" branches-covered="1" branches-valid="4">
              <sources/>
              <packages>
                <package name="FakeModule{seed}" line-rate="0.5" branch-rate="0.25" complexity="0">
                  <classes>
                    <class name="FakeClass{seed}" filename="Fake.cs" line-rate="0.5" branch-rate="0.25" complexity="0">
                      <methods/>
                      <lines>
                        <line number="1" hits="1" branch="false"/>
                        <line number="2" hits="0" branch="false"/>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
    }

    /// <summary>
    /// Same runner-throws shape as <see cref="TestCoverageFailureEnvelopeTests"/>. Used by
    /// the all-projects-lack-coverlet test to assert the fail-fast path doesn't invoke
    /// <c>dotnet test</c>.
    /// </summary>
    private sealed class ThrowingDotnetCommandRunner : IDotnetCommandRunner
    {
        private readonly Exception _toThrow;

        public ThrowingDotnetCommandRunner(Exception toThrow)
        {
            _toThrow = toThrow;
        }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            throw _toThrow;
    }

    /// <summary>
    /// Manages a temp directory containing test-only .csproj files. Disposed at the end of
    /// each test to keep the test run hermetic.
    /// </summary>
    private sealed class CoverletProjectFixture : IDisposable
    {
        private readonly string _root;

        public CoverletProjectFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-coverage-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public string WriteCsproj(string name, bool includesCoverletCollector)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{name}.csproj");
            var content = includesCoverletCollector
                ? """
                  <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                      <TargetFramework>net10.0</TargetFramework>
                      <IsPackable>false</IsPackable>
                    </PropertyGroup>
                    <ItemGroup>
                      <PackageReference Include="coverlet.collector" Version="6.0.0" />
                    </ItemGroup>
                  </Project>
                  """
                : """
                  <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                      <TargetFramework>net10.0</TargetFramework>
                      <IsPackable>false</IsPackable>
                    </PropertyGroup>
                  </Project>
                  """;
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup — test temp dirs are non-critical.
            }
        }
    }
}
