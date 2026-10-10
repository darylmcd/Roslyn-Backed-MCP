using Microsoft.CodeAnalysis;
using WorkspaceManagerType = RoslynMcp.Roslyn.Services.WorkspaceManager;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceProjectAliasLookupTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task GetProject_PhysicalAlias_SelectsMatchingProjectAndRebuildsAfterReload()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var alias = scope.GetPath("alias");
        if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, scope.GetPath("SampleLib")))
            Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        await scope.LoadAsync();
        var expected = WorkspaceManager.GetProject(scope.WorkspaceId, "SampleLib");
        Assert.IsNotNull(expected);
        var selector = Path.Combine(alias, "SampleLib.csproj");
        var matches = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => WorkspaceManager.GetProject(scope.WorkspaceId, selector))));
        Assert.IsTrue(matches.All(project => project?.Id == expected.Id));
        Assert.AreEqual(expected.Id, WorkspaceManager.GetProject(scope.WorkspaceId, selector)?.Id);
        Assert.IsNull(WorkspaceManager.GetProject(scope.WorkspaceId, Path.Combine(alias, "Missing.csproj")));
        Assert.AreEqual(expected.Id, WorkspaceManager.GetProject(scope.WorkspaceId, "sAmPlElIb")?.Id);
        Assert.IsNull(WorkspaceManager.GetProject(scope.WorkspaceId, " "));
        var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
        await scope.ReloadAsync();
        Assert.IsTrue(WorkspaceManager.GetCurrentVersion(scope.WorkspaceId) > version);
        var after = WorkspaceManager.GetProject(scope.WorkspaceId, selector);
        Assert.IsNotNull(after);
        Assert.AreNotSame(expected, after);
        Assert.AreSame(WorkspaceManager.GetCurrentSolution(scope.WorkspaceId).GetProject(after.Id), after);
    }

    [TestMethod]
    public async Task GetProject_CyclicSelector_PropagatesFilesystemRefusal()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        await scope.LoadAsync();
        using var cycle = new CycleLink(scope.GetPath("cycle"));
        if (!cycle.IsAvailable) Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var selector = Path.Combine(cycle.Path, "SampleLib.csproj");
        var exception = Assert.ThrowsExactly<IOException>(() =>
            WorkspaceManager.GetProject(scope.WorkspaceId, selector));
        StringAssert.Contains(exception.Message, "Filesystem link cycle");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GetProject_LexicalPathNameCollision_PreservesSolutionOrder(bool nameFirst)
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var firstPath = scope.GetPath("SampleLib", "SampleLib.csproj");
        var otherPath = scope.GetPath("SampleLib.Tests", "SampleLib.Tests.csproj");
        using var index = new IndexScope(nameFirst
            ? [(firstPath, otherPath), ("PathOwner", firstPath)]
            : [("PathOwner", firstPath), (firstPath, otherPath)]);

        Assert.AreEqual(nameFirst ? otherPath : firstPath,
            index.Find(firstPath)?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_NewAliasNameCollision_PreservesExistingName()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var alias = scope.GetPath("alias");
        if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, scope.GetPath("SampleLib")))
            Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var selector = Path.Combine(alias, "SampleLib.csproj");
        var otherPath = scope.GetPath("SampleLib.Tests", "SampleLib.Tests.csproj");
        using var index = new IndexScope([("AliasOwner", scope.GetPath("SampleLib", "SampleLib.csproj")), (selector, otherPath)]);
        Assert.AreEqual(otherPath, index.Find(selector)?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_DuplicateNames_PreservesFirstProject()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var firstPath = scope.GetPath("SampleLib", "SampleLib.csproj");
        using var index = new IndexScope([("Duplicate", firstPath), ("duplicate", scope.GetPath("SampleLib.Tests", "SampleLib.Tests.csproj"))]);
        Assert.AreEqual(firstPath, index.Find("DUPLICATE")?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_CaseDistinctFiles_RemainDistinct()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Requires a case-sensitive filesystem.");
        await using var scope = CreateIsolatedWorkspaceCopy();
        var upper = scope.GetPath("Case.csproj");
        var lower = scope.GetPath("case.csproj");
        File.WriteAllText(upper, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(lower, File.ReadAllText(upper));
        Assert.AreEqual(2, Directory.EnumerateFiles(scope.RootPath).Count(path => path == upper || path == lower));
        using var index = new IndexScope([("Upper", upper), ("Lower", lower)]);

        Assert.AreEqual(upper, index.Find(upper)?.FilePath);
        Assert.AreEqual(lower, index.Find(lower)?.FilePath);
        Assert.AreNotEqual(index.Find(upper)?.Id,
            index.Find(lower)?.Id);
    }

    [TestMethod]
    public async Task GetProject_NameAndLexicalHits_DoNotResolveUnrelatedProjectPaths()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        using var cycle = new CycleLink(scope.GetPath("Broken.csproj"));
        if (!cycle.IsAvailable) Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var healthy = scope.GetPath("SampleLib", "SampleLib.csproj");
        using var index = new IndexScope([("Broken", cycle.Path), ("Healthy", healthy)]);
        Assert.AreEqual(healthy, index.Find("hEaLtHy")?.FilePath);
        Assert.AreEqual(healthy, index.Find(healthy)?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_PhysicalIndexRefusal_RecoversAfterFilesystemRepair()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        using var cycle = new CycleLink(scope.GetPath("Broken.csproj"));
        if (!cycle.IsAvailable) Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var healthy = scope.GetPath("SampleLib", "SampleLib.csproj");
        var selector = scope.GetPath("SampleLib", ".", "SampleLib.csproj");
        using var index = new IndexScope([("Healthy", healthy), ("Broken", cycle.Path)]);
        Assert.ThrowsExactly<IOException>(() => index.Find(selector));
        cycle.Dispose();
        File.WriteAllText(cycle.Path, "<Project />");
        Assert.AreEqual(healthy, index.Find(selector)?.FilePath);
        Assert.AreEqual("Broken", index.Find(scope.GetPath(".", "Broken.csproj"))?.Name);
        Assert.AreEqual(healthy, index.Find(selector)?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_LinkThenParent_DoesNotShadowAnotherExactProjectPath()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var child = scope.GetPath("physical", "child");
        Directory.CreateDirectory(child);
        var physical = scope.GetPath("physical", "Shared.csproj");
        var exact = scope.GetPath("Shared.csproj");
        File.WriteAllText(physical, "<Project />");
        File.WriteAllText(exact, "<Project />");
        var link = scope.GetPath("parent-alias");
        if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, child))
            Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var raw = Path.Combine(link, "..", "Shared.csproj");
        using var index = new IndexScope([("Physical", raw), ("Exact", exact)]);
        Assert.AreEqual("Exact", index.Find(exact)?.Name);
        Assert.AreEqual("Physical", index.Find(raw)?.Name);
        Assert.AreEqual("Physical", index.Find(physical)?.Name);
        Assert.AreEqual("Physical", index.Find("Physical")?.Name);
    }

    [TestMethod]
    public async Task GetProject_CaseDistinctLexicalPathAndName_PreserveFilesystemIdentity()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Requires a case-sensitive filesystem.");
        await using var scope = CreateIsolatedWorkspaceCopy();
        var firstPath = scope.GetPath("SampleLib", "SampleLib.csproj");
        var nameSelector = scope.GetPath("SampleLib", "samplelib.csproj");
        var otherPath = scope.GetPath("SampleLib.Tests", "SampleLib.Tests.csproj");
        using var index = new IndexScope([("PathOwner", firstPath), (nameSelector, otherPath)]);

        Assert.AreEqual(otherPath, index.Find(nameSelector)?.FilePath);
        Assert.AreEqual(firstPath, index.Find(firstPath)?.FilePath);
    }

    [TestMethod]
    public async Task GetProject_PhysicalPathCollision_PreservesFirstAliasAndExactPaths()
    {
        await using var scope = CreateIsolatedWorkspaceCopy();
        var alias = scope.GetPath("alias");
        if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, scope.GetPath("SampleLib")))
            Assert.Inconclusive("Directory links are unavailable on this filesystem.");
        var physical = scope.GetPath("SampleLib", "SampleLib.csproj");
        var lexical = Path.Combine(alias, "SampleLib.csproj");
        using var index = new IndexScope([("First", lexical), ("Second", physical)]);
        Assert.AreEqual("First", index.Find(lexical)?.Name);
        Assert.AreEqual("Second", index.Find(physical)?.Name);
        Assert.AreEqual("First", index.Find(Path.Combine(alias, ".", "SampleLib.csproj"))?.Name);
    }

    [TestMethod]
    public void GetProject_PathlessProjectAndUnknownSelectors_PreserveNameLookup()
    {
        using var index = new IndexScope([("NameOnly", "")]);
        Assert.AreEqual("NameOnly", index.Find("NAMEONLY")?.Name);
        Assert.IsNull(index.Find("Unknown"));
        Assert.IsNull(index.Find(Path.Combine(Path.GetTempPath(), "Invalid") + '\0'));
        if (OperatingSystem.IsWindows()) Assert.IsNull(index.Find("C:relative.csproj"));
    }

    private sealed class CycleLink : IDisposable
    {
        private bool _exists;
        public string Path { get; }
        public bool IsAvailable { get; }

        public CycleLink(string path)
        {
            Path = path;
            IsAvailable = TestFixtureFileSystem.TryCreateDirectoryLink(path, path);
            _exists = IsAvailable;
        }

        public void Dispose()
        {
            if (!_exists) return;
            // Unlink cycles before the fixture's recursive cleanup.
            if (OperatingSystem.IsWindows()) Directory.Delete(Path);
            else File.Delete(Path);
            _exists = false;
        }
    }

    private sealed class IndexScope : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();
        private readonly WorkspaceManagerType.ProjectIndexEntry _index;


        public IndexScope((string Name, string? Path)[] projects)
        {
            var solution = _workspace.CurrentSolution;
            foreach (var project in projects)
                solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                    project.Name, project.Name, LanguageNames.CSharp, filePath: project.Path));
            _index = WorkspaceManagerType.BuildProjectIndex(1, solution);
        }

        public Project? Find(string selector) => _index.Find(selector);
        public void Dispose() => _workspace.Dispose();
    }
}
