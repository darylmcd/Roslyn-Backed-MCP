using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class ServiceCoverageTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    // ── SymbolSearchService ──────────────────────────────────────────

    [TestMethod]
    public async Task SearchSymbols_Finds_Dog_Class()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "Dog", null, null, null, 10, CancellationToken.None);

        Assert.IsTrue(results.Any(s => s.Name == "Dog"),
            "Expected at least one result with Name == 'Dog'");
    }

    [TestMethod]
    public async Task GetSymbolInfo_Returns_Details_For_AnimalService()
    {
        var result = await SymbolSearchService.GetSymbolInfoAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None);

        Assert.IsNotNull(result, "SymbolInfo should not be null for AnimalService");
        Assert.AreEqual("AnimalService", result.Name);
    }

    [TestMethod]
    public async Task GetDocumentSymbols_Returns_Members_For_Dog()
    {
        var dogPath = FindDocumentPath("Dog.cs");

        var symbols = await SymbolSearchService.GetDocumentSymbolsAsync(
            WorkspaceId, dogPath, CancellationToken.None);

        Assert.IsTrue(symbols.Count > 0,
            "Expected at least one symbol in Dog.cs");
    }

    // ── SymbolNavigationService ──────────────────────────────────────

    [TestMethod]
    public async Task GoToDefinition_Finds_IAnimal()
    {
        var definitions = await SymbolNavigationService.GoToDefinitionAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.IAnimal"),
            CancellationToken.None);

        Assert.IsTrue(definitions.Count >= 1,
            $"Expected at least 1 definition location for IAnimal, got {definitions.Count}");
    }

    [TestMethod]
    public async Task GoToTypeDefinition_Navigates_From_Variable()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 18: "foreach (var animal in animals)" — position on 'animal' should navigate to IAnimal
        var results = await SymbolNavigationService.GoToTypeDefinitionAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 18, 22),
            CancellationToken.None);

        Assert.IsTrue(results.Count > 0,
            "Expected at least one type definition result for the 'animal' variable");
    }

    [TestMethod]
    public async Task GetEnclosingSymbol_Returns_Method()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 18 is inside MakeThemSpeak method body
        var result = await SymbolNavigationService.GetEnclosingSymbolAsync(
            WorkspaceId, animalServicePath, 18, 10, CancellationToken.None);

        Assert.IsNotNull(result, "Enclosing symbol should not be null");
        Assert.IsTrue(result.Name.Contains("MakeThemSpeak"),
            $"Expected enclosing symbol to contain 'MakeThemSpeak', got '{result.Name}'");
    }

    // ── SymbolRelationshipService ────────────────────────────────────

    [TestMethod]
    public async Task GetTypeHierarchy_Shows_Shape_Hierarchy()
    {
        var hierarchy = await SymbolRelationshipService.GetTypeHierarchyAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.Hierarchy.Shape"),
            CancellationToken.None);

        Assert.IsNotNull(hierarchy, "Type hierarchy should not be null for Shape");
        Assert.IsNotNull(hierarchy.DerivedTypes, "Shape should have derived types");

        var derivedNames = hierarchy.DerivedTypes.Select(d => d.TypeName).ToList();
        Assert.IsTrue(derivedNames.Any(n => n.Contains("Circle")),
            "Circle should be a derived type of Shape");
        Assert.IsTrue(derivedNames.Any(n => n.Contains("Rectangle")),
            "Rectangle should be a derived type of Shape");
    }

    [TestMethod]
    public async Task GetTypeHierarchy_LeafType_Returns_Empty_Collections()
    {
        var hierarchy = await SymbolRelationshipService.GetTypeHierarchyAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None);

        Assert.IsNotNull(hierarchy, "Type hierarchy should not be null for AnimalService.");
        Assert.AreEqual(0, hierarchy.BaseTypes.Count, "Leaf type baseTypes should serialize as an empty array, not null.");
        Assert.AreEqual(0, hierarchy.DerivedTypes.Count, "Leaf type derivedTypes should serialize as an empty array, not null.");
        Assert.AreEqual(0, hierarchy.Interfaces.Count, "Leaf type interfaces should serialize as an empty array, not null.");
    }

    [TestMethod]
    public async Task GetSignatureHelp_Returns_Method_Signature()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 16 column 17 is MakeThemSpeak declaration
        var result = await SymbolRelationshipService.GetSignatureHelpAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 16, 17),
            preferDeclaringMember: true,
            CancellationToken.None);

        Assert.IsNotNull(result, "Signature help should not be null for MakeThemSpeak");
    }

    [TestMethod]
    public async Task GetCallersCallees_Finds_Speak_Callers()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 16 column 17 is MakeThemSpeak declaration
        var result = await SymbolRelationshipService.GetCallersCalleesAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 16, 17),
            CancellationToken.None);

        Assert.IsNotNull(result, "CallerCallee result should not be null");
        Assert.IsTrue(result.Callers.Count > 0 || result.Callees.Count > 0,
            "MakeThemSpeak should have callers or callees");
    }

    [TestMethod]
    public async Task GetCallersCallees_Callees_Populate_PreviewText()
    {
        // Regression guard for gh #742 (callers-callees-previewtext-asymmetry):
        // before the fix, CollectCalleesAsync passed no previewText argument to
        // SymbolMapper.ToLocationDto, so every callee entry had PreviewText == null
        // while callers had it populated. Post-fix, in-source callees (e.g. IAnimal.Speak
        // resolved by MakeThemSpeak) must surface a non-empty PreviewText extracted
        // from the callee's declaration site.
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 16 column 17 is MakeThemSpeak declaration; MakeThemSpeak invokes
        // animal.Speak() (in-source IAnimal.Speak) and Console.WriteLine (external).
        var result = await SymbolRelationshipService.GetCallersCalleesAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 16, 17),
            CancellationToken.None);

        Assert.IsNotNull(result, "CallerCallee result should not be null");
        Assert.IsTrue(result.Callees.Count > 0, "MakeThemSpeak should have at least one callee");

        // Scope the assertion to in-source callees only — external callees (Console.WriteLine
        // resolved via the invocation-site fallback) may legitimately yield a preview, but
        // metadata-only edge cases are not in scope. The asymmetry repro is the in-source
        // case: at least one callee whose FilePath points at a workspace document must
        // have a non-empty PreviewText.
        var inSourceCallees = result.Callees
            .Where(c => !string.IsNullOrEmpty(c.FilePath) && File.Exists(c.FilePath))
            .ToList();
        Assert.IsTrue(inSourceCallees.Count > 0,
            "Expected at least one in-source callee for MakeThemSpeak (IAnimal.Speak).");
        Assert.IsTrue(inSourceCallees.All(c => !string.IsNullOrEmpty(c.PreviewText)),
            $"Every in-source callee must have a non-empty PreviewText. " +
            $"Null callees: [{string.Join(", ", inSourceCallees.Where(c => string.IsNullOrEmpty(c.PreviewText)).Select(c => c.ContainingMember))}]");
    }

    // ── CompletionService ────────────────────────────────────────────

    [TestMethod]
    public async Task GetCompletions_Returns_Items_At_Valid_Position()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 20 column 30 is inside method body where completions are available
        var result = await CompletionService.GetCompletionsAsync(
            WorkspaceId, animalServicePath, 20, 30, filterText: null, maxItems: 100, triggerCharacter: null, CancellationToken.None);

        Assert.IsTrue(result.Items.Count > 0,
            "Expected completion items at a valid position in AnimalService.cs");
    }

    [TestMethod]
    public async Task GetCompletions_Ranking_BoostsInScopeBeforeExternalTypes()
    {
        // BUG fix (get-completions-ranking): in-scope members and locals should outrank
        // namespace-qualified external types for a given prefix. With filterText="To",
        // ToString (an instance method on every System.Object) should appear before
        // ToBase64Transform (a class in System.Security.Cryptography) and other type-tier
        // candidates.
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        var result = await CompletionService.GetCompletionsAsync(
            WorkspaceId, animalServicePath, 20, 30, filterText: "To", maxItems: 100, triggerCharacter: null, CancellationToken.None);

        Assert.IsTrue(result.Items.Count >= 2,
            "Expected at least two completion candidates starting with 'To'.");

        var toStringIndex = -1;
        for (var i = 0; i < result.Items.Count; i++)
        {
            if (result.Items[i].DisplayText == "ToString")
            {
                toStringIndex = i;
                break;
            }
        }
        if (toStringIndex >= 0)
        {
            // For each Class/Struct/Interface/Enum candidate found in the list, ToString
            // (a Method on the in-scope receiver) should appear before it.
            for (var i = 0; i < result.Items.Count; i++)
            {
                if (i == toStringIndex) continue;
                var item = result.Items[i];
                var isType = item.Tags is not null && (item.Tags.Contains("Class") || item.Tags.Contains("Structure")
                    || item.Tags.Contains("Interface") || item.Tags.Contains("Enum") || item.Tags.Contains("Delegate"));
                if (isType)
                {
                    Assert.IsTrue(toStringIndex < i,
                        $"In-scope ToString (rank=method) should appear before type-tier candidate '{item.DisplayText}' (rank=type).");
                }
            }
        }
    }

    [TestMethod]
    public async Task GetCompletions_WithDotTrigger_AtMemberAccess_PromotesInstanceMembersBeforeExternalTypes()
    {
        // BUG fix (get-completions-filtertext-doesnt-promote-in-scope-members): at a member-
        // access position (right after a '.'), the CompletionService must pass an explicit
        // CompletionTrigger.CreateInsertionTrigger('.') to Roslyn — otherwise Roslyn returns
        // only the global accessible-type set and the InScopeRank sort has no method-tier
        // candidates to promote. With triggerCharacter='.', instance members on the receiver
        // (e.g. animal.ToString from System.Object) must be ranked before namespace-qualified
        // external types like System.Security.Cryptography.ToBase64Transform.
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 20: "            var sound = animal.Speak();"
        // Column 32 is the position immediately after the '.' between 'animal' and 'Speak()'.
        var result = await CompletionService.GetCompletionsAsync(
            WorkspaceId, animalServicePath, 20, 32, filterText: "To", maxItems: 100, triggerCharacter: '.', CancellationToken.None);

        Assert.IsTrue(result.Items.Count >= 1,
            "Expected at least one completion candidate starting with 'To' at a member-access position.");

        // Method-tier rank (rank=1 per InScopeRank): Tags contain Method/Property/Field/Event/ExtensionMethod.
        var firstMethodTierIndex = -1;
        for (var i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            var isMethodTier = item.Tags is not null && (item.Tags.Contains("Method") || item.Tags.Contains("Property")
                || item.Tags.Contains("Field") || item.Tags.Contains("Event") || item.Tags.Contains("ExtensionMethod"));
            if (isMethodTier)
            {
                firstMethodTierIndex = i;
                break;
            }
        }

        Assert.IsTrue(firstMethodTierIndex >= 0,
            "Expected at least one method-tier candidate (Method/Property/Field/Event/ExtensionMethod) " +
            "with triggerCharacter='.' — without the explicit insertion trigger Roslyn omits instance " +
            "members and the in-scope ranking has nothing to promote.");

        // For each type-tier candidate (Class/Struct/Interface/Enum/Delegate) in the result,
        // the first method-tier candidate must appear before it. This is the contract the
        // bug fix restores: with the '.' trigger, Roslyn emits instance methods on the
        // receiver AND InScopeRank then orders them ahead of namespace-qualified externals.
        for (var i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            var isType = item.Tags is not null && (item.Tags.Contains("Class") || item.Tags.Contains("Structure")
                || item.Tags.Contains("Interface") || item.Tags.Contains("Enum") || item.Tags.Contains("Delegate"));
            if (isType)
            {
                Assert.IsTrue(firstMethodTierIndex < i,
                    $"First method-tier candidate (index {firstMethodTierIndex}) should appear before " +
                    $"type-tier candidate '{item.DisplayText}' (index {i}).");
            }
        }
    }

    // ── TestDiscoveryService ─────────────────────────────────────────

    [TestMethod]
    public async Task DiscoverTests_Finds_Sample_Tests()
    {
        var result = await TestDiscoveryService.DiscoverTestsAsync(
            WorkspaceId, CancellationToken.None);

        Assert.IsTrue(result.TestProjects.Count > 0,
            "Expected at least one test project to be discovered in the sample solution");
    }

    // ── TestRunnerService ────────────────────────────────────────────

    [TestMethod]
    public async Task RunTests_Executes_SampleLib_Tests()
    {
        var result = await TestRunnerService.RunTestsAsync(
            WorkspaceId, "SampleLib.Tests", null, CancellationToken.None);

        Assert.IsNotNull(result,
            "Test run result should not be null for SampleLib.Tests");
    }
}
