using System.Text;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ErrorResponseObservabilityTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [TestMethod]
    public async Task FindReferences_WithUnresolvableHandle_ReturnsStructuredNotFoundEnvelope()
    {
        // Fabricated but structurally valid handle: decodes correctly, has a metadata name,
        // but the symbol does not exist in the workspace. Pre-fix this returned
        // {count:0, totalCount:0, references:[]} which the caller could not distinguish
        // from a legitimate "valid handle, zero references" outcome.
        var fakeHandle = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"MetadataName":"NonExistentNamespace.NonExistentType"}"""));

        var json = await SymbolTools.FindReferences(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            filePath: null,
            line: null,
            column: null,
            symbolHandle: fakeHandle,
            limit: 100,
            offset: 0,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean(),
            "Error envelope must have error: true.");
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString(),
            "Unresolvable handle should map to NotFound category.");
        Assert.AreEqual("find_references", doc.RootElement.GetProperty("tool").GetString(),
            "Tool field must contain the actual tool name, not 'unknown'.");
    }

    [TestMethod]
    public async Task Resource_GetWorkspaceStatus_WithUnknownWorkspaceId_ReturnsErrorEnvelopeWithSourceUri()
    {
        // Pre-fix: a resource exception bubbled to the framework which labelled it
        // tool: "unknown". Post-fix: ExecuteResource catches the exception and emits
        // the canonical error envelope with the resource URI as the tool field.
        var json = await WorkspaceResources.GetWorkspaceStatus(WorkspaceManager, "ffffffffffffffffffffffffffffffff", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("roslyn://workspace/{workspaceId}/status",
            doc.RootElement.GetProperty("tool").GetString(),
            "Resource URI must populate the tool field, not 'unknown'.");
    }

    [TestMethod]
    public void Resource_GetProjects_WithUnknownWorkspaceId_ReturnsErrorEnvelopeWithSourceUri()
    {
        var json = WorkspaceResources.GetProjects(WorkspaceManager, "ffffffffffffffffffffffffffffffff");

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
        Assert.AreEqual("roslyn://workspace/{workspaceId}/projects",
            doc.RootElement.GetProperty("tool").GetString());
    }
}
