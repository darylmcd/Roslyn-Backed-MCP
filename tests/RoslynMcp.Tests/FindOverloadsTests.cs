using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for `find_overloads`: enumerates every overload of a member via
/// <c>GetTypeByMetadataName</c> + <c>GetMembers</c>, closing the gap where <c>symbol_search</c>
/// only finds symbols declared in the workspace's own source and <c>symbol_signature_help</c>
/// resolves a single already-bound call site rather than listing every overload choice.
/// </summary>
[DoNotParallelize]
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
        Assert.AreEqual(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task FindOverloads_ProjectName_UnknownProject_Throws_Distinct_From_TypeNotFound()
    {
        // An unresolvable projectName must surface as its own clear failure (mirrors the
        // "projectName '{0}' matched 0 projects" convention already used by compile_check), not
        // collapse into the generic "type not found" KeyNotFoundException path — those two
        // failure causes are not the same thing and callers need to be able to tell them apart.
        const string bogusProject = "DefinitelyNotAProject__Bogus";

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await SymbolTools.FindOverloads(
                WorkspaceExecutionGate,
                SymbolRelationshipService,
                WorkspaceId,
                typeMetadataName: "SampleLib.AnimalService",
                memberName: "CountAnimals",
                projectName: bogusProject,
                ct: CancellationToken.None));

        Assert.AreEqual("projectName", ex.ParamName);
        StringAssert.Contains(ex.Message, bogusProject,
            $"Error must echo the offending project name so the caller can correlate. Got: {ex.Message}");
    }
}
