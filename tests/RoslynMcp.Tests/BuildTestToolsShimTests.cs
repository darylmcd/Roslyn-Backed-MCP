using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// host-tools-layer-test-coverage-gap: direct Tools-layer smoke coverage for the eight
/// build/test-slice (S04c) tool shims that previously had ZERO tests exercising the
/// <c>*Tools</c> static entry point (only their Core services were tested). Each shim is a
/// thin <c>ToolDispatch</c>-delegating wrapper, so a single direct-invocation test per class
/// — asserting the shim wires its service and serializes a well-formed JSON envelope — is
/// sufficient regression coverage. Three services not exposed on <see cref="TestBase"/>
/// (<see cref="SecurityDiagnosticService"/>, <see cref="SuppressionService"/>,
/// <see cref="TestReferenceMapService"/>) are constructed in-test from already-exposed
/// members plus a fresh <see cref="CompilationCache"/>, avoiding any shared-fixture edit.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class BuildTestToolsShimTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ScaffoldingTools_PreviewScaffoldType_Returns_Json()
    {
        var json = await ScaffoldingTools.PreviewScaffoldType(
            WorkspaceExecutionGate,
            ScaffoldingService,
            WorkspaceId,
            projectName: "SampleLib",
            typeName: "SmokeScaffoldType",
            typeKind: "class",
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task SecurityTools_GetSecurityAnalyzerStatus_Returns_Json()
    {
        var securityService = new SecurityDiagnosticService(
            DiagnosticService,
            WorkspaceManager,
            MsBuildEvaluationService,
            NullLogger<SecurityDiagnosticService>.Instance);

        var json = await SecurityTools.GetSecurityAnalyzerStatus(
            WorkspaceExecutionGate,
            securityService,
            WorkspaceId,
            CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task SuppressionTools_VerifyPragmaSuppresses_Returns_Json()
    {
        var suppressionService = new SuppressionService(
            EditorConfigService,
            EditService,
            WorkspaceManager,
            CompileCheckService);

        var programPath = FindDocumentPath("Program.cs");
        var json = await SuppressionTools.VerifyPragmaSuppresses(
            WorkspaceExecutionGate,
            suppressionService,
            WorkspaceId,
            filePath: programPath,
            line: 1,
            diagnosticId: "CS0168",
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task FixAllTools_PreviewFixAll_Returns_Json()
    {
        var programPath = FindDocumentPath("Program.cs");
        var json = await FixAllTools.PreviewFixAll(
            WorkspaceExecutionGate,
            FixAllService,
            WorkspaceId,
            diagnosticId: "IDE0005",
            scope: "document",
            filePath: programPath,
            projectName: null,
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task EditorConfigTools_GetEditorConfigOptions_Returns_Json()
    {
        var programPath = FindDocumentPath("Program.cs");
        var json = await EditorConfigTools.GetEditorConfigOptions(
            WorkspaceExecutionGate,
            EditorConfigService,
            WorkspaceId,
            filePath: programPath,
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task MSBuildTools_EvaluateMsbuildProperty_Returns_Json()
    {
        var json = await MSBuildTools.EvaluateMsbuildProperty(
            WorkspaceExecutionGate,
            MsBuildEvaluationService,
            WorkspaceId,
            projectName: "SampleLib",
            propertyName: "TargetFramework",
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task ScriptingTools_EvaluateCSharp_Returns_Json()
    {
        var json = await ScriptingTools.EvaluateCSharp(
            ScriptingService,
            code: "1 + 1",
            imports: null,
            timeoutSeconds: null,
            progress: null,
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    [TestMethod]
    public async Task TestReferenceMapTools_BuildTestReferenceMap_Returns_Json()
    {
        var testReferenceMapService = new TestReferenceMapService(
            WorkspaceManager,
            new CompilationCache(WorkspaceManager));

        var json = await TestReferenceMapTools.BuildTestReferenceMap(
            WorkspaceExecutionGate,
            testReferenceMapService,
            WorkspaceId,
            projectName: null,
            offset: 0,
            limit: 50,
            maxMockDriftWarnings: 50,
            ct: CancellationToken.None);

        AssertJsonObject(json);
    }

    private static void AssertJsonObject(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(
            JsonValueKind.Object,
            doc.RootElement.ValueKind,
            $"Tool shim must return a JSON object envelope. Actual: {json}");
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }
}
