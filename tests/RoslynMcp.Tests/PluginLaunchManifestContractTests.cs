using System.Text.Json;
using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PluginLaunchManifestContractTests
{
    [TestMethod]
    public void PluginLaunch_UsesReleaseMatchedDnxPackageWithoutGlobalShim()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var canonicalVersion = XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants("Version")
            .Single()
            .Value;
        using var pluginDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, ".claude-plugin", "plugin.json")));
        using var launchDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, ".claude-plugin", "mcp.json")));
        using var registryDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, ".claude-plugin", "server.json")));

        Assert.AreEqual("./.claude-plugin/mcp.json",
            pluginDocument.RootElement.GetProperty("mcpServers").GetString());

        var launch = launchDocument.RootElement.GetProperty("roslyn");
        Assert.AreEqual("stdio", launch.GetProperty("type").GetString());
        Assert.AreEqual("dnx", launch.GetProperty("command").GetString());
        CollectionAssert.AreEqual(
            new[]
            {
                $"Darylmcd.RoslynMcp@{canonicalVersion}",
                "--source",
                "https://api.nuget.org/v3/index.json",
            },
            launch.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.AreEqual(".", launch.GetProperty("env")
            .GetProperty("ROSLYNMCP_SANCTIONED_ROOTS").GetString());

        var registry = registryDocument.RootElement;
        var package = registry.GetProperty("packages")[0];
        Assert.AreEqual(canonicalVersion, registry.GetProperty("version").GetString());
        Assert.AreEqual("Darylmcd.RoslynMcp", package.GetProperty("identifier").GetString());
        Assert.AreEqual(canonicalVersion, package.GetProperty("version").GetString());
        Assert.AreEqual("dnx", package.GetProperty("runtimeHint").GetString());
        Assert.AreEqual("https://api.nuget.org/v3/index.json",
            package.GetProperty("registryBaseUrl").GetString());
        Assert.AreEqual("stdio", package.GetProperty("transport").GetProperty("type").GetString());
    }
}
