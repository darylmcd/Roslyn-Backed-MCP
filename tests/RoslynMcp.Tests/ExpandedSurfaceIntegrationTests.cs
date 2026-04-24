using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class ExpandedSurfaceIntegrationTests : SharedWorkspaceTestBase
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

    [TestMethod]
    public async Task GetCompletions_Returns_Service_Members()
    {
        var programFile = FindDocumentPath("Program.cs");
        var json = await SymbolTools.GetCompletions(
            null!,
            WorkspaceExecutionGate,
            CompletionService,
            WorkspaceId,
            programFile,
            line: 6,
            column: 9,
            filterText: null,
            maxItems: 100,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.IsTrue(items.GetArrayLength() > 0, "Expected completion items.");
        Assert.IsTrue(items.EnumerateArray().Any(item => item.GetProperty("displayText").GetString() == "MakeThemSpeak"));
    }

    [TestMethod]
    public async Task GetSyntaxTree_Returns_Hierarchy_Json()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await SyntaxTools.GetSyntaxTree(
            null!,
            WorkspaceExecutionGate,
            SyntaxService,
            WorkspaceId,
            filePath,
            startLine: null,
            endLine: null,
            maxDepth: 2,
            maxOutputChars: 65536,
            maxNodes: 5000,
            maxTotalBytes: 65536,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("CompilationUnit", doc.RootElement.GetProperty("kind").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("children").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task AdvancedAnalysisTools_Return_Expected_Results()
    {
        var namespaceJson = await AdvancedAnalysisTools.GetNamespaceDependencies(
            WorkspaceExecutionGate,
            NamespaceDependencyService,
            WorkspaceId,
            projectName: "SampleLib",
            circularOnly: false,
            CancellationToken.None);
        using var namespaceDoc = JsonDocument.Parse(namespaceJson);
        Assert.IsTrue(namespaceDoc.RootElement.GetProperty("nodes").GetArrayLength() > 0);

        var complexityJson = await AdvancedAnalysisTools.GetComplexityMetrics(
            WorkspaceExecutionGate,
            CodeMetricsService,
            WorkspaceId,
            filePath: null,
            filePaths: null,
            projectName: "SampleLib",
            minComplexity: 1,
            limit: 20,
            ct: CancellationToken.None);
        using var complexityDoc = JsonDocument.Parse(complexityJson);
        Assert.IsTrue(complexityDoc.RootElement.GetProperty("metrics").GetArrayLength() > 0);

        var deadFieldsJson = await AdvancedAnalysisTools.FindDeadFields(
            WorkspaceExecutionGate,
            UnusedCodeAnalyzer,
            WorkspaceId,
            projectFilter: "SampleLib",
            includePublic: false,
            usageKind: "never-read",
            limit: 20,
            ct: CancellationToken.None);
        using var deadFieldsDoc = JsonDocument.Parse(deadFieldsJson);
        var deadFields = deadFieldsDoc.RootElement.GetProperty("deadFields").EnumerateArray().ToList();
        Assert.IsTrue(deadFields.Any(field =>
            field.GetProperty("symbolName").GetString() == "_unusedForDiagnostics"
            && field.GetProperty("usageKind").GetString() == "never-read"));
    }

    [TestMethod]
    public async Task AdvancedAnalysisTools_And_Diagnostics_Support_Pagination()
    {
        var analyzersJson = await AnalyzerInfoTools.ListAnalyzers(
            WorkspaceExecutionGate,
            AnalyzerInfoService,
            WorkspaceId,
            projectName: null,
            offset: 0,
            limit: 2,
            CancellationToken.None);
        using var analyzersDoc = JsonDocument.Parse(analyzersJson);
        Assert.IsTrue(analyzersDoc.RootElement.GetProperty("totalRules").GetInt32() >= 1);
        Assert.IsTrue(analyzersDoc.RootElement.GetProperty("returnedRules").GetInt32() <= 2);
        Assert.IsTrue(analyzersDoc.RootElement.TryGetProperty("hasMore", out _));

        var diagnosticsJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: "SampleLib",
            file: null,
            severity: null,
            diagnosticId: null,
            offset: 0,
            limit: 1,
            summary: false,
            progress: null,
            ct: CancellationToken.None);
        using var diagnosticsDoc = JsonDocument.Parse(diagnosticsJson);
        var returnedDiagnostics = diagnosticsDoc.RootElement.GetProperty("returnedDiagnostics").GetInt32();
        var pagedCount = diagnosticsDoc.RootElement.GetProperty("workspaceDiagnostics").GetArrayLength()
            + diagnosticsDoc.RootElement.GetProperty("compilerDiagnostics").GetArrayLength()
            + diagnosticsDoc.RootElement.GetProperty("analyzerDiagnostics").GetArrayLength();

        Assert.AreEqual(returnedDiagnostics, pagedCount);
        Assert.IsTrue(diagnosticsDoc.RootElement.GetProperty("totalDiagnostics").GetInt32() >= returnedDiagnostics);
        if (diagnosticsDoc.RootElement.GetProperty("hasMore").GetBoolean())
        {
            Assert.IsTrue(
                diagnosticsDoc.RootElement.GetProperty("paginationNote").GetString()?.Length > 0,
                "hasMore should surface paginationNote for follow-up paging.");
        }
    }

    [TestMethod]
    public async Task ProjectDiagnostics_IncludesSplitErrorCounts()
    {
        var diagnosticsJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: "SampleLib",
            file: null,
            severity: null,
            diagnosticId: null,
            offset: 0,
            limit: 50,
            summary: false,
            progress: null,
            ct: CancellationToken.None);
        using var diagnosticsDoc = JsonDocument.Parse(diagnosticsJson);
        Assert.IsTrue(diagnosticsDoc.RootElement.TryGetProperty("compilerErrors", out var ce));
        Assert.IsTrue(diagnosticsDoc.RootElement.TryGetProperty("analyzerErrors", out var ae));
        Assert.IsTrue(diagnosticsDoc.RootElement.TryGetProperty("workspaceErrors", out var we));
        var total = diagnosticsDoc.RootElement.GetProperty("totalErrors").GetInt32();
        Assert.AreEqual(total, ce.GetInt32() + ae.GetInt32() + we.GetInt32());
    }

    [TestMethod]
    public async Task Symbol_And_Usage_Tools_Apply_Pagination_Metadata()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");
        var refsJson = await SymbolTools.FindReferences(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            filePath: animalServicePath,
            line: 16,
            column: 17,
            symbolHandle: null,
            limit: 1,
            offset: 0,
            ct: CancellationToken.None);

        using var refsDoc = JsonDocument.Parse(refsJson);
        Assert.IsTrue(refsDoc.RootElement.GetProperty("count").GetInt32() <= 1);
        Assert.IsTrue(refsDoc.RootElement.TryGetProperty("totalCount", out _));
        Assert.IsTrue(refsDoc.RootElement.TryGetProperty("hasMore", out _));

        var usagesJson = await AnalysisTools.FindTypeUsages(
            WorkspaceExecutionGate,
            MutationAnalysisService,
            WorkspaceId,
            filePath: null,
            line: null,
            column: null,
            symbolHandle: null,
            metadataName: "SampleLib.IAnimal",
            limit: 1,
            offset: 0,
            CancellationToken.None);

        using var usagesDoc = JsonDocument.Parse(usagesJson);
        Assert.IsTrue(usagesDoc.RootElement.GetProperty("count").GetInt32() <= 1);
        Assert.IsTrue(usagesDoc.RootElement.TryGetProperty("totalCount", out _));
        Assert.IsTrue(usagesDoc.RootElement.TryGetProperty("hasMore", out _));
    }

    [TestMethod]
    public async Task FindReferencesBulk_Applies_Summary_And_PerSymbolLimit_Before_Envelope()
    {
        // find-references-bulk-summary-mode: before this change, find_references_bulk applied no
        // per-symbol cap and always serialized preview text for every hit. Across a 2-symbol batch
        // against high-fan-out targets, the aggregate envelope overflowed the MCP payload cap
        // (observed 120 KB). Two expectations:
        //   1) summary=true drops PreviewText from every returned reference.
        //   2) maxItemsPerSymbol=N caps each symbol's reference list to N items BEFORE the outer
        //      envelope is assembled. `truncated=true` + `referenceCount > returnedCount` signal
        //      that the caller should follow up with find_references (paged) for full coverage.

        var symbols = new[]
        {
            new BulkSymbolLocator(SymbolHandle: null, MetadataName: "SampleLib.IAnimal",
                FilePath: null, Line: null, Column: null),
            new BulkSymbolLocator(SymbolHandle: null, MetadataName: "SampleLib.Dog",
                FilePath: null, Line: null, Column: null),
        };

        // Baseline: no summary, no cap — capture sizes so we can assert the bounded shape is
        // strictly smaller AND the per-symbol cap actually trimmed at least one symbol.
        var fullJson = await SymbolTools.FindReferencesBulk(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            symbols,
            includeDefinition: false,
            summary: false,
            maxItemsPerSymbol: 100,
            ct: CancellationToken.None);

        using var fullDoc = JsonDocument.Parse(fullJson);
        var fullResults = fullDoc.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.AreEqual(2, fullResults.Count);
        foreach (var r in fullResults)
        {
            Assert.IsFalse(r.GetProperty("truncated").GetBoolean(),
                "Baseline run with maxItemsPerSymbol=100 should not truncate SampleLib targets.");
        }

        // Bounded: summary=true strips preview text, maxItemsPerSymbol=1 trims each symbol to
        // one reference. That floor (1 ref × 2 symbols) guarantees the envelope is smaller than
        // the baseline for this sample workspace, which has multiple refs per symbol.
        var boundedJson = await SymbolTools.FindReferencesBulk(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            symbols,
            includeDefinition: false,
            summary: true,
            maxItemsPerSymbol: 1,
            ct: CancellationToken.None);

        using var boundedDoc = JsonDocument.Parse(boundedJson);
        Assert.AreEqual(2, boundedDoc.RootElement.GetProperty("count").GetInt32());
        Assert.IsTrue(boundedDoc.RootElement.GetProperty("summary").GetBoolean());
        Assert.AreEqual(1, boundedDoc.RootElement.GetProperty("maxItemsPerSymbol").GetInt32());

        var boundedResults = boundedDoc.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.AreEqual(2, boundedResults.Count);

        var anyTruncated = false;
        foreach (var r in boundedResults)
        {
            // referenceCount is the pre-cap total; returnedCount is what we actually included.
            var totalRefs = r.GetProperty("referenceCount").GetInt32();
            var returned = r.GetProperty("returnedCount").GetInt32();
            Assert.IsTrue(returned <= 1,
                $"maxItemsPerSymbol=1 must cap returned list (saw {returned}).");

            var refs = r.GetProperty("references").EnumerateArray().ToList();
            Assert.AreEqual(returned, refs.Count,
                "references array length must match returnedCount.");

            // Every included ref must have PreviewText omitted under summary=true. JSON serializer
            // emits null fields explicitly — either the property is absent or its value is null.
            foreach (var refEl in refs)
            {
                if (refEl.TryGetProperty("previewText", out var previewEl))
                {
                    Assert.AreEqual(JsonValueKind.Null, previewEl.ValueKind,
                        "summary=true must null out previewText on every returned reference.");
                }
            }

            var truncated = r.GetProperty("truncated").GetBoolean();
            if (truncated)
            {
                anyTruncated = true;
                Assert.IsTrue(totalRefs > returned,
                    "truncated=true must imply referenceCount > returnedCount.");
            }
        }

        Assert.IsTrue(anyTruncated,
            "With maxItemsPerSymbol=1 against 2 high-fan-out SampleLib targets, at least one result must report truncated=true.");

        // Aggregate payload size must strictly shrink — this is the whole point of the knob.
        Assert.IsTrue(boundedJson.Length < fullJson.Length,
            $"Bounded envelope ({boundedJson.Length} bytes) must be smaller than baseline ({fullJson.Length} bytes) when summary+cap are applied.");
    }

    [TestMethod]
    public async Task FindReferencesBulk_InvalidMaxItemsPerSymbol_Throws()
    {
        var symbols = new[]
        {
            new BulkSymbolLocator(SymbolHandle: null, MetadataName: "SampleLib.IAnimal",
                FilePath: null, Line: null, Column: null),
        };

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => SymbolTools.FindReferencesBulk(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            symbols,
            includeDefinition: false,
            summary: false,
            maxItemsPerSymbol: 0,
            ct: CancellationToken.None));
    }

    [TestMethod]
    public async Task Relationship_And_Cohesion_Tools_Expose_New_Limit_And_Interface_Flags()
    {
        var iAnimalPath = FindDocumentPath("IAnimal.cs");
        var relationshipsJson = await SymbolTools.GetSymbolRelationships(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            filePath: iAnimalPath,
            line: 6,
            column: 12,
            symbolHandle: null,
            metadataName: null,
            limit: 1,
            preferDeclaringMember: true,
            CancellationToken.None);

        using var relationshipsDoc = JsonDocument.Parse(relationshipsJson);
        Assert.AreEqual(1, relationshipsDoc.RootElement.GetProperty("limit").GetInt32());
        Assert.IsTrue(relationshipsDoc.RootElement.TryGetProperty("totals", out _));

        var cohesionJson = await CohesionAnalysisTools.GetCohesionMetrics(
            WorkspaceExecutionGate,
            CohesionAnalysisService,
            WorkspaceId,
            filePath: null,
            projectName: "SampleLib",
            minMethods: 1,
            limit: 50,
            includeInterfaces: true,
            excludeTestProjects: false,
            excludeTests: false,
            CancellationToken.None);

        using var cohesionDoc = JsonDocument.Parse(cohesionJson);
        var metrics = cohesionDoc.RootElement.GetProperty("metrics").EnumerateArray().ToList();
        Assert.IsTrue(metrics.Any(m =>
            m.TryGetProperty("typeKind", out var typeKind) &&
            typeKind.GetString() == "Interface"),
            "Expected at least one interface metric when includeInterfaces=true.");
    }

    [TestMethod]
    public async Task Reflection_And_Di_Analysis_Run_On_Repo_Solution()
    {
        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var status = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var reflectionUsages = await CodePatternAnalyzer.FindReflectionUsagesAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);
            var diRegistrations = await DiRegistrationService.GetDiRegistrationsAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);

            Assert.IsTrue(reflectionUsages.Count > 0, "Expected reflection analysis to complete with results on the repo solution.");
            Assert.IsTrue(diRegistrations.Any(registration =>
                registration.FilePath.EndsWith("ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)),
                "Expected DI registrations from ServiceCollectionExtensions.cs.");
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }

    [TestMethod]
    public void Resources_Return_Machine_Readable_Contracts()
    {
        using var catalogDoc = JsonDocument.Parse(ServerResources.GetServerCatalog());
        Assert.AreEqual("local-first", catalogDoc.RootElement.GetProperty("productShape").GetString());
        // dr-9-11-payload-exceeds-mcp-tool-result-cap (PR #188): default catalog now returns
        // a summary with toolCount + toolsResourceTemplate instead of an inline tools array.
        Assert.IsTrue(catalogDoc.RootElement.GetProperty("toolCount").GetInt32() > 0);

        using var templatesDoc = JsonDocument.Parse(ServerResources.GetResourceTemplates());
        Assert.IsTrue(templatesDoc.RootElement.GetProperty("count").GetInt32() >= 1);
        Assert.IsTrue(templatesDoc.RootElement.GetProperty("resources")
            .EnumerateArray()
            .Any(resource => string.Equals(
                resource.GetProperty("uriTemplate").GetString(),
                "roslyn://workspace/{workspaceId}/status",
                StringComparison.Ordinal)));

        using var workspacesDoc = JsonDocument.Parse(WorkspaceResources.GetWorkspaces(WorkspaceManager));
        Assert.IsTrue(workspacesDoc.RootElement.GetProperty("count").GetInt32() >= 1);
    }

    [TestMethod]
    public async Task Prompts_Return_Actionable_Context()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var reviewMessages = (await RoslynPrompts.ReviewFile(
            WorkspaceManager,
            SymbolSearchService,
            DiagnosticService,
            WorkspaceId,
            filePath,
            CancellationToken.None)).ToList();

        Assert.AreEqual(1, reviewMessages.Count);
        var reviewText = GetPromptText(reviewMessages[0]);
        StringAssert.Contains(reviewText, "AnimalService.cs");
        StringAssert.Contains(reviewText, "Perform a thorough code review");

        var dependencyMessages = (await RoslynPrompts.AnalyzeDependencies(
            WorkspaceManager,
            NamespaceDependencyService,
            NuGetDependencyService,
            WorkspaceId,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, dependencyMessages.Count);
        StringAssert.Contains(GetPromptText(dependencyMessages[0]), "Project Dependency Graph");
    }

    [TestMethod]
    public async Task CodeActionTools_Return_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var actionsJson = await CodeActionTools.GetCodeActions(
            WorkspaceExecutionGate,
            CodeActionService,
            WorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 1,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        using var actionsDoc = JsonDocument.Parse(actionsJson);
        Assert.IsTrue(actionsDoc.RootElement.TryGetProperty("count", out _));
        Assert.IsTrue(actionsDoc.RootElement.TryGetProperty("actions", out _));
    }

    [TestMethod]
    public async Task PreviewCodeAction_Serializes_Service_Response()
    {
        var previewJson = await CodeActionTools.PreviewCodeAction(
            WorkspaceExecutionGate,
            new FakeCodeActionService(),
            WorkspaceId,
            filePath: "C:\\temp\\Example.cs",
            startLine: 1,
            startColumn: 1,
            actionIndex: 0,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(previewJson);
        Assert.AreEqual("preview-token", doc.RootElement.GetProperty("previewToken").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("changes").GetArrayLength() == 1);
    }

    [TestMethod]
    public async Task EditTools_And_MultiFileEdit_Apply_Changes_On_Workspace_Copy()
    {
        var workspacePath = CreateSampleSolutionCopy();
        var tempRoot = Path.GetDirectoryName(workspacePath)!;
        var tempWorkspaceId = await LoadWorkspaceCopyAsync(workspacePath);

        try
        {
            var programFile = FindDocumentPath(tempWorkspaceId, "Program.cs");
            var singleEditJson = await EditTools.ApplyTextEdit(
                null!,
                WorkspaceExecutionGate,
                EditService,
                tempWorkspaceId,
                programFile,
                [new TextEditDto(1, 1, 1, 1, "// edited\n")],
                CancellationToken.None);

            using var singleEditDoc = JsonDocument.Parse(singleEditJson);
            Assert.IsTrue(singleEditDoc.RootElement.GetProperty("editsApplied").GetInt32() == 1);
            StringAssert.Contains(await File.ReadAllTextAsync(programFile), "// edited");

            var animalFile = FindDocumentPath(tempWorkspaceId, "AnimalService.cs");
            var multiEditJson = await MultiFileEditTools.ApplyMultiFileEdit(
                null!,
                WorkspaceExecutionGate,
                EditService,
                tempWorkspaceId,
                [
                    new FileEditsDto(programFile, [new TextEditDto(2, 1, 2, 1, "// second edit\n")]),
                    new FileEditsDto(animalFile, [new TextEditDto(1, 1, 1, 1, "// animal edit\n")])
                ],
                CancellationToken.None);

            using var multiEditDoc = JsonDocument.Parse(multiEditJson);
            Assert.IsTrue(multiEditDoc.RootElement.GetProperty("filesModified").GetInt32() == 2);
            StringAssert.Contains(await File.ReadAllTextAsync(animalFile), "// animal edit");
        }
        finally
        {
            WorkspaceManager.Close(tempWorkspaceId);
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [TestMethod]
    public async Task TestCoverageTool_Returns_Structured_Response()
    {
        var json = await TestCoverageTools.RunTestCoverage(
            WorkspaceExecutionGate,
            WorkspaceManager,
            DotnetCommandRunner,
            WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("success", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
    }

    // test-coverage-vague-error-when-coverlet-missing: if the sample solution's test project
    // doesn't reference coverlet.collector, the tool must return success=false with
    // errorKind=CoverletMissing and a populated missingPackages list. If it DOES reference
    // coverlet, the tool should attempt to run tests (the test doesn't assert success of
    // that run, only that the short-circuit didn't fire when coverlet is present).
    [TestMethod]
    public async Task TestCoverageTool_WhenCoverletMissing_ReturnsStructuredErrorBeforeRunning()
    {
        var json = await TestCoverageTools.RunTestCoverage(
            WorkspaceExecutionGate,
            WorkspaceManager,
            DotnetCommandRunner,
            WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var hasEnvelope = doc.RootElement.TryGetProperty("failureEnvelope", out var envelope)
            && envelope.ValueKind == JsonValueKind.Object;

        // Find whether the sample test csproj references coverlet. The regression test tracks
        // the branch that matches this fixture's state.
        var testProjectPath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib.Tests", "SampleLib.Tests.csproj");
        var hasCoverlet = File.Exists(testProjectPath) &&
            File.ReadAllText(testProjectPath).Contains("coverlet.collector", StringComparison.OrdinalIgnoreCase);

        if (!hasCoverlet)
        {
            Assert.IsTrue(hasEnvelope,
                $"Sample test project lacks coverlet.collector — tool must emit a structured envelope. JSON: {json}");
            Assert.AreEqual("CoverletMissing", envelope.GetProperty("errorKind").GetString());
            Assert.IsTrue(envelope.TryGetProperty("missingPackages", out var missing)
                && missing.ValueKind == JsonValueKind.Array
                && missing.GetArrayLength() >= 1,
                "missingPackages must list the test projects that need coverlet.");
            var names = missing.EnumerateArray().Select(e => e.GetString()).ToList();
            CollectionAssert.Contains(names, "SampleLib.Tests",
                "The sample test project name must appear in missingPackages.");
        }
        else
        {
            // Sample has coverlet — the short-circuit must NOT have fired, so either success
            // is true (coverage ran) or the envelope is something other than CoverletMissing.
            if (hasEnvelope && envelope.TryGetProperty("errorKind", out var kind))
            {
                Assert.AreNotEqual("CoverletMissing", kind.GetString(),
                    "Sample has coverlet but tool short-circuited as CoverletMissing.");
            }
        }
    }

    private static string FindDocumentPath(string name) => FindDocumentPath(WorkspaceId, name);

    private static string FindDocumentPath(string workspaceId, string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }

    private static string GetPromptText(PromptMessage message)
    {
        return (message.Content as TextContentBlock)?.Text
            ?? throw new AssertFailedException("Prompt content was not a text block.");
    }

    private static async Task<string> LoadWorkspaceCopyAsync(string workspacePath)
    {
        var status = await WorkspaceManager.LoadAsync(workspacePath, CancellationToken.None);
        return status.WorkspaceId;
    }

    private sealed class FakeCodeActionService : RoslynMcp.Core.Services.ICodeActionService
    {
        public Task<CodeActionListDto> GetCodeActionsAsync(
            string workspaceId,
            string filePath,
            int startLine,
            int startColumn,
            int? endLine,
            int? endColumn,
            CancellationToken ct)
        {
            IReadOnlyList<CodeActionDto> actions = [new CodeActionDto(0, "Demo action", "Refactor", "demo")];
            return Task.FromResult(new CodeActionListDto(actions.Count, Hint: null, actions));
        }

        public Task<RefactoringPreviewDto> PreviewCodeActionAsync(
            string workspaceId,
            string filePath,
            int startLine,
            int startColumn,
            int? endLine,
            int? endColumn,
            int actionIndex,
            CancellationToken ct)
        {
            return Task.FromResult(new RefactoringPreviewDto(
                "preview-token",
                "Demo preview",
                [new FileChangeDto(filePath, "--- before\n+++ after")],
                null));
        }
    }
}
