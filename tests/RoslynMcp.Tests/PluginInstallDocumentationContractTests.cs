namespace RoslynMcp.Tests;

[TestClass]
public sealed class PluginInstallDocumentationContractTests
{
    [TestMethod]
    public void PublicInstallDocs_DescribePinnedDnxPluginTruthfully()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var documents = new[]
        {
            "README.md",
            "docs/compatibility.md",
            "docs/setup.md",
            "docs/roadmap.md",
            "docs/reinstall.md",
        }.ToDictionary(
            path => path,
            path => File.ReadAllText(Path.Combine(root,
                path.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        var combined = string.Join("\n", documents.Values);

        Assert.IsFalse(combined.Contains("plugin bundles the MCP server", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(combined.Contains("Requires `roslynmcp` on PATH", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("31 skills", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("--yes", StringComparison.Ordinal));
        StringAssert.Contains(documents["README.md"], "Darylmcd.RoslynMcp@<version>");
        StringAssert.Contains(documents["README.md"], "does not require a global `roslynmcp` shim");
        StringAssert.Contains(documents["docs/setup.md"], "32 skills");

        var updater = File.ReadAllText(Path.Combine(root, "eng", "update-claude-plugin.ps1"));
        StringAssert.Contains(updater, "release-pinned MCP launch config");
        Assert.IsFalse(updater.Contains("MCP server binary", StringComparison.Ordinal));
    }
}
