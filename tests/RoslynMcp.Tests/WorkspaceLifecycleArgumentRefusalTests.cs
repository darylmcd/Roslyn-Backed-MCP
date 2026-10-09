using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceLifecycleArgumentRefusalTests
{
    private const string ExtensionCorrection = "solution (.sln/.slnx) or C# project (.csproj)";
    private const string Sentinel = "caller-private-lifecycle-sentinel";

    private static WorkspaceManager CreateManager() => new(
        NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
        new FileWatcherService(NullLogger<FileWatcherService>.Instance));

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    public async Task Manager_BlankPathPublishesCorrection(string path)
    {
        using var manager = CreateManager();
        var exception = await Assert.ThrowsExactlyAsync<PublicArgumentException>(
            () => manager.LoadAsync(path, CancellationToken.None));
        AssertRefusal(exception, "path", ExtensionCorrection);
        Assert.HasCount(0, manager.ListWorkspaces());
    }

    [TestMethod]
    public async Task Manager_ExistingUnsupportedPathAndLoaderFallbackAgree()
    {
        var root = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Sentinel + ".txt");
        File.WriteAllText(path, "");
        try
        {
            using var manager = CreateManager();
            var managerError = await Assert.ThrowsExactlyAsync<PublicArgumentException>(
                () => manager.LoadAsync(path, CancellationToken.None));
            var loaderError = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
                new WorkspaceSessionLoader().CreateAndOpenAsync("isolated-loader", path, null,
                    new WorkspaceDiagnosticsSink(16), NullLogger.Instance, CancellationToken.None));
            AssertRefusal(managerError, "path", ExtensionCorrection, path);
            AssertRefusal(loaderError, "path", ExtensionCorrection, path);
            Assert.AreEqual(managerError.PublicMessage, loaderError.PublicMessage);
            Assert.HasCount(0, manager.ListWorkspaces());
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    public async Task Gate_ReadAndWriteBlankIdPublishCorrection(string workspaceId)
    {
        using var manager = CreateManager();
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var invoked = false;
        Task<int> Action(CancellationToken _) { invoked = true; return Task.FromResult(1); }
        var read = await Assert.ThrowsExactlyAsync<PublicArgumentException>(
            () => gate.RunReadAsync(workspaceId, Action, CancellationToken.None));
        var write = await Assert.ThrowsExactlyAsync<PublicArgumentException>(
            () => gate.RunWriteAsync(workspaceId, Action, CancellationToken.None));
        AssertRefusal(read, "workspaceId", "workspace_load");
        AssertRefusal(write, "workspaceId", "workspace_load");
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    public void Resolve_BlankPublishesCorrection(string path) =>
        AssertRefusal(Assert.ThrowsExactly<PublicArgumentException>(() => PhysicalPathResolver.Resolve(path)),
            "path", "non-empty");

    [TestMethod]
    [DataRow("C:caller-private-lifecycle-sentinel")]
    [DataRow("\\caller-private-lifecycle-sentinel")]
    public void Resolve_WindowsAmbiguousPathPublishesCorrection(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Drive-relative and root-relative path semantics require Windows.");
            return;
        }
        AssertRefusal(Assert.ThrowsExactly<PublicArgumentException>(() => PhysicalPathResolver.Resolve(path)),
            "path", "fully qualified or ordinary relative", path);
    }

    [TestMethod]
    public void Resolve_NullAndValidPathIdentitiesRemainIntact()
    {
        var exception = Assert.ThrowsExactly<ArgumentNullException>(() => PhysicalPathResolver.Resolve(null!));
        Assert.AreEqual("path", exception.ParamName);
        var relative = Path.Combine("lifecycle-relative", "child.cs");
        Assert.AreEqual(PhysicalPathResolver.Resolve(Path.GetFullPath(relative)), PhysicalPathResolver.Resolve(relative));
    }

    [TestMethod]
    public async Task Manager_MissingUnsupportedPathStillReportsMissingFileFirst()
    {
        using var manager = CreateManager();
        var missing = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"), Sentinel + ".txt");
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => manager.LoadAsync(missing, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task LoadRefusal_WirePreservesReleasedIdentityAndRedactsPath(string version)
    {
        var root = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Sentinel + ".txt");
        File.WriteAllText(path, "");
        try
        {
            await using var harness = await ProductionParityMcpHarness.CreateAsync("lifecycle-refusals", version,
                services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [root] }));
            var result = await harness.Client.CallToolAsync("workspace_load",
                new Dictionary<string, object?> { ["path"] = path, ["autoRestore"] = false },
                cancellationToken: CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            var raw = result.Content.OfType<TextContentBlock>().Single().Text;
            AssertEnvelope(raw, ExtensionCorrection, path);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private static void AssertRefusal(PublicArgumentException exception, string parameter, string correction, string? path = null)
    {
        Assert.IsInstanceOfType<ArgumentException>(exception);
        Assert.AreEqual(parameter, exception.ParamName);
        StringAssert.Contains(exception.PublicMessage, correction);
        Assert.IsFalse(exception.Message.Contains(Sentinel, StringComparison.Ordinal));
        AssertEnvelope(ToolErrorHandler.ClassifyAndFormat(exception, "workspace_load"), correction, path);
    }

    private static void AssertEnvelope(string raw, string correction, string? path)
    {
        var payload = JsonNode.Parse(raw)!;
        Assert.AreEqual(true, payload["error"]!.GetValue<bool>());
        Assert.AreEqual("InvalidArgument", payload["category"]!.GetValue<string>());
        Assert.AreEqual("ArgumentException", payload["exceptionType"]!.GetValue<string>());
        StringAssert.Contains(payload["message"]!.GetValue<string>(), correction);
        Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), raw);
        if (path is not null)
            Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(path).Trim('"'), StringComparison.Ordinal), raw);
    }
}
