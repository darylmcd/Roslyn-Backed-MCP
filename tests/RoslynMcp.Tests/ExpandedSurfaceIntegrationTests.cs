using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

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
            await GetPathAuthorizedServerAsync(),
            WorkspaceExecutionGate,
            CompletionService,
            WorkspaceId,
            programFile,
            line: 6,
            column: 9,
            filterText: null,
            maxItems: 100,
            triggerCharacter: null,
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
            await GetPathAuthorizedServerAsync(),
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
            projectName: "SampleLib",
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
            requestContext: null!,
            WorkspaceManager,
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

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => SymbolTools.FindReferencesBulk(
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
    public async Task FindReferencesBulk_MoreThan50Symbols_Throws()
    {
        var symbols = Enumerable.Range(0, 51)
            .Select(_ => new BulkSymbolLocator(SymbolHandle: null, MetadataName: "SampleLib.IAnimal",
                FilePath: null, Line: null, Column: null))
            .ToArray();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => SymbolTools.FindReferencesBulk(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            symbols,
            includeDefinition: false,
            summary: false,
            maxItemsPerSymbol: 100,
            ct: CancellationToken.None));
    }

    [TestMethod]
    public async Task GetComplexityMetrics_InvalidLimit_WithUnknownWorkspace_ThrowsBeforeDispatch()
    {
        var gate = new RecordingWorkspaceExecutionGate();
        var service = new RecordingCodeMetricsService();

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => AdvancedAnalysisTools.GetComplexityMetrics(
            gate,
            service,
            workspaceId: "missing-workspace",
            filePath: null,
            filePaths: null,
            projectName: "SampleLib",
            minComplexity: 1,
            limit: -5,
            ct: CancellationToken.None));

        StringAssert.Contains(exception.Message, "Invalid limit '-5'");
        Assert.AreEqual(0, gate.ReadCallCount, "Invalid pagination must fail before workspace dispatch.");
        Assert.AreEqual(0, service.CallCount, "Invalid pagination must fail before metrics collection.");
    }

    [TestMethod]
    public async Task GetComplexityMetrics_ValidLimit_DispatchesAndReturnsMetrics()
    {
        var gate = new RecordingWorkspaceExecutionGate();
        var service = new RecordingCodeMetricsService(
            new ComplexityMetricsDto(
                SymbolName: "Sample.Type.Method",
                SymbolKind: "Method",
                FilePath: "sample.cs",
                Line: 10,
                CyclomaticComplexity: 3,
                LinesOfCode: 12,
                MaxNestingDepth: 2,
                ParameterCount: 1,
                ContainingType: "Sample.Type",
                MaintainabilityIndex: 88.5));

        var json = await AdvancedAnalysisTools.GetComplexityMetrics(
            gate,
            service,
            workspaceId: "workspace-valid",
            filePath: null,
            filePaths: null,
            projectName: "SampleLib",
            minComplexity: 1,
            limit: 1,
            ct: CancellationToken.None);

        Assert.AreEqual(1, gate.ReadCallCount);
        Assert.AreEqual(1, service.CallCount);
        Assert.AreEqual("workspace-valid", service.LastWorkspaceId);
        Assert.AreEqual(1, service.LastLimit);

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("count").GetInt32());
        Assert.AreEqual(
            "Sample.Type.Method",
            document.RootElement.GetProperty("metrics")[0].GetProperty("symbolName").GetString());
    }

    [TestMethod]
    public async Task FindConsumers_Limit_Caps_And_Reports_HasMore()
    {
        var pagedJson = await ConsumerAnalysisTools.FindConsumers(
            WorkspaceExecutionGate,
            ConsumerAnalysisService,
            WorkspaceId,
            metadataName: "SampleLib.IAnimal",
            offset: 0,
            limit: 1,
            ct: CancellationToken.None);

        using var pagedDoc = JsonDocument.Parse(pagedJson);
        var root = pagedDoc.RootElement;
        Assert.AreEqual(1, root.GetProperty("consumers").GetArrayLength(),
            "limit=1 must cap the returned consumers array to a single entry.");
        Assert.AreEqual(1, root.GetProperty("limit").GetInt32());
        Assert.AreEqual(0, root.GetProperty("offset").GetInt32());
        var total = root.GetProperty("totals").GetProperty("consumers").GetInt32();
        Assert.IsTrue(total >= 3, $"IAnimal should report >=3 total consumers, got {total}.");
        Assert.IsTrue(root.GetProperty("hasMore").GetBoolean(),
            "With >1 total consumers and limit=1, hasMore must be true.");
    }

    [TestMethod]
    public async Task FindConsumers_Offset_Skips_Correctly()
    {
        var firstPage = await ConsumerAnalysisTools.FindConsumers(
            WorkspaceExecutionGate,
            ConsumerAnalysisService,
            WorkspaceId,
            metadataName: "SampleLib.IAnimal",
            offset: 0,
            limit: 1,
            ct: CancellationToken.None);

        var secondPage = await ConsumerAnalysisTools.FindConsumers(
            WorkspaceExecutionGate,
            ConsumerAnalysisService,
            WorkspaceId,
            metadataName: "SampleLib.IAnimal",
            offset: 1,
            limit: 1,
            ct: CancellationToken.None);

        using var firstDoc = JsonDocument.Parse(firstPage);
        using var secondDoc = JsonDocument.Parse(secondPage);

        var firstName = firstDoc.RootElement.GetProperty("consumers")[0].GetProperty("typeName").GetString();
        var secondName = secondDoc.RootElement.GetProperty("consumers")[0].GetProperty("typeName").GetString();
        Assert.AreNotEqual(firstName, secondName,
            "offset=1 must skip the first consumer returned at offset=0.");
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
    public async Task CodeActionTools_Return_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var actionsJson = await CodeActionTools.GetCodeActions(
            await GetPathAuthorizedServerAsync(),
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

    // host-analysis-tools-missing-clientroot-path-validation: AnalyzeDataFlow, AnalyzeControlFlow,
    // and GetOperations gained a leading McpServer parameter so they can call
    // ClientRootPathValidator.ValidatePathAgainstRootsAsync before dispatching to their service,
    // mirroring the pattern already covered for GetSyntaxTree/GetCodeActions above. Positive direct
    // calls use the assembly-owned server with explicit sanctioned roots; null server state is
    // fail-closed. The rejection calls below use non-covering configured roots.
    [TestMethod]
    public async Task AnalyzeDataFlow_Returns_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await FlowAnalysisTools.AnalyzeDataFlow(
            await GetPathAuthorizedServerAsync(),
            WorkspaceExecutionGate,
            FlowAnalysisService,
            WorkspaceId,
            filePath,
            startLine: 32,
            endLine: 37,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.ValueKind == JsonValueKind.Object);
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_Returns_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await FlowAnalysisTools.AnalyzeControlFlow(
            await GetPathAuthorizedServerAsync(),
            WorkspaceExecutionGate,
            FlowAnalysisService,
            WorkspaceId,
            filePath,
            startLine: 32,
            endLine: 37,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.ValueKind == JsonValueKind.Object);
    }

    [TestMethod]
    public async Task GetOperations_Returns_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await OperationTools.GetOperations(
            await GetPathAuthorizedServerAsync(),
            WorkspaceExecutionGate,
            OperationService,
            WorkspaceId,
            filePath,
            line: 27,
            column: 16,
            maxDepth: 3,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.ValueKind == JsonValueKind.Object);
    }

    // ── Root-rejection regression coverage ───────────────────────────────────
    // Positive calls above use covering server-owned roots. These pin the complementary contract:
    // a requested filePath outside the configured boundary is rejected for all five endpoints.

    [TestMethod]
    public async Task GetCodeActions_Rejects_FilePath_Outside_SanctionedRoot()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var sanctionedRoot = CreateUnrelatedSanctionedRootDirectory();
        await using var harness = await CreateServerWithSanctionedRootAsync(sanctionedRoot, CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => CodeActionTools.GetCodeActions(
            harness.Server,
            WorkspaceExecutionGate,
            CodeActionService,
            WorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 1,
            endLine: null,
            endColumn: null,
            CancellationToken.None));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    [TestMethod]
    public async Task PreviewCodeAction_Rejects_FilePath_Outside_SanctionedRoot()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var sanctionedRoot = CreateUnrelatedSanctionedRootDirectory();
        await using var harness = await CreateServerWithSanctionedRootAsync(sanctionedRoot, CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => CodeActionTools.PreviewCodeAction(
            harness.Server,
            WorkspaceExecutionGate,
            CodeActionService,
            WorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 1,
            actionIndex: 0,
            endLine: null,
            endColumn: null,
            CancellationToken.None));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    [TestMethod]
    public async Task AnalyzeDataFlow_Rejects_FilePath_Outside_SanctionedRoot()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var sanctionedRoot = CreateUnrelatedSanctionedRootDirectory();
        await using var harness = await CreateServerWithSanctionedRootAsync(sanctionedRoot, CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => FlowAnalysisTools.AnalyzeDataFlow(
            harness.Server,
            WorkspaceExecutionGate,
            FlowAnalysisService,
            WorkspaceId,
            filePath,
            startLine: 32,
            endLine: 37,
            CancellationToken.None));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    [TestMethod]
    public async Task AnalyzeControlFlow_Rejects_FilePath_Outside_SanctionedRoot()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var sanctionedRoot = CreateUnrelatedSanctionedRootDirectory();
        await using var harness = await CreateServerWithSanctionedRootAsync(sanctionedRoot, CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => FlowAnalysisTools.AnalyzeControlFlow(
            harness.Server,
            WorkspaceExecutionGate,
            FlowAnalysisService,
            WorkspaceId,
            filePath,
            startLine: 32,
            endLine: 37,
            CancellationToken.None));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    [TestMethod]
    public async Task GetOperations_Rejects_FilePath_Outside_SanctionedRoot()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var sanctionedRoot = CreateUnrelatedSanctionedRootDirectory();
        await using var harness = await CreateServerWithSanctionedRootAsync(sanctionedRoot, CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => OperationTools.GetOperations(
            harness.Server,
            WorkspaceExecutionGate,
            OperationService,
            WorkspaceId,
            filePath,
            line: 27,
            column: 16,
            maxDepth: 3,
            CancellationToken.None));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    /// <summary>
    /// Returns a fresh temp directory guaranteed to NOT be an ancestor of the shared sample
    /// workspace's files (which live under the repository's fixture tree), so any document
    /// path resolved via <see cref="FindDocumentPath(string)"/> falls outside it.
    /// </summary>
    private static string CreateUnrelatedSanctionedRootDirectory()
    {
        var dir = Path.Combine(TestTempRoot.Current, "sanctioned-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Wires a real <see cref="McpServer"/> to a real <see cref="McpClient"/> over an in-memory
    /// duplex pipe and registers <paramref name="sanctionedRoot"/> as the server-owned configured
    /// boundary consumed by <see cref="ClientRootPathValidator.ValidatePathAgainstRootsAsync"/>.
    /// The client intentionally advertises no Roots capability: the boundary remains server-owned.
    /// Dispose the returned harness to tear down the client and stop the server's receive loop.
    /// </summary>
    private static async Task<InMemoryMcpClientServerHarness> CreateServerWithSanctionedRootAsync(
        string sanctionedRoot, CancellationToken ct)
    {
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "test-server",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "sanctioned-root",
            cancellationToken: ct,
            serverServicesFactory: () => new ServiceCollection()
                .AddSingleton(new SecurityOptions { SanctionedRoots = [sanctionedRoot] })
                .BuildServiceProvider()).ConfigureAwait(false);
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
                await GetPathAuthorizedServerAsync(),
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
                await GetPathAuthorizedServerAsync(),
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

    /// <summary>
    /// path-boundary-link-swap-toctou: workspace document selection must stay pinned to the physical
    /// identity loaded by Roslyn and reject a client path whose link target changes after validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deterministic stand-in for the race: the workspace is requested THROUGH a directory link that
    /// lives inside the sanctioned root, load pins Roslyn's document identity to the physical target,
    /// validation pins the link-resolved request target, and only then is the link re-pointed outside
    /// the boundary. Before the fixes, either Roslyn or <c>PersistDocumentTextToDiskAsync</c> could
    /// re-walk the logical request path and land a post-swap write out of boundary.
    /// </para>
    /// <para>
    /// The regression also proves the former Roslyn-level residual gap is closed: after load,
    /// <c>Document.FilePath</c> is physical, so <c>MSBuildWorkspace.TryApplyChanges</c> cannot follow
    /// the swapped logical link. The decoy must remain byte-identical through the rejected apply.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ApplyTextEdit_PhysicallyPinnedWorkspace_Rejects_LinkSwap_After_Validation()
    {
        var workspacePath = CreateSampleSolutionCopy();
        var copyRoot = Path.GetDirectoryName(workspacePath)!;
        var hostRoot = Path.Combine(TestTempRoot.Current, "rmcp-linkswap-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(hostRoot, "sanctioned");
        var outsideRoot = Path.Combine(hostRoot, "outside");
        var realRoot = Path.Combine(sanctionedRoot, "real");
        var linkRoot = Path.Combine(sanctionedRoot, "link");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outsideRoot);
        Directory.Move(copyRoot, realRoot);

        string? tempWorkspaceId = null;
        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(linkRoot, realRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            tempWorkspaceId = await LoadWorkspaceCopyAsync(
                Path.Combine(linkRoot, Path.GetFileName(workspacePath)));
            var realProgramFile = FindDocumentPath(tempWorkspaceId, "Program.cs");
            StringAssert.StartsWith(realProgramFile, realRoot,
                "Workspace load must pin Roslyn document paths to the physical tree.");

            var relativeProgramPath = Path.GetRelativePath(realRoot, realProgramFile);
            var linkedProgramFile = Path.GetFullPath(Path.Combine(linkRoot, relativeProgramPath));
            var swappedProgramFile = Path.Combine(outsideRoot, relativeProgramPath);

            await using var harness = await CreateServerWithSanctionedRootAsync(
                sanctionedRoot, CancellationToken.None);

            var canonical = await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                harness.Server, linkedProgramFile, CancellationToken.None);
            Assert.AreEqual(realProgramFile, canonical,
                "Validation must hand back the in-boundary target it resolved through the link.");

            // Pre-swap: the tool wiring carries the canonical target into the write end-to-end.
            var preSwapJson = await EditTools.ApplyTextEdit(
                harness.Server,
                WorkspaceExecutionGate,
                EditService,
                tempWorkspaceId,
                linkedProgramFile,
                [new TextEditDto(1, 1, 1, 1, "// pre-swap edit\n")],
                CancellationToken.None);
            using (var preSwapDoc = JsonDocument.Parse(preSwapJson))
            {
                Assert.AreEqual(1, preSwapDoc.RootElement.GetProperty("editsApplied").GetInt32());
            }

            StringAssert.Contains(await File.ReadAllTextAsync(realProgramFile), "// pre-swap edit");

            // Swap the link out from under the already-validated path.
            Directory.CreateDirectory(Path.GetDirectoryName(swappedProgramFile)!);
            await File.WriteAllTextAsync(swappedProgramFile, "// swapped-in decoy\n");
            Directory.Delete(linkRoot);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(linkRoot, outsideRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            // The swap really did move the request path out of the boundary: a fresh validation
            // of the same string now fails. This is what an attacker races against.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    harness.Server, linkedProgramFile, CancellationToken.None));

            var realBytesBeforeRejectedApply = await File.ReadAllBytesAsync(
                realProgramFile, CancellationToken.None);
            var swappedBytesBeforeRejectedApply = await File.ReadAllBytesAsync(
                swappedProgramFile, CancellationToken.None);

            // A request path that changed physical identity between validation and document
            // resolution must fail closed. The earlier canonical target is write authority only;
            // it must not let a stale logical request select a different document identity.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EditService.ApplyTextEditsAsync(
                    tempWorkspaceId,
                    linkedProgramFile,
                    [new TextEditDto(1, 1, 1, 1, "// rejected edit\n")],
                    "apply_text_edit",
                    CancellationToken.None,
                    canonicalWritePath: canonical));

            CollectionAssert.AreEqual(
                realBytesBeforeRejectedApply,
                await File.ReadAllBytesAsync(realProgramFile, CancellationToken.None),
                "A swapped logical request must not mutate the workspace's physical document.");
            CollectionAssert.AreEqual(
                swappedBytesBeforeRejectedApply,
                await File.ReadAllBytesAsync(swappedProgramFile, CancellationToken.None),
                "A swapped logical request must not mutate its new out-of-boundary target.");
        }
        finally
        {
            if (tempWorkspaceId is not null)
            {
                WorkspaceManager.Close(tempWorkspaceId);
            }

            DeleteDirectoryIfExists(hostRoot);
        }
    }

    /// <summary>
    /// path-boundary-link-swap-toctou: the <c>apply_text_edit</c> TOOL must forward the
    /// boundary-canonicalized target that <see cref="ClientRootPathValidator"/> returned into
    /// <c>IEditService.ApplyTextEditsAsync</c> — the seam the sibling end-to-end test cannot
    /// observe directly, because pre-swap the link still resolves to the in-boundary file.
    /// </summary>
    /// <remarks>
    /// Inverting by construction: the workspace is requested through a directory link but retains
    /// physical document identities. The test reconstructs the client's logical request path, so
    /// the validator's canonical result is a DIFFERENT string. Dropping the
    /// <c>canonicalWritePath:</c> argument at
    /// <c>EditTools.ApplyTextEdit</c> makes the captured value <c>null</c>; forwarding the
    /// un-canonicalized request path instead makes it the link path. Both fail here.
    /// </remarks>
    [TestMethod]
    public async Task ApplyTextEdit_Tool_Forwards_BoundaryCanonicalPath_To_EditService()
    {
        var workspacePath = CreateSampleSolutionCopy();
        var copyRoot = Path.GetDirectoryName(workspacePath)!;
        var hostRoot = Path.Combine(TestTempRoot.Current, "rmcp-canonfwd-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(hostRoot, "sanctioned");
        var realRoot = Path.Combine(sanctionedRoot, "real");
        var linkRoot = Path.Combine(sanctionedRoot, "link");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.Move(copyRoot, realRoot);

        string? tempWorkspaceId = null;
        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(linkRoot, realRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            tempWorkspaceId = await LoadWorkspaceCopyAsync(
                Path.Combine(linkRoot, Path.GetFileName(workspacePath)));
            var physicalProgramFile = FindDocumentPath(tempWorkspaceId, "Program.cs");
            StringAssert.StartsWith(physicalProgramFile, realRoot,
                "Workspace load must pin Roslyn document paths to the physical tree.");
            var relativeProgramPath = Path.GetRelativePath(realRoot, physicalProgramFile);
            var linkedProgramFile = Path.GetFullPath(Path.Combine(linkRoot, relativeProgramPath));

            await using var harness = await CreateServerWithSanctionedRootAsync(
                sanctionedRoot, CancellationToken.None);

            var canonical = await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                harness.Server, linkedProgramFile, CancellationToken.None);
            Assert.AreNotEqual(linkedProgramFile, canonical,
                "Test premise: the canonical target must differ from the request path, otherwise " +
                "this test could not distinguish forwarding the canonical path from forwarding the " +
                "raw request path.");

            var capturing = new CanonicalPathCapturingEditService();
            var json = await EditTools.ApplyTextEdit(
                harness.Server,
                WorkspaceExecutionGate,
                capturing,
                tempWorkspaceId,
                linkedProgramFile,
                [new TextEditDto(1, 1, 1, 1, "// forwarded\n")],
                CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual(1, doc.RootElement.GetProperty("editsApplied").GetInt32());

            Assert.IsTrue(capturing.Invoked, "The tool must have reached IEditService.");
            Assert.AreEqual(linkedProgramFile, capturing.FilePath,
                "The request path is forwarded unchanged; only the write target is pinned.");
            Assert.AreEqual(canonical, capturing.CanonicalWritePath,
                "apply_text_edit must hand the edit service the boundary-canonicalized target the " +
                "validator approved. A null here means the canonicalWritePath argument was dropped; " +
                "the link path here means the un-canonicalized request path was forwarded instead.");
        }
        finally
        {
            if (tempWorkspaceId is not null)
            {
                WorkspaceManager.Close(tempWorkspaceId);
            }

            DeleteDirectoryIfExists(hostRoot);
        }
    }

    /// <summary>
    /// Records the <c>canonicalWritePath</c> the <c>apply_text_edit</c> tool forwards, so the
    /// tool-layer wiring can be asserted without going through a physical write.
    /// </summary>
    private sealed class CanonicalPathCapturingEditService : RoslynMcp.Core.Services.IEditService
    {
        public bool Invoked { get; private set; }

        public string? FilePath { get; private set; }

        public string? CanonicalWritePath { get; private set; }

        public Task<TextEditResultDto> ApplyTextEditsAsync(
            string workspaceId,
            string filePath,
            IReadOnlyList<TextEditDto> edits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false,
            string? canonicalWritePath = null)
        {
            Invoked = true;
            FilePath = filePath;
            CanonicalWritePath = canonicalWritePath;
            return Task.FromResult(new TextEditResultDto(true, filePath, edits.Count, []));
        }

        public Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
            string workspaceId,
            IReadOnlyList<FileEditsDto> fileEdits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false) =>
            throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
            string workspaceId,
            IReadOnlyList<FileEditsDto> fileEdits,
            CancellationToken ct,
            bool skipSyntaxCheck = false) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingWorkspaceExecutionGate : IWorkspaceExecutionGate
    {
        public int ReadCallCount { get; private set; }

        public async Task<T> RunReadAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct)
        {
            ReadCallCount++;
            return await action(ct).ConfigureAwait(false);
        }

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            throw new NotSupportedException();

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            throw new NotSupportedException();

        public void RemoveGate(string workspaceId) => throw new NotSupportedException();
    }

    private sealed class RecordingCodeMetricsService(params ComplexityMetricsDto[] results) : ICodeMetricsService
    {
        public int CallCount { get; private set; }

        public string? LastWorkspaceId { get; private set; }

        public int? LastLimit { get; private set; }

        public Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
            string workspaceId,
            string? filePath,
            IReadOnlyList<string>? filePaths,
            string? projectFilter,
            int? minComplexity,
            int limit,
            CancellationToken ct)
        {
            CallCount++;
            LastWorkspaceId = workspaceId;
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<ComplexityMetricsDto>>(results);
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

    private static async Task<string> LoadWorkspaceCopyAsync(string workspacePath)
    {
        var status = await WorkspaceManager.LoadAsync(workspacePath, CancellationToken.None);
        return status.WorkspaceId;
    }
}
