using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for `find_overloads`: enumerates every overload of a member via
/// <c>GetTypeByMetadataName</c> + <c>GetMembers</c>, closing the gap where <c>symbol_search</c>
/// only finds symbols declared in the workspace's own source and <c>symbol_signature_help</c>
/// resolves a single already-bound call site rather than listing every overload choice.
/// </summary>
// donotparallelize-audit-wave-09: [DoNotParallelize] removed. Every test method here is a read-only
// SymbolTools.FindOverloads query uses the assembly-shared SampleSolution workspace or a test-owned
// read-only solution. No test applies changes to the shared workspace or closes its session.
[TestClass]
public sealed class FindOverloadsTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task FindOverloads_Resolves_BclMethod_Never_Called_In_Fixture_Source()
    {
        // The actual gap being closed: System.Math.Max is never referenced anywhere in
        // SampleLib/SampleApp source, so symbol_search finds nothing for it — but it's still
        // reachable via the corlib reference already loaded into the compilation.
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "System.Math",
            memberName: "Max",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"find_overloads should resolve a BCL type/member not declared in the workspace's own source. Got: {json}");

        var count = doc.RootElement.GetProperty("count").GetInt32();
        Assert.IsTrue(count > 1, $"System.Math.Max has multiple overloads. Got count={count}.");

        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(count, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            var displaySignature = item.GetProperty("displaySignature").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(displaySignature), "Each overload must have a non-empty displaySignature.");
        }
    }

    [TestMethod]
    public async Task FindOverloads_Matches_InFixture_Overload_Count()
    {
        // SampleLib.AnimalService.CountAnimals has exactly two overloads declared in the fixture's
        // own source: (List<IAnimal>) and (IEnumerable<IAnimal>).
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.AnimalService",
            memberName: "CountAnimals",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task FindOverloads_Reports_Which_Framework_Supplied_The_Metadata_Type()
    {
        using var msbuild = MSBuildWorkspace.Create();
        var generatorPath = Path.Combine(Path.GetDirectoryName(GeneratedDocumentSolutionPath)!,
            "ConsumerLib.Generators", "ConsumerLib.Generators.csproj");
        var generator = await msbuild.OpenProjectAsync(generatorPath);
        var modern = WorkspaceManager.GetCurrentSolution(WorkspaceId).Projects.Single(p => p.Name == "SampleLib");
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, generator.Name, generator.MetadataReferences,
            targetFramework: "netstandard2.0");
        solution = AddProject(solution, modern.Name, modern.MetadataReferences, targetFramework: "net10.0");

        using var implicitResult = await FindInOrderedSolutionAsync(solution, "System.Math", "Clamp");
        using var explicitResult = await FindInOrderedSolutionAsync(solution, "System.Math", "Clamp", modern.Name);

        AssertResolution(implicitResult.RootElement, generator.Name, "netstandard2.0", memberFound: false, count: 0);
        AssertResolution(explicitResult.RootElement, modern.Name, "net10.0", memberFound: true, count: 13);
    }

    [TestMethod]
    [DataRow("OnlyInForeignAssembly", false, false, 0)]
    [DataRow("Property", false, true, 0)]
    [DataRow("DeclaredPrivate", false, true, 1)]
    [DataRow("InheritedPrivate", true, false, 0)]
    [DataRow("InheritedProperty", false, false, 0)]
    [DataRow("InheritedProperty", true, true, 0)]
    public async Task FindOverloads_Reports_Member_In_Selected_Definition_With_Unknown_Framework(
        string memberName, bool includeInherited, bool memberFound, int count)
    {
        var modern = WorkspaceManager.GetCurrentSolution(WorkspaceId).Projects.Single(p => p.Name == "SampleLib");
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "Selected", modern.MetadataReferences, source: """
            namespace Probe;
            public class Root
            {
                private void InheritedPrivate() { }
                protected int InheritedProperty => 0;
            }
            public class Subject : Root
            {
                public int Property => 0;
                private void DeclaredPrivate() { }
            }
            """);
        solution = AddProject(solution, "Foreign", modern.MetadataReferences, source: """
            namespace Probe;
            public class Subject
            {
                public void OnlyInForeignAssembly() { }
            }
            """);

        using var result = await FindInOrderedSolutionAsync(solution, "Probe.Subject", memberName, includeInherited: includeInherited);
        AssertResolution(result.RootElement, "Selected", null, memberFound, count);
    }

    private static Solution AddProject(
        Solution solution, string name, IEnumerable<MetadataReference> references,
        string? targetFramework = null, string? source = null)
    {
        var id = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name,
            LanguageNames.CSharp, metadataReferences: references));
        if (targetFramework is not null)
        {
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), "framework.globalconfig",
                SourceText.From($"is_global = true\nbuild_property.TargetFramework = {targetFramework}\n"),
                filePath: Path.Combine(Path.GetTempPath(), name, "framework.globalconfig"));
        }

        return source is null ? solution : solution.AddDocument(DocumentId.CreateNewId(id), "Subject.cs", source);
    }

    private static async Task<JsonDocument> FindInOrderedSolutionAsync(
        Solution solution, string typeMetadataName, string memberName, string? projectName = null, bool includeInherited = false)
    {
        var selected = new InterfaceExtractionTestContext.FixedSolutionWorkspace(WorkspaceManager, solution);
        using var cache = new CompilationCache(selected);
        var service = new SymbolRelationshipService(selected, ReferenceService, cache,
            NullLogger<SymbolRelationshipService>.Instance);
        var result = await SymbolTools.FindOverloads(WorkspaceExecutionGate, service, WorkspaceId,
            typeMetadataName, memberName, includeInherited, projectName);
        return JsonDocument.Parse(result);
    }

    private static void AssertResolution(JsonElement result, string project, string? framework, bool memberFound, int count)
    {
        Assert.AreEqual(count, result.GetProperty("count").GetInt32());
        Assert.AreEqual(count, result.GetProperty("items").GetArrayLength());
        Assert.AreEqual(project, result.GetProperty("resolvedProject").GetString());
        Assert.AreEqual(framework, result.GetProperty("resolvedTargetFramework").GetString());
        Assert.AreEqual(memberFound, result.GetProperty("memberFound").GetBoolean());
    }

    [TestMethod]
    public async Task FindOverloads_IncludeInherited_Default_False_Excludes_Base_Members()
    {
        // SampleLib.AnimalService does not override ToString, so with includeInherited left at its
        // default (false), querying "ToString" must return zero direct-declared overloads —
        // object.ToString is not pulled in unasked.
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.AnimalService",
            memberName: "ToString",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task FindOverloads_IncludeInherited_True_Walks_Base_Chain()
    {
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.AnimalService",
            memberName: "ToString",
            includeInherited: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(
            doc.RootElement.GetProperty("count").GetInt32() >= 1,
            "includeInherited=true should surface object.ToString from the base chain.");
    }

    [TestMethod]
    public async Task FindOverloads_Unresolvable_Type_Throws_NotFound()
    {
        var ex = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(async () =>
            await SymbolTools.FindOverloads(
                WorkspaceExecutionGate,
                SymbolRelationshipService,
                WorkspaceId,
                typeMetadataName: "SampleLib.DefinitelyDoesNotExist__Bogus",
                memberName: "Max",
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "SampleLib.DefinitelyDoesNotExist__Bogus",
            $"Error must echo the offending type name so the caller can correlate. Got: {ex.Message}");
    }

    [TestMethod]
    public async Task FindOverloads_Resolvable_Type_Unknown_Member_Returns_Empty_Not_Error()
    {
        // A resolvable type with no method by that name is a normal empty result, not a NotFound —
        // distinct from FindOverloads_Unresolvable_Type_Throws_NotFound above.
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.AnimalService",
            memberName: "DefinitelyNotAMember__Bogus",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"An unknown member name on a resolvable type should be an empty success, not an error. Got: {json}");
        Assert.AreEqual(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.IsFalse(doc.RootElement.GetProperty("memberFound").GetBoolean());
    }

    [TestMethod]
    public async Task FindOverloads_IncludeInherited_Excludes_Inaccessible_Private_Base_Member()
    {
        // SampleLib.OverloadAccessibilityProbeBase declares a private Probe() and a protected
        // Probe(int). GetMembers on the base type returns both regardless of accessibility, but
        // the private overload is not actually inherited/visible from the derived type — only the
        // protected one should be advertised.
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.OverloadAccessibilityProbeDerived",
            memberName: "Probe",
            includeInherited: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1, doc.RootElement.GetProperty("count").GetInt32(),
            $"Only the protected Probe(int) overload should be inherited-visible; the private parameterless one must be excluded. Got: {json}");

        var onlyItem = doc.RootElement.GetProperty("items")[0];
        var parameters = onlyItem.GetProperty("parameters");
        Assert.AreEqual(1, parameters.GetArrayLength(), "The surviving overload must be the one-parameter protected Probe(int), not the private parameterless one.");
    }

    [TestMethod]
    public async Task FindOverloads_ProjectName_Scopes_Successful_Lookup()
    {
        // A valid projectName should behave identically to the unscoped lookup for a type that
        // genuinely lives in that project, proving the scoping path resolves rather than
        // accidentally excluding everything.
        var json = await SymbolTools.FindOverloads(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            typeMetadataName: "SampleLib.AnimalService",
            memberName: "CountAnimals",
            projectName: "SampleLib",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"A valid projectName scoping the type's own project should succeed. Got: {json}");
        AssertResolution(doc.RootElement, "SampleLib", "net10.0", memberFound: true, count: 2);
    }

    [TestMethod]
    public async Task FindOverloads_ProjectName_UnknownProject_Throws_Distinct_From_TypeNotFound()
    {
        // A project filter miss needs its own correction without echoing caller text.
        // Keep it distinct from resolving a missing type in a valid project.
        const string bogusProject = "DefinitelyNotAProject__Bogus";

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await SymbolTools.FindOverloads(
                WorkspaceExecutionGate,
                SymbolRelationshipService,
                WorkspaceId,
                typeMetadataName: "SampleLib.AnimalService",
                memberName: "CountAnimals",
                projectName: bogusProject,
                ct: CancellationToken.None));

        Assert.AreEqual("projectName", ex.ParamName);
        StringAssert.Contains(ex.PublicMessage, "workspace_status");
        Assert.IsFalse(ex.PublicMessage.Contains(bogusProject, StringComparison.Ordinal));
    }
}
