using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Wire-level contract for the structured <c>nextCall</c> on <c>compile_check</c>'s
/// <c>restore-required</c> result, asserted on the real tool's serialized JSON.
/// </summary>
[TestClass]
public sealed class CompileCheckRestoreRequiredWireTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task CompileCheckTool_RestoreRequired_EmitsWorkspaceReloadNextCall()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var propsPath = workspace.GetPath("Directory.Packages.props");
        var props = XDocument.Load(propsPath);
        props.Descendants("PackageVersion")
            .Single(e => (string?)e.Attribute("Include") == "Microsoft.NET.Test.Sdk")
            .SetAttributeValue("Version", "0.0.0");
        props.Save(propsPath);
        await workspace.LoadAsync(CancellationToken.None);
        Assert.IsTrue((await WorkspaceManager.GetStatusAsync(workspace.WorkspaceId)).RestoreRequired);

        using var doc = JsonDocument.Parse(await CallCompileCheckAsync(workspace.WorkspaceId));
        var root = doc.RootElement;

        Assert.AreEqual("restore-required", root.GetProperty("readiness").GetString());
        var nextCall = root.GetProperty("nextCall");
        Assert.AreEqual("workspace_reload", nextCall.GetProperty("tool").GetString());
        var arguments = nextCall.GetProperty("arguments");
        Assert.AreEqual(workspace.WorkspaceId, arguments.GetProperty("workspaceId").GetString());
        Assert.IsTrue(arguments.GetProperty("autoRestore").GetBoolean());
    }

    [TestMethod]
    public async Task CompileCheckTool_NotRestoreRequired_OmitsNextCall()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        Assert.IsNotNull(project.Root);
        project.Root.Add(new XElement("ItemGroup",
            new XElement("Analyzer", new XAttribute("Include", "MissingAnalyzer.dll"))));
        project.Save(projectPath);
        await workspace.LoadAsync(CancellationToken.None);
        Assert.IsFalse((await WorkspaceManager.GetStatusAsync(workspace.WorkspaceId)).RestoreRequired);

        using var doc = JsonDocument.Parse(await CallCompileCheckAsync(workspace.WorkspaceId));

        Assert.AreEqual("analyzer-limited", doc.RootElement.GetProperty("readiness").GetString());
        Assert.IsFalse(doc.RootElement.TryGetProperty("nextCall", out _),
            "nextCall must be omitted (not null) on results that do not require a restore.");
    }

    private static Task<string> CallCompileCheckAsync(string workspaceId) =>
        CompileCheckTools.CompileCheck(
            WorkspaceExecutionGate,
            CompileCheckService,
            workspaceId,
            projectName: null,
            emitValidation: false,
            severity: null,
            file: null,
            files: null,
            offset: 0,
            limit: 50,
            ct: CancellationToken.None);
}
